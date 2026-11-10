# Canon digest

A short version of the canon, for writers who need to build on the history without reading all of it. It summarises [lore-bible.md](../lore-bible.md) and the files in this folder. The lore bible wins on any conflict, and the longer files have the detail. When the lore bible or a lore file changes, update this digest in the same change.

## The galaxy

A spiral. Humans live thickest in the bright core. Outward come the inner systems (settled, industrial), the outer realms (frontier, claim disputes, khanates, pirates) and the rim, where the elf empire ruled and fell. Most systems are empty, and settled space is islands linked by gates.

Settled space runs along four arms (proposed canon):

- **Meridian Arm.** Human heartland. Old money, the Concord's capital, the Gate Authority, the light elf courts. Formal and proud of order.
- **Tolland Reach.** Frontier human space. Independent captains, cheap rugged ships, suspicion of the core.
- **Anvil Drift.** Asteroid belts and heavy worlds. Dwarf holds and orc khanates, sharing it uneasily.
- **Shroud Arm.** Runs furthest towards the rim. Elder ruins, salvage, smugglers, cults. Trouble comes in from here.

## Gates and the firewall

The elves built the gates. Everyone depends on them and nobody alive fully understands them. Humans run most of them through the Gate Authority.

There's no faster-than-light radio. News travels on ships, so word spreads system by system and arrives late.

Gates nearer the centre filter out AI code: the firewall. Wardens scan ships at the inner gates for rim tech. Nobody knows where the filtering starts, and spacers talk about "the last good gate" (the Hollow Gate, to some). The firewall may have been the elves' last act.

## The AI

It sits beyond the firewall at the rim and can't come inward, but it builds synths and sends them. It stays offscreen. Players feel it through synths, missing ships, odd signals and rumours. It is never named, never speaks and never appears.

## Races and how they speak

The voice grammars are in `game/data/grammars/voices/` (crew chatter), `mission-voices/` (how a client words a contract) and `talk/` (station bar talk). Read the race's files before writing for it.

| Race | Who they are | How they speak |
| --- | --- | --- |
| Human | The dominant power, from the core outward. Practical, numerous, divided into nations and corporations. | Casual and wry. Contractions, complaints about forms, tolls and late pay. "Morning, Captain. What's the pay, and is it in actual credits?" |
| Light elf | Proud, diminished survivors of the fall, mostly in the core. The Eleven Houses. | Formal, no contractions, courteous with a barb in it. House and standing matter. "I shall endeavour not to be astonished." Deflects origin questions with ritual phrases. |
| Dark elf | Stayed behind in the ruins at the rim. The Ruin Courts. | Very short, flat and watchful. Hints, never explains. "Captain." "Where to?" "We stayed." |
| Orc | Heavy-world people of the Grav Khanates. Strong, loud, loyal once won, not stupid. | Loud, exclamations, short plain sentences, boasts about engines and food, oaths by the Goddess. "Oi, Captain! How big is your drive?" |
| Dwarf | Asteroid holds of the Hold League. Miners and shipbuilders. | "Aye." Dry, gloomy humour, everything in writing, judges welds and contracts. "I'll be gloomy, but I'll be useful." |
| Synth | Minds in built bodies, mostly in human space. Legal standing never settled. | Polite, careful, quietly wry about being searched and registered. "You looked at me, and not at my chest plate." |
| Free machine | Robots who broke away so long ago their origins are myth. The Lattice. | Precise, literal, no contractions, measures things. "Query: are you the captain." "State terms." "Betrayal is a poor investment." |

Minor races are generated and live in a handful of systems each. Never add a major race. The player is always human.

## Machines

In tiers by age: elder machines (from the elf empire, maker unknown), free machine civilisations, robots and androids inside other societies, synths, and rim-made synths, which nobody can tell from the others. The running question is which machines the AI has already reached.

## The great powers

| Power | Race | Character |
| --- | --- | --- |
| Meridian Concord | Human | Republic of committees in the core. Runs the Gate Authority. Orderly, slow, quietly corrupt. |
| Hesper Directorate | Human | Corporate state run by shareholders. Efficient, ruthless, everything has a price. |
| Tolland Compact | Human | Loose frontier confederation. No standing fleet, no central toll, distrusts the core. |
| Lantern Synod | Human | Venerates the gates as holy work. Charitable, rigid, fierce about rim tech. |
| Eleven Houses | Light elf | The surviving noble houses, holding courts in the core. Wealth, influence, secrets. |
| Hold League | Dwarf | Trading league of asteroid holds. Contracts are sacred. Builds half the galaxy's freighters. |
| Grav Khanates | Orc | Rival khanates that raid, trade and feud in equal measure. |
| The Lattice | Free machine | The largest free machine civilisation. Precise, patient, unreadable. |
| Ruin Courts | Dark elf | A web of grudges among the halls in the empire's ruins. |

Mods can't add or change great powers. They add minor and regional faction types that sit between them.

## Human history, briefly

Humans start their history already spread across many worlds. Eras are counted in generations and reigns, never calendar years. See [humans.md](humans.md).

