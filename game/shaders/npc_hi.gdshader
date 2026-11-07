// Close-up NPC (skinned Kenney Mini Character). Per-NPC colours come from an instance uniform, so 50 pooled
// characters share one material per model. See npc_palette.gdshaderinc for the params layout.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_lambert_wrap, specular_disabled;

#include "res://shaders/npc_palette.gdshaderinc"

instance uniform vec4 npc_params = vec4(5.0, 2.0, 0.0, 0.0);

void fragment() {
	vec3 col = npc_albedo(UV, npc_params);
	ALBEDO = col;
	ROUGHNESS = 0.85;
	EMISSION = col * (npc_params.w < 0.0 ? 0.35 : 0.05);
}
