// Baked props (trees, rocks, bushes, crops, decor, buildings) drawn through MultiMeshes.
// Mesh colour lives in the vertex colour (linear, baked by Art/MeshBaker.cs). Vertex alpha is a role mask:
// 1 plain, 0.5 foliage (takes the per-instance tint), 0 settlement accent (replaced by the instance colour).
// INSTANCE_CUSTOM: rgb = foliage tint (multiplier, ~1) or accent colour (linear), a = wind phase 0..1.
// Wind: a cheap height-weighted sway; buildings set wind_strength = 0.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_lambert_wrap, specular_disabled;

uniform float wind_strength = 0.0;
uniform float wind_speed = 1.4;
uniform float wind_height = 7.0;
uniform bool accent_replace = false;
uniform float ambient_lift = 0.06;

varying vec3 v_color;

void vertex() {
	float role = COLOR.a;
	float leaf = 1.0 - clamp(abs(role - 0.5) * 4.0, 0.0, 1.0);
	float accent = 1.0 - step(0.25, role);
	vec3 c = COLOR.rgb;
	if (accent_replace) {
		c = mix(c, INSTANCE_CUSTOM.rgb, accent);
	} else {
		c = mix(c, c * INSTANCE_CUSTOM.rgb, leaf);
	}
	v_color = c;
	if (wind_strength > 0.0) {
		vec3 origin = MODEL_MATRIX[3].xyz;
		float phase = INSTANCE_CUSTOM.a * 6.2831 + origin.x * 0.05 + origin.z * 0.07;
		float h = clamp(VERTEX.y / wind_height, 0.0, 1.0);
		h *= h;
		float sway = sin(TIME * wind_speed + phase) * 0.7 + sin(TIME * wind_speed * 2.3 + phase * 1.7) * 0.3;
		VERTEX.x += sway * wind_strength * h * wind_height;
		VERTEX.z += sway * wind_strength * 0.6 * h * wind_height;
	}
}

void fragment() {
	ALBEDO = v_color;
	ROUGHNESS = 0.9;
	EMISSION = v_color * ambient_lift;
}
