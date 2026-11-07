# 10 — Eden.Sim: simulation core

**Milestones:** M1 (headless genesis), then M3 (society) and M4 (knowledge) as separate
worktrees inside this module. This spec covers everything in `game/src/Eden.Sim/`.

## Purpose

A deterministic, fixed-step, headless simulation of an island and its people. Starts from two
NPCs. Runs on its own thread; the rest of the game only sees `IWorldSnapshot` and talks back
through `ISimHost` commands and `IIntentionSink` intentions. Zero Godot references.

## Public interface

Implements, exactly as written in `docs/specs/contracts/`:

- `ISimHost` (`SimHost`), `IWorldSnapshot` (`WorldSnapshot`), `IIntentionSink` and
  `IIntentionValidator` (`IntentionQueue`, `IntentionValidator`).
- Executes every `WorldCommand` and every `Intention` subtype from `Intention.cs`.
- Loads `game/data/concepts.json` validated against `concepts.schema.json`.

Also exposes, for the console runner and tests only: `World` (construct, `Tick()`,
`Snapshot()`, `StateHash()`), `SimConfig`, `Rng`.

## Fixed numbers (change only via `SimConfig`)

| Constant | Default | Note |
|---|---|---|
| Ticks per in-game hour | 10 | sim thread runs 20 ticks/s at 1× |
| Hours per day / days per year | 24 / 10 | 1 year ≈ 2 real minutes at 1× |
| Max time scale | 500× | `TimeScale` enum in `ISimHost.cs` |
| World | 256×256 cells, 2 m cells, chunk 32 | island with noise + radial falloff |
| Adult age / max lifespan | 14 / 70 years | genes shift lifespan ±15 |
| Per-NPC memory cap | 500 | eviction by low importance × age, folded into a summary line |
| Perception radius | 24 m | witness radius for memories |

## Design

**Storage.** Struct-of-arrays in `Npcs` (positions, needs, genes, state) indexed by dense id
with a free list; `Dictionary`s only for sparse data (relationships, knowledge, inventory).
No allocations in `Tick()` on the hot path. 5,000 NPCs must tick in under 5 ms on a laptop.

**World gen.** `WorldGen.Generate(SimConfig)` → heightmap (fBm simplex, radial falloff so the
border is sea), biomes from height + moisture, resource nodes (tree, stone, ore, berry, game
animal spawn), fertility map. Deterministic from `Seed`.

**Entities.** NPC: 12 genes in `[0,1]` (strength, stamina, curiosity, sociability, aggression,
fertility, lifespan, empathy, patience, ambition, piety, creativity); traits = genes blended
with upbringing; needs (food, water, rest, warmth, safety, social, purpose) decay per tick;
skills; inventory; relationships `(affinity, trust, debt)` per pair; settlement; home; job.
Animals and plants are simpler agents with the same storage pattern.

**Decision layer.** Utility AI: `ActionScorer` scores every `ActionKind` candidate from needs ×
personality × context; best wins with hysteresis; runs as a small state machine
(`move → act → done`). Movement in M1 is straight-line on the grid with a cost field; Godot's
NavigationServer3D is used only for rendering-side agents (M2+), never for sim truth.

**Intentions.** `IntentionQueue` (lock-free MPSC) drains at the start of each tick.
`IntentionValidator.Reject` checks: NPC alive, action unlocked for that NPC, target exists /
is in range, belief importance in range, invention passes `concepts.schema.json` rules and its
prerequisites are mastered. A valid `GoalIntention` becomes a scored candidate with a bonus
(`Priority`), not a forced action; `SayIntention` writes a memory for NPCs in earshot;
`BeliefIntention` writes an importance-N memory of kind `belief`; `InventionIntention` inserts
the concept with proficiency 0.3 for the inventor.

**Life cycle.** Ageing by ticks; courtship when `social` need + affinity cross thresholds;
pairing → pregnancy (0.75 year) → child with uniform crossover + 3% mutation per gene; death
by age (probability curve past lifespan), starvation, violence, disease (M3+). Death emits a
Chronicle entry and memories for witnesses.

**Memory stream.** `Memory { Tick, Kind, Subjects[], Text, Importance, Embedding? }` recorded
for every witnessed event (birth, death, gift, fight, god action, discovery, conversation,
speech). God actions are importance 8–10 for everyone in range. Gossip during `Talk`: memories
of importance ≥ 6 copy to the listener with a distortion roll (subject swap or importance ±1).
Eviction keeps the cap and appends to `NpcMemorySummary`. Embeddings are set by Eden.Mind, the
sim only stores them.

