using System.Text.Json;
using System.Text.RegularExpressions;
using Galaxus.Core;
using Galaxus.Core.Content;
using Galaxus.Core.Text;

// Checks mods against the base game and prints readable problems.
//   ModCheck DATA_FOLDER MODS_FOLDER            checks every enabled mod in the folder, together
//   ModCheck DATA_FOLDER MODS_FOLDER MOD_NAME   checks one mod, even if it is switched off
// Add --samples (or --samples=N) to print N expansions of every grammar the mods add or extend,
// so new text can be read back. Mission voices rotate through the races.
// Add --grammar=ID[,ID...] to print samples of particular grammars instead, core or mod.
int samples = 0;
var sampleArg = args.FirstOrDefault(a => a == "--samples" || a.StartsWith("--samples="));
if (sampleArg != null) samples = sampleArg == "--samples" ? 3 : int.Parse(sampleArg["--samples=".Length..]);
var grammarArg = args.FirstOrDefault(a => a.StartsWith("--grammar="));
var onlyGrammars = grammarArg?["--grammar=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
if (onlyGrammars != null && samples == 0) samples = 3;
args = args.Where(a => a != sampleArg && a != grammarArg).ToArray();

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: ModCheck DATA_FOLDER MODS_FOLDER [MOD_FOLDER_NAME] [--samples[=N]] [--grammar=ID,...]");
    return 2;
}

string dataFolder = args[0], modsFolder = args[1];
var problems = new List<string>();
List<ModSource> mods;

if (args.Length >= 3)
{
    string dir = Path.Combine(modsFolder, args[2]);
    if (!Directory.Exists(dir))
    {
        Console.Error.WriteLine($"There is no mod folder at {dir}.");
        return 2;
    }
    var mod = ModDiscovery.Open(dir, problems);
    mods = mod == null ? [] : [mod];
    if (mod is { Info.Enabled: false }) Console.WriteLine($"Note: '{mod.Name}' is switched off in mod.json. Checking it anyway.");
}
else
{
    mods = ModDiscovery.Find([modsFolder], problems);
}

if (mods.Count == 0 && problems.Count == 0 && onlyGrammars == null)
{
    Console.WriteLine($"No enabled mods found in {modsFolder}.");
    return 0;
}

var db = ContentDatabase.Load(new DirectoryContentSource(dataFolder), mods, throwOnProblems: false);
problems.AddRange(db.Problems);

foreach (var mod in mods)
{
    Console.WriteLine($"Checked mod: {mod.Name}" + (mod.Info.Version == "" ? "" : $" (version {mod.Info.Version})"));
}

if (problems.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"{problems.Count} problem{(problems.Count == 1 ? "" : "s")} found:");
    foreach (var problem in problems) Console.WriteLine($"  - {problem}");
    return 1;
}

Console.WriteLine("No problems found.");
if (onlyGrammars != null) return Samples.PrintGrammars(db, onlyGrammars, samples) ? 0 : 1;
if (samples > 0) Samples.Print(db, ContentDatabase.Load(new DirectoryContentSource(dataFolder), throwOnProblems: false), mods, samples);
return 0;

static class Samples
{
    // Stand-ins for the values the game fills in, so samples read like real text.
    private static readonly Dictionary<string, string> Values = new()
    {
        ["captain"] = "Captain Hale", ["client"] = "Mara Quent", ["faction"] = "Lamp Ferry Cooperative",
        ["system"] = "Vesper 12", ["destination"] = "Halden Station", ["cargo"] = "medical supplies",
        ["tonnes"] = "24", ["reward"] = "4,250 cr", ["target"] = "the Grey Wake", ["deadline"] = "6 days",
        ["place"] = "Corran", ["founder"] = "Ada Venn", ["race"] = "human", ["ship"] = "Long Patience",
        ["crewmate"] = "Tam", ["rival"] = "the Hesper Directorate",
    };

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex Reference = new(@"#([^#\s]+)#");

    private static int _voiceTurn;

