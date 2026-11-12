using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Law;
using Galaxus.Core.Missions;
using Xunit;

namespace Galaxus.Core.Tests;

/// <summary>
/// Passengers as people: their looks and pay, berths, who comes after them and how often, what
/// each choice does, customs finding the wanted, grudges, and the work that follows.
/// </summary>
public class PassengerTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    private static string Security(Session s, StarSystem system) => SystemProfiles.For(s.Galaxy, system, Db).Security;

    private static StarSystem SystemWith(Session s, Func<StarSystem, bool> test) =>
        s.Galaxy.Systems.First(test);

    /// <summary>A passenger job in hand, with the passenger made to order.</summary>
    private static AcceptedMission Carry(Session s, string look, string? trouble, int fare = 4000, string template = "fare-paying-passage")
    {
        var giver = s.Galaxy.Factions.First(f => f.Alive && !f.Criminal);
        var context = new MissionContext(new FactionInfo(giver.Name, giver.TypeId, giver.RaceId), s.Here.Name, "Somewhere", "Vale");
        var mission = MissionGenerator.Build(Db, Db.Missions[template], context, new Rng(7));
        mission = mission with { Reward = fare, Passenger = mission.Passenger! with { Look = look, Trouble = trouble } };
        var destination = s.Galaxy.WithinJumps(s.Here.Id, 3).FirstOrDefault(x => x.Stations.Count > 0) ?? s.Galaxy.Systems.First(x => x.Stations.Count > 0 && x.IsGateSystem);
        var offer = new MissionOffer($"test-{s.Career.Missions.Count}-{look}-{trouble}", mission, giver.Id, s.Here.Id, destination.Id, destination.Stations[0], s.Galaxy.Day);
        var job = new AcceptedMission(offer, s.Galaxy.Day, s.Galaxy.Day + 20);
        s.Career.Missions.Add(job);
        return job;
    }

    [Fact]
    public void The_boards_show_all_three_looks_with_hints_and_pay_to_match()
    {
        var s = Session.StartNew(Db, 2026);
        var offers = s.Galaxy.Systems.Where(x => x.Stations.Count > 0).Take(120)
            .SelectMany(x => MissionBoard.For(Db, s.Galaxy, x, "Vale", _ => 2))
            .Where(o => o.Mission.Passenger != null).ToList();
        foreach (var look in ContentRules.PassengerLooks) Assert.Contains(offers, o => o.Mission.Passenger!.Look == look);
        Assert.All(offers, o => Assert.Contains(o.Mission.Passenger!.Name, o.Mission.Briefing + o.Mission.Title));
        Assert.All(offers, o => Assert.DoesNotContain("{", o.Mission.Briefing));

        // Pay against the template's base: dodgy beats shady beats respectable.
        float Pay(string look) => offers.Where(o => o.Mission.Passenger!.Look == look).Average(o => o.Mission.Reward / (float)o.Mission.Template.BaseReward);
        Assert.True(Pay("respectable") < Pay("shady") && Pay("shady") < Pay("dodgy"), $"{Pay("respectable")} {Pay("shady")} {Pay("dodgy")}");

        // Trouble follows the look.
        float Trouble(string look) => offers.Where(o => o.Mission.Passenger!.Look == look).Average(o => o.Mission.Passenger!.Trouble != null ? 1f : 0f);
        Assert.True(Trouble("respectable") < 0.3f && Trouble("dodgy") > 0.8f, $"{Trouble("respectable")} {Trouble("dodgy")}");
    }

    [Fact]
    public void Follow_up_work_is_never_on_the_open_board()
    {
        var s = Session.StartNew(Db, 42);
        var offers = s.Galaxy.Systems.Where(x => x.Stations.Count > 0).Take(150).SelectMany(x => MissionBoard.For(Db, s.Galaxy, x, "Vale", _ => 4));
        Assert.DoesNotContain(offers, o => o.Mission.Template.FollowUpOnly);
    }

    [Fact]
    public void Passengers_need_a_berth_and_share_them_with_crew()
    {
        var s = Session.StartNew(Db, 42);
        var job = Carry(s, "respectable", null);
        s.Career.Missions.Clear();
        var offer = job.Offer with { Id = "berth-test" };

        s.Career.HullId = Db.Hulls.Values.First(h => h.Class == "fighter" && h.CrewBerths == 0 && h.ForSale).Id;
        Assert.Contains("berth", s.Career.CannotAccept(Db, offer));

        s.Career.HullId = Db.Hulls.Values.First(h => h.CrewBerths == 1 && h.ForSale).Id;
        Assert.Null(s.Career.CannotAccept(Db, offer));
        s.Career.Accept(s.Galaxy, offer);
        Assert.Equal(1, Passengers.Aboard(s.Career));
        Assert.Contains("berth", s.Career.CannotAccept(Db, offer with { Id = "berth-test-2" }));

        // And the berth isn't free for an engineer either.
        var engineer = People.PeopleDirectory.AtStation(Db, s.Galaxy, s.Here).Concat(
                s.Galaxy.Systems.Take(300).SelectMany(x => People.PeopleDirectory.AtStation(Db, s.Galaxy, x)))
            .First(p => People.CrewRules.JobFor(p) is "engineer" or "gunner" or "navigator");
        s.Career.Credits = 100000;
        Assert.Contains("berth", People.CrewRules.CannotHire(s, engineer));
    }

    [Fact]
    public void Respectable_passengers_in_safe_systems_are_almost_never_intercepted()
    {
        var s = Session.StartNew(Db, 42);
        var safe = SystemWith(s, x => x.Stations.Count > 0 && Security(s, x) == "High");
        var lawless = SystemWith(s, x => Security(s, x) == "Lawless");

        s.Career.SystemId = safe.Id;
        Assert.Equal(0, Passengers.Chance(s, Carry(s, "respectable", null), safe));
        Assert.True(Passengers.Chance(s, Carry(s, "respectable", "debtor"), safe) < 0.15f);

        s.Career.SystemId = lawless.Id;
        var dodgy = Carry(s, "dodgy", "debtor");
        Assert.True(Passengers.Chance(s, dodgy, lawless) > 0.6f, $"{Passengers.Chance(s, dodgy, lawless)}");
        // Once it's caught up with them, that's it.
        s.Career.PassengerOutcomes[dodgy.Offer.Id] = "paid";
        Assert.Equal(0, Passengers.Chance(s, dodgy, lawless));
    }

    [Fact]
    public void The_right_people_come_for_each_kind_of_trouble()
    {
        var s = Session.StartNew(Db, 42);
        var policed = SystemWith(s, x => LawRules.Police(s.Galaxy, x) != null);
        var lawless = SystemWith(s, x => LawRules.Police(s.Galaxy, x) == null);

        Assert.Equal("creditors", Passengers.PursuerFor(s, Carry(s, "shady", "debtor"), lawless)!.Id);
        Assert.Equal("scavengers", Passengers.PursuerFor(s, Carry(s, "shady", "valuables"), lawless)!.Id);
        Assert.Equal("bounty-hunters", Passengers.PursuerFor(s, Carry(s, "dodgy", "bounty"), lawless)!.Id);
        // The police only come where there are police.
        Assert.DoesNotContain(Passengers.PursuersFor(s, "wanted", lawless), p => p.Side == "police");
        Assert.Contains(Passengers.PursuersFor(s, "wanted", policed), p => p.Id == "customs-officers");
        Assert.Null(Passengers.PursuerFor(s, Carry(s, "respectable", null), policed));
    }

    [Fact]
    public void Arrivals_with_dodgy_passengers_get_interceptions()
    {
        var s = Session.StartNew(Db, 42);
        int intercepted = 0;
        foreach (var system in s.Galaxy.Systems.Where(x => x.Stations.Count > 0 && Security(s, x) is "Low" or "Lawless").Take(40))
        {
            s.Career.SystemId = system.Id;
            s.Career.Missions.Clear();
            s.Career.PassengerOutcomes.Clear();
            Carry(s, "dodgy", "debtor");
            if (Passengers.OnArrival(s) is { } x)
            {
                intercepted++;
                Assert.Equal("creditors", x.Pursuer.Id);
                Assert.Equal(Passengers.Hulls(s, x).Count, x.Ships);
                Assert.DoesNotContain("{", Passengers.Open(s, x).Text);
            }
        }
        Assert.InRange(intercepted, 15, 40);
    }

    private static (Session S, AcceptedMission Job, Interception X) Intercepted(string trouble, bool policed)
    {
        var s = Session.StartNew(Db, 42);
        s.Career.SystemId = SystemWith(s, x => (LawRules.Police(s.Galaxy, x) != null) == policed).Id;
        var job = Carry(s, "dodgy", trouble);
        return (s, job, Passengers.Intercept(s, job)!);
    }

    [Fact]
    public void Handing_a_passenger_over_loses_the_job_and_pays_a_share()
    {
        var (s, job, x) = Intercepted("bounty", policed: false);
        int credits = s.Career.Credits;
        var turn = Passengers.Respond(s, x, "px:hand-over");
        Assert.Equal(HailOutcome.Leave, turn.Outcome);
        Assert.DoesNotContain(job, s.Career.Missions);
        Assert.Equal(credits + Passengers.HandOverPay(x), s.Career.Credits);
        Assert.True(Passengers.HandOverPay(x) > 0);
        Assert.True(s.Career.Reputation[job.Offer.GiverFactionId] < 0);
    }

    [Fact]
    public void Paying_keeps_the_passenger()
    {
        var (s, job, x) = Intercepted("debtor", policed: false);
        int credits = s.Career.Credits;
        Assert.Contains(Passengers.Open(s, x).Options, o => o.Action == "px:pay");
        Assert.Equal(HailOutcome.Leave, Passengers.Respond(s, x, "px:pay").Outcome);
        Assert.Contains(job, s.Career.Missions);
        Assert.Equal(credits - Passengers.Price(s, x), s.Career.Credits);
        Assert.Equal("paid", s.Career.PassengerOutcomes[job.Offer.Id]);
    }

    [Fact]
    public void Refusing_the_police_is_harbouring_and_a_fight()
    {
        var (s, job, x) = Intercepted("wanted", policed: true);
        x = Passengers.Intercept(s, job, Db.Pursuers["customs-officers"])!;
        Assert.Equal(HailOutcome.Hostile, Passengers.Respond(s, x, "px:fight").Outcome);
        Assert.Contains(s.Career.Offences, o => o.Kind == "harbouring");
        Assert.Equal(Standing.Wanted, LawRules.StandingHere(s));
    }

    [Fact]
    public void Running_works_only_if_you_are_faster()
    {
        var (s, job, x) = Intercepted("debtor", policed: false);
        var hulls = Db.Hulls.Values.Where(h => h.ForSale).Select(h => (h.Id, Speed: Ships.ShipFactory.Build(Db, h.Id).MaxSpeed)).OrderBy(h => h.Speed).ToList();

        s.Career.HullId = hulls[0].Id;
        Assert.False(Passengers.Outruns(s, x));
        Assert.Equal(HailOutcome.Hostile, Passengers.Respond(s, x, "px:run").Outcome);

        s.Career.HullId = hulls[^1].Id;
        s.Career.PassengerOutcomes.Clear();
        Assert.True(Passengers.Outruns(s, x));
        Assert.Equal(HailOutcome.Leave, Passengers.Respond(s, x, "px:run").Outcome);
        Assert.Equal("outran", s.Career.PassengerOutcomes[job.Offer.Id]);
    }

    [Fact]
    public void Talking_round_can_fail_and_then_it_is_off_the_table()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var s = Session.StartNew(Db, seed);
            s.Career.SystemId = SystemWith(s, _ => true).Id;
            var job = Carry(s, "dodgy", "witness");
            var x = Passengers.Intercept(s, job, Db.Pursuers["house-agents"])!;
            var turn = Passengers.Respond(s, x, "px:talk");
            if (turn.Outcome == HailOutcome.Leave) { Assert.Equal("talked", s.Career.PassengerOutcomes[job.Offer.Id]); continue; }
            Assert.Equal(HailOutcome.None, turn.Outcome);
            Assert.DoesNotContain(turn.Options, o => o.Action == "px:talk");
            Assert.Contains(turn.Options, o => o.Action == "px:hand-over");
            return;
        }
        Assert.Fail("Talking worked every time.");
    }

    [Fact]
    public void Beaten_pursuers_with_a_grudge_come_back_with_friends_then_give_up()
    {
        var (s, job, x) = Intercepted("bounty", policed: false);
        Passengers.Respond(s, x, "px:fight");
        Passengers.Fought(s, x);
        var grudge = Assert.Single(s.Career.Grudges);
        Assert.Equal("beat", s.Career.PassengerOutcomes[job.Offer.Id]);

        s.Galaxy.Day += 3;
        var lawless = SystemWith(s, x => Security(s, x) == "Lawless");
        Assert.True(Passengers.GrudgeChance(s, grudge, lawless) > 0.3f);
        var back = Passengers.Comeback(s, grudge);
        Assert.True(back.Ships > x.Ships);
        Assert.Equal(x.Group, back.Group);
        var open = Passengers.Open(s, back);
        Assert.Contains(open.Options, o => o.Action == "px:pay");
        Assert.DoesNotContain(open.Options, o => o.Action == "px:hand-over");

        Passengers.Fought(s, back);
        Assert.Empty(s.Career.Grudges);
    }

    [Fact]
    public void Customs_can_find_a_wanted_passenger()
    {
        var s = Session.StartNew(Db, 42);
        s.Career.SystemId = SystemWith(s, x => x.Stations.Count > 0 && LawRules.Police(s.Galaxy, x) != null).Id;
        int found = 0;
        for (int i = 0; i < 40; i++)
        {
            s.Career.Missions.Clear();
            s.Career.Offences.Clear();
            var job = Carry(s, "dodgy", "wanted");
            var result = LawRules.Inspect(s, new Rng((ulong)i + 1));
            if (!result.Found) { Assert.Contains(job, s.Career.Missions); continue; }
            found++;
            Assert.DoesNotContain(job, s.Career.Missions);
            Assert.Contains(job.Offer.Mission.Passenger!.Name, result.Text);
            Assert.Contains(s.Career.Offences, o => o.Kind == "harbouring");
        }
        Assert.InRange(found, 10, 32);

        // Nobody else is in trouble with customs.
        s.Career.Missions.Clear();
        s.Career.Offences.Clear();
        Carry(s, "dodgy", "debtor");
        Assert.False(LawRules.Inspect(s, new Rng(3)).Found);
    }

    [Fact]
    public void Getting_a_passenger_through_trouble_leads_to_more_work()
    {
        int followed = 0;
        for (int i = 0; i < 12; i++)
        {
            var s = Session.StartNew(Db, 42);
            var job = Carry(s, "shady", "witness");
            s.Career.PassengerOutcomes[job.Offer.Id] = "paid";
            s.Career.SystemId = job.Offer.DestinationSystemId;
            job = job with { Offer = job.Offer with { Id = $"through-{i}" } };
            s.Career.Missions.Clear();
            s.Career.Missions.Add(job);
            s.Career.PassengerOutcomes[job.Offer.Id] = "paid";
            var results = s.Dock(job.Offer.DestinationStation);
            Assert.Single(results, r => r.Success);
            Assert.Empty(s.Career.PassengerOutcomes);
            if (s.Career.FollowUps.Count == 0) continue;
            followed++;
            var next = Assert.Single(s.Board(), o => o.Id == s.Career.FollowUps[0].Id);
            Assert.True(next.Mission.Template.FollowUpOnly || job.Offer.Mission.Template.FollowUps.Contains(next.Mission.Template.Id));
            Assert.Contains(job.Offer.Mission.Passenger!.Name, next.Mission.Briefing + next.Mission.Title);
            Assert.Contains(job.Offer.Mission.Passenger!.Name, s.DockNotes);

            // It stays a fortnight, then it's gone.
            s.Galaxy.Day += Passengers.FollowUpDays + 1;
            Assert.DoesNotContain(s.Board(), o => o.Id == next.Id);
        }
        Assert.InRange(followed, 5, 12);
    }

    [Fact]
    public void Passengers_and_what_happened_to_them_survive_a_save()
    {
        var (s, job, x) = Intercepted("bounty", policed: false);
        Passengers.Respond(s, x, "px:fight");
        Passengers.Fought(s, x);
        s.Career.FollowUps.Add(new FollowUp("fu-x", "passenger-family", job.Offer.GiverFactionId, s.Here.Id, s.Galaxy.Day, "Ada Vey", null));

        var loaded = SaveGame.Load(Db, SaveGame.Save(s));
        var passenger = Assert.Single(loaded.Career.Missions).Offer.Mission.Passenger;
        Assert.Equal(job.Offer.Mission.Passenger, passenger);
        Assert.Equal("beat", loaded.Career.PassengerOutcomes[job.Offer.Id]);
        Assert.Equal(s.Career.Grudges, loaded.Career.Grudges);
        Assert.Equal(s.Career.FollowUps, loaded.Career.FollowUps);
    }
}
