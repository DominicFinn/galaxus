using Galaxus.Core.Content;

namespace Galaxus.Core.Tests;

public class ModTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "galaxus-mod-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static string RepoMods
    {
        get
        {
            var data = new DirectoryInfo(ContentTests.DataFolder);
            return Path.Combine(data.Parent!.Parent!.FullName, "mods");
        }
    }

    private void Write(string mod, string file, string json)
    {
        string path = Path.Combine(_root, mod, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private ContentDatabase Load(bool throwOnProblems = true) =>
        ModDiscovery.Load(new DirectoryContentSource(ContentTests.DataFolder), [_root], throwOnProblems);

    private const string Faction = """
        { "id": "{ID}", "name": "Test", "description": "x", "tiers": ["minor"],
          "traits": { "greed": [0.1, 0.5] }, "nameGrammar": "test-names" }
        """;

    private void WriteFaction(string mod, string id)
    {
        Write(mod, "factions/f.json", Faction.Replace("{ID}", id));
        Write(mod, "grammars/names.json", """{ "id": "test-names", "variables": [], "rules": { "name": ["Test Outfit"] } }""");
    }

    [Fact]
    public void A_mod_adds_content_to_the_base_game()
    {
        WriteFaction("good-mod", "good-mod-faction");
        var db = Load();
        Assert.Contains("good-mod-faction", db.FactionTypes.Keys);
        Assert.Contains("good-mod", db.LoadedMods);
        Assert.Contains("mission-voice-human", db.Grammars.All.Select(g => g.Id));
    }

    [Fact]
    public void An_id_clash_with_base_content_is_rejected_and_names_the_mod_and_file()
    {
        Write("clashing", "factions/f.json", Faction.Replace("{ID}", "mining-corporation"));
        Write("clashing", "grammars/names.json", """{ "id": "test-names", "variables": [], "rules": { "name": ["Test Outfit"] } }""");
        var e = Assert.Throws<ContentException>(() => Load());
        string message = string.Join("\n", e.Problems);
        Assert.Contains("Mod 'clashing'", message);
        Assert.Contains("factions/f.json", message);
        Assert.Contains("'mining-corporation'", message);
        Assert.Contains("can add content but not replace it", message);
    }

    [Fact]
    public void Two_mods_cannot_share_an_id()
    {
        WriteFaction("a-mod", "shared-id");
        Write("b-mod", "factions/f.json", Faction.Replace("{ID}", "shared-id"));
        var e = Assert.Throws<ContentException>(() => Load());
        Assert.Contains(e.Problems, p => p.StartsWith("Mod 'b-mod'") && p.Contains("shared-id"));
        Assert.DoesNotContain(e.Problems, p => p.StartsWith("Mod 'a-mod'"));
    }

    [Fact]
    public void Problems_inside_mod_content_name_the_mod()
    {
        Write("broken", "factions/f.json", Faction.Replace("{ID}", "broken-faction").Replace("test-names", "no-such-grammar"));
        var e = Assert.Throws<ContentException>(() => Load());
        Assert.Contains(e.Problems, p => p.StartsWith("Mod 'broken', file 'factions/f.json'") && p.Contains("no-such-grammar"));
    }

    [Fact]
    public void Invalid_json_names_the_mod_and_file()
    {
        Write("typo", "events/e.json", "{ \"id\": \"x\", ");
        var e = Assert.Throws<ContentException>(() => Load());
        Assert.Contains(e.Problems, p => p.Contains("Mod 'typo', file 'events/e.json'") && p.Contains("invalid JSON"));
    }

    [Fact]
    public void A_mod_can_add_options_and_rules_to_an_existing_grammar()
    {
        var before = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));
        int options = before.Grammars.All.First(g => g.Id == "news-trade-boom").Rules["ongoing"].Count;
        Write("flavour", "grammars/g.json", """
            { "id": "news-trade-boom", "variables": ["system"],
              "rules": { "ongoing": ["Extra line about {system}."], "extra-rule": ["Something new."] } }
            """);
        var db = Load();
        var grammar = db.Grammars.All.First(g => g.Id == "news-trade-boom");
        Assert.Equal(options + 1, grammar.Rules["ongoing"].Count);
        Assert.Contains("Extra line about {system}.", grammar.Rules["ongoing"]);
        Assert.Contains("extra-rule", grammar.Rules.Keys);
        Assert.Contains("start", grammar.Rules.Keys);
    }

    [Fact]
    public void A_grammar_extension_cannot_use_variables_the_grammar_lacks()
    {
        Write("flavour", "grammars/g.json", """
            { "id": "news-trade-boom", "variables": ["system", "banana"], "rules": { "ongoing": ["A {banana}."] } }
            """);
        var e = Assert.Throws<ContentException>(() => Load());
        Assert.Contains(e.Problems, p => p.Contains("Mod 'flavour'") && p.Contains("banana"));
    }

    [Fact]
    public void Mods_cannot_add_great_powers()
    {
        Write("greedy", "powers/p.json", """{ "id": "new-power" }""");
        var e = Assert.Throws<ContentException>(() => Load());
        Assert.Contains(e.Problems, p => p.Contains("Mod 'greedy'") && p.Contains("great powers"));
    }

    [Fact]
    public void Disabled_mods_are_skipped()
    {
        WriteFaction("off-mod", "off-mod-faction");
        Write("off-mod", "mod.json", """{ "name": "Off", "enabled": false }""");
        // This would be an error if it were loaded.
        Write("off-mod", "events/e.json", "not json at all");
        var db = Load();
        Assert.DoesNotContain("off-mod-faction", db.FactionTypes.Keys);
        Assert.Empty(db.LoadedMods);
    }

    [Fact]
    public void Mods_load_in_alphabetical_order_and_use_the_name_from_mod_json()
    {
        WriteFaction("b-second", "second-faction");
        Write("b-second", "mod.json", """{ "name": "Second Mod" }""");
        Write("a-first", "factions/f.json", Faction.Replace("{ID}", "first-faction"));
        var db = Load();
        Assert.Equal(["a-first", "Second Mod"], db.LoadedMods);
    }

    [Fact]
    public void The_example_mod_is_valid_and_adds_content()
    {
        string example = Path.Combine(RepoMods, "example-free-traders");
        Assert.True(Directory.Exists(example), "mods/example-free-traders is missing.");
        var db = ModDiscovery.Load(new DirectoryContentSource(ContentTests.DataFolder), [RepoMods]);
        Assert.Contains("eft-lane-cooperative", db.FactionTypes.Keys);
        Assert.Contains("eft-toll-relief", db.Missions.Keys);
        Assert.Contains("eft-toll-dispute", db.Events.Keys);
        // The mod's text expands without leaving anything unresolved.
        foreach (var mission in db.Missions.Values.Where(m => m.Id.StartsWith("eft-")))
            foreach (var rule in ContentRules.MissionTextRules)
            {
                string text = db.Grammars.Expand(mission.TextGrammar, rule, new Rng(7), aliases: new Dictionary<string, string> { [ContentRules.VoiceAlias] = "mission-voice-human" });
                Assert.DoesNotContain('#', text);
            }
    }

    [Fact]
    public void The_example_missions_generate_in_every_race_voice()
    {
        var db = ModDiscovery.Load(new DirectoryContentSource(ContentTests.DataFolder), [RepoMods]);
        foreach (var template in db.Missions.Values.Where(m => m.Id.StartsWith("eft-")))
        foreach (var race in db.Races.Keys)
        for (ulong seed = 0; seed < 10; seed++)
        {
            var context = new Galaxus.Core.Missions.MissionContext(
                new Galaxus.Core.Missions.FactionInfo("Test Co-op", template.Givers[0], race), "Kessa", "Halven Ring", "Ashby", Rank: 4);
            var mission = Galaxus.Core.Missions.MissionGenerator.Build(db, template, context, new Rng(seed));
            foreach (var text in new[] { mission.Title, mission.Briefing, mission.SuccessText, mission.FailureText })
            {
                Assert.False(string.IsNullOrWhiteSpace(text));
                Assert.DoesNotContain("{", text);
                Assert.DoesNotContain("#", text);
            }
        }
    }

    [Fact]
    public void Shipped_mods_never_break_the_base_game()
    {
        // Every folder under mods/ must load cleanly together, as long as it is enabled.
        var db = ModDiscovery.Load(new DirectoryContentSource(ContentTests.DataFolder), [RepoMods], throwOnProblems: false);
        Assert.Empty(db.Problems);
    }
}
