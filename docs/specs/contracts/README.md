# Contracts

Written first, shared by every module. Change them only with a note in the PR, and run
`node scripts/validate-contracts.mjs` plus `dotnet test game/Eden.sln` afterwards.

| File | Consumed by | Purpose |
|---|---|---|
| `IWorldSnapshot.cs` | Eden.Sim (impl), Eden.Game (reads), Eden.Mind (reads) | Read-only world view Godot renders from |
| `ISimHost.cs` | Eden.Sim (impl), Eden.Game (owns), Eden.Mind (intentions) | Command side: god powers, time scale, possession, save/load, sim thread lifecycle |
| `Intention.cs` | Eden.Sim (executes), Eden.Mind (produces) | Fixed vocabulary of LLM-sourced goals, speech, beliefs, inventions |
| `IBrainProvider.cs` | Eden.Mind (impl + uses) | LLM backend interface and the DTOs for /think, /chat, /embed |
| `common.schema.json` | Worker (Zod mirror), C# tests | Shared `$defs`: action enum, npcContext, usage, error envelope |
| `think.schema.json` | Worker, Eden.Mind | `$defs/request`, `$defs/response`, `$defs/result` for `POST /think` |
| `chat.schema.json` | Worker, Eden.Mind | same for `POST /chat` |
| `embed.schema.json` | Worker, Eden.Mind | same for `POST /embed` |
| `concepts.schema.json` | Eden.Sim (loads data), Worker (invention output) | Concept graph and the five effect primitives |
| `fixtures/` | `scripts/validate-contracts.mjs`, `Eden.Sim.Tests/ContractTests.cs`, `brain/src/schemas.test.ts` | `<schema>.<def>.<ok|bad>[.n].json` examples every side must agree on |

The `.cs` files are linked as compile items into `game/src/Eden.Sim/Eden.Sim.csproj` and
`game/src/Eden.Mind/Eden.Mind.csproj`; there is no copy in the game tree.

Changes:

- 2026-11-06 (M5): `SetThinkingCommand(IReadOnlyList<int> NpcIds)` added to `ISimHost.cs`. Sent by
  Eden.Mind each in-game hour to replace the spotlight set behind `NpcView.IsThinking`. Display only:
  not part of the state hash or the save file.

- 2026-11-06 (A1, pre-approved): `ResourceView(Id, Kind, X, Y, Z, Amount, Variant)` and
  `ReadOnlyMemory<ResourceView> Resources` on `IWorldSnapshot.cs`, for drawing trees, stone, ore, berries, herds and
  farm-plot crops. `Kind` is a `game/data/resources.json` id (`tree`, `stone`, `ore`, `berry`, `game`, and `grain` for
  the crop on a finished farm plot); `Amount` is 0..1 of the node's maximum (crop growth for `grain`); `Variant` is a
  stable visual byte. Eden.Sim fills it in `World.Snapshot()` only (read-only, no RNG, no state hash change); crop ids
  are `World.CropViewIdBase` + building id. `FakeSimHost` emits static nodes. Display only, not saved.

- 2026-11-06 (M7 UI pass, pre-approved): `SettlementView(Id, Name, X, Z, Members, FactionId, LeaderId, Favour, Awe,
  Envy, Fear, EnviedId, FearedId, Stance, ReligionName?)`, a `GodStance` byte enum (Neutral / Favourable / Fearful /
  Hostile, same byte values as `Eden.Sim.GodStance`) and `ReadOnlyMemory<SettlementView> Settlements` on
  `IWorldSnapshot.cs`, for the settlement panel and the religion / god-standing overlay. Ids are -1 when absent; the
  envied/feared ids are -1 when the feeling is faint (≤ 0.05), as in the Mind's `GodStandingView`. `MemoryView` gains
  `int Generation = 0` (0 first-hand, n = heard through n retellings) for the inspector's myth marker; the default keeps
  existing constructors compiling. Eden.Sim fills both in `World.Snapshot()` / `TryBuildDetail` only (read-only, no
  RNG, no state hash or save change); `FakeSimHost` emits six fake settlements. Note: code that imports both
  `Eden.Sim` and `Eden.Sim.Contracts` outside the `Eden.Sim` namespace must qualify `GodStance` (two enums share it).