    /// <summary>Prints samples of named grammars, core or mod. Returns false if one doesn't exist.</summary>
    public static bool PrintGrammars(ContentDatabase db, List<string> ids, int count)
    {
        bool ok = true;
        foreach (var id in ids)
        {
            if (!db.Grammars.Contains(id)) { Console.WriteLine($"There is no grammar '{id}'."); ok = false; continue; }
            Console.WriteLine();
            Console.WriteLine($"  {id}");
            Show(db, db.Grammars.Get(id), extends: false, count);
        }
        return ok;
    }

    public static void Print(ContentDatabase db, ContentDatabase baseGame, List<ModSource> mods, int count)
    {
        foreach (var mod in mods)
        {
            Console.WriteLine();
            Console.WriteLine($"Samples from {mod.Name}:");
            foreach (var path in mod.Source.ListJsonFiles("grammars"))
            {
                foreach (var g in Read(mod.Source.ReadText(path)))
                {
                    if (!db.Grammars.Contains(g.Id)) continue;
                    bool extends = baseGame.Grammars.Contains(g.Id);
                    Console.WriteLine();
                    Console.WriteLine($"  {g.Id}" + (extends ? " (adds to an existing grammar; showing the new options)" : ""));
                    Show(db, g, extends, count);
                }
            }
        }
    }

    // g is the grammar as written (for an extension, only the options it adds).
    private static void Show(ContentDatabase db, GrammarDef g, bool extends, int count)
    {
        var voices = db.Races.Values
            .Where(r => r.MissionVoiceGrammar != "" && db.Grammars.Contains(r.MissionVoiceGrammar))
            .OrderBy(r => r.Id).ToList();
        foreach (var rule in RulesToShow(g, extends))
        {
            List<string?> options = extends ? [.. g.Rules[rule].Take(count)] : [.. Enumerable.Repeat<string?>(null, count)];
            for (int i = 0; i < options.Count; i++)
            {
                var race = voices.Count == 0 ? null : voices[_voiceTurn++ % voices.Count];
                var aliases = race == null ? null : new Dictionary<string, string> { [ContentRules.VoiceAlias] = race.MissionVoiceGrammar };
                var rng = new Rng($"{g.Id}/{rule}/{i}");
                string text;
                try { text = options[i] == null ? db.Grammars.Expand(g.Id, rule, rng, Values, aliases) : ExpandOption(db, g.Id, rule, options[i]!, rng, aliases); }
                catch (Exception e) { text = $"(could not expand: {e.Message})"; }
                bool voiced = (options[i] ?? string.Join(" ", db.Grammars.Get(g.Id).Rules[rule])).Contains("#voice:");
                Console.WriteLine($"    {rule}{(voiced && race != null ? $" [{race.Name} voice]" : "")}: {text}");
            }
        }
    }

    // A new grammar shows its top-level rules (the ones nothing else in it refers to).
    // An extension shows every rule it adds options to.
    private static IEnumerable<string> RulesToShow(GrammarDef g, bool extends)
    {
        if (extends) return g.Rules.Keys;
        var referenced = g.Rules.Values.SelectMany(o => o)
            .SelectMany(o => Reference.Matches(o).Select(m => m.Groups[1].Value))
            .Where(r => !r.Contains(':'))
            .Select(r => r.Split('.')[0])
            .ToHashSet();
        var top = g.Rules.Keys.Where(r => !referenced.Contains(r)).ToList();
        return top.Count > 0 ? top : g.Rules.Keys;
    }

    // Expands one particular option of a rule by briefly narrowing the rule to that option.
    private static string ExpandOption(ContentDatabase db, string grammarId, string rule, string option, Rng rng, Dictionary<string, string>? aliases)
    {
        var merged = db.Grammars.Get(grammarId);
        var narrowed = new GrammarDef { Id = merged.Id, Description = merged.Description, Variables = merged.Variables, Rules = new(merged.Rules) { [rule] = [option] } };
        db.Grammars.Add(narrowed);
        try { return db.Grammars.Expand(grammarId, rule, rng, Values, aliases); }
        finally { db.Grammars.Add(merged); }
    }

    private static List<GrammarDef> Read(string text)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.Deserialize<List<GrammarDef>>(Json) ?? []
            : [doc.RootElement.Deserialize<GrammarDef>(Json)!];
    }
}
