#!/usr/bin/env bash
# M8: export Eden with the presets in game/export_presets.cfg into build/.
#
#   game/tools/export/export.sh            # macOS -> build/macos/Eden.app
#   game/tools/export/export.sh Linux      # Linux -> build/linux/Eden.x86_64
#
# game/assets/ carries a .gdignore (A1 loads the GLB/PNG/JPG/TTF files at runtime from res:// paths, DECISIONS
# 2026-11-06), and Godot's exporter never looks inside .gdignore'd folders, not even for the non-resource include
# filter. So the export runs from a staging copy of game/ in build/stage/game/ where assets/ has no .gdignore and every
# asset file has an `importer="keep"` .import file: Godot packs it as is, at the same res:// path. The repository
# copy is never touched. Needs the 4.7.2 .NET export templates (see DECISIONS "M8: export").
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
PRESET="${1:-macOS}"
case "$PRESET" in
  macOS) OUT="${2:-$ROOT/build/macos/Eden.app}" ;;
  Linux) OUT="${2:-$ROOT/build/linux/Eden.x86_64}" ;;
  *) OUT="${2:?output path required for preset $PRESET}" ;;
esac
STAGE="$ROOT/build/stage/game"

rm -rf "$ROOT/build/stage"
mkdir -p "$STAGE" "$ROOT/build/stage/docs/specs" "$(dirname "$OUT")"
# Eden.Sim / Eden.Mind compile the shared contracts from ../docs/specs/contracts (game/Directory.Build.props).
cp -R "$ROOT/docs/specs/contracts" "$ROOT/build/stage/docs/specs/"
rsync -a --exclude '.godot/' --exclude 'bin/' --exclude 'obj/' --exclude 'gdunit4_testadapter*/' "$ROOT/game/" "$STAGE/"
rm -f "$STAGE/assets/.gdignore"
find "$STAGE/assets" -type f \( -name '*.glb' -o -name '*.png' -o -name '*.jpg' -o -name '*.ttf' \) -print0 |
  while IFS= read -r -d '' f; do printf '[remap]\n\nimporter="keep"\n' > "$f.import"; done

godot --headless --path "$STAGE" --export-release "$PRESET" "$OUT"
echo "exported $PRESET to $OUT"
