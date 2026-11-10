using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Missions;

namespace Galaxus.Core.Tests;

public class GalaxyTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));
    private static readonly GalaxySettings Small = new() { SystemCount = 300, MinorFactions = 50 };

    [Fact]
    public void Same_seed_gives_the_same_galaxy()
    {
        var a = GalaxyGenerator.Generate(Db, 7, Small);
        var b = GalaxyGenerator.Generate(Db, 7, Small);
        Assert.Equal(a.Systems.Select(s => (s.Name, s.Owner, s.State)), b.Systems.Select(s => (s.Name, s.Owner, s.State)));
        Assert.Equal(a.Factions.Select(f => f.Name), b.Factions.Select(f => f.Name));
    }

    [Fact]
    public void Every_system_is_reachable_by_gate()
    {
        var galaxy = GalaxyGenerator.Generate(Db, 11, Small);
        var reached = galaxy.WithinJumps(0, 1000).Count + 1;
        Assert.Equal(galaxy.Systems.Count, reached);
    }

    [Fact]
    public void Most_systems_start_empty_and_every_power_is_placed()
    {
        var galaxy = GalaxyGenerator.Generate(Db, 3, new GalaxySettings());
        float empty = galaxy.Systems.Count(s => s.Owner < 0) / (float)galaxy.Systems.Count;
        Assert.InRange(empty, 0.4f, 0.9f);
        foreach (var power in Db.Powers.Values)
            Assert.Contains(galaxy.Factions, f => f.PowerId == power.Id && galaxy.OwnedBy(f.Id).Any());
        Assert.All(galaxy.Systems.Where(s => s.Settled), s => Assert.NotEmpty(s.Stations));
    }

    [Fact]
    public void A_year_of_simulation_changes_the_galaxy_and_writes_news()
    {
        var galaxy = GalaxyGenerator.Generate(Db, 5, Small);
        int ownedBefore = galaxy.Systems.Count(s => s.Owner >= 0);
        new Simulation(Db, galaxy).Run(365);
        Assert.Equal(365, galaxy.Day);
        Assert.True(galaxy.Systems.Count(s => s.Owner >= 0) > ownedBefore, "Nobody expanded in a year.");
        Assert.True(galaxy.News.Count > 20, $"Only {galaxy.News.Count} news items in a year.");
        Assert.All(galaxy.News, n => Assert.DoesNotContain("{", n.Text));
    }

    [Fact]
    public void Simulation_is_deterministic()
    {
        var a = GalaxyGenerator.Generate(Db, 9, Small);
        var b = GalaxyGenerator.Generate(Db, 9, Small);
        new Simulation(Db, a).Run(120);
        new Simulation(Db, b).Run(120);
        Assert.Equal(a.News.Select(n => n.Text), b.News.Select(n => n.Text));
    }

    [Fact]
    public void Markets_price_every_commodity_and_events_move_prices()
    {
        var galaxy = GalaxyGenerator.Generate(Db, 5, Small);
        var system = galaxy.Systems.First(s => s.Settled);
        var food = Db.Commodities.Values.First(c => c.Category == "food");
        int before = Market.Price(Db, galaxy, system, food);
        galaxy.Events.Add(new ActiveEvent { EventId = "famine", SystemId = system.Id, EndDay = 100 });
        Assert.True(Market.Price(Db, galaxy, system, food) > before * 2);
        Assert.All(Db.Commodities.Values, c => Assert.True(Market.Price(Db, galaxy, system, c) > 0));
    }

    [Fact]
    public void Stations_offer_jobs_and_delivering_pays()
    {
        var galaxy = GalaxyGenerator.Generate(Db, 5, Small);
        var career = new Career();
        bool Fits(MissionOffer o, StarSystem s) => o.Playable && o.DestinationSystemId != s.Id && o.Mission.Tonnes <= career.CargoCapacity(Db);
        var start = galaxy.Systems.First(s => s.Settled && MissionBoard.For(Db, galaxy, s, "Ashby", career.RankWith).Any(o => Fits(o, s)));
        career.SystemId = start.Id;

        var offer = MissionBoard.For(Db, galaxy, start, "Ashby", career.RankWith)
            .First(o => Fits(o, start));
        Assert.Null(career.CannotAccept(Db, offer));
        career.Accept(galaxy, offer);

        int credits = career.Credits;
        career.SystemId = offer.DestinationSystemId;
        var results = career.Settle(galaxy);
        Assert.Single(results);
        Assert.True(results[0].Success);
        Assert.Equal(credits + offer.Mission.Reward, career.Credits);
        Assert.True(career.Reputation[offer.GiverFactionId] > 0);
    }

    [Fact]
    public void Deadlines_leave_time_to_travel()
    {
        var galaxy = GalaxyGenerator.Generate(Db, 5, Small);
        foreach (var system in galaxy.Systems.Where(s => s.Settled).Take(30))
        foreach (var offer in MissionBoard.For(Db, galaxy, system, "Ashby", _ => 4))
            Assert.True(offer.Mission.DeadlineDays >= MissionBoard.TravelDays(galaxy, system.Id, offer.DestinationSystemId),
                $"{offer.Mission.Title}: {offer.Mission.DeadlineDays} days to make a {MissionBoard.TravelDays(galaxy, system.Id, offer.DestinationSystemId)}-day trip.");
    }
}

public class SessionTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    [Fact]
    public void A_new_game_starts_docked_somewhere_with_work_and_ships()
    {
        var session = Session.StartNew(Db, 2026);
        Assert.NotEmpty(session.Here.Stations);
        Assert.NotEmpty(session.Board());
        Assert.NotEmpty(session.Shipyard());
        Assert.DoesNotContain(session.Shipyard(), h => Db.Builders[h.Builder].Region == "rim");
        Assert.Equal(30, session.Galaxy.Day);
    }

    [Fact]
    public void Jumping_takes_time_and_delivers_jobs()
    {
        var session = Session.StartNew(Db, 2026);
        var offer = session.Board().First(o => o.Playable && o.DestinationSystemId != session.Career.SystemId
                                              && o.Mission.Tonnes <= session.Career.FreeCargo(Db));
        session.Career.Accept(session.Galaxy, offer);
        int day = session.Galaxy.Day;

        var report = session.Jump(offer.DestinationSystemId)!;
        Assert.Equal(offer.DestinationSystemId, session.Career.SystemId);
        Assert.Equal(day + report.Days, session.Galaxy.Day);
        Assert.Contains(report.Results, r => r.Success && r.Mission.Offer.Id == offer.Id);
    }

    [Fact]
    public void Trading_buys_and_sells_within_the_hold()
    {
        var session = Session.StartNew(Db, 2026);
        var career = session.Career;
        var cheap = Db.Commodities.Values.Where(c => c.Legality == "legal").MinBy(c => c.BasePrice)!;
        int bought = career.Buy(Db, session.Galaxy, cheap.Id, 1000);
        Assert.Equal(career.CargoCapacity(Db), bought);
        Assert.Equal(0, career.FreeCargo(Db));
        int credits = career.Credits;
        Assert.Equal(bought, career.Sell(Db, session.Galaxy, cheap.Id, 1000));
        Assert.True(career.Credits > credits);
        Assert.Empty(career.Cargo);
    }
}
