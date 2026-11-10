---
name: lore-with-claude
description: Guide someone through growing the Galaxus universe. Use whenever a person wants to make or talk about a mod, a faction, a group, a people, a place, a station, a ship, a mission, a campaign, a legend, a story, a race's voice or any lore, or wants to extend the core game's missions, factions or history. Also use as soon as someone mentions a new faction, group, place or story in passing. Covers where the canon and race voices live, the three levels of story (canon, legends, mods), who may change what, the question flow before writing, ship design by conversation, and the checks before handing over.
---

# Growing the Galaxus universe with Claude

Galaxus is a space trading game with a hand-written setting. People can add to it at three levels (canon, legends and mods), and you help them do that well. The job has three parts: talk the idea through, write it so it fits the canon and sounds like the right race, and check it runs.

Don't write files until the person has agreed a plan.

## The map

| What | Where |
| --- | --- |
| Canon. Wins over everything else. Sections marked "(proposed)" aren't signed off. | `docs/lore-bible.md` |
| Short version of the canon: history, powers, races and how each speaks, running stories, what must never be explained | `docs/lore/canon-digest.md` (read this first) |
| Long lore, told from inside the setting | `docs/lore/elves.md`, `humans.md`, `orcs.md`, `origins.md` |
| Legends: what they are, where they go, how they surface in game | `docs/lore/legends/README.md` |
| Content formats, balance tables, grammar contracts, race voice rules | `docs/content-guide.md` |
| Mods: folders, walkthroughs, ship/station/landmark/campaign hooks, music styles, the checker | `docs/expanding-the-universe.md` |
| How each race talks: crew chatter, contract wording, bar talk | `game/data/grammars/voices/`, `mission-voices/`, `talk/` |
| Core content | `game/data/` (races, builders, hulls, parts, factions, events, missions, complications, battles, campaigns, music, grammars) |
| Mods | `mods/<name>/`. `example-free-traders` is the simple pattern, `gyptians` the full one (builder, ships, station, landmark, campaign), `old-stories` a legends-style mod |
| Ship and station design | [ship-design.md](ship-design.md) in this folder |

Where each kind of content goes:

- Faction types: `factions/`, plus a name grammar in `grammars/`.
- Missions: `missions/`, plus a text grammar in `grammars/`.
- Events and news: `events/`, plus a news grammar.
- Complications: `complications/`, plus a text grammar.
- Ships: `builders/`, `hulls/`, `parts/`, plus a model-name grammar.
- Fixed places: `landmarks/`.
- Campaigns: `campaigns/` and `battles/`.
- Music: `music/`.
- Extra lines for existing text (bar talk rumours, news, names): a grammar in `grammars/` with the same id as the existing one, which adds options and replaces nothing.
- Legend prose: `docs/lore/legends/` for the core game, `mods/<name>/legends/` for a mod.

## Step 1: offer, and ask before writing

When someone mentions a new faction, group, people, place or story, even in passing, first ask whether they'd like to expand the lore around it. If they open with "I want to make a mod", "I want to add some lore" or "I want to extend the core missions", say briefly what you can help with and start the conversation below.

Keep it a conversation, two or three questions at a time, with suggestions they can accept. Cover:

1. **What they want to make.** Lore (a history, a place, a people), a legend, a mod (a faction, ships, missions, a campaign, a landmark, a music style), or a mix.
2. **The details.** Tone. Which race or races. Which region (core, inner, outer realms, rim) and which arm (Meridian, Tolland, Anvil, Shroud). How it relates to the great powers and the running stories in the canon digest. Offer two or three concrete hooks from the digest that would fit.
3. **How they speak.** Work out the voice together. Start from the race's existing voice and ask what's different about this group: formality, what they complain about, what they swear by, a saying or two. Write two or three sample lines and ask if they sound right. This becomes the voice grammar or the lines in their mission and bar talk text.
4. **What it changes in play.** Missions (who gives them, what kind, how risky), ships, places on the map, news, bar talk, music.

Then give a short plan: the level (see below), the files you'll create or change, ids and their prefix, and two or three sample lines in the agreed voice. Wait for a yes.

## Step 2: pick the level

Explain the difference in a sentence or two and suggest the right one. Most new ideas should start as a legend or a mod.

