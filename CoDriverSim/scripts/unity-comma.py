"""Simulation-only Unity adapter for the unmodified official openpilot v0.9.7.

Run using the installed WSL checkout's Python. The portable observer never
imports this file. This process owns its manager and stops only descendants.
"""

import argparse
import ctypes
import json
import math
import os
import queue
import secrets
import signal
import socket
import struct
import subprocess
import threading
import time
from collections import deque
from pathlib import Path


def fresh_command(command, updated, now):
    """A responsive socket does not prove the 100 Hz producer is still alive."""
    if now - updated > 0.25 or now < updated:
        return {
            **command,
            "active": False,
            "healthy": False,
            "engageable": False,
            "status": "Comma vehicle-update loop stale",
        }
    return command


def stock_imu_axes(ego):
    """Measured device FRD vectors -> v0.9.7's raw sensor event axes.

    Upstream locationd.cc decodes both event types as (-v[2], -v[1], -v[0]).
    Do not put yaw in the raw event's Z slot or negate forward acceleration.
    """
    if not isinstance(ego, dict):
        raise ValueError("Invalid IMU telemetry object")
    result = []
    for key in ("specific_force_device_mps2", "angular_velocity_device_rps"):
        values = ego.get(key)
        if not isinstance(values, (list, tuple)) or len(values) != 3:
            raise ValueError("Missing measured three-axis IMU: " + key)
        if any(type(v) not in (int, float) or not math.isfinite(v) for v in values):
            raise ValueError("Invalid measured IMU vector: " + key)
        result.append(tuple(-v for v in reversed(values)))
    return tuple(result)


def read(sock, count):
    data = bytearray()
    while len(data) < count:
        block = sock.recv(count - len(data))
        if not block:
            raise EOFError()
        data.extend(block)
    return bytes(data)


def descendants(parent):
    pairs = []
    for item in Path("/proc").iterdir():
        if not item.name.isdigit():
            continue
        try:
            status = (item / "stat").read_text().rsplit(")", 1)[1].split()
            pairs.append((int(item.name), int(status[1])))
        except (OSError, ValueError):
            pass
    found = {parent}
    for _ in range(12):
        found.update(pid for pid, ppid in pairs if ppid in found)
    return found - {parent}


