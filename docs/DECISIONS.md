# Decision log

Dated, append-only. One entry per decision that is not obvious from the code, with the reason
and the evidence. Agents: when you choose between real alternatives, add an entry in the same PR.
Newest at the bottom. Format: `## YYYY-MM-DD — title`, then **Decision**, **Why**, **Evidence**,
**Revisit when**.

## 2026-11-06 — Client is Godot 4.7.2 .NET on .NET 10, backend is a Hono Worker on Cloudflare free tier
**Decision:** C# for everything client-side, TypeScript on Workers for the brain, Workers AI as the
default LLM behind a provider interface. (From the approved plan, `specs/00-architecture.md`.)
**Why:** Antoine's constraints: free tier, local saves, no money, any provider swappable from a
settings panel. **Evidence:** plan review conversation. **Revisit when:** never for v1.

## 2026-11-06 — All .NET projects target net10.0
**Decision:** `net10.0` everywhere, including the Godot-SDK projects, pinned explicitly in each
Godot csproj. **Why:** the .NET 10 SDK/runtime is the only one installed and Antoine asked not to
downgrade; GodotSharp 4.7.2 ships net8.0 assemblies that load fine under 10. The Godot editor
inserts `net8.0` into any Godot-SDK csproj that lacks an explicit TargetFramework, which silently
broke a restore once. **Evidence:** gdUnit4 run on 2026-11-06 rewrote the test csproj.
**Revisit when:** Godot ships an SDK that targets net10 natively.

## 2026-11-06 — `godot` on PATH is a wrapper script, never a symlink
**Decision:** `~/.local/bin/godot` execs the real binary. **Why:** Godot resolves `GodotSharp/Api`
relative to its executable path; via a symlink it prints `ERROR: .NET: Assemblies not found` and
segfaults in both play and editor mode. **Evidence:** headless boot before/after the swap.
**Revisit when:** Godot resolves symlinks (check release notes).

## 2026-11-06 — Contracts live in docs/specs/contracts/ and are linked into the csproj files
**Decision:** the C# interfaces are compile items linked from `docs/specs/contracts/`; JSON schemas
are the single wire format shared with the Worker; fixtures are validated by ajv, JsonSchema.Net
and Zod. **Why:** one source of truth, three validators that must agree, and parallel agents can
be told "do not touch contracts without a PR note". **Evidence:** `ContractTests.cs`,
`brain/src/schemas.test.ts`, `scripts/validate-contracts.mjs`. **Revisit when:** never.

## 2026-11-06 — xUnit 2.x on VSTest, not xunit.v3
**Decision:** `Eden.Sim.Tests` uses xunit 2.9 + VSTest. **Why:** gdUnit4's adapter is VSTest-only
and the .NET 10 SDK cannot mix VSTest and Microsoft.Testing.Platform projects in one `dotnet test`.
**Evidence:** the MTP error on the first `dotnet test` run. **Revisit when:** gdUnit4Net ships an MTP
adapter.

## 2026-11-06 — Eden.Game.Tests is a mirror Godot project that compiles the game scripts itself
**Decision:** the gdUnit4 test folder has its own `project.godot`, symlinks `scenes/src/data/assets/shaders`
to the real ones, and compiles `src/Eden.Game/**/*.cs` into the test assembly instead of referencing
`Eden.Game.csproj`. **Why:** gdUnit4 launches Godot with the test project directory as the Godot
project, and Godot only binds C# scripts from that project's own assembly (a scene instantiated
with a referenced-assembly script came back as a plain `Node`). **Evidence:** test runs on
2026-11-06. **Revisit when:** Godot binds scripts from referenced assemblies.

## 2026-11-06 — Worker is stateless; budget is tracked by the client
**Decision:** no KV/D1/DO; the Worker returns `X-Eden-Neurons-Cost` (and `X-Eden-Usd-Cost` for paid
providers) per request and the game sums them against a local daily budget. **Why:** free tier, no
money, nothing to leak. The plan's "remaining Neurons header" is impossible without state.
**Evidence:** `specs/30-brain-worker.md`. **Revisit when:** multiplayer or shared budgets exist.

