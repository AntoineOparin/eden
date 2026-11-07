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

## 2026-11-06 — K1: forgetting details the roadmap left open
**Decision.** Forgetting follows roadmap item 1 (`ForgetRatePerYear` 0.05, `ForgetGraceYears` 2), with
four choices the roadmap did not make. (1) "Practised" means any of: honed by a completed action
(even at p = 1), explained in chatter or a lesson, or raised by learning, apprenticeship or reading.
(2) The floor is the written level in the nearest record within `ForgetRecordRadius` (64 m, the
distance inside which writers do not found a second record), and only for an NPC that can read
(`writing` ≥ 0.3 or `pictographs` ≥ 0.6); an illiterate villager next to an archive gets no floor.
(3) Decay is asymptotic, so a third tunable `ForgetDropBelow` (0.05) sets p to 0 below it: the concept
is then forgotten, can be rediscovered, and if nobody else holds it a `knowledge-lost` entry is
written. Without it a concept would linger at 0.0001 forever and block rediscovery. (4) Last-practised
ticks are per NPC per concept index (`KnowledgeState.LastPractised`, arrays reused per slot), saved as
a versioned tail block inside KNW2 so old saves still load (their holders start the grace at load),
and mixed into `StateHash`.
**Why.** (1) matches "practising, teaching and reading already raise p". (2) "nearest readable record":
reading needs literacy everywhere else in the sim. (3) see above. (4) the brief: additive KNW2, no
per-tick cost (the pass runs in the existing daily knowledge hook and walks one array).
**Evidence.** `ForgettingTests` (formula, grace, practice, floor, loss, determinism, save round trip);
`KnowledgeTests.Save_and_load_round_trip_knowledge_records_tools_and_inventions` unchanged and green.
**Revisit when.** Settlement archives should floor everyone in the settlement, literate or not.