def main():
    from comma_vehicle_config import validate_configuration

    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--ready", type=Path, required=True)
    parser.add_argument("--token", required=True)
    args = parser.parse_args()
    root = args.root.resolve()
    os.chdir(root)
    import sys

    sys.path.insert(0, str(root))
    prefix = "codriver_unity_" + args.token[:12]
    os.environ.update(
        OPENPILOT_PREFIX=prefix,
        ONNXCPU="1",
        PYOPENCL_CTX="0",
        POCL_MAX_PTHREAD_COUNT="4",
        SIMULATION="1",
        NOBOARD="1",
        PASSIVE="0",
        SKIP_FW_QUERY="1",
        FINGERPRINT="HONDA_CIVIC_2022",
        CI="1",
        BLOCK="mapsd,navd,camerad,loggerd,encoderd,micd,logmessaged,ui",
        PYTHONPATH=str(Path(__file__).resolve().parent) + os.pathsep + str(root),
        GALLIUM_DRIVER="d3d12",
    )
    Path("/dev/shm", prefix).mkdir(exist_ok=True)
    ctypes.CDLL(None).prctl(36, 1, 0, 0, 0)  # adopt only our orphaned descendants
    import cereal.messaging as messaging
    import cv2
    import numpy as np
    from openpilot.common.params import Params
    from openpilot.selfdrive.car.honda.values import CruiseButtons
    from openpilot.selfdrive.test.helpers import set_params_enabled
    from openpilot.tools.sim.lib.common import SimulatorState, vec3
    from openpilot.tools.sim.lib.simulated_car import SimulatedCar
    from openpilot.tools.sim.lib.simulated_sensors import SimulatedSensors

    set_params_enabled()
    # The test helper sets Toyota in its own environment. The stock launcher
    # runs it in a child; restore the stock simulator's Honda here as well.
    os.environ["FINGERPRINT"] = "HONDA_CIVIC_2022"
    os.environ["PATH"] = str(root / ".venv/bin") + os.pathsep + os.environ["PATH"]
    os.environ["VIRTUAL_ENV"] = str(root / ".venv")
    Params().put_bool("ExperimentalLongitudinalEnabled", True)
    cv2.setNumThreads(1)
    state = SimulatorState()
    state.velocity = vec3(0, 0, 0)
    car, sensors = SimulatedCar(), SimulatedSensors(False)
    sm = messaging.SubMaster(
        ["controlsState", "carControl", "modelV2", "onroadEvents", "carParams"]
    )
    stop = threading.Event()
    lock = threading.Lock()
    latest = {"header": None, "image": None, "received": 0.0}
    command = {"active": False, "healthy": False, "status": "Waiting for camera"}
    command_at = 0.0
    presentation = queue.Queue(maxsize=1)
    tick_intervals = deque(maxlen=2000)
    button_until = 0.0
    requested = False
    manager = None
    configuration = None
    configuration_hash = ""
    mappings = {}
    road_path = args.ready.with_suffix(".road.json")
    ui_path = args.ready.with_suffix(".ui.json")
    stop_path = args.ready.with_suffix(".stop")
    log_dir = args.ready.parent
    log_dir.mkdir(parents=True, exist_ok=True)
    log = (log_dir / ("comma-" + args.token[:12] + ".log")).open("w")

    def tick():
        nonlocal command, command_at, button_until, requested
        last_request = False
        last_cruise = 0
        cruise_button = 0
        last_ui = 0.0
        ui_road = None
        deadline = time.monotonic()
        previous_tick = deadline
        while not stop.is_set():
            now = time.monotonic()
            tick_intervals.append(now - previous_tick)
            previous_tick = now
            with lock:
                header, received = latest["header"], latest["received"]
            if header:
                ego = header["ego"]
                speed = ego["speed_mps"]
                radians = math.radians(ego["bearing_deg"])
                state.velocity = vec3(speed * math.sin(radians), -speed * math.cos(radians), 0)
                state.steering_angle = ego["steering_deg"]
                state.gps.latitude, state.gps.longitude = ego["latitude"], ego["longitude"]
                state.imu.bearing = ego["bearing_deg"]
                acceleration, angular_velocity = stock_imu_axes(ego)
                state.imu.accelerometer = vec3(*acceleration)
                state.imu.gyroscope = vec3(*angular_velocity)
                state.valid = now - received < 0.3
                desired = bool(header.get("request_comma", False)) and state.valid
                state.user_brake = float(header.get("brake", 0))
                state.user_gas = float(header.get("throttle", 0))
                state.user_torque = -float(header.get("steer", 0)) * 10000
                state.left_blinker = header.get("indicator", 0) == -1
                state.right_blinker = header.get("indicator", 0) == 1
                if desired != last_request:
                    button_until = now + 0.1
                    cruise_button = CruiseButtons.DECEL_SET if desired else CruiseButtons.CANCEL
                cruise = int(header.get("cruise_delta", 0))
                if desired and cruise != last_cruise:
                    cruise_button = (
                        CruiseButtons.RES_ACCEL if cruise > last_cruise else CruiseButtons.DECEL_SET
                    )
                    button_until = now + 0.1
                last_cruise = cruise
                state.cruise_button = cruise_button if now < button_until else 0
                last_request = desired
                requested = desired
            car.update(state)
            sensors.update(state, None)
            sm.update(0)
            controls = sm["controlsState"]
            state.is_engaged = controls.active
            events = [
                str(e.name)
                for e in sm["onroadEvents"]
                if e.noEntry or e.immediateDisable or e.softDisable
            ]
            healthy = (
                sm.alive["carControl"]
                and sm.alive["controlsState"]
                and state.valid
                and not events
                and all(now - sm.recv_time[name] < 0.25 for name in ("carControl", "controlsState"))
                and configuration is not None
                and abs(sm["carParams"].mass - configuration["mass_kg"]) < 0.01
                and abs(sm["carParams"].wheelbase - configuration["wheelbase_m"]) < 0.001
                and abs(sm["carParams"].steerRatio - configuration["steering_ratio"]) < 0.001
            )
            acts = sm["carControl"].actuators
            command = {
                "schema_version": 2,
                "configuration_sha256": configuration_hash,
                "session_id": header["session_id"] if header else "",
                "frame_id": header["frame_id"] if header else 0,
                "active": bool(controls.active and requested),
                "healthy": bool(healthy),
                "engageable": bool(healthy and controls.engageable),
                "steering_deg": float(acts.steeringAngleDeg),
                "acceleration_mps2": float(acts.accel),
                "status": ", ".join(events)
                if events
                else "Waiting for healthy stock controls"
                if not healthy
                else "Stock Comma active"
                if controls.active
                else "Stock Comma ready"
                if controls.engageable
                else "Comma warming up",
            }
            command_at = time.monotonic()
            if sm.updated["modelV2"]:
                model = sm["modelV2"]
                source = mappings.get(model.frameId)
                if source:
                    road = {
                        "schema_version": 2,
                        "session_id": source["session_id"],
                        "frame_id": source["frame_id"],
                        "timestamp": source["timestamp"],
                        "model": "openpilot-v0.9.7-50dd024",
                        "lanes": [
                            {"x": list(line.x), "y": list(line.y), "z": list(line.z)}
                            for line in model.laneLines
                        ],
                        "lane_probabilities": list(model.laneLineProbs),
                        "edges": [
                            {"x": list(edge.x), "y": list(edge.y), "z": list(edge.z)}
                            for edge in model.roadEdges
                        ],
                        "path": {
                            "x": list(model.position.x),
                            "y": list(model.position.y),
                            "z": list(model.position.z),
                        },
                        "leads": [
                            {
                                "x": list(lead.x),
                                "y": list(lead.y),
                                "v": list(lead.v),
                                "prob": lead.prob,
                            }
                            for lead in model.leadsV3
                        ],
                        "execution_ms": float(model.modelExecutionTime) * 1000,
                    }
                    ui_road = road
            if header and now - last_ui >= 0.05:
                last_ui = now
                # Read-only presentation channel, never parsed as actuator commands
                # or forwarded to the portable observer. Geometry keeps its source frame.
                snapshot = {
                    "schema_version": 1,
                    "session_id": header["session_id"],
                    "timestamp": header["timestamp"],
                    "frame_id": header["frame_id"],
                    "engageable": bool(controls.engageable),
                    "enabled": bool(controls.enabled),
                    "active": bool(controls.active),
                    "healthy": bool(healthy),
                    "cruise_speed_kph": float(controls.vCruise),
                    "alert_text_1": str(controls.alertText1),
                    "alert_text_2": str(controls.alertText2),
                    "alert_status": str(controls.alertStatus),
                    "alert_size": str(controls.alertSize),
                    "calibration": header["calibration"],
                    "road": ui_road,
                    "provenance": "official-openpilot-v0.9.7-50dd024",
                }
                # Never perform Windows-mounted filesystem I/O in the vehicle
                # update loop. The bounded worker replaces obsolete snapshots.
                try:
                    presentation.put_nowait(snapshot)
                except queue.Full:
                    try:
                        presentation.get_nowait()
                    except queue.Empty:
                        pass
                    presentation.put_nowait(snapshot)
            deadline += 0.01
            stop.wait(max(0, deadline - time.monotonic()))
            if time.monotonic() - deadline > 0.1:
                deadline = time.monotonic()

    def publish_presentation():
        last_frame = None
        last_health = 0.0
        while not stop.is_set():
            try:
                snapshot = presentation.get(timeout=0.2)
            except queue.Empty:
                continue
            road = snapshot["road"]
            writes = [(ui_path, snapshot)]
            if road and (road["session_id"], road["frame_id"]) != last_frame:
                writes.append((road_path, road))
                last_frame = road["session_id"], road["frame_id"]
            now = time.monotonic()
            if now - last_health >= 1:
                last_health = now
                intervals = sorted(list(tick_intervals))
                writes.append(
                    (
                        args.ready.with_suffix(".health.json"),
                        {
                            "loop_hz": len(intervals) / max(sum(intervals), 0.001),
                            "loop_p99_ms": intervals[int((len(intervals) - 1) * 0.99)] * 1000
                            if intervals
                            else 0,
                            "loop_max_ms": max(intervals, default=0) * 1000,
                            "command_age_ms": (now - command_at) * 1000,
                            "status": command["status"],
                        },
                    )
                )
            for path, value in writes:
                try:
                    tmp = path.with_suffix(".tmp")
                    tmp.write_text(json.dumps(value))
                    tmp.replace(path)
                except OSError:
                    pass

    def camera():
        last_id = -1
        while not stop.is_set():
            with lock:
                header, image = latest["header"], latest["image"]
            if header and header["frame_id"] != last_id:
                rgb = cv2.imdecode(np.frombuffer(image, np.uint8), cv2.IMREAD_COLOR)
                if rgb is not None and rgb.shape == (604, 964, 3):
                    # Calibrated 2x resize: 964x604, fx=fy=455 -> the stock
                    # 1928x1208 camera, fx=fy=910. Model/controller unchanged.
                    rgb = cv2.resize(rgb, (1928, 1208), interpolation=cv2.INTER_LINEAR)
                    mappings[sensors.camerad.frame_road_id] = header
                    for key in list(mappings):
                        if key < sensors.camerad.frame_road_id - 120:
                            del mappings[key]
                    sensors.camerad.cam_send_yuv_road(
                        sensors.camerad.rgb_to_yuv(cv2.cvtColor(rgb, cv2.COLOR_BGR2RGB))
                    )
                last_id = header["frame_id"]
            stop.wait(0.002)

    def shutdown(*_):
        stop.set()

    def watch_owner():
        while not stop.wait(0.2):
            try:
                if stop_path.read_text() == args.token:
                    stop.set()
            except FileNotFoundError:
                pass

    signal.signal(signal.SIGTERM, shutdown)
    signal.signal(signal.SIGINT, shutdown)
    workers = [
        threading.Thread(target=tick, daemon=True),
        threading.Thread(target=camera, daemon=True),
        threading.Thread(target=publish_presentation, daemon=True),
        threading.Thread(target=watch_owner, daemon=True),
    ]
    try:
        for worker in workers:
            worker.start()
        with socket.socket() as server:
            server.bind(("127.0.0.1", 0))
            server.listen(1)
            server.settimeout(0.5)
            tmp = args.ready.with_suffix(".tmp")
            tmp.write_text(
                json.dumps(
                    {
                        "port": server.getsockname()[1],
                        "pid": os.getpid(),
                        "status": "ready",
                        "prefix": prefix,
                    }
                )
            )
            tmp.replace(args.ready)
            deadline = time.monotonic() + 60
            connection = None
            while not stop.is_set() and time.monotonic() < deadline:
                try:
                    connection, _ = server.accept()
                    break
                except TimeoutError:
                    pass
            if connection is None:
                raise EOFError("Owner closed or connection timed out")
            with connection:
                connection.settimeout(10)
                previous = -1
                while not stop.is_set():
                    size, image_size = struct.unpack("<II", read(connection, 8))
                    if size > 16384 or image_size > 8_000_000:
                        raise ValueError("Oversized camera packet")
                    header = json.loads(read(connection, size))
                    image = read(connection, image_size)
                    if not secrets.compare_digest(str(header.pop("token", "")), args.token):
                        raise ValueError("Invalid capability")
                    if header["frame_id"] <= previous:
                        raise ValueError("Reordered camera frame")
                    # Reject incompatible/malformed sensor packets before a worker
                    # can publish them or start the stock runtime.
                    stock_imu_axes(header.get("ego", {}))
                    frame_config, frame_hash = validate_configuration(header)
                    if configuration is not None and frame_hash != configuration_hash:
                        raise ValueError("Vehicle configuration changed during session")
                    if manager is None:
                        configuration, configuration_hash = frame_config, frame_hash
                        config_file = args.ready.with_suffix(".vehicle.json")
                        config_file.write_text(json.dumps(configuration, indent=2))
                        os.environ["CODRIVER_VEHICLE_CONFIG"] = str(config_file)
                        manager = subprocess.Popen(
                            [
                                str(root / ".venv/bin/python"),
                                str(Path(__file__).with_name("comma_sim_manager.py")),
                            ],
                            cwd=root / "system/manager",
                            stdout=log,
                            stderr=subprocess.STDOUT,
                            start_new_session=True,
                        )
                    previous = header["frame_id"]
                    with lock:
                        latest.update(header=header, image=image, received=time.monotonic())
                    data = json.dumps(fresh_command(command, command_at, time.monotonic())).encode()
                    connection.sendall(struct.pack("<I", len(data)) + data)
    except (EOFError, OSError, ValueError) as exc:
        print("Unity Comma bridge stopped:", exc, flush=True)
    finally:
        stop.set()
        # manager.py creates another process group. Signal our actual descendants,
        # including its manager, so upstream's normal process cleanup can run.
        owned = descendants(os.getpid())
        for pid in owned:
            try:
                os.kill(pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
        for worker in workers:
            worker.join(timeout=2)
        if manager:
            try:
                manager.wait(timeout=8)
            except subprocess.TimeoutExpired:
                pass
        for pid in descendants(os.getpid()):
            try:
                os.kill(pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
        log.close()
        args.ready.unlink(missing_ok=True)
        road_path.unlink(missing_ok=True)
        ui_path.unlink(missing_ok=True)
        args.ready.with_suffix(".health.json").unlink(missing_ok=True)
        stop_path.unlink(missing_ok=True)


if __name__ == "__main__":
    main()
