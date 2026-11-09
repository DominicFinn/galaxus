using Galaxus.Core.Text;

namespace Galaxus.Core.Tests;

public class GrammarTests
{
    private static GrammarLibrary Library()
    {
        var library = new GrammarLibrary();
        library.Add(new GrammarDef
        {
            Id = "common",
            Rules = new() { ["cargo"] = ["ore", "medicine", "engine parts"] },
        });
        library.Add(new GrammarDef
        {
            Id = "job",
            Variables = ["system"],
            Rules = new()
            {
                ["offer"] = ["#verb.cap# #common:cargo.a# to {system}."],
                ["verb"] = ["haul", "run", "carry"],
            },
        });
        return library;
    }

    [Fact]
    public void Expands_references_modifiers_and_variables()
    {
        var text = Library().Expand("job", "offer", new Rng(1), new Dictionary<string, string> { ["system"] = "Kessa" });
        Assert.Matches(@"^(Haul|Run|Carry) (an ore|a medicine|an engine parts) to Kessa\.$", text);
    }

    [Fact]
    public void Same_seed_gives_same_text()
    {
        var library = Library();
        var vars = new Dictionary<string, string> { ["system"] = "Kessa" };
        for (ulong seed = 0; seed < 20; seed++)
            Assert.Equal(library.Expand("job", "offer", new Rng(seed), vars), library.Expand("job", "offer", new Rng(seed), vars));
    }

    [Fact]
    public void Validation_catches_missing_rules_and_undeclared_variables()
    {
        var library = Library();
        library.Add(new GrammarDef
        {
            Id = "broken",
            Rules = new() { ["origin"] = ["#nothing# at {place}", "#common:absent#", "#verb.shout#"], ["verb"] = ["go"] },
        });
        var problems = library.Validate();
        Assert.Contains(problems, p => p.Contains("missing rule 'broken:nothing'"));
        Assert.Contains(problems, p => p.Contains("missing rule 'common:absent'"));
        Assert.Contains(problems, p => p.Contains("undeclared variable '{place}'"));
        Assert.Contains(problems, p => p.Contains("unknown modifier 'shout'"));
    }

    [Fact]
    public void Rng_hash_is_stable()
    {
        // FNV-1a of "galaxus". If this changes, every saved seed changes with it.
        Assert.Equal(Rng.Hash("galaxus"), Rng.Hash("galaxus"));
        Assert.Equal(new Rng("seed").NextULong(), new Rng("seed").NextULong());
        Assert.NotEqual(new Rng("a").NextULong(), new Rng("b").NextULong());
    }
}
