using System.Numerics;
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
    public GalaxySettings Settings { get; }

    public StarSystem Here => Galaxy.Systems[Career.SystemId];

    private Session(ContentDatabase db, Galaxy.Galaxy galaxy, GalaxySettings settings)
    {
        Db = db;
        Galaxy = galaxy;
        Settings = settings;
        Simulation = new Simulation(db, galaxy);
    }

    /// <summary>
    /// A new game: generate the galaxy, run it for a month so there's news and history, and start
    /// the player at a Tolland Compact colony as an independent captain.
    /// </summary>
    public static Session StartNew(ContentDatabase db, ulong seed, GalaxySettings? settings = null)
    {
        settings ??= new GalaxySettings();
        var session = new Session(db, GalaxyGenerator.Generate(db, seed, settings), settings);
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

    /// <summary>Rebuilds a saved galaxy: same seed, simulated forward to the saved day, with the same deep-space stars registered.</summary>
    public static Session Restore(ContentDatabase db, ulong seed, GalaxySettings settings, int day, IEnumerable<ulong>? deepKeys = null,
        IEnumerable<PlayerAction>? actions = null)
    {
        var session = new Session(db, GalaxyGenerator.Generate(db, seed, settings), settings);
        foreach (var key in deepKeys ?? []) session.Galaxy.Register(key);
        // The player's actions are replayed on the days they happened, so the galaxy comes back the same.
        session.Simulation.Replay(actions ?? []);
        session.Simulation.Run(day);
        return session;
    }

    /// <summary>Stars within jump range of where you are, gate systems included, nearest first.</summary>
    public List<(string Name, float DistanceLy, ulong DeepKey, int SystemId)> InJumpRange()
    {
        var here = DeepSpace.PositionLy(Here);
        float range = JumpRangeLy;
        var stars = DeepSpace.Near(Galaxy.Seed, here, range)
            .Select(s => (DeepSpace.Name(s.Star.Key), s.DistanceLy, s.Star.Key, Galaxy.IsRegistered(s.Star.Key) ? Galaxy.Register(s.Star.Key) : -1));
        var gates = Galaxy.Systems.Where(s => s.IsGateSystem && s.Id != Here.Id)
            .Select(s => (s.Name, Vector3.Distance(DeepSpace.PositionLy(s), here), 0UL, s.Id))
            .Where(s => s.Item2 <= range);
        return stars.Concat(gates).OrderBy(s => s.Item2).ToList();
    }

    /// <summary>The jobs on offer here, with any haggled pay applied and withdrawn jobs removed.</summary>
    public List<MissionOffer> Board() =>
        MissionBoard.For(Db, Galaxy, Here, Career.Captain, Career.RankWith)
            .Where(o => !Career.TakenOffers.Contains(o.Id) && !Career.WithdrawnOffers.Contains(o.Id))
            .Select(o => Career.Haggled.TryGetValue(o.Id, out var m) && m > 1
                ? o with { Mission = o.Mission with { Reward = (int)(MathF.Round(o.Mission.Reward * m / 50f) * 50) } }
                : o)
            .ToList();

    /// <summary>Hulls for sale here: builders within one region of this system. Rim designs are never sold.</summary>
    public List<HullDef> Shipyard()
    {
        if (Here.Stations.Count == 0) return [];
        int here = Array.IndexOf(ContentRules.Regions, Here.Region);
        return Db.Hulls.Values
            .Where(h => h.Class != "drone" && h.ForSale && !h.IsCapital)
            .Where(h => Db.Builders.TryGetValue(h.Builder, out var b) && b.Region != "rim"
                        && Math.Abs(Array.IndexOf(ContentRules.Regions, b.Region) - here) <= 1)
            .OrderBy(h => h.Price).ThenBy(h => h.Id, StringComparer.Ordinal)
            .ToList();
    }

    public float JumpRangeLy => Ships.ShipFactory.Build(Db, Career.HullId, Career.Loadout).JumpRangeLy;

    /// <summary>Days for a direct jump: one day, plus one for every eight light years.</summary>
    public static int JumpDays(float distanceLy) => 1 + (int)(distanceLy / 8f);

    /// <summary>
    /// Travels somewhere. Gate systems are reached by the quickest gate route. Anywhere else
    /// (and any gate system within range) can be reached by a direct jump if it's within your
    /// jump drive's range. Returns null if neither works.
    /// </summary>
    /// <summary>
    /// A battle you fought changes the galaxy: wars swing, pirates are driven out, and the news
    /// spreads. Previews don't count.
    /// </summary>
    public PlayerAction? RecordBattle(AcceptedMission job, bool won)
    {
        var offer = job.Offer;
        if (offer.BattleId == null || offer.Id.StartsWith("preview-") || !Db.Battles.TryGetValue(offer.BattleId, out var def)) return null;
        int enemy = offer.EnemyFactionId;
        bool enemyIsFaction = enemy >= 0 && enemy < Galaxy.Factions.Count;
        bool atWar = enemyIsFaction && Galaxy.Conflicts.Any(c => c.Involves(offer.GiverFactionId) && c.Involves(enemy));
        string kind = def.Kind switch
        {
            "war" => "war",
            "defence" => "defence",
            "synth" => "synth",
            _ when enemy == Battles.BattleJobs.RogueMachines => "synth",
            _ when atWar && !Galaxy.Factions[enemy].Criminal => "war",
            _ => "pirates",
        };
        // Set pieces move a war a lot; an ordinary job on the side, a little.
        float weight = def.Kind is "war" or "campaign" or "defence"
            ? Math.Clamp(def.Reward / 40000f, 0.6f, 4f)
            : Math.Clamp(def.Reward / 60000f, 0.25f, 0.8f);
        var action = new PlayerAction(Galaxy.Day, kind, offer.GiverFactionId, enemy, offer.DestinationSystemId, won, weight, Career.Captain);
        Simulation.Record(action);
        return action;
    }

    /// <summary>Docking: jobs for this station are delivered.</summary>
    /// <remarks>
    /// Only a system's first orbital station is there to fly to, so any orbital station delivers
    /// jobs for the system's orbital stations. Surface ports need that port.
    /// </remarks>
    public List<MissionResult> Dock(string station)
    {
        var ports = SurfacePorts(Here);
        bool orbital = !ports.Contains(station);
        return Career.Settle(Galaxy, station, wanted => wanted == station || (orbital && !ports.Contains(wanted)));
    }

    private HashSet<string> SurfacePorts(StarSystem system) =>
        SolarSystems.For(system, Db.Grammars).Stations.Where(s => s.Surface).Select(s => s.Name).ToHashSet();

    /// <summary>What to do next for a job, in a line: where to go, how long it takes, how long is left.</summary>
    public string NextStep(AcceptedMission job, string? dockedAt = null)
    {
        var offer = job.Offer;
        int left = job.DeadlineDay - Galaxy.Day;
        string due = left <= 0 ? "due today" : left == 1 ? "1 day left" : $"{left} days left";
        var destination = Galaxy.Systems[offer.DestinationSystemId];
        if (offer.DestinationSystemId != Career.SystemId)
        {
            float distance = Galaxy.DistanceLy(Career.SystemId, offer.DestinationSystemId);
            int days;
            string how;
            if (distance <= JumpRangeLy) { days = JumpDays(distance); how = "a direct jump"; }
            else
            {
                var route = Here.IsGateSystem && destination.IsGateSystem ? Galaxy.Route(Career.SystemId, offer.DestinationSystemId) : [];
                if (route.Count < 2) return $"Get to {destination.Name}: out of jump range and off the gates. {due}.";
                days = Galaxy.RouteDays(route);
                how = route.Count == 2 ? "1 gate" : $"{route.Count - 1} gates";
            }
            string late = days > left ? " You won't make it in time." : "";
            return $"Jump to {destination.Name}: {how}, {days} days. {Capital(due)}.{late}";
        }
        if (offer.BattleId != null)
            return $"Head for the fighting near {offer.DestinationStation}: the course is set, J for autopilot. {Capital(due)}.";
        var solar = SolarSystems.For(Here, Db.Grammars);
        var port = solar.Stations.FirstOrDefault(s => s.Surface && s.Name == offer.DestinationStation);
        if (port != null)
            return dockedAt == port.Name
                ? $"You're at {port.Name}."
                : $"Land at {port.Name}, a surface port on {solar.Bodies[port.Body].Name}. Set a course on the system map. {Capital(due)}.";
        return $"Dock at the station in orbit. {Capital(due)}.";
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    public JumpReport? Jump(int destination)
    {
        float distance = Galaxy.DistanceLy(Career.SystemId, destination);
        bool gateRoute = Here.IsGateSystem && Galaxy.Systems[destination].IsGateSystem;
        if (!gateRoute || distance <= JumpRangeLy) return distance <= JumpRangeLy ? DirectJump(destination, distance) : null;
        return GateJump(destination);
    }

    private JumpReport DirectJump(int destination, float distanceLy)
    {
        int days = JumpDays(distanceLy);
        int dayBefore = Galaxy.Day;
        Simulation.Run(days);
        Career.SystemId = destination;
        var results = Career.Settle(Galaxy);
        return new JumpReport(days, results, Galaxy.News.Where(n => n.Day > dayBefore).ToList());
    }

    private JumpReport? GateJump(int destination)
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
