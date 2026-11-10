using Galaxus.Core.Battles;
using Galaxus.Core.Content;
using Galaxus.Core.Ships;
using Xunit;

namespace Galaxus.Core.Tests;

public class CampaignTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    [Fact]
    public void Every_campaign_starts_at_its_base_and_runs_its_orders()
    {
        Assert.NotEmpty(Db.Campaigns);
        foreach (var def in Db.Campaigns.Values)
        {
            var session = Campaigns.Start(Db, 77, def.Id);
            var career = session.Career;
            Assert.Equal(def.Hull, career.HullId);
            Assert.Equal(career.CampaignBase, career.SystemId);
            Assert.False(string.IsNullOrWhiteSpace(career.CampaignDebrief), $"{def.Id}: the intro should be waiting at the base");

            for (int step = 0; step < def.Steps.Count; step++)
            {
                var job = Campaigns.TakeOrders(session);
                Assert.NotNull(job);
                Assert.Null(Campaigns.TakeOrders(session)); // one set of orders at a time
                Assert.DoesNotContain("{", job!.Offer.Mission.Briefing);
                Assert.Equal(def.Steps[step].Battle, job.Offer.BattleId);

                // The battle itself starts and runs.
                var world = new World();
                var player = new Ship(1, "Player", ShipFactory.Build(Db, def.Hull));
                world.Ships.Add(player);
                var battle = new Battle(Db, Db.Battles[job.Offer.BattleId!], world, player, BattleJobs.Sides(Db, session.Galaxy, job.Offer),
                    new Rng(3), job.Offer.Mission.Variables, Battle.NameTags(Db.Battles[job.Offer.BattleId!], new Rng(4)));
                for (int i = 0; i < 300; i++) { battle.Step(1 / 30f); world.Step(1 / 30f); }
                Assert.NotEmpty(battle.Spawned);

                career.FinishBattle(job, true, 0);
                Campaigns.Record(session, job, true);
                Assert.Equal(step + 1, career.CampaignStep);
                Assert.DoesNotContain("{", career.CampaignDebrief);
            }
            Assert.True(Campaigns.Finished(session));
            Assert.Null(Campaigns.TakeOrders(session));
        }
    }

    [Fact]
    public void A_lost_mission_can_be_flown_again()
    {
        var def = Db.Campaigns.Values.First();
        var session = Campaigns.Start(Db, 5, def.Id);
        var job = Campaigns.TakeOrders(session)!;
        session.Career.FinishBattle(job, false, 0);
        Campaigns.Record(session, job, false);
        Assert.Equal(0, session.Career.CampaignStep);
        Assert.NotNull(Campaigns.TakeOrders(session));
    }

    [Fact]
    public void Campaign_progress_is_saved()
    {
        var def = Db.Campaigns.Values.First();
        var session = Campaigns.Start(Db, 9, def.Id);
        var job = Campaigns.TakeOrders(session)!;
        session.Career.FinishBattle(job, true, 0);
        Campaigns.Record(session, job, true);
        var loaded = SaveGame.Load(Db, SaveGame.Save(session));
        Assert.Equal(def.Id, loaded.Career.CampaignId);
        Assert.Equal(1, loaded.Career.CampaignStep);
        Assert.Equal(session.Career.CampaignBase, loaded.Career.CampaignBase);
    }
}
