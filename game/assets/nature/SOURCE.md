# Source: Kenney "Nature Kit"

- Pack: Nature Kit, version 2.1 (Kenney, created 2020-04-29)
- URL: https://kenney.nl/assets/nature-kit (zip via the page's "Continue without donating" link)
- Downloaded: 2026-11-06
- License: CC0 1.0 (`License.txt`, kept verbatim)
- Kept: the GLB models used for trees, stumps, rocks, ore, bushes, crops and ground decor (see
  `src/Eden.Game/Art/ArtLibrary.cs`). The pack ships its GLBs in a folder named "GLTF format"; materials are flat
  colours, no textures. Dropped: DAE/FBX/OBJ/STL formats, isometric and side preview renders, `.url` shortcuts.
- Colours are remapped at load to the art-reference palette by material name (`src/Eden.Game/Art/Palette.cs`).
