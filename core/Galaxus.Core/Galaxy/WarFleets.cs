using Galaxus.Core.Content;

namespace Galaxus.Core.Galaxy;

/// <summary>A war fleet holding a front: a flagship you can dock with, and its escorts.</summary>
public sealed record WarFleet(Faction Faction, Conflict Conflict, string FlagshipHull, string FlagshipName, List<string> EscortHulls);

/// <summary>
/// Where the war fleets are. Every system on an active front (a conflict past its first stage)
/// has one side's fleet parked off its station: the owner's if they're in the fight, otherwise the
/// first side's. The flagship docks like a station. Worked out from the galaxy as it stands.
/// </summary>
public static class WarFleets
{
    private static readonly string[] Names =
    [
        "Resolute", "Vigilant", "Iron Vow", "Steadfast", "Long Reach", "Gallant", "Reckoner", "Sentinel", "Bright Oath",
        "Warden", "Unbending", "Dauntless", "Lodestar", "Indomitable", "Covenant", "Far Light", "Bulwark", "Torchbearer",
    ];

    public static WarFleet? At(ContentDatabase db, Galaxy galaxy, StarSystem system)
    {
        if (system.Stations.Count == 0) return null;
        var conflict = galaxy.Conflicts.Where(c => c.FrontSystem == system.Id && c.Stage >= 1).OrderBy(c => c.StartDay).FirstOrDefault();
        if (conflict == null) return null;
        int side = conflict.Involves(system.Owner) ? system.Owner : conflict.A;
        var faction = galaxy.Factions[side];
        string builder = Battles.BattleJobs.BuilderFor(db, faction);
        var warships = db.Hulls.Values.Where(h => h.Builder == builder && h.IsCapital).OrderBy(h => h.Id, StringComparer.Ordinal).ToList();
        var flagship = warships.Where(h => h.Dockable).OrderByDescending(h => h.Class == "carrier").ThenByDescending(h => h.HullPoints).FirstOrDefault();
        if (flagship == null) return null;
        var escorts = warships.Where(h => h.Class is "frigate").Take(1).Concat(warships.Where(h => h.Class is "cruiser").Take(1)).ToList();
        if (escorts.Count > 0) escorts.Insert(0, escorts[0]);
        var rng = new Rng(system.Seed ^ (ulong)(uint)faction.Id ^ 0xf1ee7);
        return new WarFleet(faction, conflict, flagship.Id, rng.Pick(Names), [.. escorts.Select(h => h.Id)]);
    }
}
