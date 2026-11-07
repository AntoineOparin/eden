# 50 — Knowledge data: `game/data/concepts.json`

**Milestone:** M4 (authored with the knowledge system), extended whenever the sim learns a new
primitive. Owned by whoever executes `10-sim-core.md` M4; this file is the authoring guide.

## Purpose

The concept graph is the only way knowledge changes the world, for hand-authored concepts and
for LLM inventions alike. Keeping the vocabulary tiny is what makes inventions safe.

## Format

An array validated by `docs/specs/contracts/concepts.schema.json`. One concept:

```json
{
  "id": "pottery",
  "name": "Pottery",
  "description": "Shaping and firing clay into vessels.",
  "prerequisites": ["fire"],
  "difficulty": 0.35,
  "effects": [
    { "type": "unlockTool", "tool": "clay-pot" },
    { "type": "unlockBuilding", "building": "kiln" }
  ],
  "tags": ["craft"]
}
```

Shipped with M4: 95 concepts in `game/data/concepts.json` (survival 12, food 14, craft 14,
building 10, social 15, belief 11, metal 9, writing 10). `ConceptGraph.Load()` parses the four data
files once per process (they are embedded into Eden.Sim at build time, `JsonDocument`, no reflection)
and every world starts from that graph.

### Tags: category first, then honing actions

`tags[0]` is the category (one of the eight above; inventions get `invention`). Any further tag that
is an action name (`common.schema.json#/$defs/action`) means "performing that action hones this
concept". Effects add honing actions too: `yieldMod` on berry/tree/stone/ore/fiber/herbs/salt/clay/fish
→ gather, on game/meat/hide → hunt, on grain → farm, on cloth/metals → craft; `unlockTool` → craft;
`unlockBuilding` → build; `unlockAction x` → x; `belief` → pray and talk. A concept with none of these
is honed by talk. Every concept must be honed by at least one action M1/M4 actually runs (gather, hunt,
rest, talk, pray, craft, teach, learn, eat, drink, give, court, explore): build, farm, trade, raid and
settle only run once M3 lands, so tag farm/build concepts with gather or craft as well
(`ConceptGraphTests` checks this).

### The other data files

| File | Entry | Notes |
|---|---|---|
| `resources.json` | `{ id, name, node, description }` | `node: true` for the five M1 `ResourceKind`s (`tree`, `stone`, `ore`, `berry`, `game`); the rest are derived goods (grain, meat, fish, clay, hide, fiber, herbs, salt, cloth, copper, bronze, iron) that only M3 systems produce. |
| `buildings.json` | `{ id, name, description }` | Exactly M3's kinds: hut, storehouse, farm-plot, well, kiln, workshop, shrine, archive, wall, dock. |
| `tools.json` | `{ id, name, description, materials, resource?, factor? }` | ≤ 64 tools. `materials` are node kinds; crafting takes one unit from the nearest node of the first one. Owning the tool multiplies yields of `resource` by `factor`. Tools break (3 % per day). |

## The five effect primitives

| `type` | Fields | Sim meaning |
|---|---|---|
| `yieldMod` | `resource`, `factor` 0.5–3 | multiply yield of that resource for actions by knowers, scaled by proficiency |
| `unlockBuilding` | `building` | building kind becomes constructible by knowers |
| `unlockTool` | `tool` | tool item becomes craftable; tools gate some actions |
| `unlockAction` | `action` (from `common.schema.json#/$defs/action`) | action enters the NPC's utility candidates |
| `belief` | `text` | knowers hold this belief (importance 6 memory); used by religion emergence |

