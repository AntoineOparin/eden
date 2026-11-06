# Eden: a WorldBox-style god sim with agentic NPCs

Working title `eden` (world starts from two people). Rename freely.

## Context

Antoine wants a self-sustaining society/ecosystem sandbox like WorldBox, with three things WorldBox lacks:

1. **Smart, unique NPCs** that remember (everything they witness, including what the god did), learn (knowledge spreads NPC to NPC and is honed into new technology), and talk.
2. **Two cameras in one 3D world**: a god view (tilted, pan/zoom, no orbit) and a possession view where the player drives an NPC in third/first person.
3. **Scale**: start from two people on an island and grow to thousands, so LLM use must be tiered and budgeted.

Decisions already made with Antoine:

| Decision | Choice |
|---|---|
| Client | Godot 4.7 .NET, C# for everything client-side |
| Backend | TypeScript on Cloudflare Workers (free tier) |
| NPC LLM | Cloudflare Workers AI by default (free 10k Neurons/day), behind a provider interface so any API key from any provider can be swapped in from a settings panel |
| First milestone | One island, start from 2 NPCs, grow by reproduction; ~15 "thinking" NPCs at a time; architecture sized for thousands |
| Persistence | Single-player, saves local; backend is stateless |
| Tooling | Godot .NET CLI on PATH, a godot-mcp server, gdUnit4 for scene tests |

## Architecture in one picture

```
Godot client (C#)                                   Cloudflare Worker (TS)
┌────────────────────────────────────────┐          ┌───────────────────────┐
│ Eden.Sim   pure C# class lib, no Godot │          │ /think  /embed  /chat │
│   world grid, entities, needs,         │  HTTPS   │ provider adapters:    │
│   utility AI, jobs, villages,          │ ───────► │  workers-ai (default) │
│   genetics, knowledge, memory stream   │          │  anthropic            │
│ Eden.Mind  spotlight picker, prompts,  │          │  openai-compatible    │
│   retrieval, reflection, mind pool     │          │ zod-validated JSON out│
│ Eden.Game  Godot scenes, render, LOD,  │          │ daily budget counter  │
│   god cam, possession, UI, save/load   │          └───────────────────────┘
└────────────────────────────────────────┘
   Direct-to-provider path: when the player pastes their own Anthropic/OpenAI key,
   Eden.Mind calls that provider directly from C#; the key never leaves the machine.
```

Key principle: **the simulation never waits on an LLM.** Every NPC runs on rules every tick. LLM output arrives asynchronously as an *intention* (a goal, a line of dialogue, a belief, an invention) that the rules layer then executes.

## Repo layout

```
~/Projects/eden/
  game/                      Godot 4.7 .NET project
    project.godot
    Eden.sln
    src/Eden.Sim/            class library, zero Godot references, xUnit-tested
    src/Eden.Mind/           class library, references Eden.Sim only
    src/Eden.Game/           Godot scripts (nodes, scenes glue)
    tests/Eden.Sim.Tests/    xUnit, runs with `dotnet test` in <1s
    tests/Eden.Game.Tests/   gdUnit4Net scene tests, headless
    scenes/, assets/, data/  (data/ = JSON for concepts, biomes, names)
  brain/                     Cloudflare Worker, Hono + Zod, wrangler.jsonc
    src/index.ts, src/providers/{workers-ai,anthropic,openai-compatible}.ts
    src/schemas.ts           Zod schemas for every LLM output shape
  README.md, AGENTS.md       conventions (adapted from ~/Projects/react-router-app/AGENTS.md:
                             Zod for untrusted input, no module state in Worker, secrets via
                             wrangler, kebab-case files, colocated tests)
```

Why a plain Hono Worker instead of the React Router template: the brain has no UI in v1 and the template's auth/Supabase middleware would be dead weight. If a web dashboard (chronicle viewer, budget graphs) is wanted later, it gets added from the template as a second Worker.

## Simulation core (Eden.Sim)

Deterministic, fixed-step (e.g. 10 ticks per in-game hour), seeded RNG, struct-of-arrays storage so 5,000 NPCs tick in a few ms. Runs on a background thread; Godot reads snapshots.

