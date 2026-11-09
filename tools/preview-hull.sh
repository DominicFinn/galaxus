#!/usr/bin/env bash
# Renders a hull from four angles to a PNG, for checking ship models.
#   tools/preview-hull.sh kestrel /tmp/kestrel.png
set -euo pipefail
if [[ $# -ne 2 ]]; then echo "usage: $0 HULL_ID OUTPUT.png" >&2; exit 2; fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"
rm -f "$OUT"
"$ROOT/tools/godot.sh" -- --preview="$1" --screenshot="$OUT" 2>&1 | grep -E "ERROR|No hull|Saved" || true
[[ -f "$OUT" ]] || { echo "No image written. Run tools/validate.sh to check the content loads." >&2; exit 1; }
