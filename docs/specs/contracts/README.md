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

Invariants enforced by tests:

- `BiomeId` (C#) is the meaning of every byte in `IWorldSnapshot.Biomes`; the grid layout
  (`z * Width + x`, origin at world 0,0, cell at `x * CellSize`) is documented on `WorldInfo`.
  Added 2026-11-06 after M2 had to assume both.
- `ActionKind` (C#) == `common.schema.json#/$defs/action` enum, camelCase.
- `ConceptEffectKind` (C#) == the `const` values of `concepts.schema.json#/$defs/effect`.
- Every `*.ok.json` fixture deserialises into the C# DTOs and parses with the Zod schemas;
  every `*.bad.json` is rejected by all three validators.
