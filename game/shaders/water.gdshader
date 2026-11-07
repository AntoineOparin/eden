// Stylized sea (A1). Colour ramps from clear turquoise in the shallows to deep blue by water thickness (read from
// the depth buffer); white cartoon foam hugs every shore (and docks/cliffs that reach the water) with a second,
// breathing foam line a little further out; normals come from two scrolling NoiseTexture2D normal maps
// (built in scenes/world/water.tscn, no image asset). Soft alpha at the very edge. No reflections, no refraction.
shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_disabled, diffuse_lambert, specular_schlick_ggx;

uniform vec3 shallow_color : source_color = vec3(0.30, 0.84, 0.82);
uniform vec3 mid_color : source_color = vec3(0.10, 0.58, 0.74);
uniform vec3 deep_color : source_color = vec3(0.05, 0.26, 0.52);
uniform vec3 foam_color : source_color = vec3(0.97, 0.98, 0.96);
uniform float depth_range = 14.0;
uniform float foam_width = 0.9;
uniform float wave_scale = 0.035;
uniform float wave_speed = 0.02;
uniform float normal_strength = 0.45;
uniform sampler2D normal_a : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_b : hint_normal, filter_linear_mipmap, repeat_enable;
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

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 uv = world_pos.xz * wave_scale;
	vec3 na = texture(normal_a, uv + vec2(TIME * wave_speed, TIME * wave_speed * 0.6)).rgb;
	vec3 nb = texture(normal_b, uv * 1.7 - vec2(TIME * wave_speed * 0.8, -TIME * wave_speed * 1.1)).rgb;
	NORMAL_MAP = normalize(mix(na, nb, 0.5));
	NORMAL_MAP_DEPTH = normal_strength;

	float scene_depth = texture(depth_texture, SCREEN_UV).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, scene_depth, 1.0);
	view.xyz /= view.w;
	float thickness = max(0.0, -view.z - (-VERTEX.z));
	// Depth difference along view z -> distance along the ray -> vertical water depth in metres.
	vec3 up_view = (VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz;
	vec3 ray = normalize(VERTEX);
	float vertical = thickness / max(-ray.z, 0.05) * abs(dot(ray, up_view));

	float t = clamp(vertical / depth_range, 0.0, 1.0);
	vec3 col = mix(shallow_color, mid_color, smoothstep(0.0, 0.35, t));
	col = mix(col, deep_color, smoothstep(0.3, 1.0, t));

	float n = value_noise(world_pos.xz * 0.6 + TIME * 0.15) * 0.5 + value_noise(world_pos.xz * 1.7 - TIME * 0.1) * 0.5;
	float shore = 1.0 - smoothstep(foam_width * (0.55 + 0.6 * n), foam_width * (0.75 + 0.6 * n), vertical);
	float pulse = foam_width * (1.9 + 0.35 * sin(TIME * 0.8 + world_pos.x * 0.05 + world_pos.z * 0.04));
	float line2 = (1.0 - smoothstep(0.0, 0.18, abs(vertical - pulse))) * step(0.45, n) * 0.85;
	float foam = clamp(max(shore, line2), 0.0, 1.0);

	ALBEDO = mix(col, foam_color, foam);
	ALPHA = mix(0.55, 0.97, smoothstep(0.0, 0.25, t));
	ALPHA = max(ALPHA, foam);
	ROUGHNESS = mix(0.12, 0.9, foam);
	METALLIC = 0.0;
	SPECULAR = mix(0.45, 0.1, foam);
}
