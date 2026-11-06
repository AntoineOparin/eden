// Terrain biome splat. TerrainBuilder writes per-vertex layer weights into COLOR:
// r = sand, g = grass, b = forest, a = rock. Snow whitens rock above snow_height; steep slopes
// fade to rock; a cheap value-noise term breaks up the flat colours. No textures needed.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3 sand_color : source_color = vec3(0.86, 0.79, 0.58);
uniform vec3 grass_color : source_color = vec3(0.42, 0.62, 0.27);
uniform vec3 forest_color : source_color = vec3(0.20, 0.40, 0.18);
uniform vec3 rock_color : source_color = vec3(0.47, 0.44, 0.41);
uniform vec3 snow_color : source_color = vec3(0.95, 0.96, 0.98);
uniform vec3 seabed_color : source_color = vec3(0.55, 0.52, 0.40);
uniform float sea_level = 0.0;
uniform float snow_height = 26.0;
uniform float noise_scale = 0.35;
uniform float noise_strength = 0.12;

varying vec4 splat;
varying vec3 world_pos;
varying vec3 world_normal;

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
	splat = COLOR;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec4 w = max(splat, vec4(0.0));
	float total = max(w.r + w.g + w.b + w.a, 0.0001);
	w /= total;

	float slope = 1.0 - clamp(world_normal.y, 0.0, 1.0);
	float rocky = smoothstep(0.35, 0.6, slope);
	vec3 col = sand_color * w.r + grass_color * w.g + forest_color * w.b + rock_color * w.a;
	col = mix(col, rock_color, rocky * (1.0 - w.r));

	float snow = smoothstep(snow_height - 3.0, snow_height + 2.0, world_pos.y) * (1.0 - rocky * 0.6);
	col = mix(col, snow_color, snow);

	float underwater = smoothstep(sea_level + 0.2, sea_level - 2.0, world_pos.y);
	col = mix(col, seabed_color, underwater);

	float n = value_noise(world_pos.xz * noise_scale) * 0.6 + value_noise(world_pos.xz * noise_scale * 4.0) * 0.4;
	col *= 1.0 + (n - 0.5) * 2.0 * noise_strength;

	ALBEDO = col;
	ROUGHNESS = mix(0.95, 0.6, snow);
	SPECULAR = 0.2;
}
