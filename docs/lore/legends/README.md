# Legends

Legends are stories the galaxy tells about itself: myths, rumours, sagas and tall tales. They may not be true. A legend always has a teller, and the teller has a reason to tell it that way. The orcs' Old Horde in [../orcs.md](../orcs.md) is the model: every orc believes it, the dwarves keep the receipts, and the game never settles it.

Legends sit between canon and mods.

- **Canon** is what is so: [lore-bible.md](../../lore-bible.md) and the core content. Only repository admins change it.
- **Legends** are what people say. Anyone can write one. A legend can be told in the core game (this folder) or in a mod.
- **Mods** are optional, self-contained additions in `mods/<name>/`.

A legend never changes canon by being written down. It can't settle anything in the lore bible's "Things nobody knows", and it follows the "Never write" list like everything else. What it can do is give people something to argue about.

## Where legends live

- **Core game:** a file in this folder, `docs/lore/legends/<legend-name>.md`. Admins add these directly. Anyone else proposes one in a pull request, and it goes in once an admin has read it.
- **Mods:** a `legends/` folder inside the mod, `mods/<mod>/legends/<legend-name>.md`. The game ignores this folder; it's there so the story behind the mod's text is written down next to it.

The prose file is the reference. Players meet the legend through grammar text, which goes in the usual places (`game/data/grammars/` for the core game, the mod's `grammars/` folder for a mod).

## How a legend shows up in play

- **Bar talk.** Add lines to the `rumour` rule of a race's talk grammar (`talk-human`, `talk-orc` and so on). A mod does this by using the same grammar id, which adds options without replacing any. `mods/old-stories` is a working example.
- **Crew chatter.** The `idle` rule of a race's voice grammar (`voice-orc` and so on) is small talk on long trips, and suits a line or two of a legend.
- **News.** An event's news grammar (`start`, `ongoing`, `end`) can report a legend as rumour: a sighting, a claim, a pilgrimage.
- **Mission flavour.** A mission's briefing can rest on a legend: a collector after an artefact, a pilgrim heading for a gate, a khan who wants a stone moved. The legend is why the client wants it; the job is ordinary.
- **Landmarks and names.** A place named after the legend, with a description that tells it.

## Writing a legend

Use this outline. Not every section needs to be long.

```markdown
# <Legend name>

One or two sentences: what the story is and who tells it.

## The telling
The story as its tellers tell it, in their voice and with their certainty.

## What others say
How other races and powers react. Sceptical, amused, offended, uneasy.

## What is actually so
What is really true, if canon knows. Often "nobody knows", which is fine. Never settle anything
from "Things nobody knows".

## Rumours
Short lines a player might hear, none of them checkable.

## In game
Which grammars carry it (ids and rules), and any missions, landmarks or news that use it.
```

Then write the grammar lines, run `tools/validate-mods.sh <mod> --samples` (or `tools/validate.sh` for the core game) and read the lines back in each race's voice.
