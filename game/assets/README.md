# game/assets

CC0 art for the A1 art pass (`docs/specs/45-art-pass.md`). Each folder has the pack's license and a `SOURCE.md`
(pack, version, URL, download date, what was kept).

The folder has a `.gdignore`: the Godot editor does not import these files. `src/Eden.Game/Art/` loads the GLBs at
runtime with `GltfDocument` and the PNG/JPGs with `Image.LoadFromFile`, then bakes them into single-surface meshes.
This keeps the gdUnit mirror project (which has its own `.godot/` cache) and fresh clones working without an import
step. Exported builds will need these files shipped as non-resource files (backlog row).

A2 (2026-11-07) added `animals/` (Quaternius CC0 deer, pig drawn as a wild boar, and sheep, fetched from Poly Pizza
because the pack's Google Drive folder was over quota; 1.3 MB, see `animals/SOURCE.md`) and more procedural pieces
(clouds, record stelae, the shrine altar, plank bridges, draped roads; `generated/README.md`).