- **World**: island heightmap (noise + falloff), biomes, water, resource nodes (trees, stone, ore, berries, game animals), fertility, seasons. Chunked 32×32 for spatial queries.
- **Entities**: NPCs, animals, plants, buildings, items. NPCs have: genes (12 floats: strength, curiosity, sociability, aggression, fertility, lifespan…), traits derived from genes + upbringing, needs (food, water, rest, warmth, safety, social, purpose), skills, inventory, relationships (per-pair affinity/trust/debt), faction, home, job.
- **Decision layer**: utility AI. Each tick an NPC scores ~20 candidate actions (eat, gather X, build, farm, court, teach, trade, raid, flee, pray, explore, idle) from needs × personality × context; picks the best and runs it as a small state machine. Path requests go to Godot's NavigationServer3D asynchronously.
- **Life cycle**: aging with compressed years (1 year ≈ 2 real minutes at 1× speed, speed up to 500×), courtship → pairing → children with gene crossover + mutation, death (age, starvation, violence, disease). Starting from 2 NPCs reaches ~200 in a few generations; time controls make that minutes, not hours.
- **Society**: households → settlements (clusters of homes) → factions (shared leader, laws, religion). Leadership emerges from relationship graph centrality. Trade when settlements have surplus/deficit; conflict when territory, food or grievance scores cross thresholds.
- **Knowledge system** (the "learning" requirement):
  - `data/concepts.json`: a graph of concepts (fire, farming, pottery, bronze, writing, irrigation, the wheel…) with prerequisites and *effect primitives* (`yield_mod`, `unlock_building`, `unlock_tool`, `unlock_action`, `belief`). ~80 to start.
  - Each NPC holds `Knowledge: Dictionary<ConceptId, proficiency 0..1>`.
  - **Transmission**: proximity chatter (slow), deliberate teaching (fast, requires trust), apprenticeship (child from parent), written records once `writing` is known (buildings store knowledge that survives deaths).
  - **Honing**: performing an action raises proficiency of its concept with diminishing returns; high proficiency improves the effect.
  - **Discovery**: an NPC with proficiency above threshold in all prerequisites of an unknown concept rolls per tick, weighted by curiosity. Thinking NPCs can additionally *invent* unlisted concepts via LLM (see Mind); the LLM must return effects only in the primitive vocabulary, so the sim can apply them.
  - Knowledge dies with its holders unless written down, so dark ages and rediscovery emerge.
- **Memory stream** (the "remembering" requirement, no LLM involved):
  - Every NPC records `Memory { tick, kind, subjects[], text template, importance 1..10, embedding? }` for events it witnesses within perception radius: births, deaths, gifts, fights, god actions, discoveries, conversations.
  - God actions (lightning, blessing, terraforming, spawning) are importance 8–10 and witnessed by everyone in range.
  - **Gossip**: when two NPCs converse, high-importance memories copy across with a distortion chance, so myths form.
  - Per NPC cap (e.g. 500) with eviction by low importance × age; evicted memories fold into a one-line summary so nothing vanishes silently.
  - Embeddings are computed lazily, only for NPCs promoted to thinking (via `/embed`, bge-m3, effectively free). Retrieval is brute-force cosine in C# over ≤500 vectors, no vector DB.
- **Event bus** feeds the Chronicle (world history log), the Mind layer, and the UI.
- **Save/load**: binary snapshot of the whole sim + JSON header; autosave; saves in `user://`.

## Cognition layer (Eden.Mind)

- **Spotlight selection** every in-game hour: score = proximity to camera/possessed NPC + leadership + recent dramatic events + player interaction recency. Top N (default 15, configurable) are "thinking". Promotion is free because memories were always being recorded.
- **Thought cycle** per thinking NPC (every few in-game hours, staggered): build prompt = world rules (cached prefix) + persona + current needs/goals + retrieved memories (recency × importance × relevance, Generative Agents formula) → `/think` → JSON `{ plan: [goals], say?: string, belief?: string }` → validated → pushed as intention.
- **Reflection** at in-game day end for thinking NPCs: summarise recent memories into 1–3 beliefs (importance 7+). Beliefs about the god become religion seeds.
- **Dialogue**: when the player (god or possessed) talks to an NPC, `/chat` with the same context; the exchange becomes a memory for that NPC and spreads by gossip.
- **Invention**: thinking NPCs with high curiosity and two or more mastered concepts may ask the LLM to propose a new concept constrained to the primitive vocabulary; sim validates and inserts it into the live concept graph.
- **Mind pool**: priority queue of cognitive tasks, concurrency 2–4, daily budget in Neurons/tokens, batching of several NPC thoughts into one call where the provider supports it, fallback to rules-only when budget or provider fails. Player-facing dialogue is always highest priority.
- **Provider interface** (`IBrainProvider`): `ThinkAsync`, `ChatAsync`, `EmbedAsync`. Implementations: `WorkerProxyProvider` (default, hits the Cloudflare Worker), `AnthropicProvider` (direct, model ids `claude-haiku-5-5` / `claude-sonnet-5-5`), `OpenAiCompatibleProvider` (OpenAI, Groq, OpenRouter, Ollama local, LM Studio). Settings panel: provider, base URL, model, key; key stored encrypted in `user://`. Pricing checked against live docs when the Anthropic adapter is written, not from memory.

