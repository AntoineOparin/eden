// Sea surface: procedural scrolling normals (sum of two moving value-noise layers) and a
// depth-based colour/alpha ramp read from the depth buffer, so shallows near the shore are clear.
shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform vec3 shallow_color : source_color = vec3(0.25, 0.70, 0.72);
uniform vec3 deep_color : source_color = vec3(0.04, 0.20, 0.38);
uniform float depth_range = 10.0;
uniform float wave_scale = 0.08;
uniform float wave_speed = 0.6;
uniform float normal_strength = 0.35;
uniform sampler2D depth_texture : hint_depth_texture, filter_linear_mipmap;

varying vec3 world_pos;

float hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x),
			mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float waves(vec2 p) {
	float t = TIME * wave_speed;
	return value_noise(p * wave_scale + vec2(t * 0.30, t * 0.20)) * 0.6
		+ value_noise(p * wave_scale * 2.7 - vec2(t * 0.25, -t * 0.35)) * 0.4;
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	// Finite-difference normal from the wave height field, in tangent space for NORMAL_MAP.
	float e = 0.5;
	float h = waves(world_pos.xz);
	float hx = waves(world_pos.xz + vec2(e, 0.0));
	float hz = waves(world_pos.xz + vec2(0.0, e));
	vec3 n = normalize(vec3((h - hx) * normal_strength * 4.0, (h - hz) * normal_strength * 4.0, 1.0));
	NORMAL_MAP = n * 0.5 + 0.5;

	float scene_depth = texture(depth_texture, SCREEN_UV).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, scene_depth, 1.0);
	view.xyz /= view.w;
	float thickness = clamp((-view.z - (-VERTEX.z)) / depth_range, 0.0, 1.0);

	ALBEDO = mix(shallow_color, deep_color, thickness);
	ALPHA = mix(0.5, 1.0, smoothstep(0.0, 1.0, thickness));
	ROUGHNESS = 0.08;
	METALLIC = 0.0;
	SPECULAR = 0.6;
}
