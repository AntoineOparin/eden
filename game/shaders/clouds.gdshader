// A2 cartoon cumulus (World/CloudLayer.cs). Unshaded on purpose: the white-to-grey gradient is baked into the vertex
// colour (Art/ProceduralMeshes.Cloud), so clouds need no light variants and stay soft at any hour.
// INSTANCE_CUSTOM.a = the cloud's coverage threshold (0..1): it shows when `coverage` passes it, so rain thickens the
// sky and drought thins it smoothly. Clouds fade out near the camera so the god view is never blocked.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_opaque, cull_back, shadows_disabled, fog_disabled;

uniform vec3 tint : source_color = vec3(1.0);
uniform float coverage = 0.5;
uniform float daylight = 1.0;
uniform float near_fade_start = 30.0;
uniform float near_fade_end = 90.0;
uniform float opacity = 0.94;

varying float v_alpha;
varying vec3 v_color;

void vertex() {
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float dist = distance(world, CAMERA_POSITION_WORLD);
	float near = smoothstep(near_fade_start, near_fade_end, dist);
	float threshold = INSTANCE_CUSTOM.a;
	float shown = smoothstep(threshold - 0.08, threshold + 0.08, coverage);
	v_alpha = near * shown * opacity;
	// Night: clouds go blue-grey instead of glowing white.
	vec3 night = vec3(0.16, 0.2, 0.32);
	v_color = mix(night, COLOR.rgb * tint, clamp(daylight, 0.0, 1.0));
}

void fragment() {
	ALBEDO = v_color;
	ALPHA = v_alpha;
}
