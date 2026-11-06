#!/usr/bin/env bash
# Everything a milestone must pass. Run from anywhere; exits non-zero on the first failure.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

step() { printf '\n\033[1;34m== %s\033[0m\n' "$*"; }

step "contracts: JSON schemas + fixtures"
[ -d node_modules ] || npm install --silent
node scripts/validate-contracts.mjs

step "game: dotnet test (Eden.Sim.Tests xUnit + Eden.Game.Tests gdUnit4)"
# gdUnit4 launches Godot itself; it needs the real binary path, not a symlink or wrapper.
export GODOT_BIN="${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
dotnet test game/Eden.sln -nologo

step "game: headless boot"
dotnet build game/Eden.Game.csproj -nologo -v q
BOOT_LOG="$(mktemp)"
godot --headless --path game --quit >"$BOOT_LOG" 2>&1 || { cat "$BOOT_LOG"; echo "godot exited non-zero"; exit 1; }
cat "$BOOT_LOG"
if grep -qE "^(ERROR|SCRIPT ERROR|USER ERROR)" "$BOOT_LOG"; then echo "errors during headless boot"; exit 1; fi
grep -q "Eden boot ok" "$BOOT_LOG" || { echo "boot message missing"; exit 1; }

step "brain: format + typecheck + tests"
cd brain
[ -d node_modules ] || npm install --silent
npm run check
npm test

printf '\n\033[1;32mall checks passed\033[0m\n'
