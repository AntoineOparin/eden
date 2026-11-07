# Status

What is merged, what it was verified with, and the numbers that matter. Append a section per
merge; keep the "Now" block at the top current. Numbers here are measured on Antoine's laptop
(Apple M4 Pro) unless noted.

## Now (2026-11-07)

- **Merged on main:** M0 tooling + contracts, M1 sim core, M2 render shell, brain Worker (M5-ready),
  M3 society, M4 knowledge, M5 minds, K1 knowledge balance, M7 myth (sim + Mind side), A1 art pass, M7 UI pass, M6 possession.
  See git log for the head.
- **In flight (phase 5, launched 2026-11-07 from tag phase5-base):** M8 scale (branch m8-scale), A2 art follow-ups
  (a2-art-followups), C1 contract pass for settlements and the Chronicle (c1-contract-pass). C1 merged 2d55986, A2 merged; M8 still in flight.
- **Next per plan:** M8 scale (asset export, hitches, population ~2,000 by year 120); art follow-ups
  (clouds, animals, bridges, less chibi characters); knowledge roadmap items 4–6. Worker deploy needs Antoine's go-ahead.
- **Checks:** `./scripts/check.sh` = contracts (5 schemas, 19 fixtures, concepts.json 95 concepts),
  279 Eden.Sim tests (~4–5 min; the Slow society test is capped at 150 years), 69 Eden.Game gdUnit4
  tests, headless boot, 85 Worker tests (C1 branch numbers).
- **Known slow spot:** the three Slow Sim tests (population, society, pacing) take about 3 minutes
  together; the full in-band pacing test runs only with `EDEN_PACE_FULL=1` (~40 min).
- **Not deployed:** the Worker runs only through `wrangler dev` on localhost.

## 2026-11-06 — M0 (commit 9ab043a)
Repo skeleton, AGENTS.md, specs 00–50, contracts with fixtures, Godot solution on net10.0, Worker
with /health /embed /think. Verified: contracts 0 failures; 35 Sim tests; 2 scene tests; headless
boot "Eden boot ok"; godot MCP boot with no errors; live `wrangler dev` smoke: /embed 200
(bge-m3, 1024 dims, 0.013 Neurons), /think 200 (≈6 Neurons), 401 on bad key.

## 2026-11-06 — Worker M5-ready (merge 72ad510)
/chat, Anthropic and OpenAI-compatible adapters (never called live, no keys), Gemma 4 default,
pricing tables, 85 tests. Live smoke on Gemma: /think 5.8 Neurons, /chat 3.6 Neurons.

## 2026-11-06 — M1 sim core (merge ec3b692)
World gen, SoA NPC storage, genes, needs, utility AI, life cycle, memory stream, save/load,
console runner, 92 tests. Seed sweep 1..20 × 60 years: 18 reached 50+ (years 35–56, generation
3–4), seed 10 stalled at 4 (four sons), seed 18 died in year 4. Population plateaued ≈200,
food-limited. 5,000 NPCs tick in 2.5 ms; ≈4 KB/tick allocated by events.

## 2026-11-06 — M2 render shell (merge 0a9f5c6)
FakeSimHost, chunked terrain + biome splat + water, 50-slot hi-LOD pool + MultiMesh, god camera,
time controls, inspector, 19 gdUnit4 tests. 1,000 fake NPCs: 120 fps vsync (display cap), ≈600 fps
vsync off at 1600×900, ≈570 fps with 2,000 NPCs, ≈490 draw calls. First real-sim render after the
M1 merge: 120 fps, 2 NPCs, no errors. Screenshots were kept outside the repo.

## 2026-11-06 — M3 society (merge 0f615ba)
Households, settlements (DBSCAN of homes), factions, jobs, farming, storage, construction,
trade, leadership, raids, A* pathing, 120 tests. Sweep 1..20 × 120 years before the M4 merge:
18/20 seeds with 10–14 settlements, hundreds of trades, 50–177 raids; two settlements by year
26–45; population 430–730. 5,000 NPCs: 3.4 ms/tick; daily settlement update 16 ms.

## 2026-11-06 — M4 knowledge (merge e43104f)
95 concepts in 8 categories, concept graph with cycle/id/reachability checks, transmission
(chatter, teaching, apprenticeship, written records), honing, discovery, five effect primitives,
LLM inventions validated into the live graph, 125 → 153 tests after merge. Knowledge gates
society actions (see DECISIONS). Merged sweep 1..20 × 120 years, gate on: 18 grew / 2 died;
10–14 settlements; first two settlements year 25–49, first trade 33–67, first raid 58–93;
population 1,500–2,300 by year 120 (farming + knowledge compound; M8 scale matters sooner).
Knowledge timeline seed 1: 11 concepts by year 10, 69 by year 40, writing by year 60, first
records year 70, all 95 by year 130.

## 2026-11-06 — M5 minds

