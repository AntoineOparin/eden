# AGENTS.md

Instructions for AI coding agents (and humans) working in this repo. Follow these conventions
exactly; when the codebase and this file disagree, ask before "fixing" either.

Eden is a WorldBox-style god sim with agentic NPCs. The master plan is
`docs/specs/00-architecture.md`. Every module has its own spec in `docs/specs/`; an agent's
brief is "execute `docs/specs/<file>`, milestone Mx". Read the spec, the contracts it points at,
and this file before writing code.

## Stack

- **Client:** Godot 4.7.2 .NET (`godot` on PATH), C# 14 on **.NET 10** (`net10.0` everywhere;
  the .NET 10 SDK and runtime are the only ones installed, do not downgrade).
  - `game/src/Eden.Sim` pure C# class library, **zero Godot references**, xUnit-tested.
  - `game/src/Eden.Mind` class library, references `Eden.Sim` only, no Godot.
  - `game/src/Eden.Game` Godot scripts, the only place `Godot.*` may be used. Its csproj is
    `game/Eden.Game.csproj` (Godot requires it at the project root).
  - `game/tests/Eden.Sim.Tests` xUnit v3. Must stay fast (< 1 s) and headless.
  - `game/tests/Eden.Game.Tests` gdUnit4Net. Only tests that need a scene tree go here.
