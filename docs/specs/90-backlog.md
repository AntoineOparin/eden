# 90 — Carried-over work and open decisions

Items that an earlier milestone left out on purpose, decided differently from the plan, or
flagged as a question. Each line says where it belongs. Whoever executes that milestone owns the
item; remove it from here in the same PR that resolves it. Last updated 2026-11-07 by the M7 UI
and M6 merges.

## Eden.Sim (from M1, `10-sim-core.md`)

| Item | Belongs to | Note |
|---|---|---|
| Animals are resource herds (`ResourceNodes` kind `game`), not agents | M3 or M8 | Plan wanted animals as simple agents; herds were enough for M1 food. Revisit when predators or herding matter. |
| yieldMod on derived goods other than grain (fish, clay, hide, cloth, metals) has no producer yet | next Sim pass | Farming already multiplies grain by `World.YieldFactor`. Crafting a tool uses one unit of its first material node only. |
| Memory text is built eagerly (`$"{teller} told me: ..."`), so gossip and first lessons allocate on the tick | later perf pass | M8 removed every other tick allocation (STATUS M8); what is left is the `Memory`, its text and its subjects, which are saved state. A template id + arguments rendered on demand would remove most of it, but `Contains`, `ContainsStory`, the hash and the save read the text. |
| C1's `CHRN` chronicle section must use format-2 framing | merge of C1 + M8 | M8 changed section headers (flags byte, Brotli). `World.Section(w, tag, body)` keeps the old 3-argument call; a reader should find its section with `World.SectionsOf(body, formatVersion)` rather than parse the framing, and should run inside `SaveReader.LoadScratch` as well so a damaged section fails before the live world is touched. |

## Knowledge (from the M3/M4 merge review, `51-knowledge-roadmap.md`)

| Item | Belongs to | Note |
|---|---|---|
| Re-check pace with live LLM inventions (K1 assumed ≈ 10 % extra discovery pressure) | when a Mind runs headless or in a long play session | K1 tuned headless: median writing ≈ 190, bronze ≈ 385. Re-run `--seeds 1..20 --years 500 --pace` with inventions on. |
| Bronze era is bimodal; after M8 8/18 seeds have no bronze by year 500 and the opt-in band test fails | next balance pass | Copper → metalworking → bronze are three hard steps (0.94/0.94/0.93) after a farming boom; consider fewer, softer steps plus a population-aware discovery term. `PacingTests.Median_seed_writes_and_casts_bronze_inside_the_bands` (EDEN_PACE_FULL=1) passed on phase5-base (median bronze year 408, writing 200) and fails after M8's site picker changed every trajectory: writing 198 (in band), bronze 479 (band 300–450; 10 seeds cast bronze, 9 of them by year 450). Release sweep `--seeds 1..20 --years 500 --pace`, numbers in STATUS M8. |
| Full pacing test is opt-in (`EDEN_PACE_FULL=1`), not in check.sh | later perf pass | After M8 the same work as a Release sweep takes 17.5 min (35 min for phase5-base on a busier machine); in Debug, as check.sh runs it, it passed an hour without finishing. Under ~10 min it could join check.sh. |
| New effect primitives (`needMod`, `speedMod`, `unlockRecipe`) | when a concept needs one, one PR each | Roadmap item 4. |

## Eden.Sim (from the art pass review)

| Item | Belongs to | Note |
|---|---|---|

## Eden.Game (from M2, `40-game-client.md`)