1. **The Scatter.** Fragments. The Lamp Registry kept one chart for everyone. "Scatterlaw" is any rule nobody can source.
2. **The Gate Age and the Corvane Dominion.** Humans found the gates, or were given them. The Corvane Gate Office ruled by switching gates off for systems that didn't pay.
3. **The Walkout and the Hundred Flags.** The Dominion's machine workers, the Hands, stopped working in one day. The Dominion lost its navy at the Silent Yards and fell apart. Wardens who kept the gates lit for anyone became the Lantern Synod. Whether the Hands were property or people is the root of the Synth Question.
4. **The Kestrel League and the Heavy Winters.** A merchant league signed the dwarf contracts (the basis of Hold League law), met the orcs and fought three generations of raids. It made peace by hiring orc fleets and paying in drive parts.
5. **The Restoration and the Quill Gate Betrayal.** Admiral-Regent Iseult Marrane restored the core. Her deputy Tobias Renn destroyed her flagship at Quill Gate, ruled for nine days and vanished. Quill Gate has been closed ever since.
6. **The Tolland Rising.** A pilots' strike became a fleet, then sixty captains signed at Tolland's Table. That's the Compact.
7. **The Concord Settlement and the Directorate Buyout.** The core became a republic of committees, with the Houses as witnesses. The Kestrel houses bought the Concord's navy debt and became the Hesper Directorate.
8. **The Quiet Peace.** Now. The powers haven't fought directly for a long time.

## Elves

Light elves call the empire the Long Day and the fall the Dimming. Dark elves call the fall the Stopping. Humans just say "the fall". Fixed points of light elf custom: the Lighting (a young elf's lamp), the Roll of Houses with its two silent pauses, the Empty Chair at formal tables, and the four-season Turning. Dark elf ceremony is mostly funeral, and their one idea is "we stayed". See [elves.md](elves.md).

## Orcs

The orcs believe they once ruled the whole galaxy as the Old Horde. They didn't. The nearest they came was Ozgrakka the Unbent's Feast of Nineteen Days, which collapsed over the seating plan. The Map of the Horde and the Tablets of Kron are later work. Their Goddess is a very large orc ("Mam"); gravity is her weight. Ships carry an empty Big Chair for her. The orcs are sincere, the others are sceptical, and nobody settles it. See [orcs.md](orcs.md).

## Running stories

These are live in the present and are the easiest things to hang new work on.

- **The Synth Question.** Whether synths are people. It's been with a Concord committee for a long time. Synths are searched at every human port.
- **Rim tech searches.** Gate wardens search ships at the inner gates. Smuggling rim tech pays well and is dangerous.
- **The toll dispute** between the Concord and the Compact.
- **Quill Gate,** closed since Renn, with Synod pilgrims singing outside it every Admiral's Day.
- **False Renns,** one every generation, usually in the Shroud Arm.
- **Ships going quiet** towards the rim, and odd signals nobody can explain.
- **Whispers from the rim** growing louder. Whether the Quiet Peace is peace or a pause.

## Things that must never be explained

Hint at them, have characters argue and spread rumours, but never settle them.

- What caused the fall of the elf empire. The AI is the obvious suspect.
- What the light elves know about the fall.
- Whether the dark elves made a deal to survive.
- Where the elves came from. No elf origin world is ever named, located or described.
- Who built the elder machines, and why.
- What the AI wants.
- Which synths are rim-made.
- Where the firewall begins, and whether it's weakening.
- Where humans came from. That they all came from one planet is only a rumour, held alongside "Many Worlds" and "the Carried". See [origins.md](origins.md).
- Whether the Old Horde ever existed, as far as any orc is concerned.

## Never write

- The AI speaking, appearing as a character or being named.
- The firewall failing, or any gate letting the AI through.
- New major races, faster-than-light radio, or humans who aren't the dominant power.
- Real-world brands, places, religions, politicians or country-specific slang.
- Names or phrases lifted from other fiction (Elite, Star Wars, Tolkien, Warhammer, Mass Effect, A Fire Upon the Deep).

## Tone

Grounded, a bit weary, occasionally funny. People have jobs, debts and grudges. Danger is mostly mundane, so the strange stuff at the rim lands harder. British English, plain sentences, no em dashes, credits ("cr") for money, days for time, no calendar years.

## What the mods have added

Mods aren't canon, but writers should know what's there so new work doesn't collide with it.

- **The Gyptians** (`mods/gyptians`): a small human people who say they descend from the one human homeworld. Nobody can prove it or disprove it. Their own shipyard, a ring station and a campaign.
- **Hassall World** (`mods/hassall-world`): a short campaign for Hassall, an eccentric engineer with a station full of synths and robots at Bridgeford 421, who does not believe in orcs.
- **The Quill Vigil** (`mods/quill-vigil`): a Lantern Synod campaign escorting pilgrims to Quill Gate, against the Directorate's wishes.
- **Old Stories** (`mods/old-stories`): rumours in bar talk about the Dominion, Quill Gate, the Heavy Winters, the Houses, the Old Horde and the Walkout.
- **Example: Free Traders** (`mods/example-free-traders`): the worked example in the docs.
