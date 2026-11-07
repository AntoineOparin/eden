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
