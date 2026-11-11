using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;

namespace Galaxus.Core.Tests;

/// <summary>Getting around: fuel and jump range, the gate network's main routes, and comparing markets.</summary>
public class TravelTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    [Fact]
    public void Direct_jumps_use_fuel_and_gates_do_not()
    {
        var session = Session.StartNew(Db, 41);
        float full = session.FuelLy;
        Assert.True(full >= 100, $"A starting courier should have a generous tank, not {full} ly.");

        // A gate trip far beyond jump range costs nothing.
        var far = session.Galaxy.Systems.Where(s => s.IsGateSystem && s.Stations.Count > 0)
            .First(s => session.Galaxy.DistanceLy(session.Here.Id, s.Id) > session.JumpRangeLy * 3);
        Assert.Equal(JumpKind.Gates, session.PlanJump(far.Id).Kind);
        session.Jump(far.Id);
        Assert.Equal(full, session.FuelLy, 3);

        // A direct jump spends its distance: to a star between the gates, a few light years off.
        var (star, _) = DeepSpace.Near(session.Galaxy.Seed, DeepSpace.PositionLy(session.Here), session.JumpRangeLy).First(x => x.DistanceLy > 1);
        var near = session.Galaxy.Systems[session.Galaxy.Register(star.Key)];
        float distance = session.Galaxy.DistanceLy(session.Here.Id, near.Id);
        Assert.Equal(JumpKind.Direct, session.PlanJump(near.Id).Kind);
        session.Jump(near.Id);
        Assert.Equal(full - distance, session.FuelLy, 2);
    }

    [Fact]
    public void Without_fuel_you_are_told_why_and_can_refuel_or_be_rescued()
    {
        var session = Session.StartNew(Db, 42);
        session.Career.FuelLy = 0.5f;
        // A star between the gates a few light years away, as the map registers it when you zoom in.
        var (star, _) = DeepSpace.Near(session.Galaxy.Seed, DeepSpace.PositionLy(session.Here), 8).First(x => x.DistanceLy > 1);
        var deep = session.Galaxy.Systems[session.Galaxy.Register(star.Key)];
        var plan = session.PlanJump(deep.Id);
        Assert.Equal(JumpKind.None, plan.Kind);
        Assert.Contains("fuel", plan.Reason);

        int credits = session.Career.Credits;
        Assert.True(session.Refuel() > 0);
        Assert.Equal(session.FuelTankLy, session.FuelLy, 2);
        Assert.True(session.Career.Credits < credits);

        // Stranded between the gates with an empty tank: a tug takes you somewhere with a station.
        session.Jump(deep.Id);
        session.Career.FuelLy = 0;
        Assert.True(session.Stranded);
        var (to, _, _) = session.CallRescue();
        Assert.True(to.Stations.Count > 0);
        Assert.Equal(to.Id, session.Career.SystemId);
        Assert.True(session.FuelLy > 0);
    }

    [Fact]
    public void Fuel_survives_a_save()
    {
        var session = Session.StartNew(Db, 43);
        session.Career.FuelLy = 37.5f;
        Assert.Equal(37.5f, SaveGame.Load(Db, SaveGame.Save(session)).FuelLy, 3);
    }

    [Theory]
    [InlineData(2026UL)]
    [InlineData(7UL)]
    public void The_gate_network_knows_its_bridges_and_main_routes(ulong seed)
    {
        var galaxy = GalaxyGenerator.Generate(Db, seed, new GalaxySettings());
        var roles = GateNetwork.Roles(galaxy);
        int links = galaxy.Systems.Sum(s => s.Gates.Count) / 2;
        Assert.Equal(links, roles.Count);
        Assert.All(roles.Values, r => Assert.InRange(r.Weight, 0, 1));
        Assert.Contains(roles.Values, r => r.CrossesRegion);

        // Every bridge really does cut the network in two, and says how much lies beyond.
        int total = galaxy.Systems.Count(s => s.IsGateSystem);
        foreach (var bridge in roles.Values.Where(r => r.Bridge).Take(10))
        {
            Assert.InRange(bridge.Beyond, 1, total / 2);
            Assert.False(Reaches(galaxy, bridge.A, bridge.B), $"{bridge.A}-{bridge.B} is marked as a bridge but isn't one.");
            Assert.True(bridge.Far == bridge.A || bridge.Far == bridge.B);
            Assert.Contains(bridge.FarRegion, new[] { "core", "inner", "outer", "rim" });
            Assert.Contains("beyond", GateNetwork.Describe(galaxy, bridge));
        }
        // A non-bridge never splits it.
        var ordinary = roles.Values.First(r => !r.Bridge);
        Assert.True(Reaches(galaxy, ordinary.A, ordinary.B));
        Assert.Equal(-1, ordinary.Far);

        // Hubs are where main routes meet, and every bridge end is one.
        var hubs = GateNetwork.Hubs(galaxy);
        Assert.NotEmpty(hubs);
        Assert.All(roles.Values.Where(r => r.Bridge), r => { Assert.Contains(r.A, hubs); Assert.Contains(r.B, hubs); });
    }

    /// <summary>Whether b can be reached from a by gate without using the a-b link itself.</summary>
    private static bool Reaches(Galaxy.Galaxy galaxy, int a, int b)
    {
        var seen = new HashSet<int> { a };
        var queue = new Queue<int>([a]);
        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            foreach (var n in galaxy.Systems[node].Gates)
            {
                if ((node == a && n == b) || (node == b && n == a)) continue;
                if (n == b) return true;
                if (seen.Add(n)) queue.Enqueue(n);
            }
        }
        return false;
    }

    [Fact]
    public void Comparing_markets_shows_what_pays_more_there_and_what_is_cheaper()
    {
        var session = Session.StartNew(Db, 44, easy: true);
        var route = session.Career.TradeRoute!;
        var partner = session.Galaxy.Systems[route.B];
        var comparison = TradeIntel.Compare(session, partner, count: 100);
        Assert.NotNull(comparison.AgeDays);
        Assert.Contains(comparison.Higher, g => g.CommodityId == route.GoodsAtoB && g.Margin > 0);
        Assert.Contains(comparison.Lower, g => g.CommodityId == route.GoodsBtoA && g.Margin > 0);
        Assert.NotNull(comparison.Best);

        var unknown = session.Galaxy.Systems.First(s => s.Stations.Count > 0 && !session.Career.SeenMarkets.ContainsKey(s.Id));
        Assert.Null(TradeIntel.Compare(session, unknown).AgeDays);
    }
}

public class WarFleetTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    [Fact]
    public void Fronts_have_a_war_fleet_with_a_flagship_you_can_dock_with()
    {
        var session = Session.StartNew(Db, 45);
        session.Simulation.Run(120);
        var galaxy = session.Galaxy;
        var fronts = galaxy.Conflicts.Where(c => c.Stage >= 1).Select(c => galaxy.Systems[c.FrontSystem]).Where(s => s.Stations.Count > 0).ToList();
        Assert.NotEmpty(fronts);
        var fleets = fronts.Select(s => WarFleets.At(Db, galaxy, s)).Where(f => f != null).ToList();
        Assert.NotEmpty(fleets);
        Assert.All(fleets, f => Assert.True(Db.Hulls[f!.FlagshipHull].Dockable));
        Assert.All(fleets, f => Assert.True(f!.Conflict.Involves(f.Faction.Id)));
        // Quiet systems have none.
        Assert.Null(WarFleets.At(Db, galaxy, galaxy.Systems.First(s => s.Stations.Count > 0 && !galaxy.Conflicts.Any(c => c.FrontSystem == s.Id))));
    }
}