## 2026-11-06 — Default think model is @cf/google/gemma-4-26b-a4b-it
**Decision:** Gemma 4 replaces the plan's qwen3 as default; qwen3 and glm-4.7-flash stay as priced
alternatives. **Why:** qwen3's model page lists a planned deprecation on 2026-11-20. A live 3-run
comparison on the think fixture: Gemma 5.3–6.0 Neurons/thought, 2.1–3.6 s, best use of memories and
situation, varied wording; GLM 4.9–5.2 Neurons but 5–6 s and flat plans; qwen3 4.6–4.8 Neurons,
1.3 s, near-identical output across runs. Cost gap is ≈150 thoughts/day out of ≈1,500 on the free
tier. **Evidence:** this log's author ran it; numbers also in `specs/30-brain-worker.md`.
**Revisit when:** prompts exceed ~1,500 tokens (GLM's input rate is 40 % lower) or Gemma quality
disappoints in M5 play.

## 2026-11-06 — Reasoning models get `enable_thinking: false`; answers may arrive in `reasoning_content`
**Decision:** the Workers AI adapter sends `chat_template_kwargs.enable_thinking=false` for
qwen3/gemma-4/glm-4 and reads the first non-empty of `content`, `reasoning_content`, `reasoning`,
`response`. **Why:** without the flag qwen3 spent the whole token budget thinking and returned no
JSON; with it, Workers AI's parser still filed the answer under `reasoning_content` with
`content: null`. **Evidence:** live smoke tests on 2026-11-06. **Revisit when:** Cloudflare fixes the
parser (then the fallback is harmless).

## 2026-11-06 — `X-Eden-Usd-Cost` header kept
**Decision:** the Worker agent added a USD cost header for Anthropic/OpenAI models; Antoine kept it.
**Why:** the budget meter needs dollars next to Neurons for paid providers; additive, no body change.
**Revisit when:** never.

## 2026-11-06 — Parallel agents inside one module get explicit file ownership
**Decision:** when two agents share `Eden.Sim` (M3 society, M4 knowledge), each brief names the files
it owns, the shared files it may edit only additively (hooks, appended save sections, new tunables),
and the files it must not touch; shared ids (the ten building kinds) are fixed in both briefs.
Merge order is fixed in advance and `./scripts/check.sh` runs after every merge. **Why:** M1/M2/Worker
had zero overlapping files; M3/M4 had eight, and all conflicts were "both added" hunks that merged
in minutes because of this rule. **Evidence:** merge commits `0f615ba`, `e43104f`.
**Revisit when:** a merge takes longer than the work it merges.

## 2026-11-06 — Knowledge gates society actions, and stays on in the acceptance test
**Decision:** build, farm, craft, teach, learn, trade, raid, settle and invent score 0 and are rejected
by the validator until a concept unlocks them (`SimConfig.KnowledgeGatesActions`, default true).
At the M3/M4 merge, isolated society mechanics tests run with the gate off; the Slow society
acceptance test keeps it on and still passes 18/20 seeds. **Why:** it is the rule the plan and the
validator always had, and it is what makes "knowledge matters" true in play. **Evidence:** merged
sweep, `docs/STATUS.md`. **Revisit when:** the balance pass after M5 (see `specs/51-knowledge-roadmap.md`).

## 2026-11-06 — M3/M4 seams live in one file
**Decision:** `World.Integration.cs` is the only place where society and knowledge call each other
(BuildingKinds as `IBuildingUnlocks`, archives host written records, farm yields use knowledge yield
factors). **Why:** easy to find, easy to reason about at the next merge. **Revisit when:** a third
module needs the same seams.

## 2026-11-06 — No deploy, no paid Cloudflare features without Antoine's go-ahead
**Decision:** `wrangler deploy` has never been run; all end-to-end checks use `wrangler dev` on
localhost with the shared key from `.dev.vars.example`. **Why:** Antoine's rule. **Revisit when:**
he says deploy.

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

## 2026-11-06 — M7 myth: god standing numbers
**Decision.** Per settlement `Favour` −1..1 and `Awe` 0..1. A blessing moves Favour 12 % of the way to +1
(a curse toward −1) and adds 3 % Awe; lightning adds 15 % Awe and drought 10 %, and each adds 0.08 to a
pending "wrath" that is subtracted from Favour (as a fraction of the gap to −1) 3 days after the first
unforgiven harsh act, unless rain or a blessing comes first. Rain +6 % Favour; spawn/terraform +8 % Awe;
gifts +4 % Favour; a whisper +5 % Awe in the listener's settlement. Daily decay ×0.995 Favour (half-life
≈ 14 years, a generation) and ×0.99 Awe (≈ 7 years). Stance: favourable above +0.15, else fearful at
Awe ≥ 0.35, else hostile below −0.15, else neutral. All in the `SimConfig` M7 block.
**Why.** Ten daily blessings must land Favour > 0.5 (they give ≈ 0.71) and ten strikes must read as fear
(Favour ≈ −0.57, Awe ≈ 0.74); the grace period is the spec's "unless followed by rain/bless within N days".
**Evidence.** `MythTests` (10/10 seeds: Favour 0.71 after a year of blessings; lightning test −0.57/0.74).
**Revisit when.** The god-powers UI lands and players can spam powers; then cap per-day gains.

