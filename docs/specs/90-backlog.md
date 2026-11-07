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

## Eden.Game (from M2, `40-game-client.md`)

| Item | Belongs to | Note |
|---|---|---|
| Trees, stone, berries not drawn | M3 render pass or M4 | `IWorldSnapshot` has no resource-node field. Add a `ResourceView` list to the contract (PR note required); Eden.Sim already has `ResourceNodes` internally. |
| Buildings not drawn | M3 | `BuildingView` exists in the contract; the sim does not emit buildings until M3. |
| Hi-LOD NPC is a code-built capsule, no skinned glTF / `AnimationTree` | M6 | Kenney/Quaternius CC0 packs as planned. Pool and slot logic are done. |
| No per-chunk `NavigationRegion3D` baking | M6 | Only the possessed `CharacterBody3D` needs navigation; the sim never does. |
| Settlement panel, Chronicle timeline, the other god powers (spawn, rain/drought, terraform, bless/curse, drop item) | M7 | M5 shipped the toolbar with Select / Lightning / Whisper, the brain settings panel, the budget meter and the dialogue panel. All other power commands already have sim handlers. |
| `FakeSimHost` stays under `src/Eden.Game/Dev/` | keep | Useful for perf runs; `--fake-sim --npcs=N --log-fps --screenshot=PATH --quit-after=S` are the CLI flags. |

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
| `ResourceView` for trees/stone/berries in `IWorldSnapshot` | M3 render pass | See Eden.Game above. |
| Save-format version bump rules | M8 | `SaveWriter` sections are tagged and versioned; write the compatibility policy down when the first breaking change happens. |
