#!/usr/bin/env bash
# Checks mods against the base game and prints readable problems. It doesn't run the test suite.
#   tools/validate-mods.sh                          every enabled mod in mods/, loaded together
#   tools/validate-mods.sh my-mod                   one mod in mods/, even if it is switched off
#   tools/validate-mods.sh /path/to/my-mod          one mod anywhere
#   tools/validate-mods.sh my-mod --samples         also print sample text from every grammar it adds
#   tools/validate-mods.sh my-mod --samples=6       the same, six samples per rule
# Exits with 0 when everything is fine and 1 when there are problems.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MODS="$ROOT/mods"
ARGS=()
FLAGS=()
for arg in "$@"; do
  if [[ "$arg" == --* ]]; then FLAGS+=("$arg"); continue; fi
  if [[ -d "$arg" && -d "$arg/../$(basename "$arg")" && "$arg" == */* ]]; then
    MODS="$(cd "$(dirname "$arg")" && pwd)"
    ARGS+=("$(basename "$arg")")
  else
    ARGS+=("$arg")
  fi
done
dotnet run --project "$ROOT/tools/ModCheck" -v quiet -- "$ROOT/game/data" "$MODS" ${ARGS[@]+"${ARGS[@]}"} ${FLAGS[@]+"${FLAGS[@]}"}