## 2026-11-06 — K1: era targets
**Decision.** On seeds 1..20, headless (no LLM inventions): the median seed first knows writing in
years 150–250 and bronze casting in years 300–450; no seed knows writing before year 75 or bronze before
year 150 (half of each band's lower bound). Copper and the early metals sit between the two. Tuning
aims a little late (writing ≈ 200–220 headless) because Mind inventions are assumed to add about 10 %
discovery pressure in a live game.
**Why.** The roadmap's suggestion. Before K1 the median seed wrote in year 64 and cast bronze in year 90,
so the whole graph was spent by year 130 and the late game had nothing left to find. Two to three
centuries between first fire and writing gives the god something to watch and to steer.
**Evidence.** STATUS "K1 knowledge balance" sweep tables (before/after). `PacingTests`.
**Revisit when.** Live LLM inventions are measured (then re-run the sweep with a Mind attached), or M8
changes population size, which moves every pace number.

## 2026-11-06 — K1: pace tunables
**Decision.** The lever is the shape of the difficulty curve, not the global rates. Discovery weight is
now `max(0.02, 1 − difficulty)^3` (`DiscoveryDifficultyExponent` = 3, int), and the late chain steps got
high difficulty: tally-marks 0.7, pictographs 0.94, writing 0.9 (unchanged, the authoring anchor),
copper-smelting 0.94, metalworking 0.94, bronze-casting 0.93, iron-smelting 0.97, smithing 0.95, plus
0.65–0.85 for the writing-era follow-ons. `DiscoveryRate` (1e-4), `ChatterRate` (0.04), `TeachRate`
(0.25) and `ApprenticeRatePerDay` (0.04) stay as they were.
**Why.** Population booms from ~50 to ~2,000 between years 40 and 90 (farming), so after that any
eligible concept with linear weight ≥ 0.02 is found within a few years by somebody. Lowering
`DiscoveryRate` or the transmission rates slows the 2-person early game just as much (and the M4 test
"discovers something within a few years" and the M1/M3 growth tests depend on it), while barely
delaying the late game. A cubic curve leaves easy concepts (difficulty ≤ 0.3) within a factor of 2–3
of before and makes the hard steps 100–1000× rarer. Splitting the delay over several hard steps
(pictographs → writing; copper → metalworking → bronze) instead of one very hard step keeps the
seed-to-seed spread down: a single 0.95 bronze step left 3 of 12 seeds without bronze at year 500.
Exponent 4 was tried and rejected: writing at 0.9 (fixed by the authoring rules) becomes a ~200-year
single step and several seeds cast bronze before writing.
**Evidence.** Sweeps (seeds 1..12, `--pace`): exponent 3 with the old difficulties gave writing ≈ 100,
bronze ≈ 140; exponent 4 gave writing 105–332 or never, bronze 149–369; candidate A (pictographs,
copper, metalworking 0.93, bronze 0.95) gave writing median 163, bronze median 392 with 3 seeds none by
500. Final values: STATUS K1 table.
**Revisit when.** Population scale changes (M8), or players report the late game as a wall.
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

## 2026-11-06 — M3 society acceptance test capped at 150 years
**Decision.** `SocietyTests.Two_settlements_trade_and_raid…` stops each seed at year 150 instead of a
600-year guard. **Why.** After M4 gating and M7 shrines the first raid moved later (years 62–147 across
seeds), and seed 1's first raid at year 131 with 1,900 people made the test take 8–21 minutes; the
assertions only need half the seeds, so a 150-year cap keeps 18/20 passing and the suite near 3 minutes.
**Evidence.** K1 and M7 reports, merge run of `./scripts/check.sh`. **Revisit when.** M8 changes
population growth, or the pace of the early game.

## 2026-11-06 — A1: asset packs (Kenney for everything, Quaternius dropped)
**Decision.** Characters: Kenney "Mini Characters" 1.0 (12 rigged GLBs, 7-bone rig, idle/walk/sprint/interact/
attack/die). Buildings: Kenney "Fantasy Town Kit" 2.0 pieces assembled per kind in `Art/BuildingFactory.cs`.
Nature, crops, rocks: Kenney "Nature Kit" 2.1. Terrain detail: five ambientCG 1K colour maps. UI: Nunito (OFL) and
Kenney game-icons. Colours of every pack are remapped at load to the reference palette (`Art/Palette.cs`).
**Why.** Quaternius "Ultimate Animated Character Pack" (the spec's first choice) is only on Google Drive, and the
Drive files answered "Quota exceeded" on 2026-11-06. Quaternius' itch.io character packs (Modular Outfits Fantasy,
Universal Base Characters) are 13k-triangle PBR models with 4K textures: too heavy for a 700-instance mid LOD and
not flat low-poly. Quaternius Stylized Nature MegaKit trees are 4–10k triangles with alpha cards. The Kenney packs are
flat-coloured, 100–700 triangles, one shared colormap per pack, and read as one family after the palette remap.
The mini characters are chibi-proportioned; that is the visible trade.
**Evidence.** `game/assets/*/SOURCE.md`; 11 MB of assets in total.
**Revisit when.** The Quaternius Drive quota resets or a CC0 human pack with normal proportions and glTF appears.

## 2026-11-06 — A1: assets load at runtime, no Godot import
**Decision.** `game/assets/` has a `.gdignore`. GLBs load through `GltfDocument.AppendFromFile`, images through
`Image.LoadFromFile`, the font through `FontFile.LoadDynamicFont`. Meshes are baked at startup into one-surface
vertex-coloured `ArrayMesh`es (`Art/MeshBaker.cs`); mid-LOD poses are CPU-skinned from the animation tracks
(`CharacterLibrary.Pose`).
**Why.** The gdUnit mirror project has its own `.godot/` and never runs an import, and `check.sh` boots headless on a
fresh clone. Runtime loading works identically in both and needs no committed `.import` files. Baking to one surface
makes every prop one draw call per MultiMesh.
**Evidence.** `ArtPassTests.Every_building_kind_has_a_mesh_and_the_assets_load_cleanly` (no loader errors); headless
boot clean; startup to first frame 0.96 s with the fake sim.
**Revisit when.** Exporting a build (M8): the `.gdignore`d files must be added as non-resource export files, or the
assets moved to imported resources.

## 2026-11-06 — A1: NPC LODs and scale
**Decision.** Hi LOD: the 50 nearest, skinned, `AnimationTree` (blend space idle/walk/sprint by speed under a
transition move/work/fight/down), palette colours by instance uniform, elders stooped by the torso bone. Mid LOD: the
next 700, baked poses (idle, walk A/B, work, fight, lying) of one model per sex in 12 MultiMeshes. Far LOD: the rest,
camera-facing quads from an 8 heading × stand/lying × 2 sex impostor atlas rendered once at startup. One unit is one
metre: adults 1.75 m, children 1.35 m, infants 1.0 m, elders 1.65 m; mid and far LODs grow to 1.6× between 60 m and
400 m camera height, hi LOD and buildings never scale. `BuildingKind.Footprint` is a clearance radius in the sim
(`SpotFree` keeps centres two footprints apart), so each building mesh keeps its half-extent within it.
**Why.** Spec 45 and the user's scale note. 700 mid slots keep the 2,000-NPC frame at ~270 fps with draw calls under
420; the mid female model is female-b because female-a carries a prop that doubles her silhouette width.
**Evidence.** `ArtPassTests` (LOD counts, heights 1.7–1.8 m, footprints, zoom scale); STATUS A1 table.
**Revisit when.** M8 scale work, or when mid NPCs need more than two silhouettes.

## 2026-11-06 — A1: foundations and render-side flattening
**Decision.** Buildings stand at the highest of five footprint samples on a stone plinth (earth bank for fields)
reaching the lowest sample minus 0.35 m; `TerrainBuilder` flattens only its render copy of the heights under each
footprint (one-cell apron half way) and paints dirt yards and roads to the settlement centre, rebuilding only the
touched chunks. Props and NPCs sample that render ground; small props tilt up to 15°, trees sink 2–5 %.
**Why.** User feedback (assets half-sunk); the sim's heightmap and pathing must not change.
**Evidence.** `ArtPassTests.Buildings_on_a_30_degree_slope_stay_above_ground_and_plinths_reach_it`,
`Terrain_flattens_only_its_render_copy_under_footprints`.
**Revisit when.** The sim's site picker prefers flat ground (backlog row), which will shrink the terraces.

## 2026-11-06 — A1: day-night pacing and performance trades
**Decision.** The light follows `HourOfDay` fully up to 2.5 in-game hours per real second and settles on a fixed
14:00 above 12 h/s (1× runs 2 h/s, so a day lasts 12 s and is shown; 10× and faster do not strobe). Night keeps a
0.32 moon light and 0.55 ambient. Traded for the budgets: terrain chunks cast no shadows; 2 PSSM splits at 420 m;
decor (grass, flowers, ferns) only within 150 m of the camera; mid NPC shadows on, far off; no SSAO/SDFGI/volumetrics.
**Why.** A full cycle at 500× is a strobe; the budgets in spec 45.
**Evidence.** STATUS A1 table.
**Revisit when.** Time scales or the day length change in the sim.

## 2026-11-06 — M7 UI: contract shape, GodStance name clash, Chronicle catch-up
**Decision.** `SettlementView` and `IWorldSnapshot.Settlements` exactly as pre-approved, filled in `World.Snapshot()`
from the same arrays as the Mind's `GodStandingViews()` (ids instead of names, -1 when absent or faint). The contract's
`GodStance` enum shares its name and byte values with `Eden.Sim.GodStance`; Eden.Sim converts with a byte cast, and code
outside `Eden.Sim` that imports both namespaces must qualify it (noted in `contracts/README.md`). `MemoryView.Generation`
is an optional positional parameter (default 0) so existing constructors and tests compile unchanged. The client keeps
its own Chronicle (`ChronicleLog`, ring buffer of 5,000) and, for the real sim, merges `IMindSnapshot.RecentChronicle`
(last 32) with `NewChronicle`, de-duplicated by tick and text, because the sim can publish several snapshots between
two frames and `NewChronicle` only covers the last one.
**Why.** The brief fixed the field list and the enum name; renaming the sim's enum would have touched myth code this
pass may not edit. Without the merge the timeline silently lost entries at 10× and faster.
**Evidence.** `SettlementViewTests` (3), `MythUiTests.Chronicle_log_*`.
**Revisit when.** A contract change is allowed: `NewChronicle` could carry everything since the previous *read*, and a
`TerritoryRadius` field would let the overlay stop reading `SimConfig` defaults.

## 2026-11-06 — M7 UI: settlement knowledge is sampled, not complete
**Decision.** "What a settlement knows" (settlement panel, knowledge-spread overlay) is estimated from a sample of
up to 8 adults (the leader first) whose `NpcDetail.Knowledge` the client requests one settlement at a time, cached per
in-game day with a 6 s real-time floor. The panel says how many were asked.
**Why.** Details exist only for the sim's bounded watch list (32), shared with the inspector and the Mind's spotlight;
asking for every member would evict them. No contract field carries per-settlement knowledge.
**Evidence.** `MythUiTests.Settlement_knowledge_samples_members_and_caches_per_day`; seed 2 year 51 shows "57 concepts
known among 8 of 11 adults asked".
**Revisit when.** The sim exposes per-settlement knowledge (a contract row in the backlog).

## 2026-11-06 — M7 UI: drawn power icons, overlay as draped discs, local effects only
**Decision.** The ten new toolbar icons (person, paw, sprout, rain, drought, raise, lower, star, skull, basket) and
three panel icons (scroll, house, map) are drawn in code like the A1 bolt (`UiTheme.M7Glyphs`), not Kenney PNGs. The
overlay is one polar-grid disc per settlement draped on the render terrain (radius `SettlementRadius × TerritoryFactor`
= 64 m), one shared unshaded material with a per-instance tint, rebuilt only when centres or the terrain change. Power
effects are CPU particles, a ripple ring shader, a light and (for weather, which is island-wide) a short screen tint.
Hotkeys are letters (H N T R Y U J B K G for powers, C / O / M for the panels) because 1–5 are the time controls.
**Why.** kenney.nl refused the download from this machine (403, bot check) and no further asset was needed; decals
would also tint buildings and NPCs; the brief asks for cheap local visuals.
**Evidence.** `MythUiTests.Toolbar_glyphs_are_drawn_for_every_m7_icon`; 2,000 fake NPCs with overlay, Chronicle and
settlement panel open: 199 fps average (display-capped at 200), scripts 1.3 ms per frame.
**Revisit when.** Someone fetches the Kenney packs by hand and prefers them, or the overlay needs real territory shapes.
## 2026-11-06 — M6: the world runs at walking pace while possessed
**Decision.** While an NPC is possessed, `SimHost` ticks at `SimConfig.PossessionTicksPerRealSecond` (2) instead of
`TicksPerRealSecond` (20) at 1×: an in-game hour takes 5 s, a day 2 minutes. The possessed NPC walks
`PossessedWalkSpeed` = 1 m/tick (2 m/s), sprints ×2.2 (4.4 m/s), children ×0.7, elders ×0.8; genes and terrain cost
do not change it (`World.PossessedSpeed`, shared with the client's prediction). Possessing sets the time controls to
1× and releasing restores the previous speed; Space still pauses.
**Why.** At 20 ticks/s ordinary NPCs move 40 m/s and needs run out in seconds (water 0.9/day = empty in 13 s, dead
24 s later), so a walk to the next village would kill the body and the neighbours would blur past. At 2 ticks/s the
others walk 3–5 m/s, a talk lasts 3 s and the body lasts several minutes without help. Same ticks, same rules: only
the real-time rate changes, so determinism and saves are untouched.
**Evidence.** `PossessionTests` (sim), `Possess_walk_and_release_through_the_main_scene` (gdUnit); STATUS M6.
**Revisit when.** Possession needs a different feel (a config value), or M8 retunes the tick rate.

## 2026-11-06 — M6: prediction by command magnitude, the sim stays the authority
**Decision.** The client moves its `CharacterBody3D` at once from WASD at the sim's possessed speed, and each physics
frame sends `PossessedMoveCommand` with the vector from the snapshot position to the body measured in one tick's
step (length clamped to 1, `Sprint` set when the gap exceeds a walking step). The sim moves its NPC by
`speed × min(1, length)` toward the body, so it lands exactly on the body's position instead of overshooting, and
stops when the body stops. With no input the body follows the sim, interpolated between snapshots like the
renderer (this is also how "work alongside" moves it). The sim is the authority: if it refuses a step (deep water,
the map edge) the gap grows past one tick of lead (3.2 m, ×2.2 sprinting) and the body is pulled back; beyond 10 m it
snaps; the client also stops at water deeper than 0.9 m so this rarely shows. Commands go out every frame while
moving and once on stopping.
**Why.** Snapshots arrive at 2 Hz while possessed; drawing the sim position directly is a 2 Hz stutter, and plain
prediction plus correction leaves a 1–2 m slide-back after every stop because the sim moves in whole ticks. Using
the command's length keeps the contract unchanged (`DirX`, `DirZ`, `Sprint`).
**Evidence.** gdUnit test: after 1.5 s of walking and a stop the sim is within 1 m of the body; STATUS M6 (gap and
correction counts from the scripted walk).
**Revisit when.** Networked play, or the sim gains obstacles the client cannot predict (walls, crowds).

## 2026-11-06 — M6: terrain collision around the body instead of navigation baking
**Decision.** No `NavigationRegion3D` anywhere. `TerrainCollision` builds one `ConcavePolygonShape3D` per terrain chunk
for the 3×3 chunks (192 m square) around the possessed body from the render heights (foundation-flattened, not clamped
to sea level), on its own physics layer 10; shapes are rebuilt only when the body changes chunk or the render heights
change, and dropped on release. The possession camera's `SpringArm3D` uses the same layer. A trimesh rather than
`HeightMapShape3D` because the grid has 2 m cells (a height map would need a scaled shape node) and the trimesh
matches the drawn triangles exactly. The body moves horizontally without collision (the terrain is a height field
with no overhangs, and the sim already decides where a body may stand) and, while grounded, follows the drawn ground;
over a drop of more than 1.2 m it falls, and the capsule with `MoveAndSlide` against the trimesh carries it until it
lands. Backface collision is off so a body that ends up a few centimetres under the trimesh pops out. The first
version slid the capsule horizontally: it snagged on the render terraces under buildings and on banks the sim walks
over, and depenetration pushed it back every frame (scripted walks stalled), so it was changed.
**Why.** Only the possessed body needs physics; the sim decides where everyone walks, so navigation meshes would be
unused, and the god view keeps zero physics cost (A1 budgets).
**Evidence.** `PossessionTests.Terrain_collision_covers_only_the_chunks_around_the_body`; STATUS M6 fps.
**Revisit when.** Buildings or trees should block the body (add their shapes to the same layer near the player).

## 2026-11-06 — M6: controls, verbs and what the body does on its own
**Decision.** P (or the HUD "Possess" button) possesses the selected NPC; the camera glides 0.7 s from the god rig to
the possession rig and back. WASD relative to the camera, Shift sprint, mouse look (captured; a click recaptures),
F first person, E talk to the nearest person within 3.5 m (sends `talk`, opens the dialogue panel with the possessed
NPC as speaker) or work at a berry bush within 3.5 m / the own village's storehouse within 8 m (takes up to 4 food:
year-120 villages live on farms and bushes are bare) / a building site within 8 m (`work`; T does the same when
people stand around), G give food (`give:food`),
R work alongside the person (`work` with their id), Space pause, Esc closes the dialogue or releases. God keys
(Q/L/V, 1–5) do nothing while possessed. The verbs stay the contract's three; standing still, the body eats from what
it carries when hungry (food < 0.2) and drinks at the shore when thirsty, so no `eat` verb was needed. Talking through
the Mind uses a new `MindPool.AskAsPossessedAsync`: speaker `possessed` named after the body, and the line becomes a
`SayIntention` from the possessed NPC to the listener (heard by both and bystanders) instead of a whisper from the god.
Possession is never saved: the save writes "nobody" and a load releases.
**Why.** The brief's controls; `AskAsync` hard-codes the god (speaker and whisper), so the possessed path needed a
small additive Mind method (the Spotlight already excluded the possessed NPC, no change there).
**Evidence.** `PossessionTests` (both projects), `PossessedDialogueTests`.
**Revisit when.** More verbs (trade, court) or a radial menu.