## Brain Worker (brain/)

- Hono router, every input Zod-validated, every LLM output parsed through a Zod schema with one repair retry.
- `POST /think`, `POST /chat`, `POST /embed`; provider chosen by `BRAIN_PROVIDER` env with per-request override so the game's settings panel can route through the Worker too (keys for that path set with `wrangler secret put`).
- Default models: `@cf/qwen/qwen3-30b-a3b-fp8` for think/chat (~11 Neurons per thought ≈ 850 free/day), `@cf/baai/bge-m3` for embeddings.
- Budget: Worker returns remaining Neurons estimate in a header; client tracks daily spend locally and stops non-essential thoughts near the cap. Stateless, no DB, no KV.
- Shared-secret header so only the game can call it.

## Godot client (Eden.Game)

- **Terrain**: heightmap → `ArrayMesh` chunks with a biome splat shader; water plane; `NavigationRegion3D` baked per chunk at generation, rebaked locally when buildings change.
- **NPC rendering with LOD**: skinned glTF characters (Kenney/Quaternius packs, CC0) with `AnimationTree` for the ~50 nearest NPCs; `MultiMeshInstance3D` billboards/low-poly for the rest. Buildings as instanced meshes. Target 60 fps at 2,000 NPCs on a laptop.
- **God camera**: `Camera3D` on a rig with fixed pitch (~55°), WASD/edge pan, scroll zoom that also eases pitch slightly, no yaw. Click to select/inspect, drag for powers.
- **Possession**: click an NPC → "Possess": sim marks the NPC player-controlled, camera swaps to a `SpringArm3D` third-person rig (F toggles first person), `CharacterBody3D` controller drives its body; needs still tick; nearby NPCs react (memories of "the chief started acting strangely"). Escape returns to god view; the NPC remembers a blackout.
- **God powers v1**: spawn human/animal/plant, lightning, rain/drought, raise/lower terrain, bless/curse, drop item, whisper (send a message to one NPC, which becomes a high-importance memory).
- **UI** (Godot Control nodes): time controls (pause/1×/10×/100×/500×), NPC inspector (needs, traits, knowledge, relationships, memory list with importance, current thought), settlement panel, Chronicle timeline, Brain settings, budget meter.
- **Threading**: sim on a dedicated thread with double-buffered snapshots; Godot main thread only renders and sends commands.

## Milestones and verification

Each milestone ends with: `dotnet test` green, `godot --headless --path game --quit` boots clean, and the listed manual check.

- **M0 Tooling (you)**: install Godot 4.7.x .NET build and put `godot` on PATH; .NET 8 SDK; pick and configure a godot-mcp server that can run the project and return logs/screenshots; `claude mcp add` it. I verify by running the empty project headless and through the MCP.
- **M1 Headless genesis**: `Eden.Sim` with world gen, two NPCs, needs, gathering, pairing, children, aging, death; console runner prints population per year. Check: from 2 NPCs, population reaches 50+ within 10 generations on most seeds, and some seeds die out (so it is not scripted). xUnit tests for genetics, needs, utility scoring, determinism (same seed → same hash after 10k ticks).
- **M2 Island on screen**: Godot renders terrain, water, trees, NPCs (LOD), god camera, time controls, inspector. Check: 1,000 synthetic NPCs at 60 fps; MCP screenshot attached.
- **M3 Society**: homes, settlements, jobs, farming, storage, trade, leadership, conflict. Check: two or more settlements appear from a 2-person start, at least one trade and one raid in the Chronicle within 20 generations.
- **M4 Knowledge**: concept graph, transmission, honing, discovery, written records. Check: a tech reaches a second settlement only after contact; killing all knowers of a concept with no writing removes it from the world.
- **M5 Minds**: memory stream, brain Worker deployed, provider interface, spotlight selection, think/reflect cycle, dialogue UI. Check: talk to a leader after striking their village with lightning and they reference it unprompted; switch provider to a pasted Anthropic key and the same conversation works; budget meter stops background thoughts at the cap.
- **M6 Possession**: controller, camera swap, interactions (talk, give, work alongside), NPC reactions. Check: possess, walk to another village, trade, release; both NPCs' memory lists show the encounter.
- **M7 Myth and god**: whisper power, gossip distortion, belief/religion emergence, Chronicle. Check: after repeated blessings of one settlement, their reflections mention the god favourably and a neighbour's mention envy or fear.
- **M8 Scale pass**: profile at 5,000 NPCs, spatial hashing, batching thoughts, chunked nav rebakes, save size. Check: 5,000 NPCs at ≥ 30 fps, save/load under 2 s.

