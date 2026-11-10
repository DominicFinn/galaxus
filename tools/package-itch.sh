#!/usr/bin/env bash
# Zips build/web for itch.io: index.html at the top of the zip, as itch expects.
# Run tools/export-web.sh first. Upload the zip as an HTML game with SharedArrayBuffer support on.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/build/Galaxus-web-itch.zip"
rm -f "$OUT"
(cd "$ROOT/build/web" && zip -qr9 "$OUT" . -x '.*')
ls -lh "$OUT"
unzip -l "$OUT"
