# Content guide

Everything the game knows about races, ship builders, hulls, parts and generated text lives in JSON under `game/data`. Code never hard-codes content, so adding a ship or a name list is a data change only.

Run `tools/validate.sh` after any change. It loads every file, checks every reference, expands every grammar and fails with a list of problems. Content isn't done until it passes.

## The setting in brief

**Read docs/lore-bible.md before writing anything.** It's the canon: regions, arms, powers, what nobody knows and what must never be written. The short version:

- A PC space game in the spirit of Frontier: Elite II. You're an independent human captain who trades, takes missions and picks sides.
- The elf empire fell long ago at the rim of the galaxy. Its gate network still links everything. Humans now dominate from nearer the core, running gates they don't fully understand.
- An AI sits beyond a firewall at the rim. It can't cross the inner gates but builds synths and sends them inward. Nobody can tell a rim-made synth from a free one.
- Going outward means better tech, richer rewards, stranger missions, ships going quiet. The core is safer, crowded and politically tense.
- Tone: grounded, a little weary, occasionally funny. Not grimdark, not jokey. Avoid real-world brand names and direct copies from other fiction.

### Core races

| Race | Where | Character |
| --- | --- | --- |
| Human | Nearer the core | Dominant, practical, bureaucratic, run the gates without understanding them |
| Light elf | Among humans, near the core | Proud, diminished, formal, guard what they know about the fall |
| Dark elf | Ruins of the old empire at the rim | Stayed behind; terse, watchful, nobody knows if they made a deal with the AI |
| Orc | Heavy-gravity worlds | Strong, blunt, short-tempered, loyal once won, love big engines |
| Dwarf | Hollowed-out asteroids | Miners and shipbuilders, dry humour, value craft and contracts |
| Synth | Mostly human space | Minds in built bodies, legal status never settled, hassled at ports |
| Free machine | Their own systems | Ancient robot civilisation, origins are myth even to them, trade with all, trust none |

## File layout

Each file holds one definition object or an array of them. Split files however suits; the loader reads every `.json` in the folder and its subfolders. Comments (`//`) and trailing commas are allowed.

```
game/data/
  races/        RaceDef
  builders/     BuilderDef
  hulls/        HullDef, one file per builder is the convention
  parts/        PartDef, one file per builder is the convention
  commodities/  CommodityDef
  factions/     FactionTypeDef
  events/       EventDef
  missions/     MissionTemplateDef
  complications/ ComplicationDef
  grammars/     GrammarDef, in subfolders: names, ships, voices, mission-voices,
                factions, events, missions, complications, places
```

Ids are lower-case words joined by hyphens (`light-elf`, `hc-laser-2`) and must be unique within their type. Prefix part and hull ids with a short builder code so parallel work can't collide: `hc-` human corporate, `dw-` dwarf yards, `le-` light elf houses, `orc-` orc yards, `fm-` free machine foundries.

## Grammars

A grammar is a set of rules. Each rule is a list of alternatives, one picked at random (seeded, so the same seed always gives the same text).

```json
{
  "id": "names-orc",
  "description": "Orc personal names.",
  "variables": [],
  "rules": {
    "name": ["#given# #clan#", "#given# the #epithet#"],
    "given": ["Grukk", "Varsha"],
    "clan": ["Ironback", "Deepgrav"],
    "epithet": ["Loud", "Unbent"]
  }
}
```

Inside an alternative:

- `#rule#` expands another rule in the same grammar.
- `#other-grammar:rule#` expands a rule in another grammar, for shared word lists.
- `#rule.cap#`, `.upper`, `.lower` change case; `.a` adds "a" or "an". Modifiers chain: `#rule.a.cap#`.
- `{variable}` is filled in by the game. Every variable used must be listed in `variables`.

Repeat an alternative to make it more likely. Build names from syllables (`#start##end#`) as well as whole-word lists, so a few dozen entries give thousands of results. Check that combinations read well; avoid syllable pairs that make rude or silly words.

### Grammar contracts

The game asks for particular rules by name.

