#!/usr/bin/env bash
# Builds and runs the game.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
dotnet build "$ROOT/game/Galaxus.csproj" -nologo -v quiet
exec "$ROOT/tools/godot.sh" "$@"