- 2026-11-07 (C1 contract pass, pre-approved): settlements and the Chronicle get real data instead of client estimates.

  | Addition | Where | Meaning |
  |---|---|---|
  | `SettlementView.Food` (float, default 0) | `IWorldSnapshot.cs` | Food the settlement holds now, in the sim's food units (1 unit restores `SimConfig.FoodPerUnit` = 0.35 of a person's food need; an adult eats ≈ 1.6 a day): shared stock (pile + storehouses) plus what living members carry. Households keep no larder. |
  | `SettlementView.TerritoryRadius` (float, default 0) | `IWorldSnapshot.cs` | Metres; the sim's own land circle, `SettlementRadius × TerritoryFactor` (what trespass and grievance use). 0 only from snapshots that predate C1. |
  | `SettlementDetail(Id, Name, Members, Adults, Concepts, Buildings, Food, FoodPerDay)` | `IWorldSnapshot.cs` | On-request detail. `Adults` counts adults and elders. `Concepts`: every concept a member knows (proficiency > 0), most holders first. `Buildings`: finished buildings by kind, most first. `FoodPerDay`: what the members eat a day at the sim's rates (need decay / food per unit; children 0.85, infants 0.8; awake all day, so an upper bound). |
  | `SettlementConceptView(ConceptId, Holders, BestProficiency, HasRecord)` | `IWorldSnapshot.cs` | `HasRecord`: a written record holding the concept lies inside the territory circle or in one of the settlement's buildings. |
  | `BuildingCount(Kind, Count)` | `IWorldSnapshot.cs` | `Kind` is a `game/data/buildings.json` id. |
  | `bool TryGetSettlementDetail(int id, out SettlementDetail)` | `IWorldSnapshot` | Same "request now, present on a later snapshot" semantics as `TryGetNpcDetail`. |
  | `IReadOnlyList<ChronicleEntry> ChronicleSince(long tick)` | `IWorldSnapshot` | Every entry the sim still holds with `Tick > tick`, oldest first. The sim keeps 2,048 and saves them (`CHRN` section). `NewChronicle` stays for compatibility. |
  | `WatchSettlementCommand(int Id)` | `ISimHost.cs` | Asks the sim to build that settlement's detail from the next snapshot on. Display only. |

  **How a client gets a settlement detail.** Either send `WatchSettlementCommand(id)` through `ISimHost.Enqueue`
  (what `SettlementKnowledge` does when a request starts) or just call `TryGetSettlementDetail(id, …)` on the
  frame's snapshot: a miss for a living settlement asks the sim to watch it, exactly like a missed `TryGetNpcDetail`.
  Snapshots from the next published batch on return the detail. The sim keeps a watch list of 8 settlements (least
  recently requested dropped first) and refreshes a watched detail at most once per in-game hour, one per snapshot (new requests are built at once); abandoned
  settlements drop out. While the sim is paused nothing is published, so a request waits. Keep at most a few requests
  in flight (the client uses 4) so the open panel's settlement is not evicted.

  **How a client reads the Chronicle.** Call `ChronicleSince(lastSeenTick − 1)` once per new snapshot and drop entries
  already seen (same tick and text): god commands run between ticks, so an entry can carry the tick of a snapshot that
  was already read. The first read (`ChronicleSince(long.MinValue)`) returns the whole history, including what a
  loaded save carried; a new `load` entry means the world was replaced and the history with it.

  All of it is read-only: no RNG draw, no state-hash change (tested over 10,000 ticks with and without requests); the
  Chronicle section is saved but not hashed. Eden.Sim fills it in `World.Snapshot()` (`World.SettlementDetail.cs`,
  `World.ChronicleSave.cs`); `FakeSimHost` fills every field with made-up but stable numbers.

Invariants enforced by tests:

- `BiomeId` (C#) is the meaning of every byte in `IWorldSnapshot.Biomes`; the grid layout
  (`z * Width + x`, origin at world 0,0, cell at `x * CellSize`) is documented on `WorldInfo`.
  Added 2026-11-06 after M2 had to assume both.
- `ActionKind` (C#) == `common.schema.json#/$defs/action` enum, camelCase.
- `ConceptEffectKind` (C#) == the `const` values of `concepts.schema.json#/$defs/effect`.
- Every `*.ok.json` fixture deserialises into the C# DTOs and parses with the Zod schemas;
  every `*.bad.json` is rejected by all three validators.
