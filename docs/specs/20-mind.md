# 20 — Eden.Mind: cognition layer

**Milestone:** M5 (minds), extended in M7 (myth). Lives in `game/src/Eden.Mind/`. References
`Eden.Sim` only; no Godot.

## Purpose

Decide *which* NPCs get an LLM, *what* they are asked, and turn the answer into validated
`Intention`s for the sim. Never blocks the sim. Budgeted. Provider-agnostic.

## Public interface

- Implements `IBrainProvider` three times (`contracts/IBrainProvider.cs`):
  `WorkerProxyProvider` (default, calls `brain/`), `AnthropicProvider` (direct,
  `claude-haiku-5-5` default, `claude-sonnet-5-5` optional), `OpenAiCompatibleProvider`
  (OpenAI, Groq, OpenRouter, Ollama, LM Studio: base URL + model + key). `NullBrainProvider`
  is the rules-only fallback. Pricing and model ids for direct providers are checked against the
  live docs when written, not from memory.
- `MindPool` : consumes `IWorldSnapshot` + a `ISimHost` and pushes intentions through
  `ISimHost.Intentions`. Public surface: `Start()`, `Stop()`, `Settings`, `Budget`,
  `AskAsync(ChatRequest)` for the dialogue UI, `Status` for the budget meter.
- Reads `BrainSettings` (contract) supplied by Eden.Game; never touches `user://` itself.

## Design

**Spotlight.** Every in-game hour: score every living NPC =
`2·proximityToCamera + 3·isLeader + 2·recentDrama(24h) + 4·playerInteraction(1 day decay) + 1·isPossessedNeighbour`.
Top `MaxThinkingNpcs` (default 15) are thinking; the sim flag `NpcView.IsThinking` is set
through `SetThinkingCommand(ids)` (in `ISimHost.cs` since M5; display only, not hashed or saved). Promotion is free because
memories were always being recorded; on promotion, missing embeddings are requested via
`/embed` in batches of ≤ 64.

**Retrieval.** Generative Agents formula: `score = α·recency + β·importance + γ·relevance`,
recency = `0.995^hoursSince`, importance = `/10`, relevance = cosine(query, memory) with
brute-force over ≤ 500 vectors; α=β=γ=1, top 12 memories into the prompt.

**Thought cycle.** Per thinking NPC every 4 in-game hours, staggered by id: assemble
`ThinkRequest` (world rules = cached constant string, persona from traits, needs, goals, the
retrieved memories, `situation` from the last 10 Chronicle entries in range) → provider →
`ThinkResponse` → `IntentionMapper` → `GoalIntention`s (priority 2), optional `SayIntention`,
`BeliefIntention`, `InventionIntention` (only if `AllowInvention`, which requires curiosity ≥ 0.7
and ≥ 2 concepts mastered ≥ 0.8). Anything the sim's validator rejects is logged, never retried.

**Reflection.** At in-game day end for thinking NPCs: a `ThinkRequest` with
`situation = "Reflect on your day and state 1–3 beliefs"` whose `belief` output gets
importance ≥ 7. Beliefs mentioning the god are tagged for religion emergence (M7).

**Dialogue.** `AskAsync` builds a `ChatRequest` with the same retrieval, bypasses the queue
(highest priority), and when the reply arrives: a `SayIntention` (so neighbours hear) and the
`memory` field becomes a `BeliefIntention` of kind memory.

**Mind pool.** Priority queue (dialogue > reflection > thought > embeddings), concurrency
`Settings.Concurrency` (default 3), per-task timeout 20 s, daily budget in Neurons tracked
locally (`BrainUsage.Neurons` summed, reset at local midnight, persisted by Eden.Game). At
90 % budget: only dialogue. Provider failures: exponential backoff 5 s → 5 min, status
visible in the budget meter, sim unaffected.

**Serialization.** `BrainJson.Options`: camelCase, `JsonStringEnumConverter(camelCase)`,
unknown fields rejected for responses, nulls allowed. Already present in `BrainJson.cs`.

## Acceptance tests (xUnit, in `game/tests/Eden.Sim.Tests/Mind/`)

- `SpotlightTests`: scores rank a struck-by-lightning leader above an idle farmer.
- `RetrievalTests`: formula picks a relevant old memory over an irrelevant recent one.
- `IntentionMapperTests`: every `think.response.*.json` fixture maps or is rejected as expected.
- `MindPoolTests`: with a fake provider that delays 10 s, the sim thread keeps ticking and the
  pool respects concurrency and budget; dialogue preempts.
- `WorkerProxyProviderTests`: against a stub `HttpMessageHandler`, request bodies match the
  `*.request.ok.json` fixtures, 401/422/5xx map to `BrainFailure`.

Manual (M5): strike a village, talk to its leader, they reference the lightning unprompted;
switch to a pasted Anthropic key and the same conversation works; budget meter halts
background thoughts at the cap.

