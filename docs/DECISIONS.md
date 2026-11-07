# Decisions

## 2026-11-06 — M5: `SetThinkingCommand` replaces the whole spotlight set
**Decision.** `SetThinkingCommand(IReadOnlyList<int> NpcIds)` in `contracts/ISimHost.cs` (pre-approved
contract change). The sim keeps the set in memory only: it is not hashed and not saved.
**Why.** One command per spotlight change is simpler than per-NPC on/off. The flag is display state.
Keeping it out of the hash keeps determinism tests independent of the Mind.
**Evidence.** `SpotlightTests.SetThinkingCommand_flags_npcs_in_the_snapshot_without_touching_the_hash`.
**Revisit when.** The sim needs to behave differently for thinking NPCs.

## 2026-11-06 — M5: Mind-only facts come through `IMindSnapshot`, not the contract
**Decision.** Leaders, settlement names, last-drama tick, recent Chronicle and ticks per hour are
exposed by `WorldSnapshot` through the non-contract `Eden.Sim.IMindSnapshot`. Consumers treat it as
optional, so `FakeSimHost` still works.
**Why.** The spotlight needs `isLeader` and `recentDrama`. `NewChronicle` is drained per snapshot, so a
poller misses entries. Lightning chronicle entries name only the god, not the witnesses. Adding all
this to `IWorldSnapshot` was not pre-approved.
**Evidence.** `SpotlightTests.Lightning_shows_up_as_drama_in_the_mind_snapshot`.
**Revisit when.** Godot needs the same data (settlement panel, M7). Then promote it to the contract.

## 2026-11-06 — M5: embeddings stay in the Mind, keyed by memory text
**Decision.** `EmbeddingCache` (20k entries, cleared when full) instead of `Memory.Embedding`.
**Why.** Writing sim objects from the Mind thread would be a data race, and embeddings must never touch
sim state or saves. A shared text (a strike witnessed by a whole village) needs one vector.
**Evidence.** `RetrievalTests`; the live session made 82 `/embed` calls for 72 thoughts.
**Revisit when.** Memory text stops being a stable identity, for example if gossip rewrites text in place.

## 2026-11-06 — M5: spotlight term shapes
**Decision.** The weights are as specified (2/3/2/4/1). proximity = 1 − dist/200 m. drama and player
interaction decay linearly to 0 over one in-game day. Possessed neighbour = within 24 m. Infants and
the possessed NPC are excluded.
**Why.** The spec fixes the weights but not the term shapes. 200 m is about the default god-camera
view, and 24 m is the sim's perception radius.
**Evidence.** `SpotlightTests` (a struck leader beats an idle farmer by more than 4.5).
**Revisit when.** Playtests show the spotlight lagging the camera.

## 2026-11-06 — M5: 60 s real-time floor between background thoughts of one NPC
**Decision.** On top of "every 4 in-game hours", each NPC gets at most one thought (and one
reflection) per 60 real seconds.
**Why.** At 1× an in-game hour is 0.5 s. Fifteen NPCs every 4 hours would spend the 10,000 free
Neurons in under 30 minutes, and at 500× the queue would never drain.
**Evidence.** A fast-forwarded manual run cost 75–165 Neurons.
**Revisit when.** Thought batching (M8) or a paid provider makes thoughts cheap.

## 2026-11-06 — M5: timeouts and backoff
**Decision.** 20 s per task, with the provider call and its embed lookup together. The HTTP client cap
is 30 s. Network, Auth and Budget failures and timeouts back off 5 s, doubling to 5 min, reset on the
next success. Invalid output is dropped without backoff. `Unsupported` on `/embed` turns embeddings
off for that provider.
**Why.** This follows the spec. A model producing bad JSON is not a provider outage, so it should not
pause other NPCs.
**Evidence.** `MindPoolTests.Timeouts_back_off_exponentially_and_recover`.
**Revisit when.** Real providers rate-limit (429) often. Then honour `Retry-After`.

