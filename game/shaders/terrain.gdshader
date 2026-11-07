// Stylized terrain (A1). TerrainBuilder writes per-vertex layer weights into COLOR (r sand, g grass, b forest,
// a rock) and a dirt weight (yards, roads, fields) into UV2.x. Each layer is a flat palette colour from the art
// references, modulated by the luminance of a CC0 ambientCG texture (detail only, not the colour), plus a large-scale
// tone variation. Steep slopes turn to cream cliff rock (triplanar), snow caps by height, wet sand at the waterline.
// Light uses a soft two-band ramp ("subtle cell shading") with a warm wrap so shadows are never black.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, specular_disabled;

uniform vec3 sand_color : source_color = vec3(0.91, 0.84, 0.62);
uniform vec3 grass_color : source_color = vec3(0.50, 0.75, 0.30);
uniform vec3 grass_dry_color : source_color = vec3(0.70, 0.76, 0.36);
uniform vec3 forest_color : source_color = vec3(0.33, 0.56, 0.24);
uniform vec3 rock_color : source_color = vec3(0.80, 0.75, 0.64);
uniform vec3 rock_dark_color : source_color = vec3(0.66, 0.60, 0.50);
uniform vec3 snow_color : source_color = vec3(0.95, 0.96, 0.98);
uniform vec3 dirt_color : source_color = vec3(0.58, 0.40, 0.24);
uniform vec3 seabed_color : source_color = vec3(0.62, 0.78, 0.66);
uniform vec3 wet_sand_color : source_color = vec3(0.78, 0.69, 0.50);
uniform float sea_level = 0.0;
uniform float snow_height = 26.0;
uniform float detail_scale = 0.11;
uniform float detail_strength = 0.45;
uniform float macro_scale = 0.012;
uniform float band_softness = 0.35;

uniform sampler2D tex_sand : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_grass : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_forest : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_rock : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_snow : source_color, filter_linear_mipmap, repeat_enable;

varying vec4 splat;
varying vec3 world_pos;
varying vec3 world_normal;
varying float dirt;

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

float lum(vec3 c) {
	return dot(c, vec3(0.299, 0.587, 0.114));
}

// Detail factor around 1.0 from a texture's luminance (textures average roughly 0.35-0.5 luminance).
float detail_top(sampler2D t, vec2 uv, float mean) {
	return lum(texture(t, uv).rgb) / mean;
}

float detail_triplanar(sampler2D t, vec3 p, vec3 n, float mean) {
	vec3 w = pow(abs(n), vec3(4.0));
	w /= (w.x + w.y + w.z);
	float x = lum(texture(t, p.zy).rgb);
	float y = lum(texture(t, p.xz).rgb);
	float z = lum(texture(t, p.xy).rgb);
	return (x * w.x + y * w.y + z * w.z) / mean;
}

void vertex() {
	splat = COLOR;
	dirt = UV2.x;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec4 w = max(splat, vec4(0.0));
	w /= max(w.r + w.g + w.b + w.a, 0.0001);
	vec2 uv = world_pos.xz * detail_scale;

	float macro = value_noise(world_pos.xz * macro_scale) * 0.65 + value_noise(world_pos.xz * macro_scale * 3.1) * 0.35;
	vec3 grass = mix(grass_color, grass_dry_color, smoothstep(0.55, 0.85, macro));

	vec3 sand = sand_color * mix(1.0, detail_top(tex_sand, uv, 0.62), detail_strength * 0.6);
	grass *= mix(1.0, detail_top(tex_grass, uv * 1.3, 0.33), detail_strength);
	vec3 forest = forest_color * mix(1.0, detail_top(tex_forest, uv, 0.30), detail_strength);
	float rock_detail = detail_triplanar(tex_rock, world_pos * detail_scale * 0.8, world_normal, 0.42);
	vec3 rock = mix(rock_dark_color, rock_color, smoothstep(0.3, 0.7, macro)) * mix(1.0, rock_detail, detail_strength);

	float slope = 1.0 - clamp(world_normal.y, 0.0, 1.0);
	float cliff = smoothstep(0.28, 0.45, slope);
	// Mountain biome: dry stony grass on the flats, bare cream rock only where it is steep (cliffs, below).
	vec3 highland = mix(grass_dry_color * 0.92, rock, 0.35 + 0.3 * macro);
	vec3 col = sand * w.r + grass * w.g + forest * w.b + highland * w.a;
	col = mix(col, rock, cliff * (1.0 - w.r * 0.5));

	float d = clamp(dirt, 0.0, 1.0);
	vec3 dirt_col = dirt_color * mix(1.0, detail_top(tex_forest, uv * 2.0, 0.30), detail_strength * 0.5);
	col = mix(col, dirt_col, smoothstep(0.15, 0.6, d) * 0.9 * (1.0 - cliff));

	float snow = smoothstep(snow_height - 3.0, snow_height + 2.0, world_pos.y + (macro - 0.5) * 4.0) * (1.0 - cliff * 0.7);
	vec3 snow_col = snow_color * mix(1.0, detail_top(tex_snow, uv, 0.85), detail_strength * 0.4);
	col = mix(col, snow_col, snow);

	// Wet sand band just above the waterline, seabed below it.
	float wet = smoothstep(sea_level + 0.7, sea_level + 0.1, world_pos.y);
	col = mix(col, wet_sand_color, wet * 0.6);
	float underwater = smoothstep(sea_level + 0.05, sea_level - 1.5, world_pos.y);
	col = mix(col, seabed_color, underwater);

	ALBEDO = col;
	ROUGHNESS = 0.95;
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	float wrap = clamp((ndl + 0.3) / 1.3, 0.0, 1.0);
	float bands = smoothstep(0.30 - band_softness * 0.3, 0.30 + band_softness * 0.3, wrap) * 0.55
			+ smoothstep(0.70 - band_softness * 0.3, 0.70 + band_softness * 0.3, wrap) * 0.45;
	float ramp = mix(wrap, bands, 0.45);
	DIFFUSE_LIGHT += ramp * ATTENUATION * LIGHT_COLOR / PI;
}
