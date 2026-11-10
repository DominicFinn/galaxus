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

Run this from the top of the repository:

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

`tools/validate.sh` runs the whole test suite, which includes loading everything in `mods/`. Run it before you share a mod.

## Asking Claude to do it

You don't need to write the files yourself. Claude Code can read the lore bible, write the files in the right shape and run the validation for you. Open Claude Code in the repository and paste a prompt like this, filling in the square brackets:

```
I want to add a mod to Galaxus. Read docs/expanding-the-universe.md, docs/content-guide.md
and docs/lore-bible.md first, and look at mods/example-free-traders as a pattern.

Create a new mod in mods/[my-mod-name]/ with a mod.json. It should add:
- [one or two minor factions, described in a sentence each]
- [three missions in those factions' voices, described in a sentence each]
- [an event, if you want one]
- [any extra flavour, such as news lines or station names]

Rules: add content only and never reuse an existing id, so prefix every id with
"[prefix]-". No great powers, no new major races, and nothing from the lore bible's
"Never write" list. British English, plain sentences, no em dashes, the tone described in
the lore bible. Make the missions varied, with several title and briefing options each,
and use #voice:opener#, #voice:payment# and the other voice rules in briefings.

When you've written it, run tools/validate-mods.sh [my-mod-name] and fix every problem
until it passes. Then run tools/validate.sh. Tell me what you added and anything in it
that touches lore I should check.
```

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