- **Backend:** `brain/` is a Cloudflare Worker, **Hono + Zod**, TypeScript, `wrangler.jsonc`.
  Workers AI (free tier) is the default NPC brain. Other providers are optional adapters only.
  `godot` on this machine is a wrapper script that execs the real binary (a symlink breaks
  Godot's .NET init); gdUnit4 needs `GODOT_BIN` pointing at the real binary.
- **Test runners:** `Eden.Sim.Tests` uses xUnit 2.x on VSTest on purpose. gdUnit4's adapter is
  VSTest-only and .NET 10 cannot mix VSTest and Microsoft.Testing.Platform projects in one
  `dotnet test`, so do not upgrade to xunit.v3. `Eden.Game.Tests` is a mirror Godot project
  (see `docs/specs/40-game-client.md`, "Test harness layout"). Keep `<TargetFramework>net10.0`
  explicit in both Godot-SDK csproj files: the Godot editor rewrites them with `net8.0` otherwise.
- **Contracts:** `docs/specs/contracts/` holds the C# interfaces and JSON schemas shared by the
  game and the Worker. The `.cs` files there are linked into `Eden.Sim` / `Eden.Mind` as
  compile items; edit them in `docs/specs/contracts/` only.

For Godot 4.7 C# APIs read the class reference at https://docs.godotengine.org/en/4.7/ (the
C# names are one-to-one with GDScript, PascalCase). Do not rely on memory of Godot 3 or 4.0.

## Commands

| What | Command |
|---|---|
| Everything a milestone must pass | `./scripts/check.sh` |
| .NET unit tests | `cd game && dotnet test Eden.sln` |
| Headless boot | `dotnet build game/Eden.Game.csproj && godot --headless --path game --quit` |
| Godot MCP check | `run_project` on `game/`, then `get_debug_output` must show `Eden boot ok` and no errors |
| Worker tests | `cd brain && npm test` |
| Worker typecheck | `cd brain && npm run check` |
| Worker local dev | `cd brain && npx wrangler dev` (needs `.dev.vars`, see `.dev.vars.example`) |
| Contract validation | `node scripts/validate-contracts.mjs` |

**Definition of done for any change:** `./scripts/check.sh` is green. It runs contract
validation, `dotnet test`, the Godot headless boot, and the Worker checks.

## Hard rules

1. **The simulation never waits on an LLM.** Every NPC runs on rules every tick. LLM output
   arrives asynchronously as an `Intention` (see `contracts/Intention.cs`) that the rules layer
   validates and then executes or drops. No `await` on a network call inside the sim thread.
2. **Eden.Sim is deterministic.** Same seed + same command stream = same world hash. Use the
   provided `Rng`, never `System.Random`/`Guid.NewGuid()`/`DateTime.Now` inside `Eden.Sim`.
3. **No Godot types outside `Eden.Game`.** `Eden.Sim` and `Eden.Mind` must build without the
   Godot SDK. The csproj references enforce it; do not add `GodotSharp` to them.
4. **LLM output is untrusted.** It is parsed through the Zod schema (Worker) or the C# DTOs +
   `IntentionValidator` (game) in the fixed vocabulary. Invalid output is logged and dropped,
   never applied.
5. **No money.** Workers AI free tier only. Do not enable paid Cloudflare features, KV, D1,
   Durable Objects, or deploy without being asked. Other providers are opt-in from the settings
   panel with the player's own key.
6. **Worktree scope.** A worktree merges only when its spec's acceptance tests pass and it
   changed nothing outside its module except `docs/specs/contracts/` (needs a note in the PR).

## Runtime: Cloudflare Workers (read before writing `brain/` code)

- **No state between requests.** No module-level caches, clients, counters or per-user data.
  Budget tracking is client-side; the Worker only reports per-request cost in a header.
- **Not Node.** `nodejs_compat` covers `Buffer`, `crypto`, `node:*` basics; no filesystem, no
  long-running timers, no native addons. Check a dependency runs on Workers before adding it.
- **Bindings and secrets** come from the `env` argument Hono passes (`c.env.AI`,
  `c.env.EDEN_SHARED_SECRET`). Never read `process.env`. Local secrets live in `brain/.dev.vars`
  (gitignored); production secrets via `npx wrangler secret put <NAME>`.
- After editing `wrangler.jsonc`, run `npm run cf-typegen` so `worker-configuration.d.ts` stays
  accurate. Do not hand-edit that file.
- **CPU budget:** free plan ≈ 10 ms CPU per request. Waiting on Workers AI does not count.

## Validation (Zod / JSON Schema)

- Every request body is Zod-validated before use; every LLM output is parsed through a Zod
  schema with **one** repair retry, then rejected with a 422.
- `brain/src/schemas.ts` must stay equivalent to `docs/specs/contracts/*.schema.json`. The
  contract test (`brain/src/schemas.test.ts`) validates the shared fixtures with both; keep it
  passing when you change either side.
- Derive TS types with `z.infer`; derive nothing by hand twice.

## C# conventions

- File-scoped namespaces, `Nullable` enabled, warnings are errors in `Eden.Sim`.
- Struct-of-arrays for hot entity data in `Eden.Sim`; no LINQ or allocations in the tick loop.
- Public API of `Eden.Sim` consumed by Godot goes through `IWorldSnapshot` / `ISimHost` only.
  Godot reads snapshots, never sim internals.
- Tests: one `<Thing>Tests.cs` per unit, `[Fact]`/`[Theory]`, no sleeping, no network.
- Godot scripts: one `partial class` per scene node, `[Export]` for tuning, `GD.Print` only
  behind a `Debug` flag outside of boot messages.

## TypeScript conventions

- kebab-case file names, named exports, `import type` for types, no `any`.
- Colocate tests as `<name>.test.ts` next to the code. Pure modules get unit tests; routes get
  `app.request()` tests with a fake `AI` binding, never a live call.
- Prettier + `tsc --noEmit` are part of `npm run check`.

## File names

- C#: PascalCase files matching the type. Specs/docs: `NN-kebab-case.md`. Everything else
  kebab-case. Godot scenes `snake_case.tscn` under `game/scenes/`, data JSON under `game/data/`.

## Don'ts

- Don't edit `game/.godot/`, `brain/worker-configuration.d.ts`, or lock files by hand.
- Don't add NuGet/npm dependencies without noting them in the PR and checking Workers/.NET 10
  compatibility.
- Don't change analyzer/TS/Prettier config to silence an error. Fix the code.
- Don't call Workers AI or any paid API from tests.
- Don't touch another module's files from your worktree. If you need something from another
  module, add it to `contracts/` and say so.
