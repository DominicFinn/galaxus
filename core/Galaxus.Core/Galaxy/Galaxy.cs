using System.Numerics;

namespace Galaxus.Core.Galaxy;

/// <summary>How far settlement has got in a system. Most systems start unsurveyed.</summary>
public enum SystemState { Unsurveyed, Surveyed, Claimed, Outpost, Colony }

/// <summary>What a settled system mostly produces, which sets its market prices.</summary>
public enum Economy { None, Agricultural, Mining, Refinery, Industrial, HighTech, Frontier }

public sealed class StarSystem
{
    public int Id { get; init; }
    /// <summary>Settable so a mod's landmark can name a system.</summary>
    public string Name { get; set; } = "";
    /// <summary>A landmark's line about this system, if a mod placed one here.</summary>
    public string? LandmarkNote { get; set; }
    /// <summary>Position on the galactic disc in light years, core at the origin.</summary>
    public Vector2 Position { get; init; }
    public string Region { get; init; } = "";
    public string Arm { get; init; } = "";
    public ulong Seed { get; init; }

    public SystemState State { get; set; }
    public Economy Economy { get; set; }
    /// <summary>Owning faction id, or -1.</summary>
    public int Owner { get; set; } = -1;
    /// <summary>Faction that surveyed it first, or -1. Surveyed but unclaimed systems can be claimed by anyone.</summary>
    public int SurveyedBy { get; set; } = -1;
    /// <summary>Day the state last changed, so growth takes time.</summary>
    public int StateSince { get; set; }
    public List<string> Stations { get; } = [];
    /// <summary>Gate links to other system ids.</summary>
    public List<int> Gates { get; } = [];

    public bool Settled => State is SystemState.Outpost or SystemState.Colony;

    /// <summary>For deep-space stars: their key in DeepSpace. Zero for gate systems.</summary>
    public ulong DeepKey { get; init; }
    /// <summary>For deep-space stars: their position in light years, including height above the plane.</summary>
    public System.Numerics.Vector3 PositionLyCache { get; init; }
    public bool IsGateSystem => DeepKey == 0;
}

public sealed class Faction
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string TypeId { get; init; } = "";
    public string RaceId { get; init; } = "";
    /// <summary>minor, regional or great.</summary>
    public string Tier { get; init; } = "minor";
    /// <summary>Great power id from the content, if this is one.</summary>
    public string? PowerId { get; init; }
    public bool Criminal { get; init; }
    public string Colour { get; init; } = "#888888";
    public int HomeSystem { get; init; }
    /// <summary>Rolled traits, 0 to 1: aggression, greed, expansion, paranoia, honour, piety.</summary>
    public Dictionary<string, float> Traits { get; init; } = [];
    /// <summary>Criminal factions hide in systems they don't own. These are where they operate.</summary>
    public List<int> Haunts { get; } = [];
    public bool Alive { get; set; } = true;

    public float Trait(string name) => Traits.GetValueOrDefault(name, 0.5f);
}

/// <summary>A galaxy event running in a system: a famine, a mining rush, a war front.</summary>
public sealed class ActiveEvent
{
    public string EventId { get; init; } = "";
    public int SystemId { get; init; }
    public int FactionId { get; init; } = -1;
    public int RivalId { get; init; } = -1;
    public int StartDay { get; init; }
    public int EndDay { get; set; }
    public int LastNewsDay { get; set; }
}

/// <summary>
/// Two factions at odds. Conflicts climb one step at a time (dispute, raids, blockade, open war)
/// and each step has odds of going up or down, so there are plenty of squabbles and few wars.
/// </summary>
public sealed class Conflict
{
    public static readonly string[] StageEvents = ["border-dispute", "raids", "blockade", "open-war"];

    public int A { get; init; }
    public int B { get; init; }
    /// <summary>0 dispute, 1 raids, 2 blockade, 3 open war.</summary>
    public int Stage { get; set; }
    public int FrontSystem { get; set; }
    /// <summary>Which way the war is going: positive favours A. Moved by the player's battles; fades over time.</summary>
    public float Momentum { get; set; }
    public int StartDay { get; init; }
    public bool Involves(int faction) => A == faction || B == faction;
}

public sealed record NewsItem(int Day, int SystemId, string Text);

public sealed class Galaxy
{
    public ulong Seed { get; init; }
    public List<StarSystem> Systems { get; } = [];
    public List<Faction> Factions { get; } = [];
    public List<ActiveEvent> Events { get; } = [];
    public List<Conflict> Conflicts { get; } = [];
    /// <summary>Landmarks placed by mods: landmark id to system id.</summary>
    public Dictionary<string, int> Landmarks { get; } = [];
    /// <summary>What the player has done to the galaxy, in order, so a save can replay it.</summary>
    public List<PlayerAction> PlayerActions { get; } = [];
    public List<NewsItem> News { get; } = [];
    public int Day { get; set; }

