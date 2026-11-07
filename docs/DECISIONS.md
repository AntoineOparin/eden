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
