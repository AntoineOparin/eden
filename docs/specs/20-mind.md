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
through a `SetThinkingCommand` (add to `ISimHost.cs` with a PR note). Promotion is free because
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

## Carried-over items

See `90-backlog.md`; the rows naming your milestone are part of your scope.

## Do not touch

- `game/src/Eden.Sim/**` except by adding an intention type or command in `contracts/` with a note.
- `game/src/Eden.Game/**` (the settings panel and key storage are theirs; you expose
  `BrainSettings` and `Status`).
- `brain/**`.