How `EffectApplier` applies them: `yieldMod` multiplies by `1 + (factor − 1) × proficiency` for every
known concept (product over concepts, times owned tools); M1 gather and hunt use it, M3 calls
`EffectApplier.YieldFactor(world, slot, "grain")` and friends. The other four apply once, when an NPC's
proficiency first reaches `SimConfig.UnlockProficiency` (0.3): `unlockBuilding` calls
`World.BuildingUnlocks.Unlock(npcId, building)` (an `IBuildingUnlocks`, no-op until M3 plugs its
registry in; `World.CanBuild(slot, id)` answers the same question on demand), `unlockTool` makes the
tool craftable by the Craft action, `unlockAction` adds the action to the NPC's candidate set, `belief`
writes the memory. Knowledge-gated actions (build, farm, craft, teach, learn, trade, raid, settle,
invent) score 0 for an NPC that has not unlocked them (`SimConfig.KnowledgeGatesActions`), matching what
`IntentionValidator` already refuses.

## How knowledge moves (Eden.Sim, `World.Knowledge.cs`)

Proficiency is 0..1 per concept per NPC (`Dictionary<string, float>`, shown in `NpcDetail.Knowledge`).

- **Honing:** every completed action hones the concepts it practises, `Δ = 0.01 × (1 − p)²`.
  Explaining something (chatter, a lesson) hones the speaker too.
- **Chatter (slow):** each Talk passes one concept the speaker knows (≥ 0.3) to the listener,
  closing 4 % of the gap.
- **Teaching (fast, trust-gated):** an NPC with Teach unlocked (`teaching`, `apprenticeship`,
  `literacy`) picks the nearest kin or trusted (trust ≥ 0.05) neighbour and a concept it knows at
  ≥ 0.5; a lesson closes `0.25 × (0.5 + curiosity)` of the gap. Learn is the same from the pupil's side.
- **Apprenticeship:** once a day a child within earshot of a parent closes 4 % (× (0.5 + curiosity),
  doubled if the parent knows `apprenticeship`) of the gap to 90 % of the parent's level.
- **First lessons are remembered:** learning a concept from someone writes a conversation memory with
  both as subjects ("Nolaa told me bronze casting."). That memory is the recorded contact: a concept
  never reaches a group except through one.
- **Written records:** an NPC with `writing` ≥ 0.5 founds a record site where it stands (unless one is
  within 64 m) and writes two concepts a day it knows at ≥ 0.6, at 90 % of its level. Literate NPCs
  (`writing` ≥ 0.3, or `pictographs` ≥ 0.6) within 16 m read two entries a day. M3 binds sites to
  archive buildings with `World.AddRecordSite(x, z, buildingId)` / `DestroyRecordSite(id)`.
- **Discovery:** every 60 ticks (staggered) an adult rolls for the concepts it does not know whose
  prerequisites it has all at ≥ 0.6: chance `curiosity × Σ w × 1e-4 × 60` with
  `w = max(0.02, 1 − difficulty)^DiscoveryDifficultyExponent` (3 since K1), the pick weighted by `w`. The Invent action rolls ×30. First discoveries, and rediscoveries of lost
  concepts, write `discovery` Chronicle entries; a discoverer starts at 0.3.
- **Loss:** `ConceptGraph.KnownAnywhere(id)` is true while a living NPC knows the concept or a record
  holds it. When the last knower of an unwritten concept dies, a `knowledge-lost` Chronicle entry is
  written and the concept is gone until someone rediscovers it.
