#!/usr/bin/env bash
# Exports the experimental web build into build/web/ (see docs/web-version.md).
# Needs the community C# web editor (GODOT_WEB), its web templates installed, and the
# wasm-tools-net9 workload. Test it with: python3 tools/serve-web.py build/web
set -euo pipefail
GODOT_WEB="${GODOT_WEB:-$HOME/git/godot-dotnet-web/bin/godot.macos.editor.arm64.mono}"
if [[ -z "${DOTNET_ROOT:-}" && -d /opt/homebrew/opt/dotnet/libexec ]]; then
  export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
  export PATH="$DOTNET_ROOT:$PATH"
fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$ROOT/build/web"
cd "$ROOT/game"
"$GODOT_WEB" --headless --path . --export-release "Web" "$ROOT/build/web/index.html"
ls -lh "$ROOT/build/web"
