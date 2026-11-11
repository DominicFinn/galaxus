using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Xunit;

namespace Galaxus.Core.Tests;

/// <summary>
/// What a landmark can do for its system, checked against the Martins World mod: its own prices,
/// parts sold only there, work offered by a named character, a belt to mine, a faction that exists
/// only there, and a campaign that gives you a part and names its enemy.
/// </summary>
public class LandmarkWorkTests
{
    private static string RepoMods => Path.Combine(new DirectoryInfo(ContentTests.DataFolder).Parent!.Parent!.FullName, "mods");

    private static readonly ContentDatabase Db = ModDiscovery.Load(new DirectoryContentSource(ContentTests.DataFolder), [RepoMods]);

    private static (Session Session, StarSystem Works) AtTheWorks()
    {
        var session = Session.StartNew(Db, 42);
        var works = session.Galaxy.Systems[session.Galaxy.Landmarks["mw-works"]];
        session.Career.SystemId = works.Id;
        return (session, works);
    }

    [Fact]
    public void Martins_World_has_a_belt_and_the_only_Martins_World_faction()
    {
        foreach (ulong seed in new ulong[] { 42, 7, 2026 })
        {
            var galaxy = GalaxyGenerator.Generate(Db, seed);
            var works = galaxy.Systems[galaxy.Landmarks["mw-works"]];
            Assert.True(Mining.Asteroids.BeltIndex(SolarSystems.For(works)) >= 0, $"Seed {seed}: no belt at Martins World.");
            Assert.Equal("mw-martins-world", galaxy.Factions[works.Owner].TypeId);
            Assert.Single(galaxy.Factions, f => f.TypeId == "mw-martins-world");
        }
    }

    [Fact]
    public void Computer_cores_are_cheap_at_Martins_World()
    {
        var (session, works) = AtTheWorks();
        var cores = Db.Commodities["computer-cores"];
        int cheap = Market.Price(Db, session.Galaxy, works, cores);

        var plain = ModDiscovery.Load(new DirectoryContentSource(ContentTests.DataFolder), [RepoMods]);
        plain.Landmarks["mw-works"].Prices.Clear();
        var galaxy = GalaxyGenerator.Generate(plain, 42);
        int usual = Market.Price(plain, galaxy, galaxy.Systems[works.Id], plain.Commodities["computer-cores"]);
        Assert.True(cheap < usual * 0.8f, $"Cores cost {cheap} at Martins World against {usual} without its prices.");
    }

    [Fact]
    public void The_Martin_Field_is_sold_at_Martins_World_and_nowhere_else()
    {
        var (session, works) = AtTheWorks();
        var offer = Assert.Single(Outfitting.Offers(session, works), o => o.Part.Id == "mw-martin-field");
        Assert.Equal(1500, offer.Price);
        foreach (var other in session.Galaxy.Systems.Where(s => s.IsGateSystem && s.Stations.Count > 0 && s.Id != works.Id).Take(150))
            Assert.DoesNotContain(Outfitting.Offers(session, other), o => o.Part.Id == "mw-martin-field");
    }

    [Fact]
    public void Tally_offers_a_mining_contract_at_Martins_World_only()
    {
        var (session, works) = AtTheWorks();
        var job = Assert.Single(session.Board(), o => o.Mission.Template.Id == "mw-tally-belt");
        Assert.Equal(works.Id, job.DestinationSystemId);
        Assert.StartsWith("mine:", job.Task);
        Assert.Contains("Tally", job.Mission.Briefing + job.Mission.Title);
        Assert.DoesNotContain("{", job.Mission.Briefing);
        Assert.True(job.Mission.Reward >= 10000, $"Tally pays {job.Mission.Reward}, which isn't a good load of money.");

        var elsewhere = session.Galaxy.Systems.First(s => s.IsGateSystem && s.Stations.Count > 0 && s.Id != works.Id);
        session.Career.SystemId = elsewhere.Id;
        Assert.DoesNotContain(session.Board(), o => o.Mission.Template.Id == "mw-tally-belt");
    }

    [Fact]
    public void The_first_orders_give_a_Martin_Field_to_fit_free_and_name_the_League()
    {
        var session = Campaigns.Start(Db, 42, "mw-martins-world");
        var job = Campaigns.TakeOrders(session);
        Assert.NotNull(job);
        Assert.Equal("the League of Time", job!.Offer.Mission.Variables["enemy"]);
        Assert.Contains("mw-martin-field", session.Career.GiftParts);

        // Still there after a save and load.
        session = SaveGame.Load(Db, SaveGame.Save(session));
        Assert.Contains("mw-martin-field", session.Career.GiftParts);

        var gift = Assert.Single(Outfitting.Offers(session, session.Here), o => o.Part.Id == "mw-martin-field" && o.Price == 0);
        int slot = Db.Hulls[session.Career.HullId].Slots.FindIndex(s => s.Type == "shield");
        int credits = session.Career.Credits;
        Assert.Null(Outfitting.CannotFit(session, slot, gift));
        Outfitting.Fit(session, slot, gift);
        Assert.Equal("mw-martin-field", Outfitting.Fitted(session)[slot]!.Id);
        Assert.True(session.Career.Credits >= credits, "Fitting the gift shouldn't cost anything.");
        Assert.Empty(session.Career.GiftParts);
        Assert.DoesNotContain(Outfitting.Offers(session, session.Here), o => o.Price == 0);
    }
}