| Grammar | Referenced from | Must have | Variables available |
| --- | --- | --- | --- |
| Personal names | `race.nameGrammar` | `name` | none |
| Ship model names | `builder.modelNameGrammar` | `model` | none |
| Race voice | `race.voiceGrammar` | `greeting`, `hire`, `complaint`, `praise`, `combat`, `idle`, `leaving` | `{captain}`, `{ship}`, `{system}`, `{crewmate}` |
| Mission voice | `race.missionVoiceGrammar` | `opener`, `urgency`, `payment`, `warning`, `closer` | mission variables (below) |
| Faction names | `factionType.nameGrammar` | `name` | `{place}`, `{founder}`, `{race}` |
| Event news | `event.newsGrammar` | `start`, `ongoing`, `end` | `{system}`, `{faction}`, `{rival}` |
| Mission text | `mission.textGrammar` | `title`, `briefing`, `success`, `failure` | mission variables |
| Complication text | `complication.textGrammar` | `reveal`, `aftermath` | mission variables |
| Places | fixed ids `places-system`, `places-station` | `name` | none |

Mission variables: `{captain}`, `{client}`, `{faction}`, `{system}`, `{destination}`, `{cargo}`, `{tonnes}`, `{reward}`, `{target}`, `{deadline}`. `{reward}` comes formatted ("4,250 cr") and `{deadline}` comes as "6 days". `{cargo}` is lower case.

Mission and complication text can use `#voice:rule#` with any mission voice rule. The game swaps in the client race's mission voice, so the same template reads differently from an orc and from an elf. Use it in briefings so contracts carry the client's voice.

Voice rules are crew chatter, spoken by a crew member of that race:

- `greeting`: meeting the captain at a bar or station.
- `hire`: accepting a job on the ship.
- `complaint`: unhappy about pay, comfort, danger or the captain's choices.
- `praise`: pleased after a success or good treatment.
- `combat`: a short line during a fight.
- `idle`: small talk on a long trip; stories, opinions, rumours about the fall, the rim or the AI.
- `leaving`: quitting at the next port.

Each voice line should sound like that race (see the table above) and be one or two sentences. Use sub-rules so lines combine, for example `"complaint": ["#grumble# #reason#"]`.

## Races

```json
{
  "id": "orc",
  "name": "Orc",
  "kind": "core",                 // core (hand-written) or minor (generated)
  "description": "...",
  "homeEnvironment": "...",
  "gTolerance": 13,               // g; sets turn rate at speed. Human 9.
  "needsLifeSupport": true,
  "crewModifiers": { "enginePower": 1.15 },
  "nameGrammar": "names-orc",
  "voiceGrammar": "voice-orc"
}
```

Modifier and bias keys: `speed`, `acceleration`, `agility`, `hull`, `shields`, `shieldRecharge`, `weaponRecharge`, `enginePower`, `sensors`, `stealth`, `starRecharge`, `repair`, `mining`. Values are multipliers: 1.2 is 20% better.

## Builders

Builders now sit in a region (`core`, `inner`, `outer`, `rim`) and a `culture` (free text naming the power or culture, e.g. "Meridian Concord" or "Tolland Reach shipwrights"). Ships differ across the galaxy, so a human builder in the Tolland Reach should look and feel nothing like one in the Meridian core. Rim builders' ships are mostly salvaged or researched, not bought.

```json
{
  "id": "dwarf-yards",
  "name": "Dwarf yards",
  "race": "dwarf",
  "region": "inner",              // core, inner, outer or rim
  "culture": "Hold League",
  "style": "Heavy and armoured",
  "bias": "Tough and easy to repair, but slow",
  "statBias": { "hull": 1.4, "speed": 0.85, "agility": 0.85 },
  "palette": { "hull": "#7a6a55", "accent": "#c9a227", "trim": "#3a2f25", "glow": "#ffb347" },
  "modelNameGrammar": "models-dwarf-yards"
}
```

`statBias` is applied to every hull the builder makes. Don't bake it into hull numbers as well: design hulls to the class ranges below and let the bias do its job.

## Hulls