- **Forgetting (K1):** every concept an NPC holds carries the tick it was last practised (honed by an
  action, explained in chatter or a lesson, or raised by learning or reading). Once a day, a concept
  not practised for `ForgetGraceYears` (2) decays `Δp = −ForgetRatePerYear / DaysPerYear × (p − floor)`
  (0.05 a year). `floor` is the written level of that concept in the nearest record within
  `ForgetRecordRadius` (64 m), for literate NPCs only; 0 otherwise. Effects switch off when p falls
  under `UnlockProficiency`; under `ForgetDropBelow` (0.05) the concept is forgotten (p = 0, eligible
  for rediscovery), and if nobody else knows it and nothing holds it in writing, a `knowledge-lost`
  entry is written. Last-practised ticks are saved in KNW2 (an additive, versioned tail block; older
  saves start every holder's grace at load).

Every tunable is in `SimConfig` (M4 block, K1 additions at its end). Since K1 (seeds 1..20, headless):
the median seed knows ~56 concepts by year 40, writing in year ~190 (123–350) and bronze casting in year
~385 (204–479; 7 of 18 living seeds not by year 500). `dotnet run -c Release --project
game/tools/Eden.Sim.Runner -- --seeds 1..20 --years 500 --pace` prints the sweep, `--seed 1 --years 200
--knowledge` one seed's decade-by-decade timeline.

Anything else is rejected by schema and by `IntentionValidator`. New primitives require a
change to `concepts.schema.json`, `ConceptEffectKind` in `Intention.cs`, `EffectApplier` in
Eden.Sim, and the Worker prompt, in one PR.

## Authoring rules

- `id` kebab-case, stable forever (saves reference it). `name` ≤ 48 chars, `description` one
  or two sentences in plain words an NPC could say.
- `difficulty` is the discovery hardness: 0.1 fire, 0.3 pottery, 0.5 irrigation, 0.9 writing,
  0.93–0.97 for the metal steps. Discovery chance per tick ≈ `curiosity × (1 − difficulty)³ × 1e-4`
  once all prerequisites are ≥ 0.6, so 0.3 → 0.34, 0.6 → 0.064, 0.9 → 0.001, 0.94 → 0.0002: each
  0.01 near the top is worth decades once a population has boomed (see DECISIONS, K1).
- ≤ 6 prerequisites, no cycles (the loader rejects them). Keep the early game wide: nothing within
  two steps of the starters needs more than two prerequisites, so a 2-person start reaches something
  new within a couple of generations. Late-era concepts (bronze, iron, smithing, writing, law-codes,
  astronomy and their neighbours) take 3 to 5 prerequisites and sit at least five steps deep
  (`ConceptGraphTests.Late_eras_sit_behind_deep_prerequisite_chains_and_the_early_game_stays_wide`).
- Resource, building and tool ids must exist in `game/data/{resources,buildings,tools}.json`
  (loader cross-checks). Add them in the same PR.
- ~80 concepts at M4 across survival, food, craft, building, social, belief, metal, writing.
  Starter set every save begins with: none; the first NPCs know `foraging` and `fire` at 0.5.
  `foraging` and `fire` are the only concepts without prerequisites, and they unlock no gated
  action. God-spawned strangers know them at 0.5 too.
- Belief text ≤ 140 chars (the invention limit), so data and inventions read alike.

## Inventions from the LLM

`ThinkResponse.invention` is a single concept in this exact format. The sim additionally
requires: prerequisites all known by the inventor at ≥ 0.8, id not already present, at most
3 effects, `factor` ≤ 1.5 for inventions, and `belief` text ≤ 140 chars. Accepted inventions
are appended to the save, not to `concepts.json`. M4 also requires every resource, building and tool id
an invention names to exist in the data files (an invented tool has no recipe otherwise), and
`ActionKind` stays the fixed vocabulary. Accepted inventions join the live graph (`ConceptGraph.AddInvention`),
write an `invention` Chronicle entry, start the inventor at 0.3 and spread, hone and die like any concept.
The save's `KNW2` section keeps the graph order and which concepts are inventions.

## Acceptance tests

- `concepts.json` validates against the schema: `node scripts/validate-contracts.mjs` checks it
  (and cross-checks prerequisite, resource, building and tool ids) whenever the file exists.
- `ConceptGraphTests`: no cycles, all referenced ids exist, every concept reachable from the
  starter set (plus: starters are the only roots, every concept can be honed, eight categories).
- `TransmissionTests`, `KnowledgeTests`, `InventionTests` (Eden.Sim.Tests) cover the sim side.

## Carried-over items

See `90-backlog.md`; the rows naming your milestone are part of your scope. Planned evolution of
this system (forgetting, pace, graph depth, new primitives, modding, UI) is in `51-knowledge-roadmap.md`.

## Do not touch

- The schema or `ConceptEffectKind` without the one-PR rule above.
