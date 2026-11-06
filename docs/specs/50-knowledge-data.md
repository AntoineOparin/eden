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

## The five effect primitives

| `type` | Fields | Sim meaning |
|---|---|---|
| `yieldMod` | `resource`, `factor` 0.5–3 | multiply yield of that resource for actions by knowers, scaled by proficiency |
| `unlockBuilding` | `building` | building kind becomes constructible by knowers |
| `unlockTool` | `tool` | tool item becomes craftable; tools gate some actions |
| `unlockAction` | `action` (from `common.schema.json#/$defs/action`) | action enters the NPC's utility candidates |
| `belief` | `text` | knowers hold this belief (importance 6 memory); used by religion emergence |

Anything else is rejected by schema and by `IntentionValidator`. New primitives require a
change to `concepts.schema.json`, `ConceptEffectKind` in `Intention.cs`, `EffectApplier` in
Eden.Sim, and the Worker prompt, in one PR.

## Authoring rules

- `id` kebab-case, stable forever (saves reference it). `name` ≤ 48 chars, `description` one
  or two sentences in plain words an NPC could say.
- `difficulty` is the discovery hardness: 0.1 fire, 0.3 pottery, 0.5 irrigation, 0.7 bronze,
  0.9 writing. Discovery chance per tick ≈ `curiosity × (1 − difficulty) × 1e-4` once all
  prerequisites are ≥ 0.6.
- ≤ 6 prerequisites, no cycles (the loader rejects them). Keep the graph wide, not deep: a
  2-person start must reach something new within a couple of generations.
- Resource, building and tool ids must exist in `game/data/{resources,buildings,tools}.json`
  (loader cross-checks). Add them in the same PR.
- ~80 concepts at M4 across survival, food, craft, building, social, belief, metal, writing.
  Starter set every save begins with: none; the first NPCs know `foraging` and `fire` at 0.5.

## Inventions from the LLM

`ThinkResponse.invention` is a single concept in this exact format. The sim additionally
requires: prerequisites all known by the inventor at ≥ 0.8, id not already present, at most
3 effects, `factor` ≤ 1.5 for inventions, and `belief` text ≤ 140 chars. Accepted inventions
are appended to the save, not to `concepts.json`.

## Acceptance tests

- `concepts.json` validates against the schema (`scripts/validate-contracts.mjs` covers it
  once the file exists: add a fixture-style check or call the validator with the path).
- `ConceptGraphTests`: no cycles, all referenced ids exist, every concept reachable from the
  starter set.

## Carried-over items

See `90-backlog.md`; the rows naming your milestone are part of your scope.

## Do not touch

- The schema or `ConceptEffectKind` without the one-PR rule above.
