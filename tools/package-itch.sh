#!/usr/bin/env bash
# Zips a web build for itch.io: index.html at the top of the zip, as itch expects.
# Run tools/build-web-aot.sh (or tools/export-web.sh) first. Upload the zip as an HTML game with
# SharedArrayBuffer support on.
#   tools/package-itch.sh [build folder]   (default build/web-aot, the fast build)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="${1:-$ROOT/build/web-aot}"
OUT="$ROOT/build/Galaxus-web-itch.zip"
rm -f "$OUT"
(cd "$SRC" && zip -qr9 "$OUT" . -x '.*')
ls -lh "$OUT"
unzip -l "$OUT"
