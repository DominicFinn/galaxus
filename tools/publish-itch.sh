#!/usr/bin/env bash
# Builds the fast web version and pushes it to itch.io with butler, itch's uploader. butler only
# sends what changed, and players get the new build the next time they load the page.
#
#   tools/publish-itch.sh            build, then push
#   tools/publish-itch.sh --no-build push the build that's already in build/web-aot
#
# Needs butler logged in once with "butler login". It's found on the PATH, at $BUTLER, or in
# ~/git/butler-darwin-amd64. ITCH_TARGET picks the page and channel (default domfinn/galaxus:web).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TARGET="${ITCH_TARGET:-domfinn/galaxus:web}"
BUTLER="${BUTLER:-$(command -v butler || echo "$HOME/git/butler-darwin-amd64/butler")}"
[[ -x "$BUTLER" ]] || { echo "butler isn't installed: see docs/building.md (Publishing to itch.io)."; exit 1; }
if [[ "${1:-}" != "--no-build" ]]; then "$ROOT/tools/build-web-aot.sh"; fi
[[ -f "$ROOT/build/web-aot/index.html" ]] || { echo "No web build in build/web-aot."; exit 1; }
VERSION="$(git -C "$ROOT" describe --tags --always --dirty)"
"$BUTLER" push "$ROOT/build/web-aot" "$TARGET" --userversion "$VERSION"
"$BUTLER" status "$TARGET"
