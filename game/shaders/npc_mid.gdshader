// Mid-LOD NPCs: baked pose meshes of a Kenney Mini Character in MultiMeshes. INSTANCE_CUSTOM carries the
// per-NPC palette params (npc_palette.gdshaderinc). Walk frames are separate baked meshes; this shader only adds a
// small vertical bob for walkers (custom data alpha < 0 is not used here; bob comes from the instance transform).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_lambert_wrap, specular_disabled;

#include "res://shaders/npc_palette.gdshaderinc"

varying vec4 v_params;

void vertex() {
	v_params = INSTANCE_CUSTOM;
}

void fragment() {
	vec3 col = npc_albedo(UV, v_params);
	ALBEDO = col;
	ROUGHNESS = 0.85;
	EMISSION = col * (v_params.w < 0.0 ? 0.35 : 0.05);
}
