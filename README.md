# Eden

A WorldBox-style god simulation that starts from two people on an island and grows into
societies of NPCs that remember, learn, invent and talk. Godot 4.7 .NET client, Cloudflare
Worker brain running on the Workers AI free tier.

Master plan: [docs/specs/00-architecture.md](docs/specs/00-architecture.md).
Conventions for agents and humans: [AGENTS.md](AGENTS.md).

## Layout

```
docs/specs/        one spec per module + contracts/ (C# interfaces, JSON schemas, fixtures)
game/              Godot 4.7 .NET project
  Eden.sln
  Eden.Game.csproj Godot scripts (src/Eden.Game) — must sit at the Godot project root
  src/Eden.Sim     deterministic simulation, no Godot
  src/Eden.Mind    cognition layer, no Godot
  tests/           Eden.Sim.Tests (xUnit v3), Eden.Game.Tests (gdUnit4Net)
  scenes/ data/    scenes, JSON data (concepts, biomes, names)
brain/             Cloudflare Worker: Hono + Zod, /think /chat /embed
scripts/           check.sh (everything), validate-contracts.mjs
```

## Prerequisites

- Godot 4.7.2 .NET build on PATH as `godot`
- .NET SDK 10.0.x
- Node 24 (wrangler is run through `npx`)

## Run the checks

```sh
./scripts/check.sh
```

Individually:

```sh
node scripts/validate-contracts.mjs          # JSON schemas + fixtures
cd game && dotnet test Eden.sln              # C# unit tests
dotnet build game/Eden.sln && godot --headless --path game --quit
cd brain && npm install && npm run check && npm test
```

## Run the brain Worker locally

```sh
cd brain
cp .dev.vars.example .dev.vars               # set EDEN_SHARED_SECRET
npx wrangler dev                             # Workers AI calls hit your Cloudflare account (free tier)
curl -s localhost:8787/health
```

Deployment is a separate, deliberate step (`npx wrangler deploy`), not part of the checks.

## Milestones

M0 tooling and contracts → M1 headless genesis → M2 island on screen → M3 society →
M4 knowledge → M5 minds → M6 possession → M7 myth and god → M8 scale. See the plan for the
acceptance check of each.