    public const int MaxNews = 400;

    /// <summary>Deep-space stars that have been visited or looked at, in the order they were registered.</summary>
    public List<ulong> DeepKeys { get; } = [];
    private readonly Dictionary<ulong, int> _deepIds = [];

    /// <summary>
    /// Adds a deep-space star to the galaxy so it can be travelled to, and returns its id. Calling
    /// again with the same key returns the same id. Saves store the keys in order, so ids survive loading.
    /// </summary>
    public int Register(ulong key)
    {
        if (_deepIds.TryGetValue(key, out int id)) return id;
        var star = DeepSpace.Find(Seed, key);
        var system = new StarSystem
        {
            Id = Systems.Count,
            Name = DeepSpace.Name(key),
            Position = new System.Numerics.Vector2(star.PositionLy.X / DeepSpace.LyPerUnit, star.PositionLy.Y / DeepSpace.LyPerUnit),
            PositionLyCache = star.PositionLy,
            Region = DeepSpace.Region(star.PositionLy),
            Arm = GalaxyGenerator.NearestArm(new System.Numerics.Vector2(star.PositionLy.X, star.PositionLy.Y) / DeepSpace.LyPerUnit),
            Seed = star.Seed,
            DeepKey = key,
        };
        Systems.Add(system);
        DeepKeys.Add(key);
        _deepIds[key] = system.Id;
        return system.Id;
    }

    public bool IsRegistered(ulong key) => _deepIds.ContainsKey(key);

    public float DistanceLy(int a, int b) =>
        Vector3.Distance(DeepSpace.PositionLy(Systems[a]), DeepSpace.PositionLy(Systems[b]));

    public Faction? OwnerOf(StarSystem system) => system.Owner >= 0 ? Factions[system.Owner] : null;

    public IEnumerable<StarSystem> Neighbours(StarSystem system) => system.Gates.Select(id => Systems[id]);

    public IEnumerable<StarSystem> OwnedBy(int faction) => Systems.Where(s => s.Owner == faction);

    public IEnumerable<ActiveEvent> EventsIn(int systemId) => Events.Where(e => e.SystemId == systemId);

    /// <summary>Gate jumps between two systems, or -1 if unreachable.</summary>
    public int Jumps(int from, int to, int limit = 64)
    {
        if (from == to) return 0;
        var seen = new HashSet<int> { from };
        var frontier = new List<int> { from };
        for (int depth = 1; depth <= limit && frontier.Count > 0; depth++)
        {
            var next = new List<int>();
            foreach (var id in frontier)
            foreach (var n in Systems[id].Gates)
            {
                if (n == to) return depth;
                if (seen.Add(n)) next.Add(n);
            }
            frontier = next;
        }
        return -1;
    }

    /// <summary>Systems within a number of gate jumps, nearest first, excluding the start.</summary>
    public List<StarSystem> WithinJumps(int from, int maxJumps)
    {
        var result = new List<StarSystem>();
        var seen = new HashSet<int> { from };
        var frontier = new List<int> { from };
        for (int depth = 1; depth <= maxJumps && frontier.Count > 0; depth++)
        {
            var next = new List<int>();
            foreach (var id in frontier)
            foreach (var n in Systems[id].Gates)
                if (seen.Add(n)) { next.Add(n); result.Add(Systems[n]); }
            frontier = next;
        }
        return result;
    }

    /// <summary>The quickest route by gate, as system ids from start to finish, or empty if unreachable.</summary>
    public List<int> Route(int from, int to)
    {
        var best = new Dictionary<int, int> { [from] = 0 };
        var previous = new Dictionary<int, int>();
        var queue = new PriorityQueue<int, int>();
        queue.Enqueue(from, 0);
        while (queue.TryDequeue(out int id, out int days))
        {
            if (id == to) break;
            if (days > best[id]) continue;
            foreach (var n in Systems[id].Gates)
            {
                int d = days + TravelDays(id, n);
                if (d < best.GetValueOrDefault(n, int.MaxValue)) { best[n] = d; previous[n] = id; queue.Enqueue(n, d); }
            }
        }
        if (from != to && !previous.ContainsKey(to)) return [];
        var route = new List<int> { to };
        while (route[^1] != from) route.Add(previous[route[^1]]);
        route.Reverse();
        return route;
    }

    public int RouteDays(List<int> route) => route.Zip(route.Skip(1)).Sum(p => TravelDays(p.First, p.Second));

    /// <summary>Days to travel one gate link: one day per 40 light years, at least one.</summary>
    public int TravelDays(int from, int to) =>
        Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(Systems[from].Position, Systems[to].Position) / 40f));

    public void AddNews(int systemId, string text)
    {
        News.Add(new NewsItem(Day, systemId, text));
        if (News.Count > MaxNews) News.RemoveRange(0, News.Count - MaxNews);
    }
}
