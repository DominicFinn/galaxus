#!/usr/bin/env bash
# Exports release builds for macOS, Windows and Linux into build/<platform>/ and zips each.
# Needs the matching .NET export templates installed (see docs/building.md).
set -euo pipefail
GODOT="${GODOT:-$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot}"
if [[ -z "${DOTNET_ROOT:-}" && -d /opt/homebrew/opt/dotnet/libexec ]]; then
  export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BUILD="$ROOT/build"
# Godot is x86_64-capable here, but the thinned editor is arm64 only.
run_godot() { if [[ "$(uname -s)" == Darwin ]]; then arch -arm64 "$GODOT" "$@"; else "$GODOT" "$@"; fi; }

rm -rf "$BUILD"
mkdir -p "$BUILD/macos" "$BUILD/windows" "$BUILD/linux"

cd "$ROOT/game"
run_godot --headless --path . --build-solutions --quit >/dev/null
run_godot --headless --path . --export-release "macOS" "$BUILD/macos/Galaxus.app"
run_godot --headless --path . --export-release "Windows Desktop" "$BUILD/windows/Galaxus.exe"
run_godot --headless --path . --export-release "Linux" "$BUILD/linux/Galaxus.x86_64"

# Mods folder beside the Windows and Linux executables (see game/src/ModRoots.cs).
for p in windows linux; do cp -R "$ROOT/mods" "$BUILD/$p/mods"; done

cd "$BUILD"
for p in macos windows linux; do
  (cd "$p" && zip -qry "../Galaxus-$p.zip" .)
done
ls -lh "$BUILD"/*.zip
