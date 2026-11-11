using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Mining;
using Galaxus.Core.Missions;
using Galaxus.Core.Ships;

namespace Galaxus.Core.Tests;

/// <summary>Asteroid belts, the scanner, finds and leads, cutting ore, and jobs that need them.</summary>
public class MiningTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    private static Session NewGame(ulong seed)
    {
        var session = Session.StartNew(Db, seed);
        session.Career.Credits = 100_000;
        return session;
    }

    /// <summary>Moves the session to a gate system with a belt.</summary>
    private static (SolarSystem Solar, int Belt) GoToBelt(Session session)
    {
        foreach (var system in session.Galaxy.Systems.Where(s => s.IsGateSystem))
        {
            var solar = SolarSystems.For(system, Db.Grammars);
            int belt = Asteroids.BeltIndex(solar);
            if (belt < 0) continue;
            session.Career.SystemId = system.Id;
            return (solar, belt);
        }
        throw new InvalidOperationException("No belt anywhere.");
    }

    [Fact]
    public void Belts_have_the_same_rocks_every_visit()
    {
        var session = NewGame(31);
        var (solar, belt) = GoToBelt(session);
        var body = solar.Bodies[belt];
        var point = Asteroids.NearestKm(body, (1e9, 0, 3e8));
        Assert.True(Asteroids.Inside(body, point));
        var first = Asteroids.Near(body, point, 40);
        var again = Asteroids.Near(body, point, 40);
        Assert.NotEmpty(first);
        Assert.Equal(first.Select(r => r.Id), again.Select(r => r.Id));
        Assert.All(first, r => Assert.True(Db.Commodities.ContainsKey(r.OreId), r.OreId));
        Assert.All(first, r => Assert.True(r.Tonnes > 0 && r.RadiusM > 0));
        // Outside the belt there's nothing.
        Assert.Empty(Asteroids.Near(body, (point.X * 0.5, 0, point.Z * 0.5), 40));
    }

    [Fact]
    public void Cutting_a_rock_fills_the_hold_and_wears_it_down()
    {
        var session = NewGame(32);
        var (solar, belt) = GoToBelt(session);
        session.Career.Cargo.Clear();
        var rock = Asteroids.Near(solar.Bodies[belt], Asteroids.NearestKm(solar.Bodies[belt], (1e9, 0, 0)), 60).First();
        int free = session.Career.FreeCargo(Db);
        int got = Scanning.Cut(session, rock, 5);
        Assert.Equal(Math.Min(5, Math.Min(free, rock.Tonnes)), got);
        Assert.Equal(got, session.Career.Cargo[rock.OreId]);
        Assert.Equal(rock.Tonnes - got, Scanning.RockLeft(session.Career, rock));
        // A full hold stops it.
        while (Scanning.Cut(session, rock, 100) > 0) { }
        Assert.True(session.Career.FreeCargo(Db) == 0 || Scanning.RockLeft(session.Career, rock) == 0);
    }

    [Fact]
    public void Scanning_a_world_pays_survey_data_once()
    {
        var session = NewGame(33);
        var solar = SolarSystems.For(session.Here, Db.Grammars);
        int body = solar.Bodies.FindIndex(b => b.Kind != BodyKind.Belt);
        var first = Scanning.Body(session, solar, body);
        Assert.True(first.SurveyValue > 0);
        Assert.Equal(0, Scanning.Body(session, solar, body).SurveyValue);
        int credits = session.Career.Credits;
        int worth = Scanning.SurveyWorth(session.Career);
        Assert.Equal(worth, Scanning.SellSurvey(session.Career));
        Assert.Equal(credits + worth, session.Career.Credits);
        Assert.Empty(session.Career.SurveyData);
    }

    [Fact]
    public void Finds_turn_up_and_leads_point_at_caches()
    {
        var session = NewGame(34);
        int finds = 0;
        // Scan every world within a few jumps; some carry finds.
        foreach (var system in session.Galaxy.WithinJumps(session.Career.SystemId, 4))
        {
            session.Career.SystemId = system.Id;
            var solar = SolarSystems.For(system, Db.Grammars);
            for (int i = 0; i < solar.Bodies.Count; i++)
                if (solar.Bodies[i].Kind != BodyKind.Belt && Scanning.Body(session, solar, i).Find != null) finds++;
        }
        Assert.True(finds > 0, "Some worlds should carry finds.");
        Assert.Equal(finds, session.Career.Logbook.Count);
        Assert.All(session.Career.Logbook, e => Assert.DoesNotContain("{", e.Text));

        // Follow a lead to its cache.
        var session2 = NewGame(35);
        session2.Career.Trails.Add(new Trail("dominion-paychest", session2.Career.SystemId, 0, 0));
        var here = SolarSystems.For(session2.Here, Db.Grammars);
        int credits = session2.Career.Credits;
        var result = Scanning.Body(session2, here, 0);
        Assert.Equal("dominion-paychest", result.Find?.FindId);
        Assert.Empty(session2.Career.Trails);
        Assert.True(session2.Career.Credits >= credits + 9000);
    }

    [Fact]
    public void Mining_lasers_fit_gun_mounts()
    {
        var hull = Db.Hulls["hc-sparrow"];
        int gun = hull.Slots.FindIndex(s => s.Type == "weapon");
        Assert.True(ShipFactory.SlotTakes(hull.Slots[gun], Db.Parts["hc-mining-laser-1"]));
        Assert.False(ShipFactory.SlotTakes(hull.Slots[gun], Db.Parts["hc-repair-rig-1"]));
        var ids = ShipFactory.Assign(Db, hull, null).Select(p => p?.Id ?? "").ToList();
        ids[gun] = "hc-mining-laser-1";
        var spec = ShipFactory.Build(Db, hull.Id, ids);
        Assert.True(spec.Mining > 0 && spec.MiningRange > 0);
        Assert.Empty(spec.Weapons);
    }

    [Fact]
    public void Jobs_with_work_need_it_done_before_they_pay()
    {
        var session = NewGame(36);
        var galaxy = session.Galaxy;
        // Find a mining job with real mining and a survey job with a scan on the boards nearby.
        var offers = galaxy.WithinJumps(session.Career.SystemId, 6).Where(s => s.Stations.Count > 0)
            .SelectMany(s => Enumerable.Range(0, 4).SelectMany(w => { galaxy.Day = w * 7; return MissionBoard.For(Db, galaxy, s, "Ashby", _ => 4); }))
            .ToList();
        var mine = offers.FirstOrDefault(o => o.Task?.StartsWith("mine:") == true);
        var survey = offers.FirstOrDefault(o => o.Task?.StartsWith("scan:") == true);
        Assert.NotNull(survey);
        Assert.True(survey!.Playable);

        foreach (var offer in new[] { mine, survey }.OfType<MissionOffer>())
        {
            session.Career.Missions.Clear();
            session.Career.Cargo.Clear();
            galaxy.Day = offer.PostedDay;
            session.Career.SystemId = offer.DestinationSystemId;
            session.Career.Missions.Add(new AcceptedMission(offer, galaxy.Day, galaxy.Day + 30));
            // Docking before the work is done doesn't finish it.
            Assert.Empty(session.Career.Settle(galaxy, offer.DestinationStation));
            Assert.Single(session.Career.Missions);
            var parts = offer.Task!.Split(':');
            if (parts[0] == "mine") session.Career.Cargo[parts[1]] = int.Parse(parts[2]) + 2;
            else session.Career.Scanned.Add($"{offer.DestinationSystemId}:body:{parts[1]}");
            var results = session.Career.Settle(galaxy, offer.DestinationStation);
            Assert.True(Assert.Single(results).Success);
            if (parts[0] == "mine") Assert.Equal(2, session.Career.Cargo[parts[1]]);
        }
    }

    [Fact]
    public void Scans_and_finds_survive_a_save()
    {
        var session = NewGame(37);
        var solar = SolarSystems.For(session.Here, Db.Grammars);
        Scanning.Body(session, solar, solar.Bodies.FindIndex(b => b.Kind != BodyKind.Belt));
        session.Career.Logbook.Add(new LogEntry(3, session.Career.SystemId, "Test", "A test find.", null));
        session.Career.Trails.Add(new Trail("walkout-store", session.Career.SystemId, 1, 3));
        session.Career.Mined["rock-x"] = 7;
        var loaded = SaveGame.Load(Db, SaveGame.Save(session));
        Assert.Equal(session.Career.Scanned, loaded.Career.Scanned);
        Assert.Equal(session.Career.SurveyData, loaded.Career.SurveyData);
        Assert.Equal(session.Career.Logbook, loaded.Career.Logbook);
        Assert.Equal(session.Career.Trails, loaded.Career.Trails);
        Assert.Equal(7, loaded.Career.Mined["rock-x"]);
    }
}