| Item | Belongs to | Note |
|---|---|---|
| Buildings, trees and other people do not block the possessed body | next possession pass | M6 resolved the nav-baking row without navigation: the sim decides where bodies walk and `TerrainCollision` adds a trimesh for the 3×3 chunks around the possessed body only (DECISIONS 2026-11-06). Add building/tree shapes on layer 10 near the player if walking through houses bothers. |
| `FakeSimHost` ignores possession commands | keep unless needed | The director falls back to god view after 3 s ("The world did not let you in"); possession needs the real sim. |
| More possessed verbs (trade, court, eat on demand) | when wanted | M6 kept the contract's talk/give/work; the body eats and drinks on its own when desperate. |
| Export packs `game/assets/` through a staging copy | art owner, when convenient | Godot skips `.gdignore`d folders on export, so `game/tools/export/export.sh` copies `game/` to `build/stage/`, drops the `.gdignore` and marks each asset `importer="keep"` (DECISIONS 2026-11-07 "M8: export"). Moving the assets to imported resources (or a different ignore scheme) would let the editor export directly. |
| `TerrainBuilder` rebuilds every chunk when the heightmap instance changes | art owner (`World/TerrainBuilder.cs`) | M8 flattens the sim heightmap under finished buildings and therefore republishes heights only once per in-game year; rebuilding just the chunks under changed cells would allow every completion to show at once. |
| Linux export is built but not run | whoever has a Linux box | `game/tools/export/export.sh Linux` produces `build/linux/Eden.x86_64` + PCK; only the macOS app was booted. |
| Game herds (`ResourceView` kind `game`) are not drawn | next art pass or M8 | No CC0 animal model was chosen in A1; the views are emitted and skipped by `ResourceRenderer`. |
| Nova Roma extras not done: cartoon cumulus clouds, roads that follow the terrain, bridges, record stelae and shrine altars as own meshes | next art pass | A1 paints dirt yards and straight roads to the settlement centre and builds plinths/boats procedurally; the rest is open. |
| One-off hitches: decor rebuild when a building completes (~65k-cell scan), impostor atlas readback at startup, first night shader variant | M8 | Visible as a single low sample in the 5,000-NPC run; averages stay within budget. |
| Settlement panel has no food stock, and "what a settlement knows" is a sample of ≤ 8 adults | next contract pass | Needs `SettlementView.Food` and a per-settlement knowledge summary in the contract (DECISIONS "settlement knowledge is sampled"). |
| Overlay territory is a 64 m circle from `SimConfig` defaults, not the sim's real land or its config | next contract pass | Add a radius (or a territory mask) to `SettlementView` if a run changes `SettlementRadius`/`TerritoryFactor`. |
| Chronicle bursts of more than 32 entries between two frames lose the oldest ones; history before the session starts is not shown | next contract pass or save pass | `NewChronicle` covers only the last published snapshot; the client merges the 32-entry `RecentChronicle` tail. A "chronicle since tick" query or saved history would close it. |
| Power effects have no screenshot; brush preview and effects were checked by hand and by `MythUiTests` (counts), not visually reviewed | next UI pass | Rain/drought tint, ripple, bless glow, curse smoke are CPU particles and one ring shader (`Fx/PowerFx.cs`). |
| The display forces V-Sync (100–200 Hz) on this laptop now, so fps budgets read as the cap | M8 | `--log-frame-time` measures script and render CPU cost instead; Metal returns 0 for GPU time. |
| `FakeSimHost` stays under `src/Eden.Game/Dev/` | keep | Useful for perf runs; `--fake-sim --npcs=N --log-fps --screenshot=PATH --quit-after=S` are the CLI flags; A1 added `--year=N --hour=H --focus=X,Z|town --paused --zoom=M --gallery --lineup --hide=Node,...`; M7 UI added `--ui=... --m7-check --log-frame-time`. |

## brain/ (from the Worker agent, `30-brain-worker.md`)

| Item | Belongs to | Note |
|---|---|---|
| Anthropic and OpenAI-compatible adapters never called against a real API | first pasted key | Both the Worker adapters and the M5 C# direct providers (`Eden.Mind/Providers/`) were built from the live docs (2026-11-06) with stub-handler tests only; no key was available. Expect small fixes on the first real call. |
| OpenAI-compatible request shape is a model-name heuristic (`max_completion_tokens` + `reasoning_effort` for o-series/GPT-5, else `max_tokens` + `temperature`) | when it bites | The C# `OpenAiCompatibleProvider` mirrors the Worker heuristic. Some compatible servers may reject fields; make the switch a setting if so. |
| qwen3 deprecation 2026-11-20 | none | Default is Gemma 4; qwen3 remains a priced alternative. Drop the row after the date. |
| Deploy | needs the user's go-ahead | `npx wrangler deploy` has never been run. M5 was verified against `wrangler dev` on :8787; `BrainDefaults` points at `http://localhost:8787` with key `change-me-dev` until a deployed URL exists (then set the URL there and a real secret with `wrangler secret put EDEN_SHARED_SECRET`). |

## Eden.Mind (from M5, `20-mind.md`)

| Item | Belongs to | Note |
|---|---|---|
| Thought batching (`SupportsBatching`) not implemented | M8 | Every provider says false; one call per thought. |
| Live check of "budget meter halts background thoughts at the cap" | next session with spare Neurons | Covered by `MindPoolTests` (90 % → dialogue only, 100 % → nothing); not exercised against the live Worker to save Neurons. |
| At 500× a thought can land after its NPC died | balance pass | Intention is refused by the sim queue and logged; harmless, but thoughts are effectively rare at high speed (60 s real floor per NPC). |

## Myth (from M7 sim side, `World.Myth.cs`)

| Item | Belongs to | Note |
|---|---|---|
| Prayer is rare until villages are large (seed 20: first prayer at year 66) | balance pass | M1's Pray score (piety × (0.45 dPurpose + 0.3 fear)) seldom wins; M7 only multiplies it near a shrine (×1.6, + 0.1 × piety for believers). Raise the base if faiths should feel more present. |
| Faith is per faction, not per person | later, if schisms need to split villages | A settlement's adults all follow their faction's faith; "faithful" is only the 40 % founding count. |
| "Bad harvests" have no event to attribute | when crop failure becomes an event | Attribution covers deaths (starvation included), raids, discoveries and retold stories. |

## Contracts (`docs/specs/contracts/`)

| Item | Belongs to | Note |
|---|---|---|
