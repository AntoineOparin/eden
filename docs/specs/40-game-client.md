# 40 — Eden.Game: Godot client

**Milestones:** M2 (island on screen), M6 (possession), parts of M5 (UI) and M8 (scale).
Lives in `game/src/Eden.Game/`, `game/scenes/`, `game/assets/`, `game/shaders/`.

## Purpose

Render the sim, give the player two cameras (god view, possession) and the god powers, and
host the UI. The main thread renders and sends commands; it never mutates sim state.

## Public interface consumed

- `ISimHost` / `IWorldSnapshot` / `WorldCommand` from `contracts/`. `Main.cs` constructs the
  `SimHost` (already present) and hands `Latest` to every renderer each frame.
- `Eden.Mind.MindPool` (M5): `Status` for the budget meter, `AskAsync` for dialogue, `Settings`.
- Until M1 merges, render against `FakeSimHost` (write it under `src/Eden.Game/Dev/`): N
  synthetic NPCs wandering on a generated heightmap, so M2 is independent of M1.

## Design

**Scenes.** `scenes/main.tscn` (root `Main`), `scenes/world/terrain.tscn`,
`scenes/world/water.tscn`, `scenes/npc/npc_hi.tscn` (skinned glTF + `AnimationTree`),
`scenes/cameras/god_rig.tscn`, `scenes/cameras/possess_rig.tscn`, `scenes/ui/hud.tscn`,
`scenes/ui/inspector.tscn`, `scenes/ui/chronicle.tscn`, `scenes/ui/brain_settings.tscn`.

**Terrain.** `TerrainBuilder`: snapshot `Heights`/`Biomes` → one `ArrayMesh` per 32×32 chunk
with normals and a biome splat (`shaders/terrain.gdshader`, 4-way blend by biome id in a
vertex colour); water as a plane at `SeaLevel` with a simple scrolling-normal shader; chunk
`NavigationRegion3D` baked at generation, rebaked locally on `TerraformCommand`/building change.

**NPC rendering / LOD.** `NpcRenderer`: the ≤ 50 nearest NPCs (by camera distance, hysteresis)
get an `npc_hi` instance pooled and re-targeted; everyone else goes into a
`MultiMeshInstance3D` (low-poly body, per-instance colour by settlement, `NpcPose` picks a
frame). Buildings via one `MultiMesh` per kind. Target 60 fps with 2,000 NPCs.

**A1 as built.** Art and LODs follow `45-art-pass.md` ("As built"): `NpcRenderer` has hi (50, skinned
`NpcHi` + `AnimationTree`), mid (700 baked poses) and far (impostor quads) LODs; `ResourceRenderer` and
`BuildingRenderer` draw `IWorldSnapshot.Resources` / `Buildings` through chunked MultiMeshes; `TerrainBuilder` keeps a
render copy of the heights flattened under building foundations; `SkyCycle` drives sun, moon and sky from the hour;
`UiTheme` is the one HUD theme. Assets load at runtime from the `.gdignore`d `game/assets/`.

**God camera.** `GodCameraRig` (`Node3D` + `Camera3D`): fixed pitch ≈ 55°, WASD and screen-edge
pan, scroll zoom 20–400 m that eases pitch between 50° and 65°, **no yaw**. Left click picks
(`PhysicsRayQueryParameters3D` against a thin NPC collision layer or nearest `NpcView` in
screen space for MultiMesh ones), right-drag applies the selected power.

**Possession (M6).** `PossessCommand` → sim marks the NPC; camera cross-fades to
`PossessRig` (`SpringArm3D` third person, F toggles first person); `PossessedController`
turns input into `PossessedMoveCommand` each physics frame; E interacts (`PossessedInteractCommand`
talk/give/work) with the nearest entity; Escape → `ReleasePossessionCommand`. Needs still
tick; nearby NPCs' reactions come from the sim.

**God powers v1.** Toolbar: spawn human/animal/plant, lightning, rain/drought, raise/lower
terrain, bless/curse, drop item, whisper (opens a one-line prompt → `WhisperCommand`). Each is
a `WorldCommand`; visuals (bolt, particles) are purely local.

**UI.** Time controls (pause/1×/10×/100×/500× → `SetTimeScaleCommand`), NPC inspector
(`TryGetNpcDetail`: needs bars, traits, knowledge list, relationships, memory list sorted by
importance, current goal/thought), settlement panel, Chronicle timeline (drains `NewChronicle`),
brain settings (provider, base URL, model, key; key encrypted with `Crypto`/`AesContext` and a
machine-local key file in `user://`), budget meter.

