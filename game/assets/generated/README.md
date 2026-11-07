# Procedural assets (A1)

Pieces no CC0 pack had, made in code so they need no files here (spec 45, "Procedural assets"). They are built
at startup, flat shaded, coloured from `src/Eden.Game/Art/Palette.cs`, and drawn with `shaders/props.gdshader`
next to the Kenney meshes. Blender is not installed on the build machine, so everything stays in Godot.

| Piece | Built by | How |
|---|---|---|
| Stone terrace / plinth under buildings | `Art/ProceduralMeshes.Plinth()` | `SurfaceTool` box, unit footprint from y = -1 to 0, battered 4 % at the base, a lighter coping course, packed-earth top; scaled per building to reach the lowest terrain sample (`World/Foundation.cs`). |
| Earth bank under farm plots | `Art/ProceduralMeshes.Plinth(earth: true)` | Same shape in dirt colours with a grass lip. |
| Rowing boat at docks | `Art/ProceduralMeshes.Boat()` | Seven rim points over a four-point keel, plank floor; about 3 m long. |
| Dirt yards and roads | `World/TerrainBuilder.ApplyFoundations` | Not a mesh: a per-vertex dirt weight (UV2.x) painted under footprints and along lines from each building to its settlement's centre, shaded by `shaders/terrain.gdshader`. |
| Lightning, speech, sun and moon icons | `Ui/UiTheme.Icon` | 64 px glyphs rasterised from polygons and circles (4 × 4 supersampled) into an `ImageTexture`. |
| Water normals | `scenes/world/water.tscn` | Two `NoiseTexture2D` with `as_normal_map`, scrolled in `shaders/water.gdshader`. |
| Impostor atlas for far NPCs | `Npc/ImpostorAtlas.cs` | The mid-LOD character meshes rendered once at startup into a 768 × 384 `SubViewport` (8 headings × stand/lying × 2 sexes). |

## A2 additions (2026-11-07)

| Piece | Built by | How |
|---|---|---|
| Cartoon cumulus clouds (5 shapes) | `Art/ProceduralMeshes.Cloud(v)` | 3–5 icospheres (one subdivision, 80 faces each) merged, bottom flattened, white top to grey-blue underside in the vertex colour; drawn unshaded by `shaders/clouds.gdshader` from `World/CloudLayer.cs`. |
| Record stela | `Art/ProceduralMeshes.Stela()` | Tapered slab 1.6 m tall with a pointed cap and a footing 0.35 m into the ground; the glyph "decal" is geometry: thin raised dark inlays (a sun disc of eight marks over three lines of strokes) 12 mm proud of the front face. One to three stand in front of each finished archive (`World/RoadRenderer.cs`). |
| Shrine altar | `Art/ProceduralMeshes.Altar()` | Stone table (1.5 × 0.8 m, top at 0.78 m) on two block legs and a footing, with a terracotta bowl of fruit, a loaf, two lit candles and a flower; merged into the shrine mesh as a `proc:altar` part (`Art/MeshBaker.cs`). |
| Plank bridge | `Art/ProceduralMeshes.BridgeSegment()`, `BridgePile()` | One metre of deck (three planks, two stringers, rail posts and rails, 2.4 m wide) instanced along an arch from bank to bank; square piles scaled down to the ground or sea floor. |
| Roads | `World/RoadRenderer.cs` | Not a stored mesh: per settlement, strips 2.2 m wide (four vertices across, every metre) draped over the render heights along `World/RoadNetwork.cs` polylines, dirt in the middle and grassy dirt at the edges. A1's straight painted roads are gone (a faint dirt underlay remains). |
| Impostor atlas cache | `Npc/ImpostorAtlas.cs` | The atlas render saved once as `user://cache/impostor-atlas-<hash>.png` and loaded on later runs (not in the repo). |