**Society (M3).** Households → settlements (DBSCAN-style clustering of homes every in-game
day) → factions. Jobs assigned from settlement deficits. Storage buildings hold shared stock.
Leadership = highest weighted degree in the relationship graph of a settlement. Trade when
surplus/deficit pairs exist within travel range; raids when grievance + aggression + food
pressure exceed a threshold.

**Knowledge (M4).** `ConceptGraph` from `concepts.json`; per NPC `Knowledge: Dictionary<ConceptId, float>`;
transmission by proximity chatter (slow), teaching (fast, trust-gated), apprenticeship
(child from parents), written records (buildings with `records` once `writing` is known);
honing with diminishing returns; discovery roll per tick when all prerequisites ≥ 0.6,
weighted by curiosity. Effects applied through `EffectApplier` using only the five primitives.

**Myth (M7, as built 2026-11-06).** `World.Myth.cs`, `GodStanding.cs`, `Religion.cs`. Per settlement the god
has `Favour` (−1..1) and `Awe` (0..1), moved by god actions through hooks in the command handlers
(bless/curse ±0.12 of the gap, lightning/drought Awe + and a Favour loss that lands 3 days later unless rain
or a blessing comes first, spawn/terraform/gift/whisper Awe +) and decaying daily (×0.995 / ×0.99). Two
settlements are in contact for 30 days after a cross-settlement talk, a trade or raid trip, or someone moving
between them; in contact, the less favoured side's `Envy` (gap × (1 − their Awe)) and `Fear` (gap × Awe) rise,
and Envy adds grievance. Gossip during `Talk` copies a memory of importance ≥ 6 with one roll: < 0.15 subject
swap (also in the text), < 0.35 importance ±1, < 0.35 + 0.3 × the teller's Awe a death, raid or discovery is
credited to the god; gossip copies are retold up to 6 hops and carry `Memory.Generation` (hops) and
`Memory.Distortion`. Adults who remember the god form a belief (rule-based, no LLM) with daily chance
0.08 × (0.5 + piety) × min(1, Awe + |Favour|); when ≥ 40 % of a settlement's adults hold a belief that mentions
the god (concept beliefs and Mind beliefs count), its faction founds a `Religion` (or takes up the faith of a
settlement it is in contact with), unlocks a shrine and builds one once established (≥ 20 people, a storehouse, not hungry); Pray scores ×1.6 near one's shrine and renews the
belief; faiths spread to factions in contact (3 %/day) and split when the capital's Favour opposes the stance
for 14 years. Saved in the `MYTH` section, hashed. Myth rolls use their own seeded stream.

**Chronicle and events.** `EventBus` (in-sim, synchronous) feeds the Chronicle list that the
snapshot exposes as `NewChronicle`, the memory recorder, and the settlement logic.

**Save/load.** `SaveWriter`/`SaveReader`: JSON header `{ version, seed, tick, population, conceptSet }` +
binary body (little-endian, versioned sections). `SaveCommand`/`LoadCommand` run on the sim
thread. Round-trip must preserve `StateHash()`. A load is all or nothing (M8): the body is first read into a
scratch world; only a body that loaded cleanly there is read into the live world, so a truncated or damaged file
throws `InvalidDataException` (the commands log "load failed: …") and leaves the world as it was.

**Save format and versions (M8).** Layout: `"EDEN"`, int32 header length, UTF-8 JSON header, then sections until
the end of the file. Format version 2 frames a section as tag (4 ASCII bytes), section version (int16), flags
(byte; bit 0 = Brotli), stored length (int32), and, when compressed, the raw length (int32) before the bytes.
Format version 1 had no flags byte. Sections of 4 KB or more are Brotli-compressed (quality 1) when that saves
space. `MEMS` is at section version 2: a table of distinct memories (one event remembered by forty witnesses is
written once) and per-NPC streams of varint table indices; the load shares one `Memory` object per entry again.
Every other section is still at version 1. `conceptSet` is `KnowledgeData.SetHash` of the data the world was made
with (a load logs when it differs from the data now in use).

Version policy:
- `SaveWriter.FormatVersion` changes only when the framing changes (how sections are found, sized or decoded).
  A reader accepts every format from `SaveWriter.OldestReadable` (1) to its own and refuses a newer one with
  "save version N is newer than this build".
- A section's own version changes when the layout inside that section changes. Its reader must accept every older
  version it has shipped (keep the old read path, as `MemoryStore.ReadVersion1` does) and refuse a newer one.