**M5 as built.** `Main` builds the `MindPool` next to the host (provider "none" with the fake sim,
headless, or `--no-brain`; `--brain` enables it headless). HUD rows: top bar (time, status), powers
bar (`GodPowers`: Select / Lightning / Whisper; with Lightning a left click ray-marches the heightmap
and sends `LightningCommand`), mind bar (`BudgetMeter`, "Brain" button). `scenes/ui/dialogue.tscn`
(`DialoguePanel`) follows the selection; `scenes/ui/brain_settings.tscn` (`BrainSettingsPanel`);
`BrainSettingsStore` keeps `user://brain_settings.json` (key = base64(iv + AES-256-CBC), PKCS#7),
`user://brain.key` (32 random bytes) and `user://brain_budget.json`. The inspector's Thought line
falls back to `MindPool.CurrentThought`. `--m5-check` (`Dev/M5Check.cs`) scripts the M5 manual
check through the same UI paths (`--m5-year`, `--m5-message`, `--m5-shot`, `--m5-timeout`).

**M7 UI as built.** Toolbar (`GodPowers`): Select / Lightning / Whisper plus ten icon buttons with tooltips: spawn human
(`SpawnCommand("human")`), animals (`animal:deer` ×3), plants (`plant:berry` ×3), rain / drought (`WeatherCommand`, 3 days),
raise / lower land (`TerraformCommand` ±2.5 m with a 4–40 m brush slider and a ring preview under the cursor), bless /
curse (`BlessCommand` on the person under the cursor or the nearest within 6 m, 2 days), drop food (`DropItemCommand`
"food" ×3). Keys H N T R Y U J B K G. `Main.M7.cs` (partial of `Main`, one `SetupM7` call in `_Ready`) routes clicks
and plays `Fx/PowerFx` effects. Everything else lives under the HUD's one `GodUi` node (myth bar: Chronicle, Settlements,
overlay mode + legend + concept picker; keys C, O, M): `ChroniclePanel` (`scenes/ui/chronicle.tscn`: 12 kind filters,
importance slider, per-year density strip, newest 800 rows of a 5,000-entry `ChronicleLog`, click focuses the camera on
the subject or the named settlement), `SettlementPanel` (`scenes/ui/settlement.tscn`: picker, faction members, leader
link, favour / awe bars, stance, envied / feared neighbour, faith, finished shrines, sampled knowledge with bars, recent
entries naming it; no food stock, the contract has none). `Overlay/TerritoryOverlay` tints each settlement's land by
god standing, faith or knowledge of a concept (off by default). Inspector: settlement link, knowledge as proficiency
bars (strongest 16), `myth·n` marker on memories with `Generation` > 0, and a `PossessRequested` hook for M6. Dev flags:
`--ui=chronicle,settlement[:id|town],overlay:stance|religion|knowledge[:concept],power:<name>,select:<id>`,
`--m7-check` (`Dev/M7Check.cs`), `--log-frame-time` (`Dev/FrameTimeProbe.cs`, for when the display forces V-Sync).
Panels refresh on timers (0.25–1 s), never per frame; while the sim is paused no new details arrive, so the leader name
and knowledge sample wait for the next published snapshot.

**Threading.** `Main._Process`: read `host.Latest` once, pass the same snapshot to every
renderer and UI panel; never hold a snapshot across frames except for diffing.

**Save/load.** `SaveCommand`/`LoadCommand` to `user://saves/<name>.eden`; autosave every 5
real minutes; the JSON header is what the load menu shows.

## Test harness layout (do not restructure)

gdUnit4Net launches Godot with the test project directory as the Godot project, and Godot only
binds C# scripts from that project's own assembly. So `game/tests/Eden.Game.Tests/` is a mirror
Godot project: its `project.godot` names the assembly `Eden.Game.Tests`, it symlinks `scenes`,
`src`, `data`, `assets`, `shaders` to the real ones, and its csproj compiles `src/Eden.Game/**/*.cs`
into the test assembly (plus `gdunit4_testadapter*/**/*.cs`, injected by the adapter and
gitignored) and references `Eden.Sim` / `Eden.Mind`. Tests load scenes by their normal `res://`
paths. `GODOT_BIN` must be the real Godot binary path. New folders under `game/` that scenes
reference need a matching symlink here.

## Acceptance tests

- `game/tests/Eden.Game.Tests/` (gdUnit4Net): `main.tscn` instantiates without errors;
  `TerrainBuilder` produces the expected vertex count for a 32×32 chunk; `GodCameraRig` clamps
  zoom and never changes yaw; `NpcRenderer` assigns exactly 50 hi-LOD slots for 1,000 NPCs.
- `godot --headless --path game --quit` exits 0 with no `ERROR` lines.
- Manual M2: `FakeSimHost` with 1,000 NPCs at 60 fps on the laptop, screenshot via the godot
  MCP attached to the PR.

## Carried-over items

See `90-backlog.md`; the rows naming your milestone are part of your scope.

## Do not touch

- `game/src/Eden.Sim/**`, `game/src/Eden.Mind/**`, `brain/**`.
- Do not implement game logic (needs, pathing truth, combat) in Godot; if the sim lacks data
  you need, add a field to `IWorldSnapshot` in `contracts/` with a PR note.
