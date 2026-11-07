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
