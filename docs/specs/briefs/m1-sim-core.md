# Brief: M1 sim core (launched 2026-11-06, base 9ab043a, branch m1-sim-core)

You are working in an isolated git worktree of the Eden repo (origin at ~/Projects/eden, base commit 9ab043a "M0"). Your working directory is the worktree root; do all work there and nowhere else.

Read AGENTS.md first, then docs/specs/10-sim-core.md and the contracts it points at in docs/specs/contracts/ (ISimHost.cs, IWorldSnapshot.cs, Intention.cs). Also skim docs/specs/00-architecture.md for context.

Goal: milestone M1 "headless genesis" exactly as the spec's M1 section and acceptance tests describe. Fill in game/src/Eden.Sim/ (World.Step currently does nothing): world gen, NPC struct-of-arrays storage, genes, needs, utility AI with the ActionKind vocabulary, straight-line movement, courtship, pairing, children with crossover + mutation, aging, death, memory stream recording, Chronicle events, and the IntentionValidator. Make SimHost publish real NpcView data in snapshots. Add the console runner game/tools/Eden.Sim.Runner (net10.0 console project, plain Microsoft.NET.Sdk) and register it in game/Eden.sln. Add the M1 xUnit tests listed in the spec to game/tests/Eden.Sim.Tests/ (xUnit 2.x on VSTest, do not switch to xunit.v3; keep the existing ContractTests, RngTests, SimHostTests passing).

Hard rules: Eden.Sim stays deterministic (only the provided Rng, never System.Random/DateTime/Guid), no Godot references, no allocations on the tick hot path, never block on anything. Do not touch game/src/Eden.Game, game/src/Eden.Mind, game/scenes, game/tests/Eden.Game.Tests, brain/, or docs/specs/contracts/. If a contract must change, stop and explain why in your report instead of changing it.

Tooling facts: .NET 10 SDK is the only one installed (all projects target net10.0). `godot` on PATH is a wrapper around /Applications/Godot_mono.app/Contents/MacOS/Godot; gdUnit4 needs GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot (scripts/check.sh sets it). Do not deploy anything or spend money.

Definition of done: `cd game && dotnet test Eden.sln` green, `./scripts/check.sh` green from the worktree root, and `dotnet run --project game/tools/Eden.Sim.Runner -- --seed 7 --years 60` shows population growing from 2 to 50+ on most seeds and dying out on some. Run the runner across seeds 1..20 and report which seeds did what. When done, commit your work in the worktree on a branch named m1-sim-core with a clear message ending in "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>". Do not merge into main and do not push.

Final report: what you built (file list), test counts, the seed table, anything you could not finish and why, and any contract change you believe is needed.

Mid-run addendum: the contract gained `BiomeId` and the heightmap grid layout (commit abe34d9); the agent was told to conform and confirm in its report. It fast-forwarded onto abe34d9.
