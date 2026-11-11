using Galaxus.Core.Galaxy;

namespace Galaxus.Core.Law;

/// <summary>A ship somewhere in the system, as the system map shows it.</summary>
/// <param name="Id">Stable for the day, so it's the same ship when you get there.</param>
/// <param name="Role">trader, patrol, miner, fence or pirate.</param>
/// <param name="FactionId">Who it flies for: the police's faction, a criminal faction for fences, or -1.</param>
/// <param name="Km">Where it is, in km from the star.</param>
public sealed record Contact(string Id, string Name, string Role, string HullId, int FactionId, string RaceId, (double X, double Y, double Z) Km);

/// <summary>
/// The ships about a system beyond the ones near you: traders on their runs between worlds,
/// patrols, miners at the belt, and in poorly policed systems pirates and the fences who deal in
/// contraband, lurking off a quiet moon. Worked out from the system's seed and the day, so the
/// map, a hail and the ship you meet on arrival all agree. Busier systems have more of them.
/// </summary>
public static class SystemContacts
{
    private static readonly string[] Names =
    [
        "Ashby Rose", "Long Haul", "Patience", "Second Thoughts", "Mercy of Kell", "Grey Heron", "Fair Wind", "Old Reliable",
        "Quiet Hours", "Penny Black", "Margin Call", "Small Mercies", "Under Budget", "Kestrel", "Tally Ho", "Late Again",
        "Dunmore Lass", "Iron Promise", "Steady On", "Wren", "Far Shore", "Cold Comfort", "Lucky Strike", "Open Hand",
    ];

    private static readonly string[] Shady =
        ["No Questions", "Back Door", "Lantern Out", "Low Tide", "Spare Key", "Quiet Word", "Night Market", "Loose Change"];

    public static List<Contact> For(Session session, StarSystem system, SolarSystem solar)
    {
        var db = session.Db;
        var galaxy = session.Galaxy;
        var contacts = new List<Contact>();
        if (!system.Settled && system.Stations.Count == 0) return contacts;
        var rng = new Rng(system.Seed ^ Rng.Hash($"contacts:{galaxy.Day}"));
        var profile = SystemProfiles.For(galaxy, system, db);
        var police = LawRules.Police(galaxy, system);
        var gang = galaxy.Factions.Where(f => f.Criminal && f.Alive && (f.Haunts.Contains(system.Id) || system.Owner == f.Id)).OrderBy(f => f.Id).FirstOrDefault();
        bool shady = profile.Security is "Low" or "Lawless" || gang != null;
        var bodies = Enumerable.Range(0, solar.Bodies.Count).Where(i => solar.Bodies[i].Kind != BodyKind.Belt).ToList();
        if (bodies.Count == 0) return contacts;
        int home = solar.Stations.Count > 0 ? solar.Stations[0].Body : bodies[0];
        int belt = Mining.Asteroids.BeltIndex(solar);
        int day = galaxy.Day;

        (double X, double Y, double Z) Near(int body, double km, Rng r)
        {
            var c = solar.PositionKm(body, day);
            double a = r.NextDouble() * Math.Tau, e = (r.NextDouble() - 0.5) * 0.4;
            double reach = Math.Max(solar.RadiusKm(body) * 3, 0) + km;
            return (c.X + Math.Cos(a) * reach, c.Y + Math.Sin(e) * reach, c.Z + Math.Sin(a) * reach);
        }

        void Add(string role, string cls, string? builder, int faction, string race, (double, double, double) km, string name)
        {
            var hulls = db.Hulls.Values.Where(h => h.Class == cls && h.ForSale && (builder == null || h.Builder == builder)).OrderBy(h => h.Id, StringComparer.Ordinal).ToList();
            if (hulls.Count == 0) hulls = [.. db.Hulls.Values.Where(h => h.Class == cls && h.ForSale).OrderBy(h => h.Id, StringComparer.Ordinal)];
            if (hulls.Count == 0) return;
            var hull = rng.Pick(hulls);
            contacts.Add(new Contact($"{system.Id}:{day}:{contacts.Count}", name, role, hull.Id, faction, race, km));
        }

        int traders = profile.StationSize switch { StationSize.Outpost => 1, StationSize.Station => 3, StationSize.Hub => 5, _ => 6 };
        for (int i = 0; i < traders; i++)
        {
            // On the way between the station's world and another, some way along.
            int other = rng.Pick(bodies);
            var a = solar.PositionKm(home, day);
            var b = solar.PositionKm(other, day);
            double f = rng.Range(0.1f, 0.9f);
            Add("trader", rng.Pick(new[] { "freighter", "freighter", "courier" }), null, -1, "human",
                (a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f + rng.Range(-2e4f, 2e4f), a.Z + (b.Z - a.Z) * f), rng.Pick(Names));
        }
        if (police != null)
            for (int i = 0; i < (profile.Security == "High" ? 2 : 1); i++)
                Add("patrol", rng.NextFloat() < 0.6f ? "fighter" : "gunship", Battles.BattleJobs.BuilderFor(db, police), police.Id, police.RaceId,
                    Near(home, rng.Range(4e3f, 3e4f), rng), $"{(police.Name.StartsWith("The ") ? police.Name[4..] : police.Name)} patrol");
        if (belt >= 0)
            for (int i = 0; i < 1 + rng.Next(2); i++)
            {
                var b = solar.Bodies[belt];
                double angle = rng.NextDouble() * Math.Tau, r = b.Orbit * SolarSystem.KmPerAu;
                Add("miner", "miner", null, -1, "dwarf", Mining.Asteroids.Landing(b, (Math.Cos(angle) * r, 0, Math.Sin(angle) * r)), rng.Pick(Names));
            }
        if (shady && rng.NextFloat() < 0.75f)
        {
            // A fence waits off a quiet world, away from the station.
            var quiet = bodies.Where(i => i != home).ToList();
            int hideout = quiet.Count > 0 ? rng.Pick(quiet) : home;
            Add("fence", "courier", "breakers-guild", gang?.Id ?? -1, gang?.RaceId ?? "human", Near(hideout, rng.Range(2e3f, 2e4f), rng), rng.Pick(Shady));
        }
        if (profile.Security is "Low" or "Lawless")
            for (int i = 0; i < rng.Next(3); i++)
                Add("pirate", "fighter", "breakers-guild", gang?.Id ?? -1, "human", Near(rng.Pick(bodies), rng.Range(5e3f, 5e4f), rng), rng.Pick(Shady));
        return contacts;
    }
}
