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
        Assert.True(Market.Price(Db, galaxy, system, food) > before * 1.5f);
        Assert.All(Db.Commodities.Values, c => Assert.True(Market.Price(Db, galaxy, system, c) > 0));
    }

    [Fact]
    public void Stations_offer_jobs_and_delivering_pays()
    {
        var galaxy = GalaxyGenerator.Generate(Db, 5, Small);
        var career = new Career();
        bool Fits(MissionOffer o, StarSystem s) => o.Playable && o.BattleId == null && o.DestinationSystemId != s.Id && o.Mission.Tonnes <= career.CargoCapacity(Db);
        var start = galaxy.Systems.First(s => s.Settled && MissionBoard.For(Db, galaxy, s, "Ashby", career.RankWith).Any(o => Fits(o, s)));
        career.SystemId = start.Id;

        var offer = MissionBoard.For(Db, galaxy, start, "Ashby", career.RankWith)
            .First(o => Fits(o, start));
        Assert.Null(career.CannotAccept(Db, offer));
        career.Accept(galaxy, offer);

        int credits = career.Credits;
        career.SystemId = offer.DestinationSystemId;
        // Arriving isn't enough: the job is delivered by docking at its station.
        Assert.Empty(career.Settle(galaxy));
        var results = career.Settle(galaxy, offer.DestinationStation);
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
        var offer = session.Board().First(o => o.Playable && o.BattleId == null && o.DestinationSystemId != session.Career.SystemId
                                              && o.Mission.Tonnes <= session.Career.FreeCargo(Db));
        session.Career.Accept(session.Galaxy, offer);
        int day = session.Galaxy.Day;

        Assert.Equal(offer.Id, session.Career.ActiveJob!.Offer.Id);
        Assert.StartsWith("Jump to", session.NextStep(session.Career.ActiveJob));

        var report = session.Jump(offer.DestinationSystemId)!;
        Assert.Equal(offer.DestinationSystemId, session.Career.SystemId);
        Assert.Equal(day + report.Days, session.Galaxy.Day);
        Assert.DoesNotContain(report.Results, r => r.Success);
        Assert.Matches("^(Dock|Land)", session.NextStep(session.Career.ActiveJob!));

        var delivered = session.Dock(offer.DestinationStation);
        Assert.Contains(delivered, r => r.Success && r.Mission.Offer.Id == offer.Id);
        Assert.Null(session.Career.ActiveJob);
    }

    [Fact]
    public void Trading_buys_and_sells_within_the_hold()
    {
        var session = Session.StartNew(Db, 2026);
        var career = session.Career;
        // The good with the most stock here, so the hold is the limit.
        var plenty = Market.Quotes(session, session.Here).MaxBy(q => q.Stock)!;
        int bought = session.Buy(plenty.Commodity.Id, 1000);
        Assert.Equal(Math.Min(career.CargoCapacity(Db), plenty.Stock), bought);
        int credits = career.Credits;
        Assert.Equal(bought, session.Sell(plenty.Commodity.Id, 1000));
        Assert.True(career.Credits > credits);
        Assert.Empty(career.Cargo);
    }
}

public class SaveGameTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    [Fact]
    public void Saving_and_loading_restores_the_galaxy_and_career()
    {
        var session = Session.StartNew(Db, 77);
        var offer = session.Board().First(o => o.Playable && o.Mission.Tonnes <= session.Career.FreeCargo(Db));
        session.Career.Accept(session.Galaxy, offer);
        Assert.Equal(1, session.Buy(Market.Quotes(session, session.Here).First(q => q.Stock > 0).Commodity.Id, 1));
        session.Career.Reputation[3] = 150;
        session.Simulation.Run(40);

        var json = SaveGame.Save(session);
        var loaded = SaveGame.Load(Db, json);

        Assert.Equal(session.Galaxy.Day, loaded.Galaxy.Day);
        Assert.Equal(session.Galaxy.News.Select(n => n.Text), loaded.Galaxy.News.Select(n => n.Text));
        Assert.Equal(session.Galaxy.Systems.Select(s => s.Owner), loaded.Galaxy.Systems.Select(s => s.Owner));
        Assert.Equal(session.Career.Credits, loaded.Career.Credits);
        Assert.Equal(session.Career.Cargo, loaded.Career.Cargo);
        Assert.Equal(150, loaded.Career.Reputation[3]);
        Assert.Equal(offer.Mission.Briefing, loaded.Career.Missions.Single().Offer.Mission.Briefing);
        Assert.Equal(session.Career.Missions.Single().DeadlineDay, loaded.Career.Missions.Single().DeadlineDay);
        Assert.Equal(session.Here.Name, SaveGame.Peek(json)!.SystemName);
    }
}

public class SystemProfileTests
{
    private static readonly Galaxus.Core.Content.ContentDatabase Db =
        Galaxus.Core.Content.ContentDatabase.Load(new Galaxus.Core.Content.DirectoryContentSource(ContentTests.DataFolder));

    [Fact]
    public void Capitals_are_the_biggest_places_and_the_core_outnumbers_the_rim()
    {
        var galaxy = Galaxus.Core.Galaxy.GalaxyGenerator.Generate(Db, 3, new Galaxus.Core.Galaxy.GalaxySettings());
        var profiles = galaxy.Systems.Where(s => s.Settled).Select(s => (s, p: Galaxus.Core.Galaxy.SystemProfiles.For(galaxy, s, Db))).ToList();
        foreach (var power in galaxy.Factions.Where(f => f.PowerId != null))
        {
            var capital = profiles.First(x => x.s.Id == power.HomeSystem).p;
            Assert.Equal(Galaxus.Core.Galaxy.StationSize.Citadel, capital.StationSize);
            Assert.Equal(power.Name, capital.CapitalOf);
        }
        double Median(string region) => profiles.Where(x => x.s.Region == region && x.s.State == Galaxus.Core.Galaxy.SystemState.Colony)
            .Select(x => (double)x.p.Population).OrderBy(v => v).DefaultIfEmpty(0).ElementAt(profiles.Count(x => x.s.Region == region && x.s.State == Galaxus.Core.Galaxy.SystemState.Colony) / 2);
        Assert.True(Median("core") > Median("rim") * 100);
        Assert.Contains(profiles, x => x.p.StationSize == Galaxus.Core.Galaxy.StationSize.Hub);
        Assert.Equal(Galaxus.Core.Galaxy.SystemProfiles.For(galaxy, profiles[0].s, Db), profiles[0].p);
    }
}
