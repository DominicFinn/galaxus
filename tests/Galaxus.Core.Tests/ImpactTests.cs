using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Xunit;

namespace Galaxus.Core.Tests;

public class ImpactTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    /// <summary>A session with a war going on, and the faction you'd fight for.</summary>
    private static (Session, Conflict) AtWar()
    {
        for (ulong seed = 1; ; seed++)
        {
            var session = Session.StartNew(Db, seed);
            session.Simulation.Run(120);
            var war = session.Galaxy.Conflicts.FirstOrDefault(c => c.Stage >= 1 && session.Galaxy.Systems[c.FrontSystem].Owner >= 0);
            if (war != null) return (session, war);
        }
    }

    [Fact]
    public void Winning_battles_takes_the_front_then_ends_the_war_and_makes_the_news()
    {
        var (session, war) = AtWar();
        var galaxy = session.Galaxy;
        var front = galaxy.Systems[war.FrontSystem];
        int defender = front.Owner;
        int attacker = war.A == defender ? war.B : war.A;

        for (int i = 0; i < 8 && galaxy.Conflicts.Contains(war); i++)
            session.Simulation.Record(new PlayerAction(galaxy.Day, "war", attacker, defender, front.Id, true, 1.5f, "Vale"));

        Assert.Equal(attacker, front.Owner);
        Assert.DoesNotContain(war, galaxy.Conflicts);
        Assert.Contains(galaxy.News, n => n.Text.Contains("sue for peace"));
        Assert.Contains(galaxy.News, n => n.Text.Contains("Captain Vale"));
        // The news reaches both capitals, not just the front.
        Assert.Contains(galaxy.News, n => n.Text.Contains("Captain Vale") && (n.SystemId == galaxy.Factions[attacker].HomeSystem || n.SystemId == galaxy.Factions[defender].HomeSystem));
    }

    [Fact]
    public void A_loaded_save_replays_what_you_did()
    {
        var (session, war) = AtWar();
        var galaxy = session.Galaxy;
        var front = galaxy.Systems[war.FrontSystem];
        int defender = front.Owner;
        int attacker = war.A == defender ? war.B : war.A;
        for (int i = 0; i < 3; i++)
            session.Simulation.Record(new PlayerAction(galaxy.Day, "war", attacker, defender, front.Id, true, 1.5f, "Vale"));
        session.Simulation.Run(20);

        var loaded = SaveGame.Load(Db, SaveGame.Save(session));
        Assert.Equal(galaxy.Day, loaded.Galaxy.Day);
        Assert.Equal(galaxy.Systems.Select(s => s.Owner), loaded.Galaxy.Systems.Select(s => s.Owner));
        Assert.Equal(galaxy.PlayerActions.Count, loaded.Galaxy.PlayerActions.Count);
        Assert.Equal(galaxy.News.Select(n => n.Text).TakeLast(30), loaded.Galaxy.News.Select(n => n.Text).TakeLast(30));
    }

    [Fact]
    public void Clearing_pirates_drives_them_out()
    {
        var session = Session.StartNew(Db, 4);
        var galaxy = session.Galaxy;
        var gang = galaxy.Factions.First(f => f.Criminal && f.Haunts.Count > 0);
        int haunt = gang.Haunts[0];
        var lawful = galaxy.Factions.First(f => !f.Criminal && f.Alive);
        session.Simulation.Record(new PlayerAction(galaxy.Day, "pirates", lawful.Id, gang.Id, haunt, true, 0.5f, "Vale"));
        Assert.DoesNotContain(haunt, gang.Haunts);
    }

    [Fact]
    public void Combat_jobs_on_the_boards_are_playable_battles()
    {
        var session = Session.StartNew(Db, 2026);
        var offers = session.Galaxy.Systems.Where(s => s.Settled).Take(40)
            .SelectMany(s => Missions.MissionBoard.For(Db, session.Galaxy, s, "Vale", _ => 2)).ToList();
        var combat = offers.Where(o => !Missions.MissionBoard.TravelTypes.Contains(o.Mission.Template.Type) && o.Mission.Template.Type != "battle" && o.Task == null).ToList();
        Assert.NotEmpty(combat);
        Assert.All(combat, o => Assert.True(o.Playable && o.BattleId != null && Db.Battles[o.BattleId].Kind == "job"));
    }
}
