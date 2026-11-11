// CoDriverSim's simplified RGB terrain shader. MetaDrive supplies the textures
// and semantic surface values. Lighting and terrain shadows are omitted here.
#version 330

uniform sampler2D attribute_tex;
uniform sampler2D road_tex;
uniform sampler2D grass_tex;
uniform sampler2D crosswalk_tex;
uniform float road_tex_ratio;
uniform float grass_tex_ratio;
in vec2 terrain_uv;
out vec4 color;

void main() {
    float value = texture(attribute_tex, terrain_uv).r * 255.0;
    vec3 diffuse = texture(grass_tex, terrain_uv * grass_tex_ratio).rgb;
    if (value > 5.0 && value < 16.0) {
        diffuse = vec3(0.95);
    } else if (value >= 16.0 && value < 26.0) {
        diffuse = texture(road_tex, terrain_uv * road_tex_ratio).rgb;
    } else if (value >= 26.0 && value < 34.0) {
        diffuse = vec3(1.0, 0.78, 0.0);
    } else if (value >= 39.0 && value < 223.0) {
        float theta = radians((value - 40.0) * 2.0);
        vec2 uv = vec2(cos(theta) * terrain_uv.x - sin(theta) * terrain_uv.y,
                       sin(theta) * terrain_uv.x + cos(theta) * terrain_uv.y);
        diffuse = texture(crosswalk_tex, uv * road_tex_ratio).rgb;
    }
    color = vec4(diffuse, 1.0);
}
