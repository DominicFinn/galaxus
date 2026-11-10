#!/usr/bin/env bash
# Checks mods against the base game and prints readable problems. It doesn't run the test suite.
#   tools/validate-mods.sh                          every enabled mod in mods/, loaded together
#   tools/validate-mods.sh my-mod                   one mod in mods/, even if it is switched off
#   tools/validate-mods.sh /path/to/my-mod          one mod anywhere
# Exits with 0 when everything is fine and 1 when there are problems.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MODS="$ROOT/mods"
ARGS=()
if [[ $# -ge 1 ]]; then
  if [[ -d "$1" && -d "$1/../$(basename "$1")" && "$1" == */* ]]; then
    MODS="$(cd "$(dirname "$1")" && pwd)"
    ARGS=("$(basename "$1")")
  else
    ARGS=("$1")
  fi
fi
dotnet run --project "$ROOT/tools/ModCheck" -v quiet -- "$ROOT/game/data" "$MODS" ${ARGS[@]+"${ARGS[@]}"}