## 2026-11-06 — M7 myth: gossip distortion and god attribution
**Decision.** One main-stream roll per retelling, as in M1: < 0.15 subject swap (the teller replaces the first
subject, and the old name in the text), < 0.35 importance ±1 (never a no-op at 10), and < 0.35 + 0.3 × the
teller's settlement Awe the god is credited for a death, raid, discovery or retold story that did not involve
it (GodId prepended to the subjects, " The god willed it." / " The angry sky did this." appended). At most one
distortion per hop. Gossip copies are now retold too (M1 never retold gossip), up to 6 hops, without nesting
"X told me:" prefixes; `Memory.Generation` counts hops and `Memory.Distortion` records the last change. Both
live in the `MYTH` save section, not `MEMS`, so the M1 section format is unchanged.
**Why.** With Awe = 0 the attribution band has zero width, so a world where the god never acts draws exactly
the same random numbers in gossip as before; probability rising with Awe is the brief's rule, and 0.3 at full
Awe keeps most retellings faithful. Myths need more than one hop to form.
**Evidence.** `GossipTests` (each distorted copy differs in exactly one way, Generation + 1; 0 attributions at
Awe 0, more at Awe 1 than 2 × Awe 0.3). Seed 20, 80 years: 5,736 distorted retellings in the last decade.
**Revisit when.** Memory streams fill with hearsay (cap 500 evicts it first, importance × age).

## 2026-11-06 — M7 myth: beliefs and religions without the LLM
**Decision.** Belief formation is a rule: daily, an adult with a god memory from the last 100 days and no god
belief yet forms one (importance 6, tone from the stance) with chance 0.08 × (0.5 + piety) × min(1, Awe +
|Favour|). "Holds a god belief" = has a belief memory whose text has a god word (the M5 list: god, voice, sky,
heaven, divine, spirit, the one above), tracked per slot in `MemoryStore` and kept exact on eviction. A faction
founds a religion when ≥ 40 % (and ≥ 3) of one settlement's adults hold one; if that settlement is in contact
with a religious settlement it takes up that faith instead. Spread: 3 %/day per pair of settlements in contact
into a faction without a faith. Schism: the capital's Favour opposes the faith's stance for `SchismYears` (14,
the adult age) → a new faith with the current stance. An alliance keeps the absorbed faction's faith if the
bigger one had none. Myth rolls (beliefs, spread, names) use their own seeded stream, saved in `MYTH`.
**Why.** The acceptance test runs with the Mind off, so religion must emerge from witnessed god acts alone;
concept beliefs ("The sky watches and judges.") count, per the brief. The separate stream keeps the main
trajectory of god-free worlds unchanged until a religion actually appears.
**Evidence.** 10/10 seeds found a favourable faith 5–16 days into the blessing year. Seed 20 with no god at
all: a faith at year 17 from concept beliefs, 3 founded and 8 shrines by year 80; with a year of lightning: a
schism at year 31 into "the Defiance of …" (hostile).
**Revisit when.** Natural faiths look too early or too uniform once the knowledge pace pass lands (K1).

