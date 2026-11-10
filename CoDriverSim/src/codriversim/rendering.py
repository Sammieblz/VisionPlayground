"""Rendering compatibility for the pinned MetaDrive version, loaded only by drive."""

from importlib.resources import files


def limit_terrain_texture(engine) -> int:
    """Avoid a 45,056-square texture (about 2 GB) on the default 2 km terrain.

    These two private attributes are isolated here for the pinned MetaDrive API.
    Keep the full map region; only the visual surface mask resolution changes.
    """
    from panda3d.core import Texture

    terrain = engine.terrain
    limit = min(8192, engine.win.getGsg().getMaxTextureDimension())
    pixels_per_meter = max(1, limit // terrain._semantic_map_size)
    terrain._semantic_map_pixel_per_meter = pixels_per_meter
    size = terrain._semantic_map_size * pixels_per_meter
    terrain.semantic_tex.setup2dTexture(size, size, Texture.TUnsignedByte, Texture.F_red)
    return pixels_per_meter


def ensure_terrain_shader(engine) -> str:
    """Retain native RGB shading when it links; fall back on affected drivers.

    Only the main RGB camera's TERRAIN state changes. Vehicle shaders and
    semantic/depth camera states remain managed by MetaDrive.
    """
    from metadrive.constants import Semantics
    from metadrive.engine.logger import get_logger
    from panda3d.core import NodePath, Shader, ShaderAttrib

    camera = engine.cam.node()
    native = camera.getTagState(Semantics.TERRAIN.label).getAttrib(ShaderAttrib).getShader()
    if not native.getErrorFlag():
        return "native"
    root = files("codriversim").joinpath("shaders")
    shader = Shader.make(
        Shader.SL_GLSL,
        vertex=root.joinpath("terrain_compat.vert.glsl").read_text(encoding="utf-8"),
        fragment=root.joinpath("terrain_compat.frag.glsl").read_text(encoding="utf-8"),
    )
    dummy = NodePath("CoDriverSim compatible RGB terrain")
    dummy.setShader(shader)
    camera.setTagState(Semantics.TERRAIN.label, dummy.getState())
    # Compile through the normal drawing context, including threaded rendering.
    for _ in range(3):
        engine.graphicsEngine.renderFrame()
    engine.graphicsEngine.syncFrame()
    if shader.getErrorFlag():
        raise RuntimeError("The terrain compatibility shader failed; check the graphics driver.")
    get_logger().warning("Using CoDriverSim's simplified terrain shader for this graphics driver.")
    return "compatible"


def create_environment(config: dict, *, city: bool):
    """Attach scene lifecycle to reset/close, including MetaDrive's R shortcut."""
    from metadrive import MetaDriveEnv
    from metadrive.type import MetaDriveType

    from codriversim.scenery import plan_buildings, render_buildings

    class CoDriverEnv(MetaDriveEnv):
        buildings_node = None
        terrain_renderer = "none"
        terrain_pixels_per_meter = None
        building_count = 0

        def setup_engine(self):
            super().setup_engine()
            if self.config["use_render"]:
                self.terrain_pixels_per_meter = limit_terrain_texture(self.engine)
                self.terrain_renderer = "pending"

        def reset(self, *args, **kwargs):
            if self.buildings_node is not None:
                self.buildings_node.removeNode()
                self.buildings_node = None
            result = super().reset(*args, **kwargs)
            if self.config["use_render"]:
                if self.terrain_renderer != "compatible":
                    self.terrain_renderer = ensure_terrain_shader(self.engine)
                if city:
                    features = self.current_map.get_map_features()
                    roads = [
                        feature["polygon"]
                        for feature in features.values()
                        if MetaDriveType.is_lane(feature["type"]) and "polygon" in feature
                    ]
                    buildings = plan_buildings(roads, self.current_seed)
                    self.buildings_node = render_buildings(self.engine.render, buildings)
                    self.building_count = len(buildings)
            return result

        def close(self):
            if self.buildings_node is not None:
                self.buildings_node.removeNode()
                self.buildings_node = None
            super().close()

    return CoDriverEnv(config)
