# Expanding the universe

You can add your own factions, missions, events and flavour text to Galaxus without writing any code. Everything lives in plain text files in a folder called a mod. If you can edit a text file, you can make one. If you'd rather not, you can ask Claude to do it (see "Asking Claude to do it" below).

Mods add things. They can't change or remove what's already in the game, and they can't touch the great powers or the core lore. That keeps the main story intact and means a broken mod can be switched off without harm.

## What you can add

| You want to add | Folder in your mod | What it is |
| --- | --- | --- |
| A kind of faction | `factions/` | A faction type, such as "lane cooperative". The game rolls actual named factions from it. |
| Names for that faction | `grammars/` | A grammar the faction type uses to name itself. |
| A mission | `missions/` and `grammars/` | A mission template, plus a grammar holding the briefing text. |
| A twist on a mission | `complications/` and `grammars/` | Something that goes wrong mid-job. |
| A galaxy event | `events/` and `grammars/` | A famine, a toll dispute, a strike. Creates work and news. |
| A trade good | `commodities/` | Something to buy, sell and carry. |
| Extra flavour | `grammars/` | New options for text that already exists, such as news lines or place names. |
| Ships and parts | `hulls/`, `parts/`, `builders/` | For people comfortable with the numbers. See the [content guide](content-guide.md). |

Faction types are templates. If you add "beacon keepers", players won't meet a single fixed faction. They'll meet several generated ones with names like "Last Lamp Society", each in a different place, each with slightly different traits.

## What is off limits

- **Great powers.** The Meridian Concord, the Hesper Directorate and the others are base game only. A mod with a `powers/` folder is rejected. Add minor and regional factions instead.
- **Core lore.** Read the [lore bible](lore-bible.md) first. Its "Never write" section applies to every mod. In particular: don't write the AI speaking or appearing, don't have the firewall fail, don't settle anything listed under "Things nobody knows", and don't add major races.
- **Real-world material.** No real brands, places, politicians or religions, and nothing lifted from other fiction.
- **Replacing base content.** If your id matches one already in the game, the mod is rejected with an error naming your file. Give your ids a short prefix (the example mod uses `eft-`) so this never happens by accident.

Invent small, local things: a cooperative, a station, a feud. Large facts about the galaxy get added to the lore bible, not to mods.

## Folder layout

Mods live in a folder called `mods/` at the top of the repository, next to `game/`. Each mod is its own folder.

```
mods/
  my-mod/
    mod.json              optional, describes the mod
    factions/             faction types
    missions/             mission templates
    events/               events
    complications/        complications
    commodities/          trade goods
    grammars/             all text: names, briefings, news, extra options
```

Use only the folders you need. Inside each one you can make as many `.json` files as you like, with any names, and put them in subfolders. Each file holds one entry or a list of entries in square brackets.

Mods load in alphabetical order by folder name. Comments starting with `//` and trailing commas are allowed in the files.

### mod.json

```json
{
  "name": "My Mod",
  "author": "Your name",
  "description": "One sentence about what it adds.",
  "version": "1.0.0",
  "enabled": true
}
```

All of it is optional. The `name` appears in error messages, so make it recognisable.

### Switching a mod on and off

A mod is on when its folder is in `mods/`. To switch it off without deleting it, set `"enabled": false` in its `mod.json`. A mod with no `mod.json` is always on.

The game also looks for mods in a `mods` folder next to an exported build, and in Godot's user data folder (`mods` inside the Galaxus folder that `user://` points to). Use those if you want to keep a mod outside the repository.

### Example mod

`mods/example-free-traders/` is a small working mod. It adds two minor faction types, three missions, an event and a few extra news lines. It's labelled as an example in its `mod.json` and is switched on by default. Copy the folder to start your own, and set `"enabled": false` in the original if you don't want it in your game.

## Walkthrough: adding a faction

We'll add a faction type called "salvage union" in a mod called `my-mod`.