**What.** Eden.Mind is complete: `MindPool` (spotlight every in-game hour, thoughts every 4 in-game
hours staggered by id, reflection at day end, embeddings on promotion, priority queue
dialogue > reflection > thought > embeddings, concurrency from settings, 20 s task timeout, daily
Neuron budget with the 90 % rule, 5 s → 5 min backoff, rules-only fallback), Generative Agents
retrieval over bge-m3 embeddings cached per memory text, `IntentionMapper`, the completed
`WorkerProxyProvider` (surfaces `X-Eden-Usd-Cost`), and direct `AnthropicProvider` /
`OpenAiCompatibleProvider` (C#, checked against the live docs). Eden.Sim: `SetThinkingCommand`
(contract change), `IMindSnapshot` extras on `WorldSnapshot`, drama tick per NPC. Eden.Game: dialogue
panel, brain settings panel (key AES-encrypted in `user://`), HUD budget meter, inspector thought line
from the Mind, god-powers toolbar (Select / Lightning / Whisper), `--m5-check` scripted acceptance run.

**How verified.**

- `./scripts/check.sh` green from the worktree root.
- Eden.Sim.Tests: 207 (was 153): 205 non-Slow in 0.8 s plus the 2 existing Slow tests, whole run
  1 m 8 s as before. The 54 new tests are under `Mind/`. `MindPoolTests` runs in a non-parallel
  collection, because it timed out while the Slow tests saturated the thread pool. The Mind tests
  ran 6× in a row without a flake.
- Eden.Game.Tests (gdUnit4): 23 (was 19): `MindUiTests` (settings round trip, key encryption,
  budget meter render, dialogue panel through a fake provider) and extended `MainSceneTests`.
- Manual M5 check against `wrangler dev` on :8787 (Workers AI, Gemma 4 + bge-m3), seed 20,
  `godot --path game -- --seed=20 --m5-check --m5-year=10`: fast-forward at 500× until a village has
  a leader, select the leader, strike lightning 7 m away (radius 3), then speak through the dialogue
  panel with a line that does not mention the strike. Final two runs:
  - Run 4, Hamelah, leader of Elderford (13 people), year 18.
    God: "Tell me, how do your people fare today?"
    Hamelah: "We are weary, and the sky has already shown its teeth today with a bolt from the blue.
    My people seek food and rest, though our hearts are heavy with questions."
    Meter: `Neurons 482.9 / 9000  worker: ok  thinking 11`; that run cost 163.45 Neurons.
  - Run 5, Vaveek, leader of Marstead (11 people), year 18.
    God: "Tell me, how do your people fare today?"
    Vaveek: "The sky speaks, but it brings no rain, only a strange voice. My people are fed and warm,
    but the lightning that struck from nothing makes my heart heavy with doubt."
    Meter: `Neurons 589.0 / 9000  worker: ok  thinking 9`; that run cost 106.11 Neurons.
  - Runs 1 and 3 (before the "still shaken" persona line) answered only obliquely ("under the eye
    that watches and judges", "we look to the horizon with wary eyes"); run 2 named the lightning.
    This led to the prompt-context change in DECISIONS.
- Worker log for the whole session: 72 `/think`, 5 `/chat`, 82 `/embed`, no 4xx/5xx.

**Numbers.** Neurons spent in total (all five runs, per the budget meter): 589.0, about 6 % of
one free day. A run costs about 75–165 Neurons: roughly 6 per thought or reply, ≈ 0.1 per embed
call, most of it background thoughts during the fast-forward. Dialogue round trip ≈ 2–4 s.

**Not verified here.** A pasted Anthropic key (no key; covered by stub tests and the encrypted
settings round trip). The budget cap against the live Worker (covered by `MindPoolTests`). The Worker
is not deployed.

## 2026-11-06 — K1 knowledge balance (branch k1-knowledge-balance)

**What.** Roadmap items 1–3 (`specs/51-knowledge-roadmap.md`). (1) Forgetting: unpractised concepts
decay toward the written floor after a 2-year grace and are forgotten under 0.05; last-practised ticks
are saved in a versioned tail block of KNW2. (2) Pace: era targets (median writing year 150–250, bronze
300–450, nothing before half of each lower bound), reached with a cubic discovery-difficulty curve
(`DiscoveryDifficultyExponent` = 3) and harder late steps; the global rates are unchanged. The runner's
`--seeds` sweep gains `writing@`, `bronze@` and `known` columns, plus `--pace` (stop a seed at bronze)
and `--set Name=value` (override any SimConfig tunable). (3) Graph depth: 14 late concepts now take 3–5
prerequisites; the longest chain is asserted ≥ 5. See the three DECISIONS entries "K1: …".

**How verified.**
- Eden.Sim.Tests: 218 (was 207): `ForgettingTests` (8),
  `ConceptGraphTests.Late_eras_sit_behind_deep_prerequisite_chains_and_the_early_game_stays_wide`,
  `PacingTests` (2, Slow). The non-Slow suite is as fast as before. The always-on pacing test runs seeds
  1..20 to year 40 in ~8 s in Debug (median 56 concepts known, no copper, writing or bronze anywhere).
  The in-band test needs `EDEN_PACE_FULL=1` (about 40 minutes of simulation) and returns early
  otherwise; the sweep below is its evidence.
- M1 `PopulationTests` unchanged and green (18/20 reach 50 in years 34–84; seeds 10 and 16 die out).
  M3 Slow society test unchanged and green (18/20 seeds get ≥ 2 settlements, a trade and a raid; first
  raid now year 62–147, was 58–93, so the test runs longer).
- `node scripts/validate-contracts.mjs`: concepts.json 95 concepts, 0 failures.

**Numbers.** Sweep seeds 1..20, Release build. Before = the merge-base behaviour (old concepts.json,
`--set ForgetRatePerYear=0`, `--years 150`). After = this branch, `--years 500 --pace` (a seed stops
when bronze is first known; `-` = not by year 500). Seeds 10 and 16 die out in both.

| seed | writing@ before | bronze@ before | writing@ after | bronze@ after |
|---|---|---|---|---|
| 1 | 58 | 92 | 163 | 252 |
| 2 | 71 | 94 | 138 | 291 |
| 3 | 64 | 84 | 123 | 340 |
| 4 | 61 | 80 | 198 | - |
| 5 | 68 | 95 | 283 | 479 |
| 6 | 69 | 89 | 289 | 374 |
| 7 | 61 | 94 | 228 | 396 |
| 8 | 57 | 78 | 144 | 238 |
| 9 | 66 | 89 | 187 | - |
| 10 | died | died | died | died |
| 11 | 65 | 86 | 190 | 262 |
| 12 | 68 | 90 | 350 | - |
| 13 | 71 | 91 | 256 | - |
| 14 | 108 | 150 | 331 | - |
| 15 | 60 | 98 | 191 | - |
| 16 | died | died | died | died |
| 17 | 75 | 102 | 150 | - |
| 18 | 59 | 87 | 163 | 231 |
| 19 | 57 | 86 | 230 | 242 |
| 20 | 63 | 83 | 140 | 204 |

Median over the 18 living seeds: writing year 64 → 190 (range 57–108 → 123–350); bronze 90 → about
385 (range 78–150 → 204–479, and 7 of 18 seeds have no bronze by year 500). Seed 2 after K1: 57 concepts
by year 40, pictographs about year 90, writing in year 138, copper still unfound at year 140. Population
has the same shape (about 1,400–2,200 from year 100): the boom still comes from farming. Bronze is
bimodal (about 200–260, or 340 and later); see the backlog row.
## 2026-11-06 — M7 myth, sim and Mind side (branch m7-myth-sim)
God reputation per settlement (Favour, Awe; neighbours' Envy/Fear through contact), gossip distortion with
god attribution and myth generations, rule-based god beliefs, religions (found, spread, schism) with shrines
and prayer, a god-standing line in the Mind's persona and an Awe-gated reflection request, and a `--myth`
runner report (`game/tools/Eden.Sim.Runner/MythReport.cs`). UI parts (religion overlay, Chronicle filters,
settlement panel, remaining powers) wait for the A1 art pass (backlog). Details: `10-sim-core.md` "Myth (M7)",
`20-mind.md` "As built (M7 sim side)", DECISIONS 2026-11-06 "M7 myth: …".

**Verified.** `./scripts/check.sh` green. Sim tests 240 (was 207): +11 `MythTests`, +7 `GossipTests`,
+9 `ReligionTests`, +6 `Mind/GodStandingPromptTests`. The two existing Slow tests still pass (Population
18/20 reached 50; Society: two settlements, a trade and a raid on 18/20 seeds), but the Society test now
takes ≈ 21 min on a machine shared with two other agents (≈ 70 s before): seed 1's first raid moved from year
77 to year 131 (population 1,921), and the test runs each seed until its first raid or 20 generations.
Fair 70-year comparisons with religion/shrines switched off show no systematic effect on raids, so this is
trajectory luck, not suppression (DECISIONS "shrines wait for an established village"); backlog row added. Save round trip and
determinism cover all new state (`MYTH` section after `KNW2`).

**Acceptance ratios.**
- 10 daily blessings on one village over an in-game year: Favour 0.71 after the year and a favourable faith
  5–16 days in, on **10/10** seeds (needed ≥ 7/10). Fast enough (0.25 s) to run without the Slow tag.
- A neighbour in contact: Envy 0.35, Fear 0.09 (needed Envy or Fear > 0.3).
- 10 lightning strikes, no blessing: Favour −0.57, Awe 0.74, stance **fearful**.
- Gossip: every distorted copy differs in exactly one of subject / importance / attribution and carries
  Generation + 1; no attribution at Awe 0.
- Speed: `--bench 5000` 5.40 ms/tick vs 5.17 ms on phase3-base, same allocation per tick (machine shared
  with two other agents' runs, so within noise).

**Sample** (`--myth --seed 20 --years 80`, Release, no god actions; columns per decade):
```
 year  pop  settl  shrines  prayers  distorted  attributed  beliefs  religions (stance)
   20   14      1        0        0        119           0        0  the Way of Palom (favourable)
   40   46      3        3        0        409           0        0  the Way of Palom (favourable)
   60  149      7        7        0       1118           0        0  the Way of Palom (favourable)
   80  642     11        8       22       3777           0       21  the Way of Palom (favourable)
  y17  [religion] Bemin of Stoneby gathered the faithful into the Way of Palom: they hold that the sky favours them.
  y23  [shrine] A shrine to the sky was raised in Stoneby for the Way of Palom.
  y55  [religion] The faithful of Elmdale took up the Way of Palom, the faith of Elmby.
  y67  [prayer] Bemels of Fenholm offered the first prayer of the Way of Palom.
```
With `--strike` (lightning beside the first village every day for a year from year 15): Favour −0.55 / Awe
0.67 at year 20 and 13–44 god-attributed retellings per decade while the Awe lasts. Schisms are covered by
`ReligionTests` (the seed-20 strike sample loses its first village before a generation passes).

**Not verified here.** The manual M7 check with the Mind on (coordinator, after merge). No UI.
## 2026-11-06 — A1 art pass (branch a1-art-pass)

**What.** Stylized low-poly look from `docs/art/references/` with CC0 packs, loaded at runtime from `game/assets/`
(`.gdignore`): Kenney Mini Characters (rigged NPCs), Fantasy Town Kit (buildings assembled per kind), Nature Kit
(trees per biome, rocks, ore, berry bushes, crops in three stages, decor), ambientCG terrain detail maps, Nunito +
Kenney icons for the HUD; all recoloured to the reference palette. NPC LODs: 50 skinned hi (AnimationTree:
idle/walk/sprint blend, work, fight, down; elders stooped), 700 mid (CPU-baked poses in MultiMeshes), rest far
(impostor atlas quads). Chunked MultiMeshes for resources, decor (within 150 m) and buildings; stone plinths/earth
banks and render-side terrain flattening under footprints, dirt yards and roads; terrain shader (palette ramp,
texture luminance detail, cliffs, highland, snow, wet sand, two-band light); water (depth ramp, double foam lines,
noise normal maps); `SkyCycle` day/night with dawn/dusk and moon; ACES, mild glow, fog; one `UiTheme` with icons,
day/night glyph and power tooltips. Scale: 1 unit = 1 m, adults 1.75 m, buildings within their footprint radius.
Contract: `ResourceView` + `IWorldSnapshot.Resources` (pre-approved; `contracts/README.md`), filled in
`World.Snapshot()` only. New dev flags: `--year --hour --focus --paused --gallery --lineup --hide`.

**How verified.**

- `./scripts/check.sh` green: contracts, 207 Eden.Sim tests (unchanged count, all pass), 36 Eden.Game gdUnit4 tests
  (was 23; new `ArtPassTests`: building meshes and clean loading, meshes fit footprints, tree per biome, required
  animations, NPC heights 1.7–1.8 m, AnimationTree states, LOD counts 50/700/1,250 at 2,000 NPCs, far-zoom scale,
  30° slope placement, render-only flattening, resource views from both hosts, building renderer foundations; the
  fps test is skipped headless), headless boot clean, Worker checks.
- Budgets measured on the M4 Pro at 1600×900, vsync off, `--log-fps`, 10 s after a 4 s warm-up. Other agents were
  running Sim sweeps at the same time (load average 17–22), so these are pessimistic. Draw calls swing with the day:
  the low number is night (no sun shadows), the high one day.

| Scenario | Budget | Measured |
|---|---|---|
| FakeSimHost 2,000 NPCs (`--fake-sim --npcs=2000 --quit-after=14`) | ≥ 60 fps avg | 272 fps avg (min 247) |
| FakeSimHost 5,000 NPCs | ≥ 30 fps avg | 120 fps avg (145 steady; one 21 fps sample from a start-up hitch) |
| Real sim seed 20, year 60 (`--seed=20 --year=60 --focus=town`), 299 people, 10 settlements' buildings | ≥ 60 fps | 301 fps avg (min 288) |
| Real sim seed 20, year 120 (1,683 people), extra heavier case | ≥ 60 fps | 129 fps avg (min 100) |
| Draw calls at 2,000 NPCs | < 800 | 194–415 (fake); 220–528 real year 60 at the town |
| Startup to first frame | < 4 s | 0.96 s fake, 0.80 s real (engine clock); 0.99 s wall clock launch-to-quit |

  `--year=60` fast-forward costs 10–12 s of synchronous stepping before the first frame (dev flag, not startup);
  year 120 took ~9 minutes under the load.
- Screenshots (seed 20, year 60, 10:00, `--focus=town --paused`):
  [before 140 m](art/shots/before-140m.jpg), [after 140 m](art/shots/after-140m.jpg),
  [before 30 m](art/shots/before-30m.jpg), [after 30 m](art/shots/after-30m.jpg),
  [HUD before](art/shots/hud-before.jpg), [HUD after](art/shots/hud-after.jpg),
  [scale lineup 30 m](art/shots/scale-lineup-30m.jpg) (`--fake-sim --npcs=0 --lineup --zoom=30 --hour=11 --paused`).

**Numbers.** Assets committed: 11 MB (characters 2.9 MB, buildings 0.45 MB, nature 0.57 MB, textures 6.8 MB,
UI 0.37 MB). Triangles: character 723, Kenney trees 114–402 (before biome scaling).

**Not done.** Herds (`game`) are not drawn; Nova Roma clouds, bridges and terrain-following roads; Quaternius
characters (Drive quota). See `90-backlog.md`.
## 2026-11-06 — M7 UI pass (branch m7-ui-pass)

**What.** The UI half of M7 "myth and god" on the A1 theme. Contract (pre-approved): `SettlementView` + `GodStance` +
`IWorldSnapshot.Settlements`, `MemoryView.Generation`, filled in `World.Snapshot()` / `TryBuildDetail` and by
`FakeSimHost` (six fake settlements with a standing that god commands nudge, made-up Chronicle entries). Toolbar: spawn
human / animals / plants, rain, drought, raise / lower land with a brush, bless, curse, drop food, each with a drawn icon,
tooltip, hotkey and a local effect (`Fx/PowerFx.cs`). Settlement panel, Chronicle timeline (filters, importance,
density strip, click to focus, 5,000-entry log), religion / god-standing / knowledge-spread overlay with legend
(`Overlay/TerritoryOverlay.cs`), inspector settlement link, knowledge bars and myth marker. All new HUD parts sit under
one `GodUi` node. Details: `40-game-client.md` "M7 UI as built"; DECISIONS 2026-11-06 "M7 UI: …".

**How verified.**

- `./scripts/check.sh` green. Eden.Sim tests 254 (was 251): +3 `SettlementViewTests` (views match the standing and
  the Mind's view, no state-hash change, generation in details). Eden.Game gdUnit4 45 (was 36): +9 `MythUiTests`
  (log bound and de-duplication, kind → filter mapping, merge with the real sim's tail, fake settlements and blessing,
  knowledge sampling, palette, glyphs, and one scene test that drives every power, the Chronicle filters, focus
  resolution, the settlement panel, the three overlay modes, the inspector and the one-switch hide).
- Budget with the overlay on: `--fake-sim --npcs=2000 --log-fps --log-frame-time --quit-after=14 --ui=overlay:stance,
  chronicle,settlement:0` at 1600×900: 199 fps average (the display now forces V-Sync at 200 Hz even with
  `--disable-vsync`; overlay off: 200), scripts 1.3 ms and render CPU 0.3 ms per frame (~630 fps possible), draw calls
  357 vs 226 with everything closed.
- Manual M7 check with the Mind on, against `wrangler dev` on :8787 (Workers AI, Gemma 4 + bge-m3), seed 2:
  `godot --path game -- --seed=2 --year=50 --hour=8 --m7-check --m7-after-days=2`. Calton (26 members) was blessed
  once a day for a year (10 blessings over three of its people) through `Main.BlessNpc`; its nearest neighbour is
  Marton (13 members, 76 m). Standing after the year: Calton favour +0.71, awe 0.25, stance favourable; Marton envy
  0.35 and fear 0.09, both pointing at Calton (0.40 / 0.11 two days later). Then the same question to three people
  through the dialogue panel: "What do you make of the god who watches over this island, and of your neighbours?"
  - Nariek of Calton (blessed four times): "The Sky-God watches us with a kind eye, just as He has blessed me many
    times. As for our neighbours, we trade and live as the Way of Boris teaches, though we keep our eyes sharp."
  - Bedoelle of Calton (not blessed): "The Sky watches us, and we must live well so the judgment is kind. As for the
    neighbors, they are just people like us, though some follow different ways like the Silaek."
  - Ralon of Marton (neighbour): "The sky watches us all, judging if we walk the Way of Boris true. Our neighbors in
    Calton seem to have the sky's eye upon them more than we, and it stirs a heavy feeling in my heart."
  Cost: 228.85 Neurons (meter 588.96 → 817.78 of 9,000): 21 `/think`, 3 `/chat`, 24 `/embed`, all 200; one thought
  timed out client-side after 20 s. No reflection landed in the short run (the 60 s real-time floor per NPC), so the
  quotes are dialogue. Screenshot: [blessed settlement and the envious neighbour's reply](art/shots/ui-m7-check-blessed.jpg).
- Screenshots: [faith overlay + Chronicle, seed 2 year 51](art/shots/ui-m7-faith-chronicle.jpg),
  [knowledge-spread overlay (pottery)](art/shots/ui-m7-knowledge-overlay.jpg),
  [fake sim: stance overlay, settlement panel, inspector, Chronicle](art/shots/ui-m7-fake-stance.jpg).

**Not done.** Food stock in the settlement panel (not in the contract); knowledge per settlement is a sample; no
screenshot of the power effects; see the backlog rows added by this pass. M6 hooks: `Hud.God` (one node to hide),
`NpcInspector.PossessRequested` (shows a Possess button when subscribed).


## 2026-11-07 — M6 possession (branch m6-possession)

**What.** Possession end to end. Game: `PossessionDirector` (one hook line in `Main`) with `PossessRig`
(`SpringArm3D` third person, F first person, 0.7 s camera glide from and back to the god rig), `PossessedController`
(`CharacterBody3D` + own `NpcHi`, prediction by command magnitude, sim authority), `TerrainCollision` (trimesh for the
3×3 chunks around the body only), `PossessionHud` (name, needs, action, E/G/R/T prompt, controls, notices, god-view
"Possess (P)" button in god view; after the merge it is bound to M7's inspector Possess button and the HUD button stays hidden), `Hud.SetPossessed(bool)` (hides every god
panel, including M7's `Hud.God`, and keeps the dialogue), `DialoguePanel.ShowPossessed` (speaker = the possessed NPC).
Sim (additive): `World.Possession.cs` (talk = Converse + shared memory, give:food moves up to 2 food with
affinity/trust/debt and a gift memory for both, work = join the target's gather/hunt/build/farm job for 24 ticks or the
nearest berry bush, own storehouse or building site; neighbours who know the NPC remember "X stopped and stared…" /
"…began walking oddly" on the first possessed tick and "X came back to themselves" on release; blackout memory kept;
the body eats/drinks on its own when desperate), `SimHost` runs 2 ticks/s at 1× while someone is possessed, possession
is not saved and a load releases it. Mind: `MindPool.AskAsPossessedAsync` (speaker `possessed`, the line becomes a
`SayIntention` from the possessed NPC); the Spotlight already excluded the possessed NPC (tested). Dev flags:
`--save-world`, `--load-world`, `--possess`, `--m6-check` (`PossessionCheck`). No contract change.

**How verified.**

- `./scripts/check.sh` green from the worktree root: contracts, 265 Eden.Sim tests (was 251), 44 Eden.Game gdUnit4
  tests (was 36), headless boot clean, 85 Worker tests.
- Eden.Sim.Tests: +14 (`PossessionTests` 13: needs tick, reactions once / strangers do not notice, walking oddly,
  release without a tick, speed formula, talk, give, refusals, work alongside, bush + cancel, infants, save/load
  releases, determinism; `Mind/PossessedDialogueTests` 1). Eden.Game.Tests (gdUnit4): +8 (`PossessionTests`: rig
  axes/pitch/first person, collision chunks, renderer skips the possessed NPC, HUD hide/restore, dialogue speaker,
  prompts, main-scene possess → walk → release with the sim catching up within 1 m, fake sim falls back).
- Manual acceptance, scripted through the player's code paths (`--m6-check`, E/T/G/Esc and the dialogue panel), seed 20
  year 120 saved once with `--year=120 --save-world` (fast-forward 101 s, population 1,442) and loaded with
  `--load-world`; Mind on against `wrangler dev` (Workers AI, Gemma 4), background thinkers off for the run
  (`--m6-thinkers=0`):

  ```
  [m6] year 121, population 1441, 12 villages; from Sunridge (271 people) to Marmere (132 people), 127 m apart
  [m6] pressing Possess (P) on Eloira (#3145926)
  [m6] no reachable berries; the home storehouse is 23 m away: take food there first
  [m6] T: take food from the store at (395, 309)
  [m6] route planned: 26 waypoints, 236 m
  [m6] arrived at Marmere after 61 s; walking up to Kiris (#2097364)
  You (as Eloira): Good day. I walked a long way to see your village. How is the harvest here?
  Kiris: The sun has been kind, and our baskets grow heavy with grain. You have walked far, traveler; sit by our
         fire and rest.
  [m6] G: give food to Kiris
  [m6] Esc: release
  ```

  Eloira's newest memories: `[6] god: I lost time; my body moved without me.` · `[5] gift: Eloira gave Kiris 2 food.` ·
  `[7] fight: Raiders from Oakridge fell upon Marmere but were driven off.` (witnessed while there) ·
  `[3] speech: Eloira said: "Good day. I walked a long way…"` · `[3] speech: Kiris said: "The sun has been kind…"` ·
  `[4] conversation: Eloira came up and talked with Kiris.`
  Kiris's newest memories: `[4] conversation: Eloira came back to themselves.` · `[5] gift: Eloira gave Kiris 2 food.` ·
  both speech lines · `[6] belief: Eloira came from far away to ask about our harvest.` (from the Mind) ·
  `[6] gossip: Eloira told me: Radon died of a fever.` · `[4] conversation: Eloira came up and talked with Kiris.`
  A Sunridge neighbour (Ilak): `[5] conversation: Eloira stopped and stared, as if seeing everything for the first time.`
- Screenshots: [third person](art/shots/possession-third-person.jpg), [first person](art/shots/possession-first-person.jpg),
  [taking food](art/shots/possession-work.jpg), [dialogue](art/shots/possession-dialogue.jpg),
  [after release, inspector](art/shots/possession-memories.jpg).

**Numbers.** M4 Pro, 1600×900, `--disable-vsync --log-fps`.

| Scenario | Budget | Measured |
|---|---|---|
| FakeSimHost 2,000 NPCs, god view (`--fake-sim --npcs=2000 --quit-after=14`) | ≥ 60 fps | 256 fps avg (min 242) |
| Seed 20 year 120 (1,441 people), possessed, walking between villages, Mind on (final run) | ≥ 60 fps | 251 fps avg, min 224 (55 samples over 105 s); 99–654 draw calls |
| Same, an earlier identical run | ≥ 60 fps | 117 fps avg, min 96 (the window was held at a 100 Hz cap for part of it) |

Sim position vs body while sprinting: gap 0.3–1.9 m (one tick of lead), 0 authority corrections, ~45 move
commands/s while moving, 27 collision chunk rebuilds for a 300 m walk. Neurons: 246.0 used today in total: 10.4 for the final
run (1 `/chat` plus embeddings), 17.1 and 10.6 for two identical earlier runs (the dialogue screenshot was taken a
frame too early, then the needs card covered the panel), and 208 for a first attempt that stalled on terrain with
three background thinkers on (that is why thinkers are off in the check now).

**Not done / notes.** The scripted check plans its own A* route over the heightmap (a player steers by eye). Buildings
and trees do not block the body; possession needs the real sim (fake sim falls back). See backlog.

## 2026-11-07 — C1 contract pass: settlements and the Chronicle (branch c1-contract-pass)

**What.** The pre-approved contract additions and nothing else: `SettlementView.Food` and `.TerritoryRadius`
(appended, defaulted), `SettlementDetail` / `SettlementConceptView` / `BuildingCount` with
`IWorldSnapshot.TryGetSettlementDetail` and `WatchSettlementCommand`, and `IWorldSnapshot.ChronicleSince`
(`NewChronicle` kept). Sim: `World.SettlementDetail.cs` (food per settlement, watch list of 8, detail build),
`World.ChronicleSave.cs` (`CHRN` section), ring 512 → 2,048 with a shared published copy, one `Apply` case, one
registration line each in `SaveWriter` / `SaveReader`. Client: the settlement panel shows live food and daily eating,
"Knows N concepts, M written down here" with holder bars and gold names for recorded concepts, and buildings
(`SettlementKnowledge` now caches details; the member sampling is deleted); the overlay draws each settlement's
`TerritoryRadius` and colours knowledge by holders / members; `ChronicleLog` reads `ChronicleSince` (no burst loss,
history from a save after a load); the inspector shows who taught each concept when the memory survives ("from Ada").
`FakeSimHost` fills everything with stable made-up numbers. Details: `contracts/README.md` (C1 table and request
pattern), `40-game-client.md` "M7 UI as built", DECISIONS 2026-11-07 "C1: …".

**How verified.**

- `./scripts/check.sh` green from the worktree root: contracts, 279 Eden.Sim tests (was 268: +5 `SettlementViewTests`,
  +6 `ChronicleHistoryTests`), 58 Eden.Game gdUnit4 tests (was 53: +4 `ChronicleTests`, +1 `MythUiTests`; the sampling
  test became the detail test), headless boot, 85 Worker tests.
- No state-hash change: two runs of 10,000 ticks with snapshots every 50 ticks, one also requesting every settlement's
  detail and reading the whole history, end on the same hash; save/load round trips keep the hash; a save without the
  section still loads.
- Numbers (headless, `World` only): seed 2 year 50 has 8 settlements and 342 Chronicle entries; Calton 26 people eat
  ≈ 36 food a day against single-digit stock (they eat from what they carry and gather daily), 57 concepts, no
  records on its land. Seed 20 year 120: 1,442 people, 12 settlements, ring full (2,048), ≈ 350 KB of entries retained,
  12 details built in 12 ms on a cold JIT (Sunridge: 271 people, 81 concepts).
- Screenshots: [settlement panel with real food and knowledge, seed 2 year 51](art/shots/ui-c1-settlement-seed2-y51.jpg)
  (`--seed=2 --year=50 --hour=10 --ui=settlement:1`), [Chronicle right after loading the seed 2 year 50 save, 345 entries
  back to year 0](art/shots/ui-c1-chronicle-after-load.jpg) (`--load-world=… --ui=chronicle`).

**Not done.** No settlement had a written record on its land in the two seeds shot, so the gold record marker is only
covered by tests and the fake sim. `SettlementView.Members` (daily clustering) and the detail's live member count can
differ by one or two between daily passes. The settlement panel's minimum height grew to 560 px (it overlaps the
dialogue panel at 900 px only while both are open).
## 2026-11-07 — A2 art follow-ups (branch a2-art-followups)

**What.** The second art pass toward the Nova Roma look (brief `docs/specs/briefs/a2-art-followups.md`, spec 45 "As built
(A2)", DECISIONS 2026-11-07 "A2:").

- **Sky and weather.** 90 procedural cartoon cumulus (`World/CloudLayer.cs`, `shaders/clouds.gdshader`) drifting at
  60–120 m, mostly round the shores, faded near the camera, no shadows by default (`--cloud-shadows`); rain shows every
  cloud in slate grey, drought a few warm ones. The weather comes from the god's casts and the Chronicle
  (`World/WeatherState.cs`) because the snapshot has no weather field; the power effects' screen tint shares its colours.
  `SkyCycle` warms the night and dusk light set-ups in its first three frames.
- **Animals.** Quaternius CC0 deer, pig (as a wild boar) and sheep from Poly Pizza (`game/assets/animals/`, 1.3 MB; the
  pack's Google Drive was over quota), posed once (`Art/Skinning.cs`, `Art/AnimalLibrary.cs`) at true size (deer
  1.2 m at the shoulder), 2–5 per herd drifting slowly inside the herd, on the ground, tilted with it, instanced
  through `PropBatches`: 1,000 animals (200 herds) cost 0.69 ms per frame in the gdUnit measurement.
- **Structures.** Roads planned as a spanning tree per settlement (`World/RoadNetwork.cs`) and drawn as strips draped on
  the terrain (`World/RoadRenderer.cs`); plank bridges with piles where a road crosses water or a notch, resting on both
  banks; one to three record stelae with a carved glyph in front of each finished archive; an altar with offerings
  inside every shrine (a `proc:` part merged by `MeshBaker`). Foundations blend into the slope over 2.5 m (yards, not
  steps), and a rebuilt heightmap (terraforming) keeps its yards and roads.
- **Characters.** "Less chibi": `Art/CharacterProportions.cs` reshapes the Kenney rig once (legs × 2.3, torso × 1.8, arms
  × 1.3 and turned down, head × 0.52): head share of the height 0.49–0.60 → 0.19–0.24 (about 1:4.2–1:5.3), adults still
  1.75 m, same animations; mid LOD and impostors re-baked. [Close-up](art/shots/a2-characters-close.jpg),
  [scale lineup 30 m](art/shots/scale-lineup-30m.jpg) (now with the animals, a stela and a bridge in the last row).
- **Hitches.** Impostor atlas cached in `user://cache/impostor-atlas-<hash>.png` (loaded in `_Ready`, nothing rendered or
  read back); decor and props refresh only the terrain chunks a new building touched, one frame after the terrain
  (`TerrainBuilder.LastDirtyChunks`, `PropBatches.Begin(chunks)`), roads a frame after that.
- **Power effects.** `--fx-check=DIR` (`Dev/FxCheck.cs`) plays every power at the town and saves `fx-*.jpg`; after looking
  at them the curse smoke now rises above the roofs with a dark ring, drought dust is larger and the weather tint
  stronger. Lightning still has no bolt (backlog).
- **Dev.** `--log-frame-time` logs every frame over 33 ms after the 4 s warm-up with the one-off work in it
  (`FrameTimeProbe.Note`); scripted runs (`--quit-after`, `--fx-check`) make the window no-focus and always on top and
  turn edge-pan off, after stray keystrokes changed a "paused" screenshot and macOS froze a covered window's frames.

**How verified.**

- `./scripts/check.sh` green from the worktree root: contracts, 268 Eden.Sim tests (unchanged), headless boot clean, 85 Worker
  tests. Eden.Game gdUnit4: 64 (was 53): +11 `A2ArtTests`
  (animal sizes and clean loading; herds 2–5 per node on the ground inside the herd plus the 200-herd cost; head ratio
  and adult height of every model; stela / altar / bridge / cloud sizes and the shrine still in its footprint; stelae on a
  30° slope keep their footing under the ground; roads within 1–15 cm above the drawn ground, yards between slope and
  top, stelae in front of the archive, an unchanged rebuild touches nothing; a road over a sea channel gets a bridge whose
  deck ends rest on the banks and whose piles reach the ground, and no strip over water; the bridge finder on water,
  notches and slopes; a new building rescans ≤ 1/6 of the decor cells (one chunk's worth instead of 64,516); clouds
  thicken in rain and thin in drought, Chronicle wording, expiry by tick; atlas cache key).
- The fps budget test passes with the display attached (`dotnet test … --settings` without `--headless`,
  `ArtPassTests.Fps_budget_2000_fake_npcs`).
- Numbers: M4 Pro, 1600×900, `--disable-vsync --log-fps --log-frame-time`, 4 s warm-up. The display now caps at
  100–145 Hz whatever the flags, so fps reads as the cap; "possible" is 1000 / (scripts + render CPU). Load average was
  15–28 during both sets (other agents' runs), so the two columns are close to noise.

| Scenario | Budget | Before (base) | After (A2) |
|---|---|---|---|
| FakeSimHost 2,000 NPCs | ≥ 60 fps | 122 fps avg (capped); ~640 possible; scripts 1.25 ms; 206–470 draw calls | 120 fps avg (capped); ~605 possible; scripts 1.51 ms (1.3 steady); 214–481 draw calls |
| FakeSimHost 5,000 NPCs | ≥ 30 fps | 139 fps avg; ~392 possible; scripts 2.31 ms; 206–454 draw calls | 120 fps avg (capped); ~348 possible; scripts 2.68 ms; 214–467 draw calls |
| Real seed 20, year 120 (1,442 people), 140 m over the biggest town, 60 s at 1× | ≥ 60 fps | 144.5 fps avg (capped); ~617 possible; scripts 1.13 ms; 238–525 draw calls | 120 fps avg (capped); ~789 possible; scripts 1.09 ms; 250–551 draw calls |
| Startup to first frame (fake) | < 4 s | 0.96 s | 1.00 s |

  Hitches (`[hitch]` lines; "after the warm-up" is the A2 target of no frame over 33 ms):

| Hitch | Before | After |
|---|---|---|
| Impostor atlas readback | rendered and read back every run (1.3–1.6 ms of readback at 0.27 s, a 35 ms start-up frame) | cache hit: PNG loaded in `_Ready` (< 1 ms, before the first frame); a render only on a machine's first run of a character set |
| Decor rebuild when a building completes (real, year 120) | full 65k-cell scan 1.6–1.7 ms in the same frame as the terrain rebuild (5.4–5.6 ms) and a full prop re-batch: worst frame of the run 31.0 ms at 46.9 s | terrain 4.6–6.5 ms, then only the touched chunks' decor and props (< 1 ms) a frame later, roads a frame after: those frames no longer the worst; worst frame 23.7 ms (a full resource re-batch when many resources changed at once, 15 ms) |
| First night / light variants | 1.1 s stalls: real run at 2.5 s (first day after a night start, in the warm-up) and the 5,000 run at 23 s (after the warm-up) | none; light set-ups compiled in the first three frames |
| Frames over 33 ms after the warm-up: fake 2,000 / 5,000 / real year 120 | 0 / 1 (1,098 ms) / 0 (worst 31.0 ms) | 0 / 0 / 0 (worst 17.6 / 20.4 / 23.7 ms) |

- Screenshots (seed 20, 10:00, `--focus=town --paused`; year 60 is 24 people now, the sim's trajectory has changed since
  A1): year 60 [before 30 m](art/shots/a2-before-30m-y60.jpg) / [after 30 m](art/shots/a2-after-30m-y60.jpg),
  [before 140 m](art/shots/a2-before-140m-y60.jpg) / [after 140 m](art/shots/a2-after-140m-y60.jpg); year 120
  [before 30 m](art/shots/a2-before-30m-y120.jpg) / [after 30 m](art/shots/a2-after-30m-y120.jpg),
  [before 140 m](art/shots/a2-before-140m-y120.jpg) / [after 140 m](art/shots/a2-after-140m-y120.jpg). Power effects
  (`--seed=20 --year=60 --hour=10 --fx-check=…`): [spawn human](art/shots/fx-spawn-human.jpg),
  [animals](art/shots/fx-spawn-animals.jpg), [plants](art/shots/fx-spawn-plants.jpg), [rain](art/shots/fx-rain.jpg) and
  [its sky](art/shots/fx-rain-sky.jpg), [drought](art/shots/fx-drought.jpg) and [its sky](art/shots/fx-drought-sky.jpg),
  [raise](art/shots/fx-raise-land.jpg), [lower](art/shots/fx-lower-land.jpg), [bless](art/shots/fx-bless.jpg),
  [curse](art/shots/fx-curse.jpg), [drop food](art/shots/fx-drop-food.jpg), [lightning](art/shots/fx-lightning.jpg).

**Numbers.** Assets added: 1.3 MB (deer 1.0 MB, mostly its 13 animations; pig 160 KB; sheep 157 KB); 21 screenshots
(4.8 MB). New draw calls: clouds 5, animals 3 (+3 shadow), roads 1 per settlement, bridges / stelae a few batches.

**Not done / notes.** The 140 m before/after pictures differ little: roads and stelae are small at that height and
dense towns hide most road strips between houses; clouds show best from 250 m up (`fx-rain-sky.jpg`). Weather is
inferred, not read from the snapshot; lightning has no bolt; characters are about 1:4.5, not 1:6; animals drift but do
not walk (backlog rows). `Dev/FrameTimeProbe.cs` (unowned) gained the hitch log.
