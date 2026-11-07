# Agent briefs

The exact prompt each milestone agent received, kept so later sessions can see what was asked,
what was forbidden, and what the acceptance was. Store a new brief here before launching the
agent. All agents so far: Claude Opus 5.5, medium effort, one isolated git worktree each,
commit on a named branch, never merge or push; the coordinator merges after
`./scripts/check.sh` passes on main.

| File | Milestone | Base commit | Branch | Result |
|---|---|---|---|---|
| `m1-sim-core.md` | M1 | 9ab043a | m1-sim-core | merged ec3b692 |
| `m2-render-shell.md` | M2 | 9ab043a | m2-render-shell | merged 0a9f5c6 |
| `worker-m5-ready.md` | Worker | 9ab043a | worker-m5-ready | merged 72ad510 |
| `m3-society.md` | M3 | 50f9c5a | m3-society | merged 0f615ba |
| `m4-knowledge.md` | M4 | 50f9c5a | m4-knowledge | merged e43104f |
| `m5-minds.md` | M5 | 9cb0216 | m5-minds | merged 64fe48d |
| `a1-art-pass.md` | A1 | tag phase3-base | a1-art-pass | merged 308e67a |
| `k1-knowledge-balance.md` | K1 | tag phase3-base | k1-knowledge-balance | merged |
| `m7-myth-sim.md` | M7 (sim+mind) | tag phase3-base | m7-myth-sim | merged 8a8fc27 |
| `m7-ui-pass.md` | M7 (UI) | tag phase4-base | m7-ui-pass | merged 2026-11-07 (028b457) |
| `m6-possession.md` | M6 | tag phase4-base | m6-possession | merged 2026-11-07 (63cd7f1) |
| `m8-scale.md` | M8 | tag phase5-base | m8-scale | merged 2026-11-07 |
| `a2-art-followups.md` | A2 | tag phase5-base | a2-art-followups | merged 2026-11-07 |
| `c1-contract-pass.md` | C1 | tag phase5-base | c1-contract-pass | merged 2026-11-07 (2d55986) |

## Template

```
You are working in an isolated git worktree of the Eden repo (origin at ~/Projects/eden, base
commit <sha> on main). Your working directory is the worktree root; do all work there.
Read in this order: AGENTS.md; docs/specs/<NN-spec>.md; docs/specs/90-backlog.md (rows marked
<Mx> are yours); docs/DECISIONS.md and docs/STATUS.md; the contracts the spec points at; then the
existing code in <module> before writing anything.
Goal: milestone <Mx> "<name>", exactly as the spec's section and acceptance tests describe. <scope>
File ownership: you own <files>; you may edit <shared files> only additively; do not touch <rest>.
Contract changes: <none | this one, pre-approved>; anything else: stop and explain in the report.
Hard rules: <determinism / no Godot in Sim / no money / no deploy / tests never call real APIs>.
Tooling: .NET 10 only; `godot` is a wrapper; GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot.
Definition of done: `./scripts/check.sh` green from the worktree root; <manual check>.
Commit on branch <name> with a message ending in "Co-Authored-By: <model> <noreply@anthropic.com>".
Do not merge or push. Append your section to docs/STATUS.md and any decision to docs/DECISIONS.md.
Final report: files by module, test counts, measured numbers, what you could not finish and why,
anything you need from the coordinator.
```