**1. Make the folders.**

```
mods/my-mod/factions/
mods/my-mod/grammars/
```

**2. Write the names.** A grammar is a set of rules. Each rule is a list of options and the game picks one at random. Create `mods/my-mod/grammars/salvage-names.json`:

```json
{
  "id": "mm-names-salvage-union",
  "description": "Names for salvage unions.",
  "variables": ["place"],
  "rules": {
    "name": [
      "#adj# #noun# Union",
      "{place} #noun# Union",
      "The #adj# #noun# Brothers and Sisters"
    ],
    "adj": ["Rusted", "Open", "Patient", "Second-Hand", "Honest"],
    "noun": ["Hook", "Grapple", "Tow", "Crowbar", "Wreck"]
  }
}
```

The `name` rule is required. `{place}` is filled in by the game with a nearby place name. Faction name grammars may use `{place}`, `{founder}` and `{race}`, and each one you use must be listed in `variables`.

**3. Describe the faction type.** Create `mods/my-mod/factions/salvage-union.json`:

```json
{
  "id": "mm-salvage-union",
  "name": "Salvage union",
  "description": "Wreck pickers who've organised so that nobody undercuts anybody else.",
  "races": [],
  "tiers": ["minor"],
  "criminal": false,
  "traits": {
    "aggression": [0.1, 0.4],
    "greed": [0.4, 0.8],
    "expansion": [0.1, 0.4],
    "paranoia": [0.3, 0.6],
    "honour": [0.4, 0.8],
    "piety": [0.0, 0.2]
  },
  "nameGrammar": "mm-names-salvage-union"
}
```

- `races` is the list of races that can found it. Leave it empty for any race.
- `tiers` is how big it can grow: `minor`, `regional` or `great`. Use `minor` and `regional` in mods.
- `criminal: true` hides its bases from the public map.
- Each trait is a range between 0 and 1. The simulation rolls a value in the range. Greed pushes a faction towards trade and mining, aggression towards war, honour towards keeping deals, piety towards gate worship, paranoia towards secrecy and defence, expansion towards claiming systems.

**4. Check it.** Run `tools/validate-mods.sh my-mod`. See "Checking your mod" below.

## Walkthrough: adding a mission

A mission needs a template and a grammar. The template says who offers it, how it pays and what can go wrong. The grammar holds the words. We'll add a job for the salvage union.

**1. Write the template.** Create `mods/my-mod/missions/wreck-haul.json`:

```json
{
  "id": "mm-wreck-haul",
  "name": "Wreck haul",
  "type": "delivery",
  "scale": "small",
  "legal": true,
  "givers": ["mm-salvage-union"],
  "events": [],
  "cargoCategories": ["metals", "machinery"],
  "minRank": 0,
  "baseReward": 3000,
  "complications": ["ambush-on-route", "cargo-is-stolen", "deadline-gate-queue"],
  "followUps": [],
  "textGrammar": "mm-mission-wreck-haul"
}
```

- `type` is one of: delivery, courier, passenger, escort, raid, smuggling, bounty, survey, salvage, claim-jumping, blockade-running, patrol, rescue, investigation, mining.
- `scale` is `small`, `skirmish`, `battle` or `war`. Stay with `small` unless you have a reason.
- `givers` lists the faction types that offer the job. Empty means any faction. You can name faction types from the base game too (`factions/starter.json` lists them) as well as your own.
- `events` lists the events that create this work. Empty means it's always on offer somewhere.
- `cargoCategories` is for jobs that carry cargo: food, water, medical, ore, metals, industrial, machinery, consumer, luxury, technology, weapons, narcotics, slaves, data, elder-artefacts, rim-tech, fuel.
- `minRank` runs from 0 (unknown) to 4 (honoured).
- `baseReward` is credits. Small jobs sit between 1,000 and 8,000. Illegal jobs pay about double.
- `complications` are ids of things that can go wrong. Use ids from `game/data/complications/starter.json`, or add your own.

