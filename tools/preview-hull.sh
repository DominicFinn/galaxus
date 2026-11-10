#!/usr/bin/env bash
# Renders a hull from four angles to a PNG, for checking ship models.
#   tools/preview-hull.sh kestrel /tmp/kestrel.png
# Uses a small software renderer (tools/HullPreview), so no window opens.
# Enabled mods in mods/ are loaded too, so a mod's hulls preview the same way.
set -euo pipefail
if [[ $# -ne 2 ]]; then echo "usage: $0 HULL_ID OUTPUT.png" >&2; exit 2; fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
dotnet run --project "$ROOT/tools/HullPreview" -v quiet -- "$ROOT/game/data" "$1" "$2" --mods="$ROOT/mods"
