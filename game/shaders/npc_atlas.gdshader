// Used only to render the far-LOD impostor atlas (Npc/ImpostorAtlas.cs): unshaded palette colours with the main
// garment painted pure white, so npc_far.gdshader can tint it with the settlement colour per instance.
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_opaque;

#include "res://shaders/npc_palette.gdshaderinc"

void fragment() {
	vec2 g = fract(UV) * GRID;
	vec2 cell = floor(g);
	vec3 col = npc_albedo(UV, vec4(main_cell, 1.0, 0.0));
	if (all(equal(cell, main_cell))) col = vec3(1.0);
	ALBEDO = col;
}
