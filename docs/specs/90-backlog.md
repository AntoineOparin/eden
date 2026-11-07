# 90 — Carried-over work and open decisions

Items that an earlier milestone left out on purpose, decided differently from the plan, or
flagged as a question. Each line says where it belongs. Whoever executes that milestone owns the
item; remove it from here in the same PR that resolves it. Last updated 2026-11-06 by M5
(minds).

## Eden.Sim (from M1, `10-sim-core.md`)

| Item | Belongs to | Note |
|---|---|---|
| Animals are resource herds (`ResourceNodes` kind `game`), not agents | M3 or M8 | Plan wanted animals as simple agents; herds were enough for M1 food. Revisit when predators or herding matter. |
| Re-run the M4 "concept reaches a second settlement only after contact" test on real M3 settlements | next Sim pass (not done in M5) | M4 proved it with two separated groups; the wiring (`World.Integration.cs`: BuildingKinds as `IBuildingUnlocks`, archives host records, farm yields use knowledge) landed at the M3/M4 merge. |
| yieldMod on derived goods other than grain (fish, clay, hide, cloth, metals) has no producer yet | next Sim pass | Farming already multiplies grain by `World.YieldFactor`. Crafting a tool uses one unit of its first material node only. |
| Truncated save body can leave a half-loaded world | M8 (save pass) | Garbage files are rejected; a good header with a cut-off body is not. Load into a scratch world and swap on success. |
| ~4 KB per tick of allocation from birth/death/gossip events | M8 | 5,000 NPCs tick in 2.5 ms today; events are the remaining allocator. |

## Knowledge (from the M3/M4 merge review, `51-knowledge-roadmap.md`)

| Item | Belongs to | Note |
|---|---|---|
| Forgetting: proficiency decay without practice, records as the floor | balance pass after M5 | Roadmap item 1. |
| Pace: era length targets, then tune discovery/chatter/teach rates with LLM inventions on | balance pass after M5 | Roadmap item 2. Add writing@ / bronze@ sweep columns. |
| Graph depth: 3 to 5 prerequisite chains for late-era concepts | M8 content pass or with the pace pass | Roadmap item 3. |
| New effect primitives (`needMod`, `speedMod`, `unlockRecipe`) | when a concept needs one, one PR each | Roadmap item 4. |
| `user://data/` override for concepts and ids | M8 or later | Roadmap item 5. |
| Knowledge UI: settlement knowledge panel, Chronicle filters, spread overlay, proficiency bars | post-M5 UI pass, M7 overlay | Roadmap item 6. Not done in M5 (the M5 UI pass covered dialogue, brain settings, budget meter, god-powers toolbar). The inspector already lists knowledge with proficiency numbers. |

## Eden.Sim (from the art pass review)

| Item | Belongs to | Note |
|---|---|---|
| Building site picker should prefer flat ground (max slope under the footprint) and flatten the sim heightmap slightly on completion | M3 owner, next Sim pass | Render-side foundations/flattening land in A1 regardless; this makes pathing and placement agree with the picture. |

## Eden.Game (from M2, `40-game-client.md`)

| Item | Belongs to | Note |
|---|---|---|
| No per-chunk `NavigationRegion3D` baking | M6 | Only the possessed `CharacterBody3D` needs navigation; the sim never does. |
| Settlement panel, Chronicle timeline, the other god powers (spawn, rain/drought, terraform, bless/curse, drop item) | M7 | M5 shipped the toolbar with Select / Lightning / Whisper, the brain settings panel, the budget meter and the dialogue panel. All other power commands already have sim handlers. |
| `game/assets/` is `.gdignore`d and loaded at runtime | M8 (export) | A1 loads GLB/PNG/JPG/TTF with `GltfDocument`/`Image`/`FontFile` from `res://` files (DECISIONS 2026-11-06). An exported build must ship them as non-resource files (export filter `assets/*`) or move them to imported resources. |
| Game herds (`ResourceView` kind `game`) are not drawn | next art pass or M8 | No CC0 animal model was chosen in A1; the views are emitted and skipped by `ResourceRenderer`. |
| Nova Roma extras not done: cartoon cumulus clouds, roads that follow the terrain, bridges, record stelae and shrine altars as own meshes | next art pass | A1 paints dirt yards and straight roads to the settlement centre and builds plinths/boats procedurally; the rest is open. |
| One-off hitches: decor rebuild when a building completes (~65k-cell scan), impostor atlas readback at startup, first night shader variant | M8 | Visible as a single low sample in the 5,000-NPC run; averages stay within budget. |
| Seed 20 at year 60 has ≈300 people, not the ≈1,000 spec 45 assumed | balance pass / spec 45 | Budgets were also measured at year 120 (see STATUS A1). |
| `FakeSimHost` stays under `src/Eden.Game/Dev/` | keep | Useful for perf runs; `--fake-sim --npcs=N --log-fps --screenshot=PATH --quit-after=S` are the CLI flags; A1 added `--year=N --hour=H --focus=X,Z|town --paused --zoom=M --gallery --lineup --hide=Node,...`. |

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

## Contracts (`docs/specs/contracts/`)

| Item | Belongs to | Note |
|---|---|---|
| Save-format version bump rules | M8 | `SaveWriter` sections are tagged and versioned; write the compatibility policy down when the first breaking change happens. |
