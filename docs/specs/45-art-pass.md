# 45 — Art pass: stylized low-poly look, same performance

**Milestone:** A1 (art pass), after M5, independent of M6/M7 except where noted. Lives in
`game/src/Eden.Game/`, `game/assets/`, `game/shaders/`, `game/scenes/`. Owned by one agent.

## Purpose

Replace the placeholder capsules, flat terrain colours and plain water with a coherent
stylized low-poly look, using CC0 asset packs, without giving up the performance budgets.
Direction: stylized low-poly (Kenney / Quaternius family), flat or gradient-ramp shading,
readable from the god camera at 140 m and pleasant at 30 m. References: `docs/art/references/`
(read it first; the table there says what to take from each image).

## Budgets (tests enforce them)

| Scenario | Minimum |
|---|---|
| FakeSimHost, 2,000 NPCs, 1600×900, vsync off | 60 fps average over 10 s |
| FakeSimHost, 5,000 NPCs, same | 30 fps average |
| Real sim, seed 20, year 60 (≈1,000 people, 10+ settlements, buildings, crops) | 60 fps |
| Draw calls at 2,000 NPCs | under 800 |
| Startup to first frame | under 4 s |

Measure with the existing `--fake-sim --npcs=N --log-fps --quit-after=S` flags and add
`--year=N` fast-forward for the real-sim case. Put the numbers in `docs/STATUS.md`.

## Assets

All CC0, downloaded by the agent, committed directly to git, each pack in its own folder with
its `LICENSE`/`License.txt` kept next to the files and a `SOURCE.md` naming the pack, version,
URL and download date. Keep only the files used (delete unused formats, keep glTF/GLB and
textures). Target well under 300 MB total.

| Folder | Pack(s) | Used for |
|---|---|---|
| `game/assets/characters/` | Quaternius "Ultimate Animated Character Pack" (or Kenney "Animated Characters"), rigged glTF with idle/walk/run/work/attack/sleep/death animations | hi-LOD NPCs |
| `game/assets/buildings/` | Quaternius "Medieval Village" / Kenney "Medieval" | hut, storehouse, farm-plot, well, kiln, workshop, shrine, archive, wall, dock, plus a construction-site mesh |
| `game/assets/nature/` | Quaternius "Stylized Nature MegaKit" / Kenney "Nature Kit" | trees (3 biomes), rocks, berry bushes, crops at 3 growth stages, ore outcrops |
| `game/assets/textures/` | ambientCG / Poly Haven, 1k, a handful | terrain triplanar layers (sand, grass, forest floor, rock, snow), water normals |
| `game/assets/sky/` | procedural `ProceduralSkyMaterial` by default; one Poly Haven HDRI optional | sky, ambient |

Downloads are untrusted data: unzip into a scratch folder, inspect, copy only models and
textures in. No scripts or executables from packs ever enter the repo.

## Work

**Characters.** Hi-LOD slots (50) use the rigged mesh with an `AnimationTree` blending
idle/walk/run/work/fight/sleep from `NpcPose` and speed; per-instance material colours for
skin, hair and clothing derived from the NPC id and settlement (deterministic hash, no sim
change); children scaled 0.6–0.9 by `LifeStage`, elders slightly stooped (a bone pose, not a
mesh). Mid-LOD (51–~800 nearest) is the same mesh as a `MultiMeshInstance3D` with 2–3 baked
frames of walk cycle in a shader (vertex animation texture or simple bone-less bob), per-instance
colour. Far LOD (the rest) is a camera-facing quad with an atlas of 8 directions × 2 poses.
Switching distances tuned so the 2,000-NPC budget holds.

**Buildings.** One mesh per kind plus a construction-site mesh scaled by `BuildingView.Progress`;
instanced per kind; settlement colour accents on banners/roof trim.

**Vegetation and resources.** Needs the `ResourceView` list in `IWorldSnapshot` (backlog row,
pre-approved contract change for this milestone: id, kind, x, z, amount 0..1, variant).
Trees by biome, rocks, bushes, crops on farm plots at three growth stages, all through
`MultiMeshInstance3D` with a cheap wind vertex shader; amount drives scale/stage; chunked so
culling works.

**Terrain.** Triplanar texturing per biome layer with a stylized colour ramp on top, slope
rock blend, snow by height, subtle cell-shading. Keep the one-mesh-per-chunk layout. Beach foam
line where the terrain meets sea level.

**Water.** Depth-tinted colour, foam at shorelines and around docks, animated normals, soft
edge; no reflections probes, no screen-space refraction beyond what a simple shader gives.

**Lighting and time.** `DirectionalLight3D` + procedural sky driven by the snapshot's
`HourOfDay` (dawn/dusk colours, night with a dim moon light), simple fog by distance, ACES
tonemap, mild bloom. No SSAO, no SDFGI, no volumetrics (budget).

**UI polish (light).** Consistent panel theme (one `Theme` resource), icons for the toolbar
from the Kenney UI/game-icons packs, no layout changes.

## Not in this pass

Possession camera and character controller (M6), religion/overlay visuals (M7), navigation
baking (M6), knowledge UI (backlog), sound.

## Acceptance tests

- gdUnit4: every building kind has a mesh; every biome has at least one tree variant; the
  character scene has the required animations; LOD assignment counts (50 hi, N mid, rest far)
  at 2,000 fake NPCs; the fps budget test (tagged Slow, runs the fake sim headless-off at
  1600×900 and asserts the averages above, skipped when no display).
- `./scripts/check.sh` green; `godot --headless --path game --quit` clean.
- Before/after screenshots at 30 m and 140 m on seed 20 year 60, committed under
  `docs/art/shots/` and linked from `docs/STATUS.md`.

## Do not touch

`game/src/Eden.Sim/**`, `game/src/Eden.Mind/**`, `brain/**`. Contracts: only the `ResourceView`
addition above, with a PR note. Keep `FakeSimHost` working (it must emit `ResourceView`s too).
