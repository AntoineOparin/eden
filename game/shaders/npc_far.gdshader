// Far-LOD NPCs: camera-facing quads in one MultiMesh, textured from an impostor atlas rendered at startup
// (8 headings x 2 poses, unshaded, main garment painted white). INSTANCE_CUSTOM: rgb = settlement tint (linear),
// a = atlas cell index (heading + 8 * pose). Lit with a fixed up-facing normal so far crowds match the terrain.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_lambert_wrap, specular_disabled;

uniform sampler2D atlas : source_color, filter_linear_mipmap;
uniform vec2 atlas_grid = vec2(8.0, 2.0);

varying vec4 v_custom;

void vertex() {
	v_custom = INSTANCE_CUSTOM;
	// Spherical billboard around the instance origin, keeping the instance scale.
	float sx = length(MODEL_MATRIX[0].xyz);
	float sy = length(MODEL_MATRIX[1].xyz);
	vec3 right = normalize(INV_VIEW_MATRIX[0].xyz);
	vec3 up = normalize(INV_VIEW_MATRIX[1].xyz);
	vec3 world = MODEL_MATRIX[3].xyz + right * VERTEX.x * sx + up * VERTEX.y * sy;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world, 1.0);
	NORMAL = (VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz;
}

void fragment() {
	float cell = floor(v_custom.a + 0.5);
	vec2 cell_xy = vec2(mod(cell, atlas_grid.x), floor(cell / atlas_grid.x));
	vec2 uv = (cell_xy + UV) / atlas_grid;
	vec4 t = texture(atlas, uv);
	if (t.a < 0.5) discard;
	float white = smoothstep(0.85, 0.97, min(t.r, min(t.g, t.b)));
	ALBEDO = mix(t.rgb, t.rgb * v_custom.rgb, white);
	ROUGHNESS = 0.9;
}