- **Canon.** What is actually so: `docs/lore-bible.md`, `docs/lore/`, and core content in `game/data/`. Admins only. Large facts (a new war, a new power, anything that changes history) go into the lore bible first.
- **Legends.** In-world stories, rumours and myths that may not be true, always with a teller. The orcs' Old Horde is the model: the galaxy can tell a story without it being fact. Anyone can write one, in a mod or as a proposal for the core game. See `docs/lore/legends/README.md`.
- **Mods.** Self-contained and optional, made by anyone, in `mods/<name>/`. They add and never replace. They can't add great powers or change core lore.

If an idea would contradict canon or settle one of the "things nobody knows", say so and offer a version that works, usually a legend that someone believes.

## Step 3: check who's asking

Before planning changes to core content (anything in `docs/lore-bible.md`, `docs/lore/` or `game/data/`), check the person's role on the repository:

```sh
gh api user --jq .login
gh repo view DominicFinn/galaxus --json viewerPermission --jq .viewerPermission
```

- `ADMIN` or `MAINTAIN`: they're an admin. They can change canon and core content, and you can make those changes directly in their working copy. DominicFinn, when logged in, is the owner and can have you add new canon, legends, missions, campaigns and factions straight to the core game.
- Anything else, or `gh` missing or not logged in: treat them as a non-admin and say why in one line.

Non-admins get a mod, or, if they want the idea in the core game, a pull request whose title starts with "Canon proposal:" and whose description says what it adds and what canon it touches. Never commit or push core changes for a non-admin, and never edit the lore bible, `docs/lore/` or `game/data/` in their copy except as part of that proposal branch. Ask before opening any pull request.

Anyone can write mods and legends, in their own copy or as a pull request.

For an admin, work on a branch rather than `main`, and commit or push only when asked.

### "Extend the core missions"

Core missions are core content. For an admin, add templates to `game/data/missions/` and text grammars to `game/data/grammars/missions/`, following the existing files. For a non-admin, write the same missions as a mod: mod missions can name base faction types in `givers` (see `game/data/factions/starter.json`) and base complications, so they appear alongside the core work. Offer the canon proposal route if they want them in the base game.

## Step 4: write it

- Read `docs/lore/canon-digest.md`, then whatever longer lore file the idea touches, then the race's voice files, before writing a line.
- Follow the lore bible's "Never write" list and "Things nobody knows". Every rumour has a teller, and nobody settles anything.
- Write each character in their race's voice (the digest has a table; the grammars have the real lines). In missions, use `#voice:opener#`, `#voice:payment#` and the other voice rules so the client's race shows.
- Tone: grounded, a bit weary, occasionally funny. British English, plain sentences, no em dashes. Credits ("cr") and days; no calendar years. No real-world brands, places or religions, and nothing lifted from other fiction.
- Invent small and local things (a family, a station, a feud). Large facts go to the lore bible, and only admins add those.
- Prefix every new id in a mod with a short code (the example mod uses `eft-`). Reusing an id is only allowed to add lines to an existing grammar.
- Give grammars plenty of variety: several titles and briefings built from sub-rules, success and failure lines that react to the job.
- For ships and stations, follow [ship-design.md](ship-design.md): talk it through, build it, render it, refine it with the person.
- If the change adds or alters canon, update `docs/lore/canon-digest.md` in the same change so it stays in step with the lore bible.

## Step 5: check before it's done

Run these and fix every problem before handing over:

```sh
tools/validate-mods.sh <mod>                 # load the mod with the base game and check every id, reference and grammar
tools/validate-mods.sh <mod> --samples       # the same, then print sample text from every grammar the mod adds
tools/validate-mods.sh --grammar=<id>,<id>   # samples of particular grammars, core or mod
tools/validate.sh                            # the whole test suite, including content and every mod
```

Read the samples. Check that each race sounds like itself, that nothing reads as silly or rude when the parts combine, that variables like `{destination}` land in sensible places, and that nothing contradicts the lore bible. Rewrite anything that fails, then run the checks again.

For ships, render them as described in ship-design.md and look at the images.

## Step 6: hand over

Tell the person, briefly:

- what you added and where, with the level (canon, legend or mod);
- a few sample lines so they can hear the voice;
- anything that touches canon or a contested point they should look at;
- for a mod, how to switch it off (`"enabled": false` in `mod.json`) and how to share it;
- for a non-admin's canon proposal, the pull request link once they've agreed to open it.