```json
{
  "id": "dw-anvil",
  "name": "Anvil",
  "builder": "dwarf-yards",
  "class": "miner",               // courier, freighter, fighter, gunship, explorer, miner, drone
  "description": "One or two sentences a shipyard would print.",
  "price": 180000,
  "mass": 120,                    // tonnes
  "baseSpeed": 75,                // m/s
  "pitchRate": 30, "yawRate": 24, "rollRate": 50,   // degrees per second
  "hullPoints": 450,
  "radius": 16,                   // collision sphere, metres
  "crewBerths": 3,
  "cargoTonnes": 120,            // fixed hold; fitted cargo racks add to it
  "slots": [
    { "type": "engine", "size": 2 },
    { "type": "weapon", "size": 1, "position": [-3, 0, -6] }
  ],
  "defaultLoadout": ["dw-drive-2", "dw-plant-2", "..."],
  "model": [ ...shapes... ]
}
```

The default loadout must fit: one part per slot, matching type, part size no bigger than the slot. It needs at least an engine and a power plant. Weapon slots carry a `position` (the hardpoint, where bolts come from) in ship space.

### Balance by class

Ranges before builder bias. Pick within them to suit the design.

| Class | Mass t | Base speed m/s | Pitch / yaw / roll °/s | Hull | Radius m | Crew | Cargo t | Typical slots |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| fighter | 15 to 25 | 100 to 125 | 70-90 / 50-65 / 140-180 | 100 to 160 | 5 to 8 | 0 | 0 to 4 | engine 1, plant 1, shield 1, sensor 1, weapon 1 to 2 ×2 to 4 |
| courier | 14 to 25 | 130 to 160 | 60-70 / 45-55 / 120-140 | 80 to 120 | 6 to 9 | 0 to 1 | 8 to 20 | engine 1 to 2, plant 1, shield 1, sensor 1, weapon 1 ×1, cargo 1 |
| explorer | 40 to 80 | 95 to 120 | 40-55 / 30-45 / 70-100 | 200 to 350 | 10 to 16 | 1 to 4 | 10 to 30 | engine 2, plant 2, shield 2, sensor 2 to 3, weapon 1 ×2, utility ×2 |
| gunship | 60 to 120 | 75 to 95 | 35-50 / 28-40 / 60-90 | 350 to 600 | 12 to 20 | 2 to 4 | 4 to 10 | engine 2 to 3, plant 2 to 3, shield 2 to 3, sensor 2, weapon 2 to 3 ×4 to 6 |
| miner | 80 to 200 | 65 to 85 | 25-40 / 20-30 / 40-60 | 300 to 600 | 14 to 25 | 2 to 5 | 60 to 200 | engine 2 to 3, plant 2, shield 2, sensor 1, weapon 1 ×1 to 2, cargo ×2, utility ×2 |
| freighter | 150 to 600 | 60 to 85 | 15-30 / 12-24 / 25-45 | 400 to 1200 | 20 to 45 | 2 to 6 | 100 to 600 | engine 3 to 5, plant 3 to 4, shield 3, sensor 1, weapon 1 to 2 ×2, cargo ×3 to 5 |

Acceleration is engine thrust (kN) divided by mass (t). Aim for these with the default loadout: fighter and courier 35 to 45 m/s², explorer 25 to 35, gunship 18 to 28, miner 12 to 20, freighter 8 to 15.

Prices: fighters 30k to 90k, couriers 40k to 100k, explorers 120k to 300k, gunships 200k to 500k, miners 150k to 400k, freighters 250k to 1.2M.

### Ship models

A model is a list of tapered boxes. Each box runs along Z from a back face to a front face.

```json
{ "position": [1.25, 0, 3.6], "back": [1.0, 1.0], "front": [0.7, 0.7], "length": 3.0,
  "skew": [0, 0], "roll": 0, "color": "trim", "mirror": true }
```

