# 90 — Carried-over work and open decisions

Items that an earlier milestone left out on purpose, decided differently from the plan, or
flagged as a question. Each line says where it belongs. Whoever executes that milestone owns the
item; remove it from here in the same PR that resolves it. Last updated 2026-11-06 by M6
(possession).

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
| Re-check pace with live LLM inventions (K1 assumed ≈ 10 % extra discovery pressure) | when a Mind runs headless or in a long play session | K1 tuned headless: median writing ≈ 190, bronze ≈ 385. Re-run `--seeds 1..20 --years 500 --pace` with inventions on. |
| Bronze era is bimodal and 7/18 seeds have no bronze by year 500 | next balance pass or M8 | Copper → metalworking → bronze are three hard steps (0.94/0.94/0.93) after a farming boom; consider fewer, softer steps plus a population-aware discovery term. `PacingTests.Median_seed_writes_and_casts_bronze_inside_the_bands` (EDEN_PACE_FULL=1) checks the band. |
| Full pacing test is opt-in (`EDEN_PACE_FULL=1`), not in check.sh | M8 (scale/perf) | 20 seeds × up to 500 years is ~40 min of simulation; check.sh runs only the 40-year early-game half. |
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
| Buildings, trees and other people do not block the possessed body | next possession pass | M6 resolved the nav-baking row without navigation: the sim decides where bodies walk and `TerrainCollision` adds a trimesh for the 3×3 chunks around the possessed body only (DECISIONS 2026-11-06). Add building/tree shapes on layer 10 near the player if walking through houses bothers. |
| `FakeSimHost` ignores possession commands | keep unless needed | The director falls back to god view after 3 s ("The world did not let you in"); possession needs the real sim. |
| More possessed verbs (trade, court, eat on demand) | when wanted | M6 kept the contract's talk/give/work; the body eats and drinks on its own when desperate. |
| Settlement panel, Chronicle timeline, the other god powers (spawn, rain/drought, terraform, bless/curse, drop item) | M7 UI pass (after the A1 art pass merges) | M5 shipped the toolbar with Select / Lightning / Whisper, the brain settings panel, the budget meter and the dialogue panel. All other power commands already have sim handlers, and since M7 every one of them moves the god's standing (`World.Myth.cs`). The settlement panel should show Favour/Awe/stance, the faith, Envy/Fear and the neighbour they point at: needs a contract field (see Contracts). |
| Religion overlay and Chronicle filters for the M7 kinds (`religion`, `schism`, `shrine`, `prayer`) | M7 UI pass (after A1) | Sim side is done: faiths per faction, shrines are `BuildingView` kind `shrine`, Chronicle entries exist. The overlay needs the faction → faith map (contract field below). |
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

## Myth (from M7 sim side, `World.Myth.cs`)

| Item | Belongs to | Note |
|---|---|---|
| Prayer is rare until villages are large (seed 20: first prayer at year 66) | balance pass | M1's Pray score (piety × (0.45 dPurpose + 0.3 fear)) seldom wins; M7 only multiplies it near a shrine (×1.6, + 0.1 × piety for believers). Raise the base if faiths should feel more present. |
| Faith is per faction, not per person | later, if schisms need to split villages | A settlement's adults all follow their faction's faith; "faithful" is only the 40 % founding count. |
| "Bad harvests" have no event to attribute | when crop failure becomes an event | Attribution covers deaths (starvation included), raids, discoveries and retold stories. |
| M3 Slow test `Two_settlements_trade_and_raid…` has no year cap below 600 | next Sim pass (owner of `SocietyTests.cs`) | Each seed runs until its first raid or 20 generations. Since M7 (and any trajectory change) seed 1 raids first at year 131, pop 1,921, so the test takes ≈ 20 min instead of ≈ 1 min. Cap the horizon (e.g. 120 years) or count "raid by year X" as the ratio. |
| Manual M7 check with the Mind on (reflections mention the god favourably, a neighbour's mention envy or fear) | coordinator after merge | The prompt line and reflection request are unit-tested (`GodStandingPromptTests`); not run against a live provider. |

## Contracts (`docs/specs/contracts/`)

| Item | Belongs to | Note |
|---|---|---|
| Settlement / god-standing view in `IWorldSnapshot` for the M7 UI | M7 UI pass | Proposed: `SettlementView(Id, Name, X, Z, Members, FactionId, LeaderId, Favour, Awe, Envy, Fear, EnviedId, FearedId, Stance, ReligionName?)` and `ReadOnlyMemory<SettlementView> Settlements`. Today only `Eden.Sim.IMindSnapshot.GodStanding` (not a contract) carries it. Optionally `MemoryView.Generation` for tracing myths in the inspector (the brief kept the contract unchanged; generation is in `Eden.Sim.Memory`). |
| Save-format version bump rules | M8 | `SaveWriter` sections are tagged and versioned; write the compatibility policy down when the first breaking change happens. |
