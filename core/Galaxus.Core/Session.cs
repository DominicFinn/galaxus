using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Missions;

namespace Galaxus.Core;

/// <summary>What happened during a jump: days passed, jobs settled, news picked up on the way.</summary>
public sealed record JumpReport(int Days, List<MissionResult> Results, List<NewsItem> News);

/// <summary>
/// One playthrough: the galaxy, its simulation and the player's career. The Godot layer drives
/// this; everything here also runs headless for tests.
/// </summary>
public sealed class Session
{
    public ContentDatabase Db { get; }
    public Galaxy.Galaxy Galaxy { get; }
    public Simulation Simulation { get; }
    public Career Career { get; } = new();

    public StarSystem Here => Galaxy.Systems[Career.SystemId];

    private Session(ContentDatabase db, Galaxy.Galaxy galaxy)
    {
        Db = db;
        Galaxy = galaxy;
        Simulation = new Simulation(db, galaxy);
    }

    /// <summary>
    /// A new game: generate the galaxy, run it for a month so there's news and history, and start
    /// the player at a Tolland Compact colony as an independent captain.
    /// </summary>
    public static Session StartNew(ContentDatabase db, ulong seed, GalaxySettings? settings = null)
    {
        var session = new Session(db, GalaxyGenerator.Generate(db, seed, settings));
        session.Simulation.Run(30);

        var galaxy = session.Galaxy;
        var tolland = galaxy.Factions.FirstOrDefault(f => f.PowerId == "tolland-compact");
        var start = galaxy.Systems.Where(s => s.State == SystemState.Colony && s.Stations.Count > 0)
            .OrderByDescending(s => tolland != null && s.Owner == tolland.Id)
            .ThenBy(s => s.Id == tolland?.HomeSystem ? 1 : 0) // not the capital itself
            .First();
        session.Career.SystemId = start.Id;
        return session;
    }

    public List<MissionOffer> Board() =>
        MissionBoard.For(Db, Galaxy, Here, Career.Captain, Career.RankWith)
            .Where(o => !Career.TakenOffers.Contains(o.Id)).ToList();

    /// <summary>Hulls for sale here: builders within one region of this system. Rim designs are never sold.</summary>
    public List<HullDef> Shipyard()
    {
        if (Here.Stations.Count == 0) return [];
        int here = Array.IndexOf(ContentRules.Regions, Here.Region);
        return Db.Hulls.Values
            .Where(h => h.Class != "drone")
            .Where(h => Db.Builders.TryGetValue(h.Builder, out var b) && b.Region != "rim"
                        && Math.Abs(Array.IndexOf(ContentRules.Regions, b.Region) - here) <= 1)
            .OrderBy(h => h.Price).ThenBy(h => h.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Travels to a system along the quickest route. The galaxy moves on while you fly.</summary>
    public JumpReport? Jump(int destination)
    {
        var route = Galaxy.Route(Career.SystemId, destination);
        if (route.Count < 2) return null;
        int days = Galaxy.RouteDays(route);
        int newsBefore = Galaxy.News.Count;
        int dayBefore = Galaxy.Day;

        var results = new List<MissionResult>();
        // Settle at each stop, so a job for a system on the way pays out as you pass through.
        foreach (var (from, to) in route.Zip(route.Skip(1)))
        {
            Simulation.Run(Galaxy.TravelDays(from, to));
            Career.SystemId = to;
            results.AddRange(Career.Settle(Galaxy));
        }

        var news = Galaxy.News.Where(n => n.Day > dayBefore).TakeLast(Math.Max(0, Galaxy.News.Count - newsBefore)).ToList();
        return new JumpReport(days, results, news);
    }
}