- Ship space: forward (the nose) is **-Z**, up is +Y, right is +X. Units are metres.
- `position` is the box's centre. The back face is at `z + length/2`, the front face at `z - length/2`.
- `back` and `front` are width (X) and height (Y) of each face. Make the front tiny for a pointed nose; make it bigger than the back for a flared intake.
- `skew` shifts the front face in X and Y relative to the back, for swept wings, raked fins and drooping noses.
- `roll` turns the box about its own Z axis, in degrees, for angled fins and V-tails.
- `color` is `hull`, `accent`, `trim`, `glow` (from the builder's palette) or a hex colour. `glow` parts are unlit: use them for engine exhausts, windows and lights, as thin boxes (length 0.1) on rear faces.
- `mirror: true` adds a copy mirrored across X. Put the original on the +X side.

A wing is a wide, flat box: large `back[0]` (span), small `back[1]` (thickness), positioned out to the side. An engine pod is a box with a glow plate behind it.

Aim for 6 to 20 shapes. Read as a silhouette at 640x360: big clear shapes, a few colour breaks, engines that glow. Keep the whole model inside about 1.5 × radius (validation checks this). Fighters are 8 to 14 m long, couriers 12 to 20, explorers 25 to 40, gunships 30 to 45, miners 35 to 60, freighters 50 to 110.

Builder looks:

- Human corporate: plain boxy shapes, grey with orange accent stripes, practical.
- Dwarf yards: thick, chunky, armoured slabs, short and wide, big blocky engines, few curves.
- Light elf houses: long and slender, swept blades, pale hull with teal and gold, delicate.
- Orc yards: oversized engines (often bigger than the cockpit), crude, spiky, front-heavy rams.
- Free machine foundries: no cockpit, odd lattices and offset blocks, purple and acid-green glow.

Check every model with the preview tool. It draws four views to a PNG without opening a window: front three-quarter (top left), side with the nose left (top right), top-down with the nose up (bottom left) and rear three-quarter (bottom right). The green ring is the collision radius.

```
tools/preview-hull.sh dw-anvil /tmp/dw-anvil.png
```

Look at the image. Fix floating parts, parts pointing backwards, and anything that reads as a jumble.

## Parts

```json
{
  "id": "dw-drive-2", "name": "Deepforge drive Mk II", "type": "engine",
  "size": 2, "mark": 2, "builder": "dwarf-yards", "price": 24000,
  "description": "One sentence.",
  "stats": { "thrust": 2100, "speedFactor": 1.03 },
  "color": "#ff4a3a"              // weapons only: bolt colour
}
```

`size` is 1 to 5 and must fit the slot. `mark` is 1 to 5 (marks I to V); 0 is a rare one-off. Each part type needs these stats:

| Type | Stats | Size 1 baseline | Per size | Per mark |
| --- | --- | --- | --- | --- |
| engine | `thrust` kN, `speedFactor` | thrust 600 to 900, speedFactor 1.0 | thrust ×2.5 roughly (size 2 about 1500 to 2600, size 3 3500 to 6000, size 4 7000 to 10000, size 5 12000 to 20000) | thrust +8%, speedFactor +0.03 |
| powerPlant | `pips`, `capacitor`, `capacitorRecharge` /s | 6 pips, capacitor 60, recharge 12 | pips 6, 6 to 7, 7, 8, 8; capacitor ×1.5; recharge ×1.4 | capacitor +10%, recharge +10% |
| shield | `capacity`, `recharge` /s, `rechargeDelay` s | capacity 80, recharge 6, delay 3 | capacity 80, 180, 380, 700, 1200; recharge about 7.5% of capacity | capacity +10%, recharge +10%, delay −0.1 |
| weapon | `damage`, `fireRate` /s, `energyPerShot`, `projectileSpeed` m/s, `range` m | damage 6 to 12, fireRate 2 to 6, energy 2 to 4, speed 700 to 1200, range 1500 to 2500 | damage ×2, energy ×1.8, fireRate ×0.8 | damage +10% |
| sensor | `range` m | 6000 | +3000 per size | +1000 |
| cargo | `tonnes` | 8 | 8, 16, 32, 64, 128 | none (marks don't apply; use mark 1) |
| utility | anything | | | |

Weapons are bolts. Vary them by character, not just numbers: a pulse laser (fast, light, cheap on energy), a mass driver (slow heavy bolts, long range), a scattergun (high fire rate, short range), and so on. Keep damage per second per size roughly in line: size 1 is about 25 to 45 DPS before power.

Prices rise with size and mark: size 1 Mk I parts cost 3k to 8k, each mark adds about 60%, each size multiplies by about 3.

Builder flavour for parts follows the builder's bias: dwarf parts are tougher and heavier, light elf parts efficient and fragile, orc engines strong and shields weak, free machine parts odd and efficient.

## Commodities

```json
{ "id": "grain", "name": "Grain", "category": "food", "basePrice": 120, "legality": "legal",
  "description": "Bulk cereal from agricultural worlds." }
```

Categories: food, water, medical, ore, metals, industrial, machinery, consumer, luxury, technology, weapons, narcotics, slaves, data, elder-artefacts, rim-tech, fuel. Legality is `legal`, `restricted` (permit needed in some places) or `contraband`. Prices are credits per tonne: bulk goods 50 to 300, manufactured goods 300 to 2,500, luxuries and technology 1,000 to 5,000, artefacts and rim tech 4,000 to 15,000.

## Faction types

A faction type is a kind of faction. The simulation rolls hundreds of actual factions from these.

```json
{
  "id": "mining-corporation", "name": "Mining corporation", "description": "...",
  "races": [],                       // races that found it; empty means any
  "tiers": ["minor", "regional"],    // minor, regional, great
  "criminal": false,                 // criminal bases are hidden from the nav map
  "traits": { "aggression": [0.2, 0.5], "greed": [0.6, 1.0], "expansion": [0.6, 1.0],
              "paranoia": [0.2, 0.5], "honour": [0.2, 0.6], "piety": [0.0, 0.2] },
  "nameGrammar": "faction-names-mining-corporation"
}
```

Traits are [min, max] between 0 and 1 and drive the simulation directly: aggression pushes towards war, greed towards trade and mining, expansion towards claiming systems, paranoia towards defence and secrecy, honour towards keeping deals, piety towards gate worship and hatred of rim tech.

## Events

```json
{ "id": "famine", "name": "Famine", "description": "...", "durationDays": [20, 60],
  "commodityDemand": { "food": 3.0, "medical": 1.8 }, "newsGrammar": "news-famine" }
```

Events create work. A mission template lists the events that produce it, and while an event runs its work is three times as likely to be offered. Demand multipliers above 1 raise prices for that category.

## Missions

```json
{
  "id": "famine-relief-run", "name": "Famine relief run",
  "type": "delivery",          // see list below
  "scale": "small",            // small, skirmish, battle, war
  "legal": true,
  "givers": [],                // faction type ids; empty means any
  "events": ["famine"],        // empty means standing work, always offered somewhere
  "cargoCategories": ["food", "medical"],
  "minRank": 0,                // 0 unknown, 1 contractor, 2 trusted, 3 commissioned, 4 honoured
  "baseReward": 3000,
  "complications": ["ambush-on-route", "cargo-is-stolen"],
  "followUps": [],             // templates that can follow on success
  "textGrammar": "mission-famine-relief-run"
}
```

Types: delivery, courier, passenger, escort, raid, smuggling, bounty, survey, salvage, claim-jumping, blockade-running, patrol, rescue, investigation, mining.

Rewards: small jobs 1,000 to 8,000 cr base, scaled up by the game for skirmish (×1.5), battle (×2.5) and war (×4) work. Illegal jobs pay about double.

The aim is variety. A few dozen templates multiplied by complications and race voices should rarely feel samey, so each template should have several title and briefing patterns built from sub-rules, and success and failure lines that react to the job.

## Complications

```json
{ "id": "cargo-is-stolen", "name": "Stolen cargo", "description": "...",
  "effect": "stolen-cargo", "weight": 1, "textGrammar": "complication-stolen-cargo" }
```

Effects are game rules, so they come from a fixed list: ambush, stolen-cargo, client-lies, rival-bid, fugitive, double-cross, hidden-contraband, deadline-cut, extra-stop, bad-intel, rival-offer, rim-signal. Write several complications per effect with different stories. `reveal` is the moment it goes wrong; `aftermath` is the line after the job ends.
