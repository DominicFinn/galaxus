# Designing ships and stations by conversation

Read this when someone wants a ship, a warship or a station for a mod (or, for an admin, the core game). The numbers and the model format are in `docs/content-guide.md` under "Builders", "Hulls" and "Ship models". This file covers how to talk a design through and how to show it.

## Talk it through first

Ask about these, a few at a time, and suggest answers when the person isn't sure:

- **Role and class.** What it's for. Map that to a class: fighter, courier, explorer, gunship, miner, freighter, or for warships frigate, cruiser, battlecruiser, carrier. A station is usually a large non-buyable hull (see "Stations" below).
- **Size.** Use the class length ranges in the content guide. Give a comparison: "about the length of a Sunbird", "half a Hesper freighter".
- **Builder.** An existing builder, or a new one for the mod. A new builder needs a race, region, culture, style, bias, `statBias`, a palette and a model-name grammar.
- **Style and themes.** What the builders care about, and how that shows in the silhouette. Get two or three concrete images, such as "sails", "a ram", "a cathedral spire", "patched with other ships' plates".
- **Colours.** Four palette colours: `hull` (most of the surface), `accent` (stripes and features), `trim` (dark breaks and detail) and `glow` (engines, windows, lights). Offer hex values and describe them in words.
- **Stats and feel.** Fast and fragile, slow and tough, and so on. Express it through the builder's `statBias` and pick hull numbers inside the class ranges.

Then give a short plan (name, class, builder, size, shape in a sentence, palette) and get a yes before writing JSON.

## Shape vocabulary

Every model is a list of tapered boxes. Translate the person's words like this:

| They say | Build it as |
| --- | --- |
| Needle nose, pointed | A long box with a tiny `front` face |
| Blunt, brick, slab | A box with `front` equal to `back` |
| Wings | Wide, flat boxes (large `back[0]`, small `back[1]`) out to the side, `mirror: true` |
| Swept-back wings | A wing whose `front` face is narrower than its `back`, with negative `skew[0]` on the +X side so the tip trails |
| Forward-swept wings | The same with positive `skew[0]`, so the tip leads (the Gyptian Sunbird's wings: back 3.4 wide, front 1.6, skew 1.2) |
| Fins, sails | Tall thin boxes, small X, large Y; `roll` for a V-tail |
| Ram, prow | A heavy wedge at the nose, front face smaller than back, often in `accent` |
| Engine pods | Boxes on the sides, each with a thin `glow` plate (length 0.1) on the rear face |
| Big engines (orc style) | Engine boxes wider than the cockpit, with large glow plates |
| Cockpit, canopy | A small box near the front top in `glow` or a pale colour |
| Spine, keel | A long thin box running the length of the hull |
| Rings, arches | Several thin boxes arranged round a centre with `roll` |
| Patched, salvaged | Offset boxes of slightly different sizes, mixed colours, a few hex colours off-palette |
| Lattice, crewless | Thin struts and offset blocks, no cockpit, `glow` in odd places |

Remember: forward is **-Z**, up is +Y, right is +X. `position` is the box centre. Put mirrored parts on the +X side. Aim for 6 to 20 shapes on small ships and more on warships and stations, and keep the model inside about 1.5 times the radius.

## Builder looks already in the game

Match an existing builder's look when the ship belongs to that culture, and make a new builder look clearly different from all of these:

- Human corporate: plain boxy shapes, grey with orange stripes.
- Meridian yards: formal, symmetrical, white and navy with gold, ceremonial fins.
- Hesper works: black wedges and blades, gold trim, one amber stripe.
- Tolland wrights: boxy, patched and bolted together, rust red.
- Synod forges: cathedral silhouettes, rings, arches, spires, lantern-gold windows.
- Breakers' guild: salvage builds, one hull with parts of three others bolted on.
- Dwarf yards: thick armoured slabs, short and wide, blocky engines.
- Light elf houses: long and slender, swept blades, pale with teal and gold.
- Ruin Courts: long elven lines broken and re-forged, hooked blades, grafts.
- Orc yards: oversized engines, crude, spiky, front-heavy rams.
- Free machine foundries: no cockpit, lattices and offset blocks, purple and acid green.
- Gyptian fleet yards (mod): sky blue and brass, forward-swept wings, tall sail fins.

## Show it, then refine it

After writing the hull, check it and look at it. The mod must be enabled (it is by default) for both tools to load it.

1. `tools/validate-mods.sh <mod>` to catch slot, loadout and size problems.
2. `tools/preview-hull.sh <hull-id> <scratch>/<hull-id>.png` renders four views to a PNG without opening a window: front three-quarter, side with the nose left, top-down with the nose up, and rear three-quarter. The green ring is the collision radius. Read the image.
3. For a view in the game itself, run `tools/play.sh -- --viewer=<hull-id> --screenshot=<scratch>/<hull-id>-viewer.png`. It opens the ship viewer on that hull, saves a screenshot and quits. Add `--scale` to put a fighter alongside for size. Read the image.

Put the images in the session's scratchpad, not the repository. Look for floating parts, parts pointing backwards, a model that pokes outside the ring, and anything that reads as a jumble. Fix those before showing the person.

Then describe what the pictures show, point the person at the image paths, and ask what to change: "longer nose?", "bigger engines?", "more brass?". Change the model, render again, and repeat until they're happy. Keep each round small so the change is easy to see.

## Stations

A faction's station is a hull with a model that stands in for the standard ring. Design it like a ship but much larger, set `"forSale": false`, and point the faction type at it with `"stationHull": "<hull-id>"`. `mods/gyptians` does this with `gy-homecoming`. Stations get the same preview and viewer loop. For an unusual shape that isn't a ship at all, a faction type can give the shapes directly with `"stationModel"` and a `"stationRadius"` instead (see "Giving a faction its own ships, stations, places and campaign" in `docs/expanding-the-universe.md`).

## Before handing over

- The hull's default loadout fits its slots, and the builder's parts exist (the validator checks both).
- The silhouette reads at a distance, the engines glow, and the colours match the palette the person agreed.
- The ship's description is one or two sentences a shipyard would print, in the setting's tone.
- If the faction should fly it, the faction type has `"builder"` set to its builder.
