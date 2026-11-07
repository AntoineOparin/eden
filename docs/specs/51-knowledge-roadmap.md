# 51 — Knowledge system roadmap

Follow-ups to `50-knowledge-data.md` identified at the M3/M4 merge on 2026-11-06. None blocks
M5. Items 1 and 2 form one balance pass right after M5 (LLM inventions change the pace, so
tune once). Items 3 to 6 attach to the milestones named. Rows also appear in `90-backlog.md`;
delete both when done.

## 1. Forgetting (balance pass after M5) — landed in K1 (2026-11-06)

**Landed.** As specified, plus `ForgetDropBelow` (0.05: below it the concept is forgotten and can be
rediscovered) and `ForgetRecordRadius` (64 m; the floor applies to literate NPCs only). Runs in the
daily knowledge pass; last-practised ticks are saved in KNW2. Details in `50-knowledge-data.md`
("Forgetting") and DECISIONS "K1: forgetting details". Tests: `ForgettingTests`.

Today knowledge is lost only when every holder dies with no written record. Add slow
proficiency decay for concepts not practised: `Δp = −ForgetRatePerYear × (p − floor)` where
`floor` is the written level of the nearest readable record (0 if none). Practising,
teaching and reading already raise p. Effects switch off below `UnlockProficiency`, so a
village that stops farming for generations really does lose farming. Tunables:
`ForgetRatePerYear` (start 0.05), `ForgetGraceYears` (no decay for this long after last
practice, start 2). Tests: decay without practice, no decay with practice, record as floor,
determinism, save round-trip. Keep the M4 "dies with its holders" test passing.

## 2. Pace (balance pass after M5) — landed in K1 (2026-11-06)

**Landed.** Targets: median writing year 150–250, bronze 300–450, nothing before half of each lower
bound (DECISIONS "K1: era targets"). Tuned with a cubic discovery-difficulty curve
(`DiscoveryDifficultyExponent` 3) and harder late steps rather than the global rates (DECISIONS "K1:
pace tunables"). Runner: `--seeds 1..20 --years 500 --pace` prints `writing@`, `bronze@` and `known`;
`--set Name=value` overrides any SimConfig tunable. `PacingTests` (Slow): a 40-year early-game check
that check.sh runs, and the in-band check behind `EDEN_PACE_FULL=1`. Not done: tuning with live LLM
inventions (assumed ≈ 10 % extra pressure; backlog).

Seed 1 knows 69 of 95 concepts by year 40 and all 95 by year 130. Decide the target era
length first (suggestion: writing around year 150 to 250 on a median seed, metals after 300),
then tune `DiscoveryRate`, `ChatterRate`, `TeachRate`, `ApprenticeRatePerDay` and the
per-concept `difficulty` values, with LLM inventions enabled. Add a runner column
"writing@" and "bronze@" to the seed sweep so the pass is measurable. Do not change the
M4 acceptance tests; add a Slow pacing test with ranges once the targets are agreed.

## 3. Graph depth (M8 content pass, or with 2) — landed in K1 (2026-11-06)

**Landed.** 3–5 prerequisites for copper-smelting, metalworking, bronze-casting, iron-smelting,
smithing, metal-trade, writing, record-keeping, literacy, law-codes, scripture, mathematics,
astronomy and mapmaking; early game unchanged. `ConceptGraphTests` asserts the longest chain ≥ 5, the
named late concepts have 3–5 prerequisites, depth ≥ 5 and difficulty ≥ 0.7, nothing within two steps
of the starters needs more than two, and everything stays reachable.

No concept has more than two prerequisites, by design for the two-person start. Late eras
need bottlenecks: give bronze, iron, writing, law-codes, astronomy and smithing chains of 3 to
5 prerequisites and higher difficulty, keep the early game wide. `ConceptGraphTests` already
enforce no cycles and reachability; add a test that the longest prerequisite chain is ≥ 5.

## 4. New effect primitives (when first needed, one PR each)

Expected requests: `needMod` (concept, need, factor: clothing slows warmth loss, medicine
slows disease), `speedMod` (roads, boats, the wheel), `unlockRecipe` (crafting chains with
inputs and outputs). Each follows the rule in `50-knowledge-data.md`: `concepts.schema.json`,
`ConceptEffectKind`, `EffectApplier`, the Worker prompt and `schemas.ts` in one PR, plus
fixtures. Do not add a primitive speculatively; add it with the concept that needs it.

## 5. Data override and modding (M8 or later)

`game/data/*.json` is embedded in `Eden.Sim`. Add an optional override directory
(`user://data/`) read at world creation when present, validated with the same loader and
schema, with a clear error on failure. Saves record which concept set they were created with.
No hot reload inside a running world.

## 6. Knowledge UI (M5 dialogue/UI pass and M7) — landed in the M7 UI pass and C1 (2026-11-07)

All four bullets are done: the M7 UI pass shipped the Chronicle filters, the knowledge-spread overlay
and the inspector proficiency bars; C1 replaced the sampled settlement knowledge with the real
`SettlementDetail` (holder counts, record marker) and added the teacher column from memories.

- Settlement panel: concepts known by anyone in the settlement, with holder counts and
  whether a record exists (M5 UI pass, needs a `SettlementView` in the contract, see backlog).
- Chronicle filters for discovery, invention and "first record written".
- God-map overlay: colour settlements by whether they know a selected concept, to watch it
  spread (M7, alongside the religion overlay).
- Inspector: show proficiency bars instead of a flat list, and the teacher of each concept
  when known from memory.