**2. Write the words.** Create `mods/my-mod/grammars/wreck-haul.json`:

```json
{
  "id": "mm-mission-wreck-haul",
  "description": "Text for the wreck haul job.",
  "variables": ["client", "faction", "destination", "cargo", "tonnes", "reward", "deadline"],
  "rules": {
    "title": ["Wreck haul to {destination}", "{tonnes} t of {cargo} from the breakers"],
    "scene": [
      "The union pulled a freighter apart last week and the pieces need a home.",
      "There's a hold full of salvage and a buyer waiting at {destination}."
    ],
    "task": [
      "Take {tonnes} t of {cargo} to {destination} within {deadline}.",
      "Deliver the {cargo} to {destination} in {deadline}."
    ],
    "briefing": [
      "#voice:opener# #scene# #task# #voice:payment#",
      "#voice:opener# #task# #scene# #voice:warning# #voice:payment# #voice:closer#"
    ],
    "success": ["The buyer weighs it twice and pays. {faction} pays {reward}."],
    "failure": ["The salvage arrived too late to sell. {faction} pays nothing."]
  }
}
```

Mission grammars must have `title`, `briefing`, `success` and `failure`. The variables you may use are `{captain}`, `{client}`, `{faction}`, `{system}`, `{destination}`, `{cargo}`, `{tonnes}`, `{reward}`, `{target}` and `{deadline}`. `{reward}` arrives formatted ("4,250 cr") and `{deadline}` as "6 days".

`#voice:opener#` is the useful trick. The game swaps in the way the client's race speaks, so the same briefing reads differently from an orc than from a light elf. The voice rules are `opener`, `urgency`, `payment`, `warning` and `closer`. Put them around your own lines, as above, and the job sounds like its client.

**3. Check it** with `tools/validate-mods.sh my-mod`.

## Giving a faction its own ships, stations, places and campaign

Four optional fields let a mod's faction feel like its own people. With none of them set, the game behaves as before.

**Its own shipyard.** Add `"builder"` to the faction type, naming a builder (yours or an existing one). Factions of that type then fly that builder's ships in traffic, battles and previews.

```json
{ "id": "gy-gyptian-fleet", "builder": "gyptian-yards", ... }
```

**Its own stations.** Name one of the core station designs with `"stationDesign"`: `ring`, `claw` (a spire with curved pylons), `sphere` (an armoured battle sphere), `pyramid`, `asteroid` (a hollowed rock), `spindle` (a long cylinder habitat), `lattice` (struts and nodes), `crown` (spires round a dome) or `broken` (a ruined ring). Or add `"stationHull"` naming a hull whose model should stand in for the faction's stations (a ring or a battle station you've designed), or give the shapes directly with `"stationModel"` and a `"stationRadius"` for collisions. Stations owned by that faction use the model instead of the design their power or race would build; you dock within a few hundred metres of its radius.

```json
{ "id": "gy-gyptian-fleet", "builder": "gyptian-yards", "stationHull": "gy-homecoming", ... }
```

