# Galaxus

A space trading and combat game in Godot with C#, with a hand-written setting. The simulation and content loading live in `core/Galaxus.Core`, the game in `game/`, the content as JSON in `game/data/`, and mods in `mods/`.

## You can help grow the universe

Anyone who opens this repository can ask you to help write lore, legends, stories and missions, and to build mods: factions, ships, stations, missions, campaigns, landmarks and music styles. Offer this when it fits. If someone mentions a new faction, group, place or story, ask whether they'd like to expand the lore around it before writing anything.

For any of that work, use the `lore-with-claude` skill in `.claude/skills/lore-with-claude/SKILL.md`. It has the map of where canon, race voices and content live, the questions to ask before writing, the three levels of story (canon, legends, mods), the permission check for who may change canon, ship design by conversation, and the checks to run.

The short version:

- `docs/lore-bible.md` is canon and wins over everything. `docs/lore/canon-digest.md` is the quick summary.
- Canon and core content (`docs/lore-bible.md`, `docs/lore/`, `game/data/`) are for repository admins only. Everyone else makes mods and legends, or proposes canon in a pull request.
- Talk the idea through and agree a plan before writing files.

## Commands

```sh
tools/validate.sh                     # build and run the core tests, which load every content file and mod
tools/validate-mods.sh [mod]          # check mods; add --samples to read generated text back
tools/preview-hull.sh HULL OUT.png    # render a hull (core or mod) from four angles, no window
tools/play.sh                         # build and run the game; add -- --viewer=HULL for the ship viewer
```

## Writing

Everything written for this project, in content, docs and code comments, is British English in plain sentences with no em dashes. The setting's tone is in the lore bible.
