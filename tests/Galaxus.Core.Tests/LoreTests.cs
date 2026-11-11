using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Lore;
using Galaxus.Core.Mining;
using Galaxus.Core.People;

namespace Galaxus.Core.Tests;

/// <summary>Lore from gossip, clue chains that lead to treasure, and the logbook's records.</summary>
public class LoreTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    [Fact]
    public void Every_clue_chain_has_a_place_and_reads_cleanly()
    {
        var session = Session.StartNew(Db, 51);
        Assert.NotEmpty(Db.Clues);
        foreach (var clue in Db.Clues.Values)
        {
            var target = Tales.Target(session, clue);
            Assert.NotNull(target);
            Assert.Equal(target, Tales.Target(session, clue));
            Assert.Contains(target!.Value.System.Region, clue.Regions);
            for (int i = 0; i < 3; i++) Assert.DoesNotContain("{", Tales.Fragment(session, clue, i));
        }
    }

    [Fact]
    public void Three_pieces_lead_to_a_cache_you_can_scan()
    {
        var session = Session.StartNew(Db, 52);
        var clue = Db.Clues.Values.First();
        for (int i = 0; i < 3; i++) Assert.NotNull(Tales.Advance(session, clue));
        Assert.Null(Tales.Advance(session, clue));
        var trail = Assert.Single(session.Career.Trails);
        Assert.Equal(clue.Payoff, trail.CacheId);

        session.Career.SystemId = trail.SystemId;
        var solar = SolarSystems.For(session.Here, Db.Grammars);
        int credits = session.Career.Credits;
        var result = Scanning.Body(session, solar, trail.Body);
        Assert.Equal(clue.Payoff, result.Find?.FindId);
        Assert.Empty(session.Career.Trails);
        Assert.True(session.Career.Credits > credits || session.Career.Cargo.Count > 0);
    }

    [Fact]
    public void Friendly_gossip_hands_over_lore_and_clues()
    {
        var session = Session.StartNew(Db, 53);
        int told = 0;
        for (int i = 0; i < 60; i++)
        {
            var person = PeopleDirectory.Make(Db, session.Galaxy, $"tester-{i}", session.Here, null, regular: false);
            session.Career.Disposition[person.Id] = 40;
            if (Tales.FromGossip(session, person, 40, 1) is { } line) { told++; Assert.DoesNotContain("{", line); }
        }
        Assert.True(told > 10, $"Only {told} tales in 60 friendly conversations.");
        Assert.NotEmpty(session.Career.Lore);
        Assert.All(session.Career.Lore, l => Assert.True(Db.Lore.ContainsKey(l.Id)));
        Assert.Equal(session.Career.Lore.Count, session.Career.Lore.Select(l => l.Id).Distinct().Count());
    }

    [Fact]
    public void Lore_and_clues_survive_a_save()
    {
        var session = Session.StartNew(Db, 54);
        session.Career.Lore.Add(new LoreRecord(Db.Lore.Keys.First(), 2, session.Career.SystemId, "Someone"));
        Tales.Advance(session, Db.Clues.Values.First());
        session.Career.NewsReadDay = 9;
        var loaded = SaveGame.Load(Db, SaveGame.Save(session));
        Assert.Equal(session.Career.Lore, loaded.Career.Lore);
        Assert.Equal(session.Career.Clues, loaded.Career.Clues);
        Assert.Equal(9, loaded.Career.NewsReadDay);
    }
}
