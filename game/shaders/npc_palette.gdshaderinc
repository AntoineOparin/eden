// Shared palette recolour for Kenney Mini Characters (shaders/npc_hi.gdshader, shaders/npc_mid.gdshader).
// The characters are UV-mapped onto an 8 x 4 cell colormap (Textures/colormap.png, row 0 at the top). Per model:
// skin_cell is the face cell, main_cell the largest garment cell. Per NPC (params):
//   x, y = settlement colour cell (col, row) that the main garment becomes; other row-2 garment cells shift with it
//   z    = skin choice 0..3
//   w    = 0 normal, 1 dead (desaturated), negative = highlight (thinking)
uniform sampler2D colormap : source_color, filter_nearest, repeat_enable;
uniform vec2 skin_cell = vec2(7.0, 3.0);
uniform vec2 main_cell = vec2(5.0, 2.0);

const vec2 GRID = vec2(8.0, 4.0);

vec2 skin_choice_cell(float choice) {
	int k = int(choice + 0.5);
	if (k == 0) return vec2(5.0, 3.0);
	if (k == 1) return vec2(7.0, 3.0);
	if (k == 2) return vec2(6.0, 3.0);
	return vec2(0.0, 2.0);
}

vec3 npc_albedo(vec2 uv, vec4 params) {
	vec2 g = fract(uv) * GRID;
	vec2 cell = floor(g);
	vec2 local = clamp(g - cell, vec2(0.04), vec2(0.96));
	vec2 c = cell;
	vec2 target = params.xy;
	if (all(equal(cell, skin_cell))) {
		c = skin_choice_cell(params.z);
	} else if (all(equal(cell, main_cell))) {
		c = target;
	} else if (cell.y == 2.0 && cell.x >= 1.0) {
		float shift = (main_cell.y == 2.0 ? target.x - main_cell.x : target.x) + 7.0;
		c.x = mod(cell.x - 1.0 + shift, 7.0) + 1.0;
	}
	vec3 col = texture(colormap, (c + local) / GRID).rgb;
	if (params.w > 0.0) {
		float l = dot(col, vec3(0.3, 0.59, 0.11));
		col = mix(col, vec3(l) * 0.7, clamp(params.w, 0.0, 1.0));
	}
	return col;
}
