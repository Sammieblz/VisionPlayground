// Adapted from MetaDrive's terrain.vert.glsl (Apache-2.0).
// See THIRD_PARTY_NOTICES.md and licenses/MetaDrive-Apache-2.0.txt.
#version 330

in vec4 p3d_Vertex;
uniform mat4 p3d_ModelViewProjectionMatrix;
uniform struct {
    sampler2D data_texture;
    sampler2D heightfield;
    int view_index;
    int terrain_size;
    int chunk_size;
} ShaderTerrainMesh;

out vec2 terrain_uv;

void main() {
    vec4 terrain_data = texelFetch(ShaderTerrainMesh.data_texture,
        ivec2(gl_InstanceID, ShaderTerrainMesh.view_index), 0);
    vec3 chunk_position = p3d_Vertex.xyz;
    float clod_factor = smoothstep(0.0, 1.0, terrain_data.w);
    chunk_position.xy -= clod_factor
        * fract(chunk_position.xy * ShaderTerrainMesh.chunk_size / 2.0)
        * 2.0 / ShaderTerrainMesh.chunk_size;
    chunk_position *= terrain_data.z * float(ShaderTerrainMesh.chunk_size)
        / float(ShaderTerrainMesh.terrain_size);
    chunk_position.z *= ShaderTerrainMesh.chunk_size;
    chunk_position.xy += terrain_data.xy / float(ShaderTerrainMesh.terrain_size);
    terrain_uv = chunk_position.xy;
    chunk_position.z += texture(ShaderTerrainMesh.heightfield, terrain_uv).x;
    gl_Position = p3d_ModelViewProjectionMatrix * vec4(chunk_position, 1.0);
}
