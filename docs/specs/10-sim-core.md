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

**Save/load.** `SaveWriter`/`SaveReader`: JSON header `{ version, seed, tick, population }` +
binary body (little-endian, versioned sections). `SaveCommand`/`LoadCommand` run on the sim
thread. Round-trip must preserve `StateHash()`.

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

Everything in this project runs in under 1 s for the whole suite except the population tests,
which are tagged `[Trait("Category", "Slow")]` and must stay under 20 s.

## Carried-over items

See `90-backlog.md`; the rows naming your milestone are part of your scope.

## Do not touch

- `game/src/Eden.Game/**`, `game/src/Eden.Mind/**`, `game/scenes/**`, `brain/**`.
- `docs/specs/contracts/**` without a PR note.
- Never reference `Godot`, `System.Random`, `DateTime.Now`, `Task.Run` inside the tick.
