# Status

What is merged, what it was verified with, and the numbers that matter. Append a section per
merge; keep the "Now" block at the top current. Numbers here are measured on Antoine's laptop
(Apple M4 Pro) unless noted.

## Now (2026-11-06)

- **Merged on main:** M0 tooling + contracts, M1 sim core, M2 render shell, brain Worker (M5-ready),
  M3 society, M4 knowledge. Head `9cb0216` plus whatever follows in git log.
- **In flight:** M5 minds (one agent, branch `m5-minds`).
- **Next per plan:** balance pass (knowledge forgetting + pace, `specs/51-knowledge-roadmap.md`),
  then M6 possession and M7 myth in parallel, then M8 scale.
- **Checks:** `./scripts/check.sh` = contracts (5 schemas, 19 fixtures, concepts.json 95 concepts),
  153 Eden.Sim tests (~70 s, two Slow 20-seed tests dominate), 19 Eden.Game gdUnit4 tests,
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
