using Galaxus.Core.Content;
using Galaxus.Core.Missions;

namespace Galaxus.Core.Tests;

/// <summary>Generates every mission, complication, news line and name, so broken text fails the tests.</summary>
public class MissionTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    private static MissionContext Context(string race, string? eventId = null) =>
        new(new FactionInfo("Test Holdings", Db.FactionTypes.Keys.Order().FirstOrDefault() ?? "any", race),
            "Kessa", "Halven Ring", "Ashby", Rank: 4, EventId: eventId);

    private static void AssertFilled(string text, string what)
    {
        Assert.False(string.IsNullOrWhiteSpace(text), $"{what} is empty");
        Assert.DoesNotContain("{", text);
        Assert.DoesNotContain("#", text);
    }

    [Fact]
    public void Every_mission_expands_in_every_race_voice()
    {
        foreach (var template in Db.Missions.Values)
        foreach (var race in Db.Races.Keys)
        for (ulong seed = 0; seed < 10; seed++)
        {
            var mission = MissionGenerator.Build(Db, template, Context(race), new Rng(seed));
            string what = $"{template.Id} ({race}, seed {seed})";
            AssertFilled(mission.Title, what + " title");
            AssertFilled(mission.Briefing, what + " briefing");
            AssertFilled(mission.SuccessText, what + " success");
            AssertFilled(mission.FailureText, what + " failure");
            Assert.True(mission.Reward > 0);
            if (template.CargoCategories.Count > 0) Assert.Contains(mission.Cargo!.Category, template.CargoCategories);
        }
    }

    [Fact]
    public void Every_complication_expands_on_every_job_that_uses_it()
    {
        foreach (var template in Db.Missions.Values)
        foreach (var complication in template.Complications.Select(id => Db.Complications[id]))
        for (ulong seed = 0; seed < 10; seed++)
        {
            var mission = MissionGenerator.Build(Db, template, Context("human"), new Rng(seed), complication);
            AssertFilled(mission.ComplicationReveal!, $"{complication.Id} on {template.Id} reveal");
            AssertFilled(mission.ComplicationAftermath!, $"{complication.Id} on {template.Id} aftermath");
        }
    }

    [Fact]
    public void Every_event_news_line_expands()
    {
        var vars = new Dictionary<string, string> { ["system"] = "Kessa", ["faction"] = "Test Holdings", ["rival"] = "Other Holdings" };
        foreach (var ev in Db.Events.Values)
        foreach (var rule in ContentRules.EventNewsRules)
        for (ulong seed = 0; seed < 20; seed++)
            AssertFilled(Db.Grammars.Expand(ev.NewsGrammar, rule, new Rng(seed), vars), $"{ev.Id} {rule}");
    }

    [Fact]
    public void Every_faction_and_place_name_expands()
    {
        var vars = new Dictionary<string, string> { ["place"] = "Kessa", ["founder"] = "Ashby", ["race"] = "human" };
        foreach (var faction in Db.FactionTypes.Values)
        for (ulong seed = 0; seed < 30; seed++)
            AssertFilled(Db.Grammars.Expand(faction.NameGrammar, "name", new Rng(seed), vars), faction.Id);
        foreach (var place in ContentRules.PlaceGrammars.Where(Db.Grammars.Contains))
        for (ulong seed = 0; seed < 30; seed++)
            AssertFilled(Db.Grammars.Expand(place, "name", new Rng(seed)), place);
    }

    [Fact]
    public void Every_event_creates_its_own_work()
    {
        // For each event, a giver that one of its templates accepts should be offered that work.
        foreach (var ev in Db.Missions.Values.SelectMany(m => m.Events).Distinct())
        {
            var template = Db.Missions.Values.First(m => m.Events.Contains(ev));
            var giverType = template.Givers.FirstOrDefault() ?? "any";
            var raceId = Db.FactionTypes.TryGetValue(giverType, out var type) && type.Races.Count > 0 ? type.Races[0] : "human";
            var context = Context(raceId, ev) with { Giver = new FactionInfo("Test", giverType, raceId) };

            bool offered = false;
            for (ulong seed = 0; seed < 300 && !offered; seed++)
                offered = MissionGenerator.Generate(Db, context, new Rng(seed))?.Template.Events.Contains(ev) == true;
            Assert.True(offered, $"No missions came from event '{ev}' for giver type '{giverType}'.");
        }
    }

    [Fact]
    public void Same_seed_gives_the_same_mission()
    {
        var a = MissionGenerator.Generate(Db, Context("human"), new Rng(42));
        var b = MissionGenerator.Generate(Db, Context("human"), new Rng(42));
        Assert.Equal(a?.Briefing, b?.Briefing);
        Assert.Equal(a?.Reward, b?.Reward);
    }
}