## 2026-11-06 — M7 myth: contact, envy and fear
**Decision.** Two settlements are in contact for 30 days after a talk between their members, a trader or
raider on the road between them, or an NPC whose settlement changed since yesterday. In contact, each
settlement looks at its most favoured neighbour: gap = their Favour − max(0, own Favour); Envy moves 15 %/day
toward gap × (1 − their Awe), Fear toward gap × their Awe; otherwise both decay ×0.98. Envy adds
0.02 × Envy grievance per day toward that neighbour.
**Why.** The brief asks for neighbours to learn of favour "through trade contact and gossip"; envy feeding the
M3 grievance lets favouritism cause raids without new conflict code.
**Evidence.** `MythTests.A_neighbour_in_contact_envies_or_fears_the_blessed_settlement`: Envy 0.35, Fear 0.09.
**Revisit when.** Envy-driven raids show up too often in sweeps with god powers on.

## 2026-11-06 — M7 myth: files outside the brief's list
**Decision.** Three tiny edits outside the M7 file list: `World.cs` `MindExtras()` sets
`GodStandings = GodStandingViews()` (one line), `WorldSnapshot.cs` gains `GodStanding(int)` (one member), and
`MindView.cs` gains `GodStandingView` + a default `IMindSnapshot.GodStanding` member; and a one-line fix in
`World.Persistence.cs` `KnowledgeHash`: an empty knowledge dictionary now hashes like a missing one.
**Why.** The Mind reads only the snapshot, and the snapshot is built in those files; nothing else carries
settlement data to it. The hash fix: after Talk, a slot can hold an empty dictionary that a load leaves null,
so save → load changed `StateHash` (found by `MythTests.Myth_state_survives_a_save_round_trip`). It is not
myth-specific and touches no knowledge behaviour.
**Evidence.** All 238 fast + 2 Slow Sim tests pass; the edits are away from A1's `Snapshot()` changes.
**Revisit when.** Merging A1/K1: re-check those lines if git reports a conflict.

## 2026-11-06 — M7 myth: shrines wait for an established village
**Decision.** A religious settlement plans one shrine only with ≥ 20 members, a finished storehouse and
hungry share ≤ 0.2 (`ShrineMinMembers`); the shrine site is then outside the regular site cap. Pray scores
×1.6 within 60 m of one's own shrine, plus 0.1 × piety for believers of a religious faction; a prayer at the
shrine adds 0.15 purpose and, in a religious faction, renews the god belief at most every 5 days.
**Why.** The first version planned a shrine as soon as a 6-person village found a faith (natural faiths
appear early, from concept beliefs). Sweeps showed those villages short of builders and stone: more hunger,
more trades (which erase grievance) and raids pushed past year 90 on seeds 3, 5, 6. With the gate, a fixed
70-year comparison (seeds 1, 3, 5, 6; all / no religion / no shrine) shows no systematic effect on raids:
first raids land between years 60 and 70 or not at all in each variant.
**Evidence.** Seed sweep 1..20 × 110 years: 17 of 18 surviving seeds raid (years 49–99); seed 1 does not.
**Revisit when.** The M3 Slow test horizon (until 20 generations) makes a no-raid seed run very long; see
backlog.
