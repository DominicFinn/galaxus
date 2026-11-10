using System.Globalization;
using System.Text.Json;
using Galaxus.Core.Text;

namespace Galaxus.Core.Content;

/// <summary>Where content files come from. The game reads res://data, tests read the folder on disk.</summary>
public interface IContentSource
{
    /// <summary>Relative paths of the .json files in a folder such as "hulls", including subfolders.</summary>
    IEnumerable<string> ListJsonFiles(string folder);
    string ReadText(string path);
}

public sealed class DirectoryContentSource(string root) : IContentSource
{
    public IEnumerable<string> ListJsonFiles(string folder)
    {
        string dir = Path.Combine(root, folder);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
    }

    public string ReadText(string path) => File.ReadAllText(Path.Combine(root, path));
}

public sealed class ContentException(IReadOnlyList<string> problems)
    : Exception("Content has problems:\n  " + string.Join("\n  ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// Every piece of content in the game, loaded from data files. Each file holds either a
/// single definition object or an array of them, so content can be split however suits.
/// </summary>
public sealed class ContentDatabase
{
    public Dictionary<string, RaceDef> Races { get; } = [];
    public Dictionary<string, BuilderDef> Builders { get; } = [];
    public Dictionary<string, HullDef> Hulls { get; } = [];
    public Dictionary<string, PartDef> Parts { get; } = [];
    public Dictionary<string, CommodityDef> Commodities { get; } = [];
    public Dictionary<string, FactionTypeDef> FactionTypes { get; } = [];
    public Dictionary<string, EventDef> Events { get; } = [];
    public Dictionary<string, MissionTemplateDef> Missions { get; } = [];
    public Dictionary<string, ComplicationDef> Complications { get; } = [];
    public GrammarLibrary Grammars { get; } = new();

    /// <summary>Problems found while loading. Only filled when loaded with throwOnProblems: false.</summary>
    public List<string> Problems { get; } = [];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads and validates everything. Throws ContentException listing every problem found, unless
    /// throwOnProblems is false, in which case the problems are left in Problems (used by tools).
    /// </summary>
    public static ContentDatabase Load(IContentSource source, bool throwOnProblems = true)
    {
        var db = new ContentDatabase();
        var problems = new List<string>();

        LoadInto(source, "races", db.Races, r => r.Id, problems);
        LoadInto(source, "builders", db.Builders, b => b.Id, problems);
        LoadInto(source, "hulls", db.Hulls, h => h.Id, problems);
        LoadInto(source, "parts", db.Parts, p => p.Id, problems);
        LoadInto(source, "commodities", db.Commodities, c => c.Id, problems);
        LoadInto(source, "factions", db.FactionTypes, f => f.Id, problems);
        LoadInto(source, "events", db.Events, e => e.Id, problems);
        LoadInto(source, "missions", db.Missions, m => m.Id, problems);
        LoadInto(source, "complications", db.Complications, c => c.Id, problems);

        var grammars = new Dictionary<string, GrammarDef>();
        LoadInto(source, "grammars", grammars, g => g.Id, problems);
        foreach (var grammar in grammars.Values) db.Grammars.Add(grammar);
        db.Grammars.RegisterAlias(ContentRules.VoiceAlias, ContentRules.MissionVoiceRules);

        problems.AddRange(db.Validate());
        if (problems.Count > 0 && throwOnProblems) throw new ContentException(problems);
        db.Problems.AddRange(problems);
        return db;
    }

    private static void LoadInto<T>(IContentSource source, string folder, Dictionary<string, T> target,
        Func<T, string> idOf, List<string> problems)
    {
        foreach (var path in source.ListJsonFiles(folder))
        {
            List<T> items;
            try
            {
                string text = source.ReadText(path);
                using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
                items = doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement.Deserialize<List<T>>(Json) ?? []
                    : [doc.RootElement.Deserialize<T>(Json)!];
            }
            catch (JsonException e)
            {
                problems.Add($"{path}: invalid JSON ({e.Message})");
                continue;
            }

            foreach (var item in items)
            {
                string id = idOf(item);
                if (string.IsNullOrWhiteSpace(id))
                    problems.Add($"{path}: an entry has no id.");
                else if (!IsValidId(id))
                    problems.Add($"{path}: id '{id}' should be lower-case words joined by hyphens.");
                else if (!target.TryAdd(id, item))
                    problems.Add($"{path}: duplicate id '{id}'.");
            }
        }
    }

    private static bool IsValidId(string id) =>
        id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') && !id.StartsWith('-') && !id.EndsWith('-');

    /// <summary>Rules every race voice grammar must have. The crew system asks for these by name.</summary>
    public static readonly string[] VoiceRules = ["greeting", "hire", "complaint", "praise", "combat", "idle", "leaving"];

    private static readonly HashSet<string> HullClasses = ["courier", "freighter", "fighter", "gunship", "explorer", "miner", "drone"];
    private static readonly HashSet<string> NamedColours = ["hull", "accent", "trim", "glow"];

    public List<string> Validate()
    {
        var p = new List<string>();

        foreach (var race in Races.Values)
        {
            if (race.Kind is not ("core" or "minor")) p.Add($"Race '{race.Id}': kind must be core or minor.");
            if (race.GTolerance is < 1 or > 30) p.Add($"Race '{race.Id}': gTolerance {race.GTolerance} is outside 1 to 30.");
            CheckModifiers($"Race '{race.Id}'", race.CrewModifiers, p);
            CheckGrammar($"Race '{race.Id}'", race.NameGrammar, "name", p);
            CheckGrammar($"Race '{race.Id}'", race.VoiceGrammar, null, p);
            if (!string.IsNullOrEmpty(race.VoiceGrammar))
                foreach (var rule in VoiceRules)
                    CheckGrammar($"Race '{race.Id}' voice", race.VoiceGrammar, rule, p);
        }

        foreach (var builder in Builders.Values)
        {
            if (!Races.ContainsKey(builder.Race)) p.Add($"Builder '{builder.Id}': unknown race '{builder.Race}'.");
            if (builder.Region is not ("core" or "inner" or "outer" or "rim")) p.Add($"Builder '{builder.Id}': region must be core, inner, outer or rim.");
            CheckModifiers($"Builder '{builder.Id}'", builder.StatBias, p);
            CheckGrammar($"Builder '{builder.Id}'", builder.ModelNameGrammar, "model", p);
            foreach (var colour in new[] { builder.Palette.Hull, builder.Palette.Accent, builder.Palette.Trim, builder.Palette.Glow })
                if (!IsHexColour(colour)) p.Add($"Builder '{builder.Id}': palette colour '{colour}' is not #rrggbb.");
        }

        foreach (var hull in Hulls.Values)
        {
            string at = $"Hull '{hull.Id}'";
            if (!Builders.ContainsKey(hull.Builder)) p.Add($"{at}: unknown builder '{hull.Builder}'.");
            if (!HullClasses.Contains(hull.Class)) p.Add($"{at}: class '{hull.Class}' is not one of {string.Join(", ", HullClasses)}.");
            if (hull.Mass <= 0) p.Add($"{at}: mass must be positive.");
            if (hull.BaseSpeed <= 0) p.Add($"{at}: baseSpeed must be positive.");
            if (hull.Radius <= 0) p.Add($"{at}: radius must be positive.");
            if (hull.Model.Count == 0) p.Add($"{at}: model has no shapes.");
            foreach (var slot in hull.Slots)
                if (!PartStats.Required.ContainsKey(slot.Type)) p.Add($"{at}: slot type '{slot.Type}' is unknown.");

            for (int i = 0; i < hull.Model.Count; i++)
            {
                var s = hull.Model[i];
                if (s.Shape != "taperedBox") p.Add($"{at}: model shape {i} has unknown shape '{s.Shape}'.");
                if (s.Position.Length != 3 || s.Back.Length != 2 || s.Front.Length != 2 || s.Skew.Length != 2)
                    p.Add($"{at}: model shape {i} needs position[3], back[2], front[2] and skew[2].");
                if (s.Length <= 0) p.Add($"{at}: model shape {i} needs a positive length.");
                if (!NamedColours.Contains(s.Color) && !IsHexColour(s.Color))
                    p.Add($"{at}: model shape {i} colour '{s.Color}' is not hull, accent, trim, glow or #rrggbb.");
            }

            // The collision sphere should roughly cover the model, or shots pass through visible parts.
            float extent = ModelExtent(hull);
            if (extent > hull.Radius * 1.5f)
                p.Add($"{at}: model reaches {extent:0.0} m from the centre but radius is {hull.Radius} m. Raise radius to at least {extent / 1.5f:0.0}.");

            // The default loadout must fit the slots: one part per slot, matching type, size no bigger than the slot.
            var free = hull.Slots.Select(s => (s.Type, s.Size)).ToList();
            foreach (var partId in hull.DefaultLoadout)
            {
                if (!Parts.TryGetValue(partId, out var part)) { p.Add($"{at}: default loadout part '{partId}' is unknown."); continue; }
                int index = free.FindIndex(s => s.Type == part.Type && s.Size >= part.Size);
                if (index < 0) p.Add($"{at}: no free {part.Type} slot of size {part.Size} or more for '{partId}'.");
                else free.RemoveAt(index);
            }
            foreach (var required in new[] { "engine", "powerPlant" })
                if (!hull.DefaultLoadout.Any(id => Parts.TryGetValue(id, out var part) && part.Type == required))
                    p.Add($"{at}: default loadout has no {required}.");
        }

        foreach (var part in Parts.Values)
        {
            string at = $"Part '{part.Id}'";
            if (!PartStats.Required.TryGetValue(part.Type, out var required)) { p.Add($"{at}: type '{part.Type}' is unknown."); continue; }
            foreach (var stat in required)
                if (!part.Stats.ContainsKey(stat)) p.Add($"{at}: {part.Type} parts need a '{stat}' stat.");
            if (part.Mark is < 0 or > 5) p.Add($"{at}: mark must be 0 to 5.");
            if (part.Size is < 1 or > 5) p.Add($"{at}: size must be 1 to 5.");
            if (!string.IsNullOrEmpty(part.Builder) && !Builders.ContainsKey(part.Builder)) p.Add($"{at}: unknown builder '{part.Builder}'.");
            if (part.Color != null && !IsHexColour(part.Color)) p.Add($"{at}: colour '{part.Color}' is not #rrggbb.");
        }

        ValidateWorld(p);
        p.AddRange(Grammars.Validate());
        return p;
    }

    private void ValidateWorld(List<string> p)
    {
        foreach (var race in Races.Values.Where(r => r.MissionVoiceGrammar != ""))
        {
            foreach (var rule in ContentRules.MissionVoiceRules)
                CheckGrammar($"Race '{race.Id}' mission voice", race.MissionVoiceGrammar, rule, p);
            CheckVariables($"Race '{race.Id}' mission voice", race.MissionVoiceGrammar, ContentRules.MissionVariables, p);
        }

        foreach (var c in Commodities.Values)
        {
            string at = $"Commodity '{c.Id}'";
            if (!ContentRules.CommodityCategories.Contains(c.Category)) p.Add($"{at}: category '{c.Category}' is unknown. Use one of {Join(ContentRules.CommodityCategories)}.");
            if (!ContentRules.Legalities.Contains(c.Legality)) p.Add($"{at}: legality must be legal, restricted or contraband.");
            if (c.BasePrice <= 0) p.Add($"{at}: basePrice must be positive.");
        }

        foreach (var f in FactionTypes.Values)
        {
            string at = $"Faction type '{f.Id}'";
            foreach (var race in f.Races.Where(r => !Races.ContainsKey(r))) p.Add($"{at}: unknown race '{race}'.");
            foreach (var tier in f.Tiers.Where(t => !ContentRules.FactionTiers.Contains(t))) p.Add($"{at}: tier '{tier}' should be minor, regional or great.");
            if (f.Tiers.Count == 0) p.Add($"{at}: needs at least one tier.");
            foreach (var (trait, range) in f.Traits)
            {
                if (!ContentRules.FactionTraits.Contains(trait)) p.Add($"{at}: trait '{trait}' is unknown. Use {Join(ContentRules.FactionTraits)}.");
                if (range.Length != 2 || range[0] < 0 || range[1] > 1 || range[0] > range[1]) p.Add($"{at}: trait '{trait}' needs [min, max] within 0 to 1.");
            }
            CheckGrammar(at, f.NameGrammar, "name", p);
            CheckVariables(at, f.NameGrammar, ContentRules.FactionNameVariables, p);
            if (f.NameGrammar == "") p.Add($"{at}: needs a nameGrammar.");
        }

        foreach (var e in Events.Values)
        {
            string at = $"Event '{e.Id}'";
            if (e.DurationDays.Length != 2 || e.DurationDays[0] <= 0 || e.DurationDays[0] > e.DurationDays[1]) p.Add($"{at}: durationDays needs [min, max] in days.");
            foreach (var category in e.CommodityDemand.Keys.Where(k => !ContentRules.CommodityCategories.Contains(k))) p.Add($"{at}: unknown commodity category '{category}'.");
            if (e.NewsGrammar == "") p.Add($"{at}: needs a newsGrammar.");
            foreach (var rule in ContentRules.EventNewsRules) CheckGrammar(at, e.NewsGrammar, rule, p);
            CheckVariables(at, e.NewsGrammar, ContentRules.EventNewsVariables, p);
        }

        foreach (var c in Complications.Values)
        {
            string at = $"Complication '{c.Id}'";
            if (!ContentRules.ComplicationEffects.Contains(c.Effect)) p.Add($"{at}: effect '{c.Effect}' is unknown. Use one of {Join(ContentRules.ComplicationEffects)}.");
            if (c.Weight <= 0) p.Add($"{at}: weight must be positive.");
            if (c.TextGrammar == "") p.Add($"{at}: needs a textGrammar.");
            foreach (var rule in ContentRules.ComplicationTextRules) CheckGrammar(at, c.TextGrammar, rule, p);
            CheckVariables(at, c.TextGrammar, ContentRules.MissionVariables, p);
        }

        foreach (var m in Missions.Values)
        {
            string at = $"Mission '{m.Id}'";
            if (!ContentRules.MissionTypes.Contains(m.Type)) p.Add($"{at}: type '{m.Type}' is unknown. Use one of {Join(ContentRules.MissionTypes)}.");
            if (!ContentRules.MissionScales.Contains(m.Scale)) p.Add($"{at}: scale must be small, skirmish, battle or war.");
            if (m.MinRank is < 0 or > 4) p.Add($"{at}: minRank must be 0 to 4.");
            if (m.BaseReward <= 0) p.Add($"{at}: baseReward must be positive.");
            foreach (var giver in m.Givers.Where(g => !FactionTypes.ContainsKey(g))) p.Add($"{at}: unknown giver faction type '{giver}'.");
            foreach (var ev in m.Events.Where(e => !Events.ContainsKey(e))) p.Add($"{at}: unknown event '{ev}'.");
            foreach (var category in m.CargoCategories.Where(c => !ContentRules.CommodityCategories.Contains(c))) p.Add($"{at}: unknown cargo category '{category}'.");
            foreach (var category in m.CargoCategories.Where(c => !Commodities.Values.Any(x => x.Category == c)))
                p.Add($"{at}: no commodity has category '{category}' yet.");
            foreach (var comp in m.Complications.Where(c => !Complications.ContainsKey(c))) p.Add($"{at}: unknown complication '{comp}'.");
            foreach (var next in m.FollowUps.Where(f => !Missions.ContainsKey(f))) p.Add($"{at}: unknown follow-up mission '{next}'.");
            if (m.TextGrammar == "") p.Add($"{at}: needs a textGrammar.");
            foreach (var rule in ContentRules.MissionTextRules) CheckGrammar(at, m.TextGrammar, rule, p);
            CheckVariables(at, m.TextGrammar, ContentRules.MissionVariables, p);
        }

        if (Missions.Count > 0)
            foreach (var place in ContentRules.PlaceGrammars) CheckGrammar("Galaxy", place, "name", p);
    }

    /// <summary>A grammar may only declare variables the game will actually supply.</summary>
    private void CheckVariables(string owner, string grammarId, IEnumerable<string> allowed, List<string> p)
    {
        var grammar = Grammars.All.FirstOrDefault(g => g.Id == grammarId);
        if (grammar == null) return;
        var allowedSet = allowed.ToHashSet();
        foreach (var variable in grammar.Variables.Where(v => !allowedSet.Contains(v)))
            p.Add($"{owner}: grammar '{grammarId}' declares {{{variable}}}, which the game doesn't supply. Available: {Join(allowedSet)}.");
    }

    private static string Join(IEnumerable<string> items) => string.Join(", ", items.Order());

    /// <summary>Furthest any shape corner gets from the ship's centre, in metres. Slightly generous for rolled shapes.</summary>
    public static float ModelExtent(HullDef hull)
    {
        float extent = 0;
        foreach (var s in hull.Model.Where(s => s.Position.Length == 3 && s.Back.Length == 2 && s.Front.Length == 2 && s.Skew.Length == 2))
        {
            float half = s.Length / 2;
            float backReach = new System.Numerics.Vector3(MathF.Abs(s.Position[0]), MathF.Abs(s.Position[1]), MathF.Abs(s.Position[2] + half)).Length()
                              + new System.Numerics.Vector2(s.Back[0], s.Back[1]).Length() / 2;
            float frontReach = new System.Numerics.Vector3(MathF.Abs(s.Position[0] + s.Skew[0]), MathF.Abs(s.Position[1] + s.Skew[1]), MathF.Abs(s.Position[2] - half)).Length()
                               + new System.Numerics.Vector2(s.Front[0], s.Front[1]).Length() / 2;
            extent = MathF.Max(extent, MathF.Max(backReach, frontReach));
        }
        return extent;
    }

    private void CheckGrammar(string owner, string grammarId, string? requiredRule, List<string> p)
    {
        if (string.IsNullOrEmpty(grammarId)) return;
        var grammar = Grammars.All.FirstOrDefault(g => g.Id == grammarId);
        if (grammar == null) p.Add($"{owner}: unknown grammar '{grammarId}'.");
        else if (requiredRule != null && !grammar.Rules.ContainsKey(requiredRule))
            p.Add($"{owner}: grammar '{grammarId}' needs a '{requiredRule}' rule.");
    }

    private static void CheckModifiers(string owner, Dictionary<string, float> modifiers, List<string> p)
    {
        foreach (var key in modifiers.Keys.Where(k => !StatKeys.All.Contains(k)))
            p.Add($"{owner}: unknown stat '{key}'. Known stats: {string.Join(", ", StatKeys.All)}.");
    }

    public static bool IsHexColour(string value) =>
        value.Length == 7 && value[0] == '#' &&
        int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
}
