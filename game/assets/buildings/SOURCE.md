# Source: Kenney "Fantasy Town Kit"

- Pack: Fantasy Town Kit, version 2.0 (Kenney, created 2025-08-03)
- URL: https://kenney.nl/assets/fantasy-town-kit (zip via the page's "Continue without donating" link)
- Downloaded: 2026-11-06
- License: CC0 1.0 (`License.txt`, kept verbatim)
- Kept: the GLB pieces the building kinds are assembled from (see `src/Eden.Game/Art/BuildingFactory.cs`) and
  `Textures/colormap.png`, which the GLBs reference. Dropped: FBX/OBJ formats, the variation texture, previews,
  `.url` shortcuts, `Overview.html`.
- Colours are remapped at load to the art-reference palette (`src/Eden.Game/Art/Palette.cs`); the files are unmodified.
