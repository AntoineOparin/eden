# Status

What is merged, what it was verified with, and the numbers that matter. Append a section per
merge; keep the "Now" block at the top current. Numbers here are measured on Antoine's laptop
(Apple M4 Pro) unless noted.

## Now (2026-11-06)

- **Merged on main:** M0 tooling + contracts, M1 sim core, M2 render shell, brain Worker (M5-ready),
  M3 society, M4 knowledge, M5 minds. See git log for the head.
- **In flight:** K1 knowledge balance on branch `k1-knowledge-balance` (forgetting, pace, graph depth;
  section below), awaiting merge.
- **Next per plan:** balance pass (knowledge forgetting + pace, `specs/51-knowledge-roadmap.md`),
  then M6 possession and M7 myth in parallel, then M8 scale. Worker deploy needs Antoine's go-ahead.
- **Checks:** `./scripts/check.sh` = contracts (5 schemas, 19 fixtures, concepts.json 95 concepts),
  218 Eden.Sim tests on the K1 branch (the M3 Slow society test now dominates, see K1), 23 Eden.Game gdUnit4 tests,
  headless boot, 85 Worker tests.
- **Known slow spot:** the Sim suite's two Slow tests take about a minute together. Split them
  behind a `Category=Slow` filter if it starts to hurt.
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
