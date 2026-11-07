# Status

What is merged, what it was verified with, and the numbers that matter. Append a section per
merge; keep the "Now" block at the top current. Numbers here are measured on Antoine's laptop
(Apple M4 Pro) unless noted.

## Now (2026-11-06)

- **Merged on main:** M0 tooling + contracts, M1 sim core, M2 render shell, brain Worker (M5-ready),
  M3 society, M4 knowledge, M5 minds. See git log for the head.
- **In flight:** nothing.
- **Next per plan:** balance pass (knowledge forgetting + pace, `specs/51-knowledge-roadmap.md`),
  then M6 possession and M7 myth in parallel, then M8 scale. Worker deploy needs Antoine's go-ahead.
- **Checks:** `./scripts/check.sh` = contracts (5 schemas, 19 fixtures, concepts.json 95 concepts),
  207 Eden.Sim tests (~70 s, two Slow 20-seed tests dominate), 23 Eden.Game gdUnit4 tests,
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
