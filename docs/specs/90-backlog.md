# 90 — Carried-over work and open decisions

Items that an earlier milestone left out on purpose, decided differently from the plan, or
flagged as a question. Each line says where it belongs. Whoever executes that milestone owns the
item; remove it from here in the same PR that resolves it. Last updated 2026-11-06 after the
M3 / M4 merge.

## Eden.Sim (from M1, `10-sim-core.md`)

| Item | Belongs to | Note |
|---|---|---|
| Animals are resource herds (`ResourceNodes` kind `game`), not agents | M3 or M8 | Plan wanted animals as simple agents; herds were enough for M1 food. Revisit when predators or herding matter. |
| Re-run the M4 "concept reaches a second settlement only after contact" test on real M3 settlements | M5 or next Sim pass | M4 proved it with two separated groups; the wiring (`World.Integration.cs`: BuildingKinds as `IBuildingUnlocks`, archives host records, farm yields use knowledge) landed at the M3/M4 merge. |
| yieldMod on derived goods other than grain (fish, clay, hide, cloth, metals) has no producer yet | next Sim pass | Farming already multiplies grain by `World.YieldFactor`. Crafting a tool uses one unit of its first material node only. |
| Truncated save body can leave a half-loaded world | M8 (save pass) | Garbage files are rejected; a good header with a cut-off body is not. Load into a scratch world and swap on success. |
| ~4 KB per tick of allocation from birth/death/gossip events | M8 | 5,000 NPCs tick in 2.5 ms today; events are the remaining allocator. |
| `TryGetNpcDetail` is on-request with a 32-NPC watch list | M5 if the Mind needs more | Contract comment documents it. The spotlight picker should request details only for the NPCs it is about to think for. |

## Eden.Game (from M2, `40-game-client.md`)

| Item | Belongs to | Note |
|---|---|---|
| Trees, stone, berries not drawn | M3 render pass or M4 | `IWorldSnapshot` has no resource-node field. Add a `ResourceView` list to the contract (PR note required); Eden.Sim already has `ResourceNodes` internally. |
| Buildings not drawn | M3 | `BuildingView` exists in the contract; the sim does not emit buildings until M3. |
| Hi-LOD NPC is a code-built capsule, no skinned glTF / `AnimationTree` | M6 | Kenney/Quaternius CC0 packs as planned. Pool and slot logic are done. |
| No per-chunk `NavigationRegion3D` baking | M6 | Only the possessed `CharacterBody3D` needs navigation; the sim never does. |
| God powers toolbar, settlement panel, Chronicle timeline, brain settings, budget meter | M5 / M7 | Not part of M2. HUD, time controls and inspector exist. |
| `FakeSimHost` stays under `src/Eden.Game/Dev/` | keep | Useful for perf runs; `--fake-sim --npcs=N --log-fps --screenshot=PATH --quit-after=S` are the CLI flags. |

## brain/ (from the Worker agent, `30-brain-worker.md`)

| Item | Belongs to | Note |
|---|---|---|
| Anthropic and OpenAI-compatible adapters never called against a real API | M5 | Built from the live docs with mocked-fetch tests. First real run happens when a key is pasted in the settings panel; expect small fixes. |
| OpenAI-compatible request shape is a model-name heuristic (`max_completion_tokens` + `reasoning_effort` for o-series/GPT-5, else `max_tokens` + `temperature`) | M5 | Some compatible servers may reject fields; make the switch a setting if it bites. |
| `X-Eden-Usd-Cost` header (Anthropic/OpenAI only) | M5 | Added beyond the spec's header list, approved to stay. `WorkerProxyProvider` should surface it next to Neurons in the budget meter. |
| qwen3 deprecation 2026-11-20 | none | Default is Gemma 4; qwen3 remains a priced alternative. Drop the row after the date. |
| Deploy | M5, ask first | `npx wrangler deploy` has never been run. |

## Contracts (`docs/specs/contracts/`)

| Item | Belongs to | Note |
|---|---|---|
| `ResourceView` for trees/stone/berries in `IWorldSnapshot` | M3 render pass | See Eden.Game above. |
| `SetThinkingCommand` so the Mind can flag `NpcView.IsThinking` | M5 | Mentioned in `20-mind.md`, not yet in `ISimHost.cs`. |
| Save-format version bump rules | M8 | `SaveWriter` sections are tagged and versioned; write the compatibility policy down when the first breaking change happens. |
