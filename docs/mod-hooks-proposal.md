# Proposal: four engine hooks for mods

Mods can add factions, ships, battles and campaigns, but some story ideas need things the engine doesn't yet read from data. These came up while writing the Hassall World and Gyptians mods on the `mods/back-story` branch. Each hook is small, optional and backwards compatible: with no mod using it, the game behaves exactly as now.

Ordered by how much they would unlock.

## 1. Faction types can name their own shipbuilder

**Now.** `BattleJobs.BuilderFor` gives a great power its power's builder and every other faction its race's builder, from the fixed `RaceBuilders` table. A mod can add a builder (the Gyptians add `gyptian-yards`) but generated Gyptian factions still fly `human-corporate` ships, in traffic (`Traffic.cs:82`), in battles (`BattleJobs.cs:142` and `:145`) and in previews (`Preview.cs:51`).

**Change.** Add `string? Builder` to `FactionTypeDef`. In `BuilderFor`, after the power check, use `db.FactionTypes[faction.TypeId].Builder` when it is set and exists in `db.Builders`. Validate that the builder exists.

**Mod use.**
```json
{ "id": "gy-gyptian-fleet", "builder": "gyptian-yards", ... }
```

## 2. Faction types can supply a station model

**Now.** Every station is `MeshFactory.BuildStation`, a procedural hub and ring tinted with the owner's colour. `Collision.StationDistance` assumes that exact shape.

**Change.** Add an optional `List<ShapeDef> StationModel` (and `float StationRadius`) to `FactionTypeDef`. When a station's owner has one, build the mesh with the same code as ship models (`MeshFactory.BuildShip` with the owner's builder palette, or a palette on the faction type), scaled by `StationScale`. For collision, either fall back to a sphere of `StationRadius` for custom stations, or generalise `StationDistance` to test the shapes' boxes. The sphere is enough to start with.

**Mod use.** The Gyptians' ring station model (`gy-homecoming` in `mods/gyptians/hulls/gyptian-yards.json`) could be reused as their station model directly.

## 3. Landmarks: fixed named places on the generated map

**Now.** System and station names come from the `places-system` and `places-station` grammars (`GalaxyGenerator.cs:64` and `:288`). A mod can't guarantee that a particular named place exists, so Hassall World's "Bridgeford 421" is only named in the story text.

**Change.** A new mod folder, `landmarks/`:
```json
{
  "id": "hw-bridgeford",
  "systemName": "Bridgeford 421",
  "stationName": "Hassall World",
  "region": "outer",            // core, inner, outer or rim; optional arm too
  "ownerFactionType": "tolland-compact-or-a-faction-type-id",
  "economy": "HighTech",        // optional
  "description": "One line for the system map."
}
```
After generation, `GalaxyGenerator` picks a system in the right region with the seeded RNG, renames it and its first station (adding a station if it has none) and, if asked, gives it to a faction of the named type. Because saves keep the seed and regenerate the galaxy, the choice must depend only on the seed and the landmark id, so it's stable between loads. Switching a mod on or off mid-save will move things, which is already true of faction mods.

A campaign could then set `"baseLandmark": "hw-bridgeford"` to start there, and `"where": "landmark:hw-bridgeford"` on a step.

## 4. Campaigns for factions that aren't great powers

**Now.** `CampaignDef.Power` must be a great power, and `Campaigns.cs` finds the client with `galaxy.Factions.First(f => f.PowerId == def.Power)`. The Gyptian campaign has to fly for the Lantern Synod with the Gyptians alongside.

**Change.** Allow `"factionType"` instead of `"power"`. At start, use the largest generated faction of that type, or create one at the campaign base if none exists. `CheckCampaigns` accepts either field. Battles with enemy "rival" keep using `Rival`, which stays a great power.

## After the hooks land

The mods on `mods/back-story` would need only small additions:
- `gyptians`: `"builder"` and a `"stationModel"` on the faction type; the campaign switches to `"factionType": "gy-gyptian-fleet"`.
- `hassall-world`: a `landmarks/` file for Bridgeford 421 and Hassall World, with the campaign based there.