Also covered: `PromptContextTests` (built requests validate against the schemas, time-tagged
memory lines, "still shaken" persona) and `DirectProviderTests` (Anthropic / OpenAI-compatible
request shapes, repair retry, error mapping, USD pricing) against stub handlers. The Sim-side
`DialogueMemoryTests` shows the god's words spreading by the existing gossip.

## As built (M5, 2026-11-06): where reality differs from the design above

- **Files.** `MindPool.cs` (scheduler + dialogue), `Spotlight.cs`, `Retrieval.cs` (+ `EmbeddingCache`),
  `IntentionMapper.cs`, `PromptContext.cs` (NpcContext / situation / memory lines), `BudgetLedger.cs`,
  `MindStatus.cs` (+ `BrainDefaults`), `ProviderFactory.cs`, `WorkerProxyProvider.cs`,
  `Providers/` (`DirectProvider` base, `AnthropicProvider`, `OpenAiCompatibleProvider`,
  `PromptBuilder` = C# port of `brain/src/prompts.ts` + `json-repair.ts`, `UsdPricing`).
- **What the Mind reads.** Only `ISimHost.Latest`. Facts the spotlight needs that the contract
  lacks (leaders, settlement names, newest importance ≥ 7 memory tick, last 32 Chronicle entries,
  ticks per hour) come from `Eden.Sim.IMindSnapshot`, implemented by `WorldSnapshot` (not a
  contract; a `FakeSimHost` snapshot simply lacks it and those terms score 0). NPC details come from
  `TryGetNpcDetail`; the Mind requests details only for spotlight NPCs (≤ `MaxThinkingNpcs`), which
  fits the sim's 32-entry watch list next to the inspector.
- **Spotlight terms.** proximity = `1 − dist/200 m` to the camera focus (0 beyond); recentDrama =
  linear decay over one in-game day since the newest importance ≥ 7 memory; playerInteraction =
  linear decay over one day since the god talked to, whispered to or selected the NPC;
  possessed neighbour = within 24 m (perception radius). Infants and the possessed NPC never think.
- **Embeddings live in the Mind**, cached per memory text (`EmbeddingCache`), never in sim state
  (`Memory.Embedding` stays unused, so embeddings cannot affect determinism or saves). A thought sends
  one `/embed` with the query plus up to 63 uncached memories; promotion embeds the rest in batches of
  ≤ 64. Without embeddings (Anthropic, failures) relevance falls back to word overlap (Jaccard).
- **Thought pacing.** Every 4 in-game hours staggered by `(hour + id) % 4`, plus a real-time floor of
  60 s between two thoughts (and two reflections) of one NPC: at 1× an in-game hour is 0.5 s, so
  without it 15 NPCs would spend the free tier in under half an hour. Pending work is de-duplicated
  per (kind, NPC).
- **Reflection** returns one belief (the think schema has a single `belief`), raised to importance
  ≥ 7; beliefs mentioning the god/voice/sky are kept in `MindPool.GodBeliefs` for M7.
- **Prompt context.** Memory lines carry a "when" prefix ("Just now:", "3 days ago:"); the persona
  ends with "Still shaken by what happened (just now): …" for the newest importance ≥ 8 memory of the
  last 6 in-game hours. Without that, `/chat` (which has no situation field) answered past a fresh
  lightning strike in 2 of 3 manual runs; with it 2 of 2 referenced it.
- **Dialogue path.** `AskAsync(npcId, message)` (the `AskAsync(ChatRequest)` overload is the raw
  bypass). After the reply: the god's line is sent as a `WhisperCommand` (importance-9 memory, also
  on failure: the NPC heard it), the reply as a `SayIntention` (clipped to 280 chars), the `memory`
  field as a `BeliefIntention` raised to importance ≥ 6 so the existing gossip spreads it. History
  (≤ 40 turns, last 20 sent) is kept per NPC in the Mind.
- **Budget.** Background work stops at 90 %; dialogue continues until 100 % of `DailyNeuronBudget`
  (default 9000 of the free 10,000). Successful calls are booked from `usage.neurons`; the Worker
  provider also books the Neurons of 422 answers and the `X-Eden-Usd-Cost` header. USD is shown,
  not capped (`BrainSettings` has no USD budget).
- **Failures.** Network / Auth / Budget failures and timeouts back off 5 s → 10 s → … → 5 min (reset on
  the next success); invalid output is logged and dropped without backoff; `Unsupported` on `/embed`
  turns embeddings off for that provider.
- **Status** is `MindStatus` (provider, model, state Off/Ok/Backoff/DialogueOnly/Exhausted, Neurons and
  USD today, thinking, queued, in flight, completed, failures, last error).

## Carried-over items

See `90-backlog.md`; the rows naming your milestone are part of your scope.

## Do not touch

- `game/src/Eden.Sim/**` except by adding an intention type or command in `contracts/` with a note.
- `game/src/Eden.Game/**` (the settings panel and key storage are theirs; you expose
  `BrainSettings` and `Status`).
- `brain/**`.