## Risks and mitigations

- **Two-person start is slow early**: time acceleration up to 500× and compressed lifespans; optional "rare wanderer arrives" toggle (off by default) for genetic variety.
- **GDScript-only tutorials**: C# API is one-to-one with the docs; I pull the 4.7 class reference from the web rather than memory.
- **LLM outputs that break the sim**: everything is a Zod/C# schema-validated intention in a fixed vocabulary; invalid output is dropped, never applied.
- **Free-tier cap hit mid-session**: graceful rules-only fallback; player-facing dialogue reserved first.
- **Workers AI model quality**: provider swap is a settings change, no code change.

## Multi-agent development layout

This file is the master plan. On approval it is split into per-module specs inside the repo so
parallel agents (worktrees) and future sessions each get a narrow, self-contained brief.

```
~/Projects/eden/docs/specs/
  00-architecture.md        this plan, kept current
  10-sim-core.md            Eden.Sim: world, entities, utility AI, life cycle, society,
                            knowledge, memory stream, save format, xUnit test list
  20-mind.md                Eden.Mind: spotlight scoring, prompt assembly, retrieval formula,
                            reflection, mind pool, IBrainProvider implementations
  30-brain-worker.md        brain/: routes, Zod schemas, provider adapters, budget header, auth
  40-game-client.md         Eden.Game: terrain, LOD rendering, god camera, possession, UI, threading
  50-knowledge-data.md      concepts.json authoring rules and the effect-primitive vocabulary
  contracts/                written FIRST, before any parallel work:
    IWorldSnapshot.cs, Intention.cs, IBrainProvider.cs   (C# interfaces, in Eden.Sim/Eden.Mind)
    think.schema.json, chat.schema.json, embed.schema.json (shared by C# and Worker)
    concepts.schema.json
```

Each spec has the same shape: purpose, public interface (points at `contracts/`), data formats,
acceptance tests, explicit "do not touch" list, and the milestone it belongs to. `AGENTS.md` at
the repo root carries conventions; an agent's brief is "execute docs/specs/<file>, milestone Mx".

Parallelism plan:

| Phase | Sequential or parallel | Agents |
|---|---|---|
| M0 tooling + contracts | sequential | 1 |
| M1 sim core, M2 render shell, brain Worker skeleton | parallel in 3 worktrees | 3 |
| M3 society, M4 knowledge | parallel (both inside Eden.Sim, separate files) | 2 |
| M5 minds | sequential (needs Sim + Worker) | 1 |
| M6 possession, M7 myth | parallel | 2 |
| M8 scale pass | sequential | 1 |

Merge rule: a worktree merges only when its spec's acceptance tests pass and it changed nothing
outside its module except `contracts/` (which requires a note in the PR).

## First concrete steps once approved

1. `mkdir ~/Projects/eden`, init git, write AGENTS.md, README, and `docs/specs/` from this plan.
2. Write `docs/specs/contracts/` (interfaces and JSON schemas) and get them compiling/validating.
3. `game/`: Godot .NET project, solution with Eden.Sim / Eden.Mind / Eden.Game / tests; CI script running `dotnet test` and a headless boot.
4. `brain/`: Hono Worker with `/embed` and `/think` against Workers AI, Zod schemas, `wrangler dev` smoke test.
5. Start the M1 / M2 / Worker parallel phase.