- New state goes in a new section where it can: readers ignore tags they do not know, so older builds still load
  the file (they drop that state). Additive tail blocks inside a section carry their own version (the K1 block in
  `KNW2`). A section that older saves lack must have a default (`SOCI`, `KNW2`, `MYTH` are optional; `CONF`,
  `CLCK`, `TERR`, `RSRC`, `NPCS`, `MEMS`, `RELS`, `KNOW`, `MISC` are required).
- Every length is checked against what is left in the file before anything is read, and every count inside
  a section is bounded, so a cut-off or corrupt file fails with a message instead of a half-loaded world.
- A format change keeps a fixture of the previous format under `game/tests/Eden.Sim.Tests/Fixtures/` and a test
  that loads it (`SaveFormatTests`, `save-v1-seed1-year1.eden`).

**Data override (M8, roadmap item 5).** `SimConfig.DataOverrideDir` names a folder whose `concepts.json`,
`resources.json`, `buildings.json` and `tools.json` replace the shipped ones (missing files fall back to the shipped
copy). `KnowledgeData.FromDirectory` parses them with the same parser and graph checks as the shipped data and throws
`InvalidDataException("data override <dir>: …")` on any problem. Eden.Game passes `user://data` when it exists
(`Dev/DataOverride.cs`; a bad set is reported and the shipped data is used).

**Building sites (M8).** `TryPlaceNear` and `PickHomeSite` evaluate all their random candidates and take the
flattest acceptable one (`Terrain.SlopeUnder`: height range over the footprint ÷ its diameter); candidates steeper
than `MaxBuildSlope` (0.6) are skipped, and huts are not built on home spots steeper than that. A finished building
(not a dock) pulls the land under its footprint halfway to the footprint's mean height (`FoundationFlatten` 0.5,
fading over one cell outside it; land never drops to the sea, so regions and the water field are unchanged). The
renderer's copy of the heightmap is refreshed once per in-game year after such changes.

**Threading.** `SimHost.Start()` spawns one thread: fixed step accumulator, honours
`TimeScale`, publishes an immutable `WorldSnapshot` after each batch (double buffer, `Volatile`
swap). Commands and intentions are queued; nothing else crosses the boundary.

## Console runner

`game/tools/Eden.Sim.Runner/` (a console project in `Eden.sln`): `dotnet run -- --seed 7 --years 60`
prints population per year and the final Chronicle tail. Used for the M1 acceptance check.

## Acceptance tests (xUnit, `game/tests/Eden.Sim.Tests/`)

M1:
- `Determinism`: two worlds, same seed and command stream, 10,000 ticks → equal `StateHash()`.
- `Genetics`: child genes are within parents' range except mutations; mutation rate ≈ 3%.
- `Needs`: an NPC with no food dies within the configured starvation window; eating resets food.
- `UtilityScoring`: hungry NPC scores `Eat`/`Gather` above `Idle`; fearful NPC scores `Flee` above `Court`.
- `Population`: from 2 NPCs, across seeds 1..20, ≥ 70 % reach 50+ population within 10 generations
  and ≥ 1 seed dies out (kept as a ratio so a balance tweak does not break the suite).
- `Contracts` (already present): enum parity with the JSON schemas, fixture round-trips.
- `SaveRoundTrip`: save → load → identical hash.

M3: two settlements emerge from a 2-person start within 20 generations on ≥ 50 % of seeds; at
least one trade and one raid Chronicle entry.

M4: a concept reaches a second settlement only after a recorded contact; removing all knowers of
a concept with no written record removes it from the world (`ConceptGraph.KnownAnywhere`).

M4 on real settlements (M8): `TransmissionTests.On_real_settlements_a_concept_crosses_only_through_a_recorded_contact`
founds two M3 villages 120–200 m apart and checks, every tick, that each villager of B who knows the concept has a
lesson memory naming someone who already knew it, and that the first one learned it from someone in A.

Everything in this project runs in under 1 s for the whole suite except the population, society and pacing
acceptance tests, which are tagged `[Trait("Category", "Slow")]` (see STATUS for their wall times).

## Carried-over items

See `90-backlog.md`; the rows naming your milestone are part of your scope.

## Do not touch

- `game/src/Eden.Game/**`, `game/src/Eden.Mind/**`, `game/scenes/**`, `brain/**`.
- `docs/specs/contracts/**` without a PR note.
- Never reference `Godot`, `System.Random`, `DateTime.Now`, `Task.Run` inside the tick.