**Fixed places on the map.** A `landmarks/` folder holds named places. Each picks a system in its region from the galaxy's seed and its own id, so it's in the same place every time the galaxy is built and every time a save loads. It renames the system and its first station (adding a station if there's none), can set the economy, and can give the place to a great power or a faction type; if no faction of that type exists, one is founded there. The description shows on both maps.

```json
[{
  "id": "hw-bridgeford",
  "systemName": "Bridgeford 421",
  "stationName": "Hassall World",
  "region": "outer",
  "arm": "tolland",
  "ownerFactionType": "tolland-compact",
  "economy": "HighTech",
  "description": "The Hassall works, where half the Compact's drives are wound."
}]
```

`region` is core, inner, outer or rim; `arm` (optional) is meridian, tolland, anvil or shroud; `economy` is one of Agricultural, Mining, Refinery, Industrial, HighTech or Frontier. Turning a landmark mod on or off part-way through a saved game will move things, as with any faction mod.

A landmark can also change what its station is like:

- `"prices"` sets price multiples by commodity id or category. `{ "computer-cores": 0.45, "technology": 0.7 }` sells computer cores at under half the usual price and other technology a little cheap. An id wins over a category.
- `"parts"` lists parts on sale only there, always in stock at a fixed price: `[{ "part": "mw-martin-field", "price": 1500 }]`. Those parts never turn up on other stations' shelves.
- `"belt": true` puts the landmark in a system with an asteroid belt, for mining work.

A faction type with `"onlyAtLandmark": true` is only ever founded by its landmark, so the galaxy never rolls up copies of it elsewhere. Use it for a single named place, such as one works or one family, rather than a kind of faction.

**Work at a landmark.** A mission template with `"landmark"` is offered only at that landmark, every week, done in that system. `"giverName"` names the character who offers it, and their name fills `{client}`. A mining job there needs `"belt": true` on the landmark.

```json
{ "id": "mw-tally-belt", "type": "mining", "landmark": "mw-works", "giverName": "Tally", "baseReward": 16000, ... }
```

**A campaign for your faction.** Instead of `"power"`, a campaign can give `"factionType"`: you fly for the largest faction of that type, and if the galaxy doesn't have one, one is founded for you. `"baseLandmark"` puts your base at a landmark, and a step can happen there with `"where": "landmark:hw-bridgeford"`. Steps whose enemy is `rival` still fight the great power named in `"rival"`. A step can also give `"enemyName"`, which the briefing uses for `{enemy}` (a pirate gang with a name of its own, say), and `"givePart"`, a part handed to you with those orders that any outfitter fits free.

```json
{ "id": "gy-long-leaving", "factionType": "gy-gyptian-fleet", "rival": "meridian-concord", "baseLandmark": "gy-homecoming-reach", ... }
```

## Music styles

Each race has a lo-fi music style in `game/data/music/styles.json`, and a mod can add more in its own `music` folder. The game composes the songs itself from these rules, so a style is a few lines of JSON, not a recording. The style whose id matches a race plays in that race's systems.

```json
{
  "id": "human",
  "name": "Human space",
  "description": "Classic lo-fi, like a quiet night shift on a working port.",
  "tempo": [72, 80], "scale": "ionian", "root": 41,
  "progressions": [[2, 5, 1, 6], [4, 5, 3, 6]],
  "ninths": 0.6, "swing": 0.18,
  "keys": { "instrument": "rhodes", "pattern": "comp", "tone": 0.5 },
  "bass": { "instrument": "sub", "pattern": "lazy" },
  "lead": { "instrument": "flute", "density": 0.35 },
  "pad": { "instrument": "warm", "volume": 0.6 },
  "plucks": { "instrument": "kalimba", "pattern": "ostinato", "density": 0.4, "volume": 0.6 },
  "drums": { "kit": "boom-bap", "busy": 0.4 },
  "space": { "shimmer": 0.5, "reverb": 0.6, "vinyl": 0.45, "hiss": 0.35, "wow": 0.45, "warmth": 0.6 }
}
```

The options:

| Field | Values |
| --- | --- |
| `tempo` | [min, max] beats per minute, 50 to 120. Lo-fi sits around 60 to 90. |
| `scale` | ionian, dorian, phrygian, lydian, mixolydian, aeolian, locrian, harmonic-minor, pentatonic-minor, pentatonic-major, whole-tone |
| `root` | the key's root as a MIDI note in the bass register, 33 to 52 (41 is F) |
| `progressions` | chords as scale degrees, one a bar, four or eight to a progression |
| `ninths`, `swing` | how often chords get a ninth (0 to 1), and how late the off-beats land (0 to 0.35) |
| `keys` | rhodes, felt-piano, glass, organ or off; patterns comp, sustain, rolled |
| `bass` | sub, round, upright or off; patterns lazy, walk, sub, bounce |
| `lead` | flute, sine, mellow, bell or off |
| `pad` | warm, glass, choir, dark or off |
| `plucks` | kalimba, koto, harp, bell or off; patterns ostinato, arp, sparse |
| `drums` | kit boom-bap, brushes, dusty, glitch or off; `busy` 0 to 1 |
| `space` | shimmer, reverb, vinyl, hiss, wow (tape wobble) and warmth, each 0 to 1 |

Every part also takes `volume` (0 to 2), and `density` and `tone` (0 to 1).

To hear a style without starting the game, render it to WAV files:

```sh
dotnet run -c Release --project tools/music-preview -- game/data build/music human --moods
```

`--moods` renders the docked, flight and combat versions; `--stems` adds each channel on its own.

## Grammar basics

A grammar is a list of rules. A rule is a list of options and the game picks one at random, using a seed so the same seed always gives the same text.

Inside an option:

| Write | It means |
| --- | --- |
| `#rule#` | Expand another rule in the same grammar. |
| `#other-grammar:rule#` | Expand a rule from another grammar. |
| `#rule.cap#` | Capitalise the first letter. Also `.upper` and `.lower`. |
| `#rule.a#` | Put "a" or "an" in front. Modifiers chain: `#rule.a.cap#`. |
| `{variable}` | A value the game fills in, such as `{destination}`. List it in `variables`. |
| `#voice:rule#` | In missions and complications, the client race's own phrasing. |

Examples:

```json
"name": ["#adj# #noun#", "The #noun# of {place}"],
"adj":  ["Quiet", "Stubborn", "Second"],
"noun": ["Lamp", "Ferry", "Table"]
```

Repeating an option makes it more likely. Short lists of parts combine into many results, so six adjectives and six nouns give thirty-six names. Read the combinations aloud and drop any that come out silly or rude.

### Adding options to existing text

If your grammar has the same `id` as one in the game, your rules are added to it. Existing options are kept. This is the easiest way to add flavour. This file adds a news line to the existing trade boom story:

```json
{
  "id": "news-trade-boom",
  "description": "Extra trade boom news.",
  "variables": ["system", "faction"],
  "rules": {
    "ongoing": ["Independent captains at {system} say the boom is real."]
  }
}
```

You can add a new option to an existing rule or add a whole new rule. You can only use variables the original grammar already has, so check the original under `game/data/grammars/`. This is the one place where reusing an id is allowed.

### Which rules each kind of grammar needs

| Grammar for | Needs these rules | Variables you may use |
| --- | --- | --- |
| Faction names | `name` | `{place}`, `{founder}`, `{race}` |
| Event news | `start`, `ongoing`, `end` | `{system}`, `{faction}`, `{rival}` |
| Mission text | `title`, `briefing`, `success`, `failure` | the mission variables above |
| Complication text | `reveal`, `aftermath` | the mission variables above |

The [content guide](content-guide.md) has the full detail, including events, complications and commodities.

## Checking your mod

To try a mod in the game, choose **Mods** on the main menu. It lists every loaded mod with what it adds. A mod's page can start a new free game docked at its place (its landmark, or a station held by one of its factions), start its campaign from the beginning, or open the ship viewer on the ships it adds.

To check the files, run this from the top of the repository:

```
tools/validate-mods.sh              # every enabled mod, loaded together
tools/validate-mods.sh my-mod       # just your mod, even if it is switched off
tools/validate-mods.sh /path/to/my-mod
```

It loads the base game and your mod and checks every id, reference and piece of text. A good result looks like this:

```
Checked mod: My Mod (version 1.0.0)
No problems found.
```

Problems name the mod and the file, and say what to change:

```
Mod 'My Mod', file 'factions/salvage-union.json': id 'mining-corporation' already exists in the base game or an earlier mod. Mods can add content but not replace it; give yours a different id (a short prefix for your mod helps).
```

Common causes:

- **Invalid JSON.** A missing comma, a missing quote or an unclosed bracket. The error says the file.
- **Unknown grammar or id.** A typo in a reference, such as a `textGrammar` that doesn't match a grammar `id`.
- **Missing rule.** A mission grammar without `success`, for instance.
- **Undeclared variable.** You used `{place}` in a rule but left it out of `variables`.
- **Id already exists.** Pick another id.

To read your text back the way the game will write it, add `--samples`. After the checks it prints a few expansions of every grammar the mod adds, with stand-in names and places, and missions read in a different race's voice each time. `--samples=6` prints six of each. `--grammar=talk-orc,news-famine` prints samples of particular grammars instead, from the base game or a mod.

```
tools/validate-mods.sh my-mod --samples
```

To see a mod's ship or station, `tools/preview-hull.sh my-hull /tmp/my-hull.png` draws it from four angles, and `tools/play.sh -- --viewer=my-hull` opens it in the game's ship viewer. Both load the enabled mods in `mods/`.

`tools/validate.sh` runs the whole test suite, which includes loading everything in `mods/`. Run it before you share a mod.

## Asking Claude to do it

You don't need to write the files yourself. Open Claude Code in the repository and say what you'd like to make, in your own words:

```
I want to make a mod about a cooperative of small haulers who distrust the shipping lines.
```

```
I want to add some lore about a station in the Shroud Arm.
```

The repository's `CLAUDE.md` points Claude at the `lore-with-claude` skill (`.claude/skills/lore-with-claude/SKILL.md`), so it already knows where the canon, the race voices and the mod files live. It talks the idea through with you before writing anything: what you want to make, which races and region, how they speak and what they change in play. Then it suggests whether the idea fits best as a mod, a legend or a change to canon, gives you a short plan and waits for your go-ahead.

It also checks your role on the repository. Anyone can make mods and legends. Changes to the lore bible, `docs/lore/` and the core content in `game/data/` are for admins, so for anyone else Claude writes a mod, or prepares a pull request marked as a canon proposal if you'd like the idea in the base game.

It can design ships and stations with you too, by talking through the shape, size, builder and colours, rendering the result and changing it until you like it. When it's finished it runs the checker, reads the generated text back in each race's voice and fixes anything that's wrong.

Legends, the in-world stories that may not be true, are explained in [lore/legends/README.md](lore/legends/README.md).

Tips:

- **Be specific about the idea.** "A cooperative of small haulers who distrust shipping lines, with a dry sense of humour" gives better results than "a faction".
- **Ask for a small mod first.** One faction and two missions is easy to check. You can ask for more afterwards.
- **Read what comes back.** Claude follows the lore bible, but you're the judge of whether the tone suits your idea.

### Choosing a model

Different models suit different jobs, and the cheaper ones are fine for a lot of this.

- **Sonnet for judgement.** Use it to plan the mod, design factions and check that the result fits the lore. It's also the better choice for the first mission of each kind, because that one sets the voice the rest follow.
- **Haiku for volume.** Once the shape is settled, Haiku is quick and cheap for bulk writing: fifty extra news lines, forty place names, a dozen more briefing variants for an existing mission. Give it an existing file as the pattern and ask for more of the same.
- **Always validate.** Whichever model wrote the files, `tools/validate-mods.sh` is the check. If Haiku gets a rule name or a variable wrong, the validator says where.

A practical split: ask Sonnet to create the mod with its factions and one mission, then ask Haiku to "add ten more title options and five more briefing options to each mission grammar in mods/[my-mod-name], in the same style", then run the validator.

## Sharing a mod

A mod is a folder, so you can zip it, send it or open a pull request that adds it under `mods/`. Run `tools/validate.sh` first. If you want it in the repository but off by default, set `"enabled": false` in its `mod.json`.
