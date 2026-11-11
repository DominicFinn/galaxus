using Galaxus.Core.Content;
using Galaxus.Core.People;

namespace Galaxus.Core.Tests;

/// <summary>Hiring wingmen and crew, wages, bonuses, experience and losses.</summary>
public class CrewTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    private static Session NewGame(ulong seed)
    {
        var session = Session.StartNew(Db, seed);
        session.Career.Credits = 200_000;
        return session;
    }

    /// <summary>People who'd take a given job, made up at the start system.</summary>
    private static IEnumerable<Person> Candidates(Session session, string job) =>
        Enumerable.Range(0, 400).Select(i => PeopleDirectory.Make(Db, session.Galaxy, $"crew-test-{i}", session.Here, null, regular: false))
            .Where(p => CrewRules.JobFor(p) == job);

    [Fact]
    public void Pilots_sign_on_with_their_own_ship_and_up_to_two_fly_with_you()
    {
        var session = NewGame(61);
        var pilots = Candidates(session, "pilot").Take(3).ToList();
        Assert.Equal(3, pilots.Count);
        foreach (var p in pilots.Take(2))
        {
            var offer = CrewRules.Offer(session, p)!;
            Assert.NotNull(offer.HullId);
            Assert.Equal("fighter", Db.Hulls[offer.HullId!].Class);
            int credits = session.Career.Credits;
            Assert.NotNull(CrewRules.Hire(session, p));
            Assert.Equal(credits - offer.Fee, session.Career.Credits);
        }
        Assert.NotNull(CrewRules.CannotHire(session, pilots[2]));
        Assert.NotNull(CrewRules.CannotHire(session, pilots[0]));
    }

    [Fact]
    public void Crew_aboard_need_a_berth_and_help_a_little()
    {
        var session = NewGame(62);
        session.Career.HullId = Db.Hulls.Values.Where(h => h.ForSale && h.CrewBerths >= 1).OrderBy(h => h.CrewBerths).First().Id;
        int berths = CrewRules.Berths(session);
        var engineers = Candidates(session, "engineer").Take(berths + 1).ToList();
        Assert.Equal(1f, CrewRules.Bonus(session.Career).Shields);
        foreach (var e in engineers.Take(berths)) Assert.NotNull(CrewRules.Hire(session, e));
        Assert.NotNull(CrewRules.CannotHire(session, engineers[berths]));
        Assert.True(CrewRules.Bonus(session.Career).Shields > 1);

        float range = session.JumpRangeLy;
        var navigator = Candidates(session, "navigator").First();
        session.Career.Crew.Clear();
        CrewRules.Hire(session, navigator);
        Assert.True(session.JumpRangeLy > range);
    }

    [Fact]
    public void Wages_are_paid_on_docking_and_the_unpaid_leave()
    {
        var session = NewGame(63);
        var pilot = Candidates(session, "pilot").First();
        var member = CrewRules.Hire(session, pilot)!;
        session.Galaxy.Day += 5;
        int credits = session.Career.Credits;
        Assert.NotNull(CrewRules.PayWages(session));
        Assert.Equal(credits - member.Wage * 5, session.Career.Credits);
        Assert.Null(CrewRules.PayWages(session));

        session.Galaxy.Day += 10;
        session.Career.Credits = 10;
        var text = CrewRules.PayWages(session)!;
        Assert.Contains(member.Name, text);
        Assert.Empty(session.Career.Crew);
    }

    [Fact]
    public void Wingmen_get_better_with_kills_and_can_be_lost()
    {
        var session = NewGame(64);
        var member = CrewRules.Hire(session, Candidates(session, "pilot").First())!;
        float skill = member.Skill;
        member.Kills += 4;
        Assert.True(member.Skill > skill);
        Assert.True(member.Skill <= 0.95f);

        int died = 0, hurt = 0;
        for (int i = 0; i < 40; i++)
        {
            var career = new Career();
            var m = new CrewMember { PersonId = $"x{i}", Job = "pilot" };
            career.Crew.Add(m);
            if (CrewRules.ShotDown(career, m, new Rng((ulong)i + 1))) { died++; Assert.Empty(career.Crew); }
            else { hurt++; Assert.True(m.Injured); }
        }
        Assert.True(died > 5 && hurt > 5);
    }

    [Fact]
    public void Crew_survive_a_save()
    {
        var session = NewGame(65);
        var member = CrewRules.Hire(session, Candidates(session, "pilot").First())!;
        member.Kills = 3;
        var loaded = SaveGame.Load(Db, SaveGame.Save(session));
        var back = Assert.Single(loaded.Career.Crew);
        Assert.Equal(member.Name, back.Name);
        Assert.Equal(3, back.Kills);
        Assert.Equal(member.HullId, back.HullId);
        Assert.Equal(session.Career.WagesPaidDay, loaded.Career.WagesPaidDay);
    }
}
