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

    /// <summary>
    /// Easy difficulty, set by the game from the player's settings. Markets are kinder: a narrower
    /// spread, faster recovery, and a dependable trade loop near the start.
    /// </summary>
    public bool Easy { get; set; }

    /// <summary>Buys up to the requested tonnes here, tonne by tonne as stock runs down. Returns tonnes bought.</summary>
    public int Buy(string commodityId, int tonnes)
    {
        var c = Db.Commodities[commodityId];
        var (got, cost) = Market.BuyCost(this, Here, c, Math.Min(tonnes, Career.FreeCargo(Db)), Career.Credits);
        if (got <= 0) return 0;
        Career.Credits -= cost;
        Career.Cargo[commodityId] = Career.Cargo.GetValueOrDefault(commodityId) + got;
        Career.RecordTrade(Here.Id, commodityId, Galaxy.Day, Market.RecoveryHalfLife(Easy), got, 0);
        return got;
    }

    /// <summary>Sells up to the requested tonnes here, the price easing as the market fills. Returns tonnes sold.</summary>
    public int Sell(string commodityId, int tonnes)
    {
        int held = Career.Cargo.GetValueOrDefault(commodityId);
        int sold = Math.Min(held, tonnes);
        if (sold <= 0) return 0;
        Career.Credits += Market.SaleValue(this, Here, Db.Commodities[commodityId], sold);
        if (held == sold) Career.Cargo.Remove(commodityId); else Career.Cargo[commodityId] = held - sold;
        Career.RecordTrade(Here.Id, commodityId, Galaxy.Day, Market.RecoveryHalfLife(Easy), 0, sold);
        return sold;
    }

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
    /// <param name="easy">Easy difficulty, known from the start so the first markets you learn are priced for it.</param>
    public static Session StartNew(ContentDatabase db, ulong seed, GalaxySettings? settings = null, bool easy = false)
    {
        settings ??= new GalaxySettings();
        var session = new Session(db, GalaxyGenerator.Generate(db, seed, settings), settings) { Easy = easy };
        session.Simulation.Run(30);

        var galaxy = session.Galaxy;
        var tolland = galaxy.Factions.FirstOrDefault(f => f.PowerId == "tolland-compact");
        var start = galaxy.Systems.Where(s => s.State == SystemState.Colony && s.Stations.Count > 0)
            .OrderByDescending(s => tolland != null && s.Owner == tolland.Id)
            .ThenBy(s => s.Id == tolland?.HomeSystem ? 1 : 0) // not the capital itself
            .First();
        session.Career.SystemId = start.Id;
        session.Career.TradeRoute = Market.PickRoute(db, galaxy, start.Id);
        TradeIntel.Note(session);
        // You start knowing the prices at your trade partner, picked up on the docks.
        if (session.Career.TradeRoute is { } route) TradeIntel.Note(session, galaxy.Systems[route.B]);
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

    /// <summary>Ships on sale here this week, at this yard's prices.</summary>
    public List<ShipOffer> Shipyard() => Shipyards.Offers(this, Here);

    /// <summary>What this yard will give for your current ship.</summary>
    public int TradeInValue() => Shipyards.TradeIn(this, Here, Career.HullId, Career.Loadout);

    public List<PartOffer> Outfitter() => Outfitting.Offers(this, Here);

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
        var plan = PlanJump(destination);
        return plan.Kind switch
        {
            JumpKind.Direct => DirectJump(destination, Galaxy.DistanceLy(Career.SystemId, destination)),
            JumpKind.Gates => GateJump(destination),
            _ => null,
        };
    }

    // Fuel -------------------------------------------------------------------------------------

    public float FuelTankLy => Ships.ShipFactory.Build(Db, Career.HullId, Career.Loadout).FuelTankLy;

    /// <summary>Fuel in the tank, in light years.</summary>
    public float FuelLy => Career.FuelLy < 0 ? FuelTankLy : MathF.Min(Career.FuelLy, FuelTankLy);

    /// <summary>
    /// How a trip there would go: by gate (free) where both ends are on the network, otherwise a
    /// direct jump, which needs the destination in your drive's range and the fuel to get there.
    /// A direct jump is preferred when it's in range and you have the fuel, since it's quicker.
    /// </summary>
    public JumpPlan PlanJump(int destination)
    {
        if (destination == Career.SystemId) return new JumpPlan(JumpKind.None, 0, 0, "You're already here.");
        float distance = Galaxy.DistanceLy(Career.SystemId, destination);
        bool gates = Here.IsGateSystem && Galaxy.Systems[destination].IsGateSystem && Galaxy.Route(Career.SystemId, destination).Count > 1;
        bool inRange = distance <= JumpRangeLy;
        bool fuelled = distance <= FuelLy + 0.01f;
        if (inRange && fuelled) return new JumpPlan(JumpKind.Direct, JumpDays(distance), distance, null);
        if (gates) return new JumpPlan(JumpKind.Gates, Galaxy.RouteDays(Galaxy.Route(Career.SystemId, destination)), 0, null);
        if (!inRange) return new JumpPlan(JumpKind.None, 0, distance, $"Out of range: your drive reaches {JumpRangeLy:0} ly, this is {distance:0.0} ly.");
        return new JumpPlan(JumpKind.None, 0, distance, $"Not enough fuel: this needs {distance:0.0} ly and you have {FuelLy:0.0} ly.");
    }

    /// <summary>Credits a light year of fuel costs here: from the local price of fuel, cheaper than buying it as cargo.</summary>
    public int FuelPricePerLy()
    {
        var fuel = Here.Stations.Count > 0
            ? Market.Quotes(this, Here).Where(q => q.Commodity.Category == "fuel" && q.Stock > 0).Select(q => q.Buy).DefaultIfEmpty(160).Min()
            : 160;
        return Math.Max(1, (int)MathF.Round(fuel / (Easy ? 30f : 20f)));
    }

    /// <summary>Fills the tank as far as your credits go. Returns light years added.</summary>
    public float Refuel()
    {
        if (Here.Stations.Count == 0) return 0;
        float want = FuelTankLy - FuelLy;
        if (want <= 0.01f) return 0;
        int perLy = FuelPricePerLy();
        float afford = MathF.Min(want, Career.Credits / (float)perLy);
        if (afford <= 0) return 0;
        Career.Credits -= (int)MathF.Ceiling(afford * perLy);
        Career.FuelLy = FuelLy + afford;
        return afford;
    }

    /// <summary>True when you can't reach any system with a station: out of fuel somewhere with no station and no gates.</summary>
    public bool Stranded =>
        Here.Stations.Count == 0 && !Here.IsGateSystem
        && !Galaxy.Systems.Any(s => s.Stations.Count > 0 && s.IsGateSystem && Galaxy.DistanceLy(Career.SystemId, s.Id) <= MathF.Min(FuelLy, JumpRangeLy));

    /// <summary>
    /// A rescue tug: it takes you to the nearest system with a station and gives you enough fuel to
    /// carry on, for a fee you pay as far as you can. Running dry is never the end.
    /// </summary>
    public (StarSystem To, int Fee, int Days) CallRescue()
    {
        var to = Galaxy.Systems.Where(s => s.Stations.Count > 0 && s.IsGateSystem).MinBy(s => Galaxy.DistanceLy(Career.SystemId, s.Id))!;
        float distance = Galaxy.DistanceLy(Career.SystemId, to.Id);
        int fee = Math.Min(Career.Credits, 500 + (int)(distance * 25));
        int days = JumpDays(distance) + 2;
        Career.Credits -= fee;
        Simulation.Run(days);
        Career.SystemId = to.Id;
        Career.Settle(Galaxy);
        Career.FuelLy = MathF.Max(FuelLy, FuelTankLy * 0.2f);
        TradeIntel.Note(this);
        return (to, fee, days);
    }

    private JumpReport DirectJump(int destination, float distanceLy)
    {
        Career.FuelLy = MathF.Max(0, FuelLy - distanceLy * Ships.ShipFactory.Build(Db, Career.HullId, Career.Loadout).JumpFuel);
        int days = JumpDays(distanceLy);
        int dayBefore = Galaxy.Day;
        Simulation.Run(days);
        Career.SystemId = destination;
        var results = Career.Settle(Galaxy);
        TradeIntel.Note(this);
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
        TradeIntel.Note(this);
        return new JumpReport(days, results, news);
    }
}

public enum JumpKind { None, Direct, Gates }

/// <summary>How a trip would go: by direct jump (uses fuel), by gate (free), or not at all and why.</summary>
public sealed record JumpPlan(JumpKind Kind, int Days, float FuelLy, string? Reason);
