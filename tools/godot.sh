#!/usr/bin/env bash
# Runs Godot .NET against the game project. Set GODOT to override where Godot lives.
set -euo pipefail
GODOT="${GODOT:-$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot}"
if [[ -z "${DOTNET_ROOT:-}" && -d /opt/homebrew/opt/dotnet/libexec ]]; then
  export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
exec "$GODOT" --path "$ROOT/game" "$@"
