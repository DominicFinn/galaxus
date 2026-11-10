using Galaxus.Core.Battles;
using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Xunit;

namespace Galaxus.Core.Tests;

/// <summary>The engine hooks mods use: faction builders, station models, landmarks, campaigns for small factions.</summary>
public class ModHookTests
{
    private static ContentDatabase WithHooks()
    {
        var db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));
        var template = db.FactionTypes.Values.First(t => !t.Criminal && t.Races.Contains("human"));
        db.FactionTypes["test-wanderers"] = new FactionTypeDef
        {
            Id = "test-wanderers", Name = "Wanderers", Races = ["human"], Tiers = ["minor"], NameGrammar = template.NameGrammar,
            Traits = template.Traits, Builder = "tolland-wrights", StationHull = "tw-reckoning",
        };
        db.Landmarks["test-bridgeford"] = new LandmarkDef
        {
            Id = "test-bridgeford", SystemName = "Bridgeford 421", StationName = "Hassall World", Region = "outer",
            OwnerFactionType = "test-wanderers", Economy = "HighTech", Description = "Home of the Hassall works.",
        };
        var campaign = db.Campaigns["concord-watch"];
        db.Campaigns["test-wander"] = new CampaignDef
        {
            Id = "test-wander", Name = "Wandering", Power = "", FactionType = "test-wanderers", Rival = "grav-khanates",
            Hull = campaign.Hull, BaseHull = campaign.BaseHull, BaseName = "Long Road", BaseLandmark = "test-bridgeford",
            TextGrammar = campaign.TextGrammar,
            Steps = [new CampaignStepDef { Id = "one", Title = "One", Battle = campaign.Steps[0].Battle, Where = "landmark:test-bridgeford", Enemy = "pirates", Briefing = campaign.Steps[0].Briefing, Debrief = campaign.Steps[0].Debrief }],
        };
        return db;
    }

    [Fact]
    public void A_landmark_lands_in_the_same_place_every_time_with_its_names()
    {
        var db = WithHooks();
        var a = GalaxyGenerator.Generate(db, 42);
        var b = GalaxyGenerator.Generate(db, 42);
        Assert.Equal(a.Landmarks["test-bridgeford"], b.Landmarks["test-bridgeford"]);
        var system = a.Systems[a.Landmarks["test-bridgeford"]];
        Assert.Equal("Bridgeford 421", system.Name);
        Assert.Equal("Hassall World", system.Stations[0]);
        Assert.Equal("outer", system.Region);
        Assert.Equal(Economy.HighTech, system.Economy);
        Assert.Equal("test-wanderers", a.Factions[system.Owner].TypeId);
    }

    [Fact]
    public void A_faction_type_flies_its_own_builders_ships()
    {
        var db = WithHooks();
        var galaxy = GalaxyGenerator.Generate(db, 42);
        var wanderers = galaxy.Factions.First(f => f.TypeId == "test-wanderers");
        Assert.Equal("tolland-wrights", BattleJobs.BuilderFor(db, wanderers));
    }

    [Fact]
    public void A_campaign_can_fly_for_a_small_faction_from_a_landmark_and_survives_a_save()
    {
        var db = WithHooks();
        var session = Campaigns.Start(db, 7, "test-wander");
        var galaxy = session.Galaxy;
        Assert.Equal("test-wanderers", Campaigns.Client(galaxy, db.Campaigns["test-wander"]).TypeId);
        Assert.Equal(galaxy.Landmarks["test-bridgeford"], session.Career.CampaignBase);
        var job = Campaigns.TakeOrders(session)!;
        Assert.Equal(galaxy.Landmarks["test-bridgeford"], job.Offer.DestinationSystemId);

        var loaded = SaveGame.Load(db, SaveGame.Save(session));
        Assert.Equal("Bridgeford 421", loaded.Galaxy.Systems[loaded.Career.CampaignBase].Name);
        Assert.Equal("test-wanderers", Campaigns.Client(loaded.Galaxy, db.Campaigns["test-wander"]).TypeId);
    }

    [Fact]
    public void Bad_hooks_are_reported()
    {
        var root = Path.Combine(Path.GetTempPath(), "galaxus-hooks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "bad", "landmarks"));
        File.WriteAllText(Path.Combine(root, "bad", "landmarks", "l.json"),
            "[{\"id\":\"x\",\"systemName\":\"X\",\"region\":\"nowhere\",\"ownerFactionType\":\"nobody\"}]");
        try
        {
            var e = Assert.Throws<ContentException>(() => ModDiscovery.Load(new DirectoryContentSource(ContentTests.DataFolder), [root]));
            Assert.Contains("region must be", e.Message);
            Assert.Contains("neither a great power nor a faction type", e.Message);
        }
        finally { Directory.Delete(root, true); }
    }
}
