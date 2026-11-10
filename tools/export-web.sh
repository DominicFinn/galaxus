#!/usr/bin/env bash
# Exports the experimental web build into build/web/ (see docs/web-version.md).
# Needs the community C# web editor (GODOT_WEB), its web templates installed, and the
# wasm-tools-net9 workload. Test it with: python3 tools/serve-web.py build/web
# Optional: WEB_OUT=<dir> exports somewhere else, and WEB_TEMPLATE=<zip> uses a custom release
# template (the AOT builds in docs/web-aot.md) instead of the installed one.
set -euo pipefail
GODOT_WEB="${GODOT_WEB:-$HOME/git/godot-dotnet-web/bin/godot.macos.editor.arm64.mono}"
if [[ -z "${DOTNET_ROOT:-}" && -d /opt/homebrew/opt/dotnet/libexec ]]; then
  export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
  export PATH="$DOTNET_ROOT:$PATH"
fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${WEB_OUT:-$ROOT/build/web}"
mkdir -p "$OUT"
cd "$ROOT/game"
if [[ -n "${WEB_TEMPLATE:-}" ]]; then
  # Point the Web preset at the template for this export only, and put the preset back after.
  cp export_presets.cfg export_presets.cfg.bak
  trap 'mv export_presets.cfg.bak export_presets.cfg' EXIT
  python3 - "$WEB_TEMPLATE" <<'PY'
import sys
lines = open("export_presets.cfg").read().split("\n")
web = False
for i, line in enumerate(lines):
    if line.startswith("[preset.") and not line.endswith(".options]"):
        web = False
    if line == 'name="Web"':
        web = True
    if web and line.startswith("custom_template/release="):
        lines[i] = 'custom_template/release="%s"' % sys.argv[1]
open("export_presets.cfg", "w").write("\n".join(lines))
PY
fi
"$GODOT_WEB" --headless --path . --export-release "Web" "$OUT/index.html"
ls -lh "$OUT"
