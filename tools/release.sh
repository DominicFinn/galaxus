#!/usr/bin/env bash
# Makes a release from this machine, which is much quicker than waiting for a GitHub Mac runner.
# Builds the desktop zips and puts them on itch.io straight away, building and pushing the web
# version alongside, then tags the commit and publishes the GitHub release last.
#
#   tools/release.sh v0.3.0 notes.md
#   tools/release.sh v0.3.0 notes.md --skip-tests   when the tests have just passed on this commit
#
# Needs a clean working tree on main, gh logged in, and butler set up (see docs/building.md).
set -euo pipefail
VERSION="${1:?usage: tools/release.sh VERSION NOTES.md [--skip-tests]}"
NOTES="${2:?usage: tools/release.sh VERSION NOTES.md [--skip-tests]}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
[[ -z "$(git status --porcelain)" ]] || { echo "Commit or stash your changes first."; exit 1; }
BUTLER="${BUTLER:-$(command -v butler || echo "$HOME/git/butler-darwin-amd64/butler")}"
TARGET="${ITCH_GAME:-domfinn/galaxus}"

[[ "${3:-}" == "--skip-tests" ]] || tools/validate.sh
tools/export.sh

# itch first: the desktop zips and the web build go up side by side.
( for p in windows macos linux; do "$BUTLER" push "build/Galaxus-$p.zip" "$TARGET:$p" --userversion "$VERSION"; done ) &
DESKTOP=$!
tools/publish-itch.sh
wait "$DESKTOP"
echo "itch.io is up to date with $VERSION."

# Then GitHub.
git tag -a "$VERSION" -m "Galaxus ${VERSION#v}"
git push origin main "$VERSION"
gh release create "$VERSION" build/*.zip --title "Galaxus ${VERSION#v}" --notes-file "$NOTES" --latest
