#!/usr/bin/env bash
# Builds the web version with our C# compiled ahead of time to WebAssembly (docs/web-aot.md).
#
#   1. Export once with GalaxusWebAot=true. The export's dotnet publish trims the assemblies and
#      AOT-compiles them; the objects land in the game's obj folder. This export is thrown away.
#   2. Build a web release template from the fork (web-aot branch) with those objects linked in.
#   3. Export again with that template. The publish is now a no-op, so the assemblies in the .pck
#      are the ones that were compiled. The script checks that, because Mono quietly falls back
#      to the interpreter for any assembly that doesn't match its AOT image.
#
# Every change to the game's C# needs all three steps; a template only suits the build it came from.
# Needs: the fork worktree with the web-aot changes (GODOT_WEB_AOT_SRC), emsdk 6.0.5 (EMSDK_DIR),
# scons, and what tools/export-web.sh needs.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FORK="${GODOT_WEB_AOT_SRC:-$HOME/git/godot-dotnet-web-aot}"
EMSDK_DIR="${EMSDK_DIR:-$HOME/git/emsdk}"
SCONS="${SCONS:-$(command -v scons || echo "$HOME/Library/Python/3.9/bin/scons")}"
# The community project's release flags, less link-time optimisation (lto=full takes far longer).
SCONS_FLAGS="${SCONS_FLAGS:-optimize=speed lto=none ccflags=-DNDEBUG linkflags=-sMALLOC=mimalloc}"
OUT="${WEB_OUT:-$ROOT/build/web-aot}"
TEMPLATE_DIR="$ROOT/build/web-aot-template"
AOT_DIR="$ROOT/game/.godot/mono/temp/obj/ExportRelease/browser-wasm/wasm/for-publish"
LINKED_DIR="$ROOT/game/.godot/mono/temp/obj/ExportRelease/browser-wasm/linked"

if [[ -z "${DOTNET_ROOT:-}" && -d /opt/homebrew/opt/dotnet/libexec ]]; then
  export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
  export PATH="$DOTNET_ROOT:$PATH"
fi
export GalaxusWebAot=true
mkdir -p "$TEMPLATE_DIR"
step() { printf '\n== %s (%s)\n' "$1" "$(date +%H:%M:%S)"; }

step "1/3 Publishing and AOT-compiling the game"
WEB_OUT="$TEMPLATE_DIR/throwaway-export" "$ROOT/tools/export-web.sh" > "$TEMPLATE_DIR/export1.log" 2>&1 \
  || { tail -40 "$TEMPLATE_DIR/export1.log"; exit 1; }
rm -rf "$TEMPLATE_DIR/throwaway-export"
[[ -f "$AOT_DIR/driver-gen.c" ]] || { echo "No AOT output in $AOT_DIR: did GalaxusWebAot reach the publish?"; exit 1; }
(cd "$LINKED_DIR" && shasum *.dll) > "$TEMPLATE_DIR/compiled-assemblies.sha"

step "2/3 Building the web template with the AOT objects"
if [[ -z "${EMSDK_PYTHON:-}" && -x /opt/homebrew/bin/python3 ]]; then export EMSDK_PYTHON=/opt/homebrew/bin/python3; fi
# shellcheck disable=SC1091
source "$EMSDK_DIR/emsdk_env.sh" > /dev/null 2>&1
emcc --version | head -1
# shellcheck disable=SC2086
(cd "$FORK" && "$SCONS" platform=web target=template_release module_mono_enabled=yes $SCONS_FLAGS \
  mono_wasm_aot_dir="$AOT_DIR" -j"$(sysctl -n hw.ncpu 2>/dev/null || nproc)") > "$TEMPLATE_DIR/scons.log" 2>&1 \
  || { tail -40 "$TEMPLATE_DIR/scons.log"; exit 1; }
cp "$FORK/bin/godot.web.template_release.wasm32.mono.zip" "$TEMPLATE_DIR/web_release_aot.zip"

step "3/3 Exporting with the AOT template"
WEB_TEMPLATE="$TEMPLATE_DIR/web_release_aot.zip" WEB_OUT="$OUT" "$ROOT/tools/export-web.sh" > "$TEMPLATE_DIR/export2.log" 2>&1 \
  || { tail -40 "$TEMPLATE_DIR/export2.log"; exit 1; }
if ! (cd "$LINKED_DIR" && shasum -c --quiet "$TEMPLATE_DIR/compiled-assemblies.sha"); then
  echo "The assemblies changed between the AOT compile and the final export, so the AOT code won't be used. Run this again."
  exit 1
fi
ls -lh "$OUT"
step "Done: python3 tools/serve-web.py ${OUT#"$ROOT"/} 8766"
