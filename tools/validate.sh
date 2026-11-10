#!/usr/bin/env bash
# Loads and checks every content file (including mods), and runs the core tests.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
dotnet test "$ROOT/tests/Galaxus.Core.Tests" -nologo -v quiet "$@"
