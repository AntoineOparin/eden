# Source: ambientCG 1K JPG colour maps

- Site: https://ambientcg.com (every asset CC0 1.0, see `LICENSE.txt`)
- Downloaded: 2026-11-06 through `https://ambientcg.com/get?file=<Id>_1K-JPG.zip`
- Kept only the `_Color.jpg` map of each set (renamed to lower case); dropped normal, roughness, AO, displacement,
  `.blend`, `.mtlx`, `.usdc` and the Godot `.tres` files that ship in the zips.

| File | ambientCG asset | Page | Used as |
|---|---|---|---|
| `grass004_color.jpg` | Grass 004 | https://ambientcg.com/view?id=Grass004 | grassland detail |
| `ground037_color.jpg` | Ground 037 | https://ambientcg.com/view?id=Ground037 | forest floor detail |
| `rock030_color.jpg` | Rock 030 | https://ambientcg.com/view?id=Rock030 | rock / cliff detail (triplanar) |
| `snow010a_color.jpg` | Snow 010 A | https://ambientcg.com/view?id=Snow010A | snow detail |
| `ground093c_color.jpg` | Ground 093 C | https://ambientcg.com/view?id=Ground093C | beach sand detail |

The terrain shader uses these as luminance detail under a stylized colour ramp, not as the final colour.
Water normals are not a texture: they come from a Godot `NoiseTexture2D` with `as_normal_map` (no asset needed).