## 2026-11-06 — M5: budget rules
**Decision.** Background work stops at 90 % of `DailyNeuronBudget`. Dialogue continues until 100 %.
The default budget is 9000 of the free 10,000. USD is shown but not capped. Neurons from failed 422
calls are booked too.
**Why.** Player-facing dialogue is reserved first (plan). `BrainSettings` has no USD field.
**Evidence.** `MindPoolTests.At_ninety_percent_only_dialogue_runs_and_at_the_cap_nothing`.
**Revisit when.** Paid providers are used in earnest. Then add a USD cap to `BrainSettings`.

## 2026-11-06 — M5: dialogue writes through existing commands
**Decision.** The god's line goes in as a `WhisperCommand`, an importance-9 memory, sent even when the
provider fails. The reply goes in as a `SayIntention`, clipped to 280 characters. The model's `memory`
goes in as a `BeliefIntention` with importance at least 6.
**Why.** No new contract is needed. Importance ≥ 6 is the existing gossip threshold, so the exchange
spreads.
**Evidence.** `MindPoolTests.Dialogue_preempts_saturated_background_work` and
`DialogueMemoryTests.Gods_words_spread_by_gossip_when_the_npc_talks_to_a_neighbour`.
**Revisit when.** M6 adds possessed talk. That may want its own memory kind.

## 2026-11-06 — M5: prompt context gets "when" and "still shaken"
**Decision.** Each memory line carries a relative-time prefix ("Just now:", "3 days ago:"). The persona
ends with "Still shaken by what happened (just now): …", using the newest importance ≥ 8 memory from
the last 6 in-game hours. This deviates from 20-mind.md, which lists only the retrieved memories.
**Why.** `/chat` has no situation field, and raw ticks mean nothing to the model. Before the change,
2 of 3 manual runs answered past a fresh strike. After it, 2 of 2 named the lightning unprompted.
**Evidence.** STATUS M5 transcripts. `PromptContextTests`.
**Revisit when.** The chat schema gains a situation field.

## 2026-11-06 — M5: API key encryption at rest
**Decision.** The key is stored in `user://brain_settings.json` as base64(iv ‖ AES-256-CBC(PKCS#7)).
The AES key is 32 random bytes from `Crypto.GenerateRandomBytes`, kept in `user://brain.key`. A fresh
IV is used on every save.
**Why.** The spec asks for `Crypto`/`AesContext` and a machine-local key file. This keeps the key out of
plain text and saves. It is not a vault: anyone who has both files can decrypt the key.
**Evidence.** `MindUiTests.Settings_store_encrypts_the_key_and_reads_it_back`.
**Revisit when.** Keys need to sync across machines, or the OS keychain becomes reachable from Godot.

## 2026-11-06 — M5: direct providers mirror the Worker adapters
**Decision.** The C# `AnthropicProvider` mirrors the Worker adapter. It uses `anthropic-version:
2026-10-21` and sends no `temperature`. Thinking is `disabled` for Haiku 5.5 and `between_tools` for
Sonnet 5.5. Output uses `output_config.format = json_schema` without numeric or length bounds.
The default model is `claude-haiku-5-5`. USD rates per MTok are Haiku 5.5 $0.10/$0.50 (≤ 100k
prompt) and $0.50/$2.50 above that, Sonnet 5.5 $2/$10. These were re-checked on
platform.claude.com on 2026-11-06. The OpenAI-compatible provider keeps the Worker's model-name
heuristic. Both use the same prompts (C# port of `prompts.ts`) and one repair retry.
**Why.** A conversation then reads the same whichever path it takes, and the bugs found on one path
apply to the other.
**Evidence.** `DirectProviderTests` (stub handlers only; no key was available).
**Revisit when.** The first real key is pasted (backlog row).

## 2026-11-06 — M5: headless runs and tests never start a real brain
**Decision.** `Main` runs provider "none" with the fake sim, headless (unless `--brain` is passed), or
with `UseBrain = false`. The main-scene test sets `UseBrain = false`.
**Why.** `check.sh` and gdUnit run headless. A wrangler left running on :8787 would otherwise get
real calls from tests (AGENTS.md: no API calls from tests).
**Evidence.** Headless boot prints `Eden mind: provider none`.
**Revisit when.** Never for tests. A deployed URL changes only `BrainDefaults`.
