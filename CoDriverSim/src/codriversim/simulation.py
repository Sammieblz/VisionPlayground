"""MetaDrive integration. This module is loaded only for the drive command."""

import time
from dataclasses import asdict

from codriversim.adapters import MetaDriveTruthSource
from codriversim.domain import evaluate


def drive(
    *, seed: int, density: float, steps: int | None, headless: bool, scene: str = "city"
) -> dict:
    try:
        from metadrive.policy.idm_policy import IDMPolicy

        from codriversim.rendering import create_environment
    except ImportError as exc:
        raise RuntimeError(
            'Simulator dependency missing. Install with: python -m pip install ".[sim]"'
        ) from exc

    config = {
        "use_render": not headless,
        "manual_control": not headless,
        "traffic_density": density,
        "traffic_mode": "respawn",
        "num_scenarios": 100,
        "start_seed": seed,
        "map": "XTX" if scene == "city" else 3,
        "map_region_size": 1024 if scene == "city" else 2048,
        "height_scale": 1 if scene == "city" else 50,
        "window_size": (960, 540),
        "show_fps": True,
        "on_continuous_line_done": False,
        "vehicle_config": {"show_lidar": False},
    }
    if headless:
        # Test-harness driver only. The co-driver never sends control commands.
        config["agent_policy"] = IDMPolicy
    env = create_environment(config, city=scene == "city")
    count = 0
    transitions = []
    last_level = None
    started = time.perf_counter()
    try:
        env.reset(seed=seed)
        source = MetaDriveTruthSource(env)
        timestep = env.config["physics_world_step_size"] * env.config["decision_repeat"]
        while steps is None or count < steps:
            _, _, terminated, truncated, info = env.step([0, 0])
            count += 1
            state = source.observe(count * timestep)
            warning = evaluate(state)
            if warning.level != last_level:
                event = {"state": asdict(state), "warning": asdict(warning)}
                transitions.append(event)
                # Bound memory in long interactive sessions.
                if len(transitions) > 1000:
                    transitions.pop(0)
                last_level = warning.level
            if not headless:
                env.render(
                    text={
                        "CoDriverSim": "SIMULATOR TRUTH - NO CAMERA AI",
                        "Co-driver": warning.level.upper(),
                        "Message": warning.message,
                        "Speed": f"{state.speed_mps * 3.6:.0f} km/h",
                        "Scene": scene,
                        "Gap": "unknown" if state.gap_m is None else f"{state.gap_m:.1f} m",
                        "Controls": "WASD drive | H help | R reset | Esc exit",
                    }
                )
            if terminated or truncated:
                if headless:
                    break
                env.reset(seed=seed)
                last_level = None
        elapsed = time.perf_counter() - started
        return {
            "project": "codriversim",
            "mode": "simulator_truth",
            "synthetic": True,
            "model_inference": False,
            "vehicle_control": False,
            "test_driver": "IDMPolicy" if headless else "manual",
            "seed": seed,
            "scene": scene,
            "map": config["map"],
            "terrain_renderer": env.terrain_renderer,
            "terrain_pixels_per_meter": env.terrain_pixels_per_meter,
            "decorative_buildings": env.building_count,
            "steps": count,
            "wall_seconds": elapsed,
            "simulation_seconds": count * timestep,
            "warning_transitions": transitions,
            "last_step": {key: bool(info[key]) for key in ("arrive_dest", "crash") if key in info},
        }
    finally:
        env.close()
