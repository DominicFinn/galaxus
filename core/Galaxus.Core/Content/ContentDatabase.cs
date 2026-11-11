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
    /// <summary>Things a scan can turn up: wrecks, beacons, seams, caches and leads.</summary>
    public Dictionary<string, FindDef> Finds { get; } = [];
    public Dictionary<string, BattleDef> Battles { get; } = [];
    public Dictionary<string, CampaignDef> Campaigns { get; } = [];
    public Dictionary<string, LandmarkDef> Landmarks { get; } = [];
    /// <summary>Mods that had problems and were left out, with why.</summary>
    public List<string> SkippedMods { get; } = [];
    public Dictionary<string, PowerDef> Powers { get; } = [];
    public Dictionary<string, Audio.MusicStyleDef> MusicStyles { get; } = [];
    /// <summary>Face pools, keyed by race id.</summary>
    public Dictionary<string, Portraits.PortraitStyleDef> Portraits { get; } = [];
    public Dictionary<string, People.RoleDef> Roles { get; } = [];
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
    public static ContentDatabase Load(IContentSource source, bool throwOnProblems = true) =>
        Load(source, [], throwOnProblems);

    /// <summary>Names of the mods that were loaded, in load order.</summary>
    public List<string> LoadedMods { get; } = [];

    /// <summary>
    /// Loads the base content, then adds each mod's content in the order given (callers pass them
    /// alphabetically). Mods can add content but not replace it. Problems name the mod and file.
    /// </summary>
    public static ContentDatabase Load(IContentSource source, IReadOnlyList<ModSource> mods, bool throwOnProblems = true)
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
        LoadInto(source, "finds", db.Finds, f => f.Id, problems);
        LoadInto(source, "battles", db.Battles, b => b.Id, problems);
        LoadInto(source, "campaigns", db.Campaigns, c => c.Id, problems);
        LoadInto(source, "landmarks", db.Landmarks, l => l.Id, problems);
        LoadInto(source, "powers", db.Powers, p => p.Id, problems);
        LoadInto(source, "music", db.MusicStyles, m => m.Id, problems);
        LoadInto(source, "portraits", db.Portraits, p => p.Race, problems);
        LoadInto(source, "people", db.Roles, r => r.Id, problems);

        var grammars = new Dictionary<string, GrammarDef>();
        LoadInto(source, "grammars", grammars, g => g.Id, problems);
        db.Grammars.RegisterAlias(ContentRules.VoiceAlias, ContentRules.MissionVoiceRules);

        var origins = new Dictionary<string, string>();
        foreach (var mod in mods)
        {
            db.LoadedMods.Add(mod.Name);
            db.AddMod(mod, grammars, origins, problems);
        }
        foreach (var grammar in grammars.Values) db.Grammars.Add(grammar);

        var validation = db.Validate();
        problems.AddRange(validation.Select(v => AttributeToMod(v, origins)));
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

    /// <summary>Folders a mod may add to. Powers are left out: the great powers belong to the base game.</summary>
    public static readonly string[] ModFolders =
        ["races", "builders", "hulls", "parts", "commodities", "factions", "events", "missions", "complications", "finds", "battles", "campaigns", "landmarks", "music", "portraits", "people", "grammars"];

    private static readonly System.Text.RegularExpressions.Regex OwnerPattern =
        new(@"^(?:Grammar|[A-Z][A-Za-z]*(?: [a-z]+)?) '([^']+)'", System.Text.RegularExpressions.RegexOptions.Compiled);

    private void AddMod(ModSource mod, Dictionary<string, GrammarDef> grammars, Dictionary<string, string> origins, List<string> problems)
    {
        string at = $"Mod '{mod.Name}'";
        foreach (var path in mod.Source.ListJsonFiles("powers"))
            problems.Add($"{at}, file '{path}': mods can't add great powers. Remove the powers folder; use a minor faction type instead.");

        AddFromMod(mod, "races", Races, r => r.Id, origins, problems);
        AddFromMod(mod, "builders", Builders, b => b.Id, origins, problems);
        AddFromMod(mod, "hulls", Hulls, h => h.Id, origins, problems);
        AddFromMod(mod, "parts", Parts, x => x.Id, origins, problems);
        AddFromMod(mod, "commodities", Commodities, c => c.Id, origins, problems);
        AddFromMod(mod, "factions", FactionTypes, f => f.Id, origins, problems);
        AddFromMod(mod, "events", Events, e => e.Id, origins, problems);
        AddFromMod(mod, "missions", Missions, m => m.Id, origins, problems);
        AddFromMod(mod, "complications", Complications, c => c.Id, origins, problems);
        AddFromMod(mod, "finds", Finds, f => f.Id, origins, problems);
        AddFromMod(mod, "battles", Battles, b => b.Id, origins, problems);
        AddFromMod(mod, "campaigns", Campaigns, c => c.Id, origins, problems);
        AddFromMod(mod, "landmarks", Landmarks, l => l.Id, origins, problems);
        AddFromMod(mod, "music", MusicStyles, m => m.Id, origins, problems);
        AddFromMod(mod, "portraits", Portraits, x => x.Race, origins, problems);
        AddFromMod(mod, "people", Roles, r => r.Id, origins, problems);

        // Grammars: new ids are added. An existing id may only gain rules and extra options.
        foreach (var path in mod.Source.ListJsonFiles("grammars"))
        {
            foreach (var g in ReadFile<GrammarDef>(mod.Source, path, problems, $"{at}, file '{path}'"))
            {
                string where = $"{at}, file '{path}'";
                if (string.IsNullOrWhiteSpace(g.Id)) { problems.Add($"{where}: a grammar has no id."); continue; }
                if (!IsValidId(g.Id)) { problems.Add($"{where}: id '{g.Id}' should be lower-case words joined by hyphens."); continue; }
                if (!grammars.TryGetValue(g.Id, out var existing))
                {
                    grammars[g.Id] = g;
                    origins[g.Id] = where;
                    continue;
                }
                foreach (var v in g.Variables.Where(v => !existing.Variables.Contains(v)))
                    problems.Add($"{where}: grammar '{g.Id}' declares {{{v}}}, but the grammar it extends doesn't supply that variable.");
                foreach (var (rule, options) in g.Rules)
                {
                    if (!existing.Rules.TryGetValue(rule, out var list)) existing.Rules[rule] = list = [];
                    // Repeats are allowed on purpose: they make an option more likely.
                    list.AddRange(options);
                }
                origins.TryGetValue("extended:" + g.Id, out var by);
                origins["extended:" + g.Id] = by == null ? mod.Name : by + ", " + mod.Name;
            }
        }
    }

    private static void AddFromMod<T>(ModSource mod, string folder, Dictionary<string, T> target, Func<T, string> idOf,
        Dictionary<string, string> origins, List<string> problems)
    {
        foreach (var path in mod.Source.ListJsonFiles(folder))
        {
            string where = $"Mod '{mod.Name}', file '{path}'";
            foreach (var item in ReadFile<T>(mod.Source, path, problems, where))
            {
                string id = idOf(item);
                if (string.IsNullOrWhiteSpace(id)) problems.Add($"{where}: an entry has no id.");
                else if (!IsValidId(id)) problems.Add($"{where}: id '{id}' should be lower-case words joined by hyphens.");
                else if (target.ContainsKey(id))
                    problems.Add($"{where}: id '{id}' already exists in the base game or an earlier mod. Mods can add content but not replace it; give yours a different id (a short prefix for your mod helps).");
                else
                {
                    target.Add(id, item);
                    origins[id] = where;
                }
            }
        }
    }

    /// <summary>Adds the mod and file to a validation problem when it is about something a mod added.</summary>
    private static string AttributeToMod(string problem, Dictionary<string, string> origins)
    {
        var match = OwnerPattern.Match(problem);
        if (!match.Success) return problem;
        string id = match.Groups[1].Value;
        if (origins.TryGetValue(id, out var where)) return $"{where}: {problem}";
        if (problem.StartsWith("Grammar '") && origins.TryGetValue("extended:" + id, out var mods))
            return $"{problem} (grammar extended by mod {mods}; check the options it added)";
        return problem;
    }

    private static List<T> ReadFile<T>(IContentSource source, string path, List<string> problems, string label)
    {
        try
        {
            using var doc = JsonDocument.Parse(source.ReadText(path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.Deserialize<List<T>>(Json) ?? []
                : [doc.RootElement.Deserialize<T>(Json)!];
        }
        catch (JsonException e)
        {
            problems.Add($"{label}: invalid JSON ({e.Message})");
            return [];
        }
    }

    private static bool IsValidId(string id) =>
        id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') && !id.StartsWith('-') && !id.EndsWith('-');

    /// <summary>Rules every race voice grammar must have. The crew system asks for these by name.</summary>
    public static readonly string[] VoiceRules = ["greeting", "hire", "complaint", "praise", "combat", "idle", "leaving"];

    private static readonly HashSet<string> HullClasses = ["courier", "freighter", "fighter", "gunship", "explorer", "miner", "drone", "frigate", "cruiser", "battlecruiser", "carrier"];
    public static readonly HashSet<string> WeaponKinds = ["laser", "pulse", "beam", "plasma", "rail", "scatter", "flak", "missile", "torpedo"];
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
            if (hull.IsCapital && hull.ForSale) p.Add($"{at}: warships ({hull.Class}) must have \"forSale\": false.");
            if (hull.Hangar > 0 && hull.HangarHull == null) p.Add($"{at}: a hangar needs a hangarHull.");
            if (hull.HangarHull != null && !Hulls.ContainsKey(hull.HangarHull)) p.Add($"{at}: hangarHull '{hull.HangarHull}' isn't a known hull.");
            if ((hull.PriceLow > 0 && hull.PriceLow > hull.Price) || (hull.PriceHigh > 0 && hull.PriceHigh < hull.Price))
                p.Add($"{at}: priceLow and priceHigh should sit either side of price.");
            foreach (var slot in hull.Slots.Where(sl => sl.Turret && sl.Type != "weapon")) p.Add($"{at}: only weapon slots can be turrets.");
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
            if (part.Type == "weapon" && !WeaponKinds.Contains(part.Kind)) p.Add($"{at}: kind '{part.Kind}' is not one of {string.Join(", ", WeaponKinds)}.");
            if (!string.IsNullOrEmpty(part.Builder) && !Builders.ContainsKey(part.Builder)) p.Add($"{at}: unknown builder '{part.Builder}'.");
            if (part.Color != null && !IsHexColour(part.Color)) p.Add($"{at}: colour '{part.Color}' is not #rrggbb.");
        }

        ValidateWorld(p);
        p.AddRange(Grammars.Validate());
        return p;
    }

    private void ValidateWorld(List<string> p)
    {
        foreach (var race in Races.Values.Where(r => r.TalkGrammar != ""))
        {
            foreach (var rule in ContentRules.TalkRules) CheckGrammar($"Race '{race.Id}' talk", race.TalkGrammar, rule, p);
            CheckVariables($"Race '{race.Id}' talk", race.TalkGrammar, ContentRules.TalkVariables, p);
            if (race.HaggleChance is < 0 or > 1 || race.HaggleOffence is < 0 or > 1) p.Add($"Race '{race.Id}': haggleChance and haggleOffence must be 0 to 1.");
        }

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
            if (c.PriceLow > 0 && c.PriceLow > c.BasePrice) p.Add($"{at}: priceLow can't be above basePrice.");
            if (c.PriceHigh > 0 && c.PriceHigh < c.BasePrice) p.Add($"{at}: priceHigh can't be below basePrice.");
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

        foreach (var f in Finds.Values)
        {
            string at = $"Find '{f.Id}'";
            if (f.Where.Count == 0 || f.Where.Any(w => w is not ("body" or "rock" or "ship" or "cache"))) p.Add($"{at}: where must list body, rock, ship or cache.");
            if (f.Weight <= 0) p.Add($"{at}: weight must be positive.");
            if (f.Title == "" || f.Text == "") p.Add($"{at}: needs a title and text.");
            if (f.LeadsTo != null && (!Finds.TryGetValue(f.LeadsTo, out var cache) || !cache.Where.Contains("cache"))) p.Add($"{at}: leadsTo '{f.LeadsTo}' must be a find with where \"cache\".");
            if (f.LeadsTo == null && (f.Text.Contains("{target}") || f.Text.Contains("{targetBody}"))) p.Add($"{at}: only a find with leadsTo can name {{target}}.");
            foreach (var g in f.Goods.Keys.Where(g => !Commodities.ContainsKey(g))) p.Add($"{at}: unknown goods '{g}'.");
            foreach (var r in f.Regions.Where(r => !ContentRules.Regions.Contains(r))) p.Add($"{at}: unknown region '{r}'.");
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

        CheckBattles(p);
        CheckCampaigns(p);
        CheckMoreFactionTypes(p);
        CheckLandmarks(p);

        foreach (var power in Powers.Values)
        {
            string at = $"Power '{power.Id}'";
            if (!Races.ContainsKey(power.Race)) p.Add($"{at}: unknown race '{power.Race}'.");
            if (!FactionTypes.ContainsKey(power.FactionType)) p.Add($"{at}: unknown faction type '{power.FactionType}'.");
            if (!ContentRules.Arms.Contains(power.Arm)) p.Add($"{at}: arm must be one of {Join(ContentRules.Arms)}.");
            if (!ContentRules.Regions.Contains(power.Region)) p.Add($"{at}: region must be one of {Join(ContentRules.Regions)}.");
            if (power.Systems < 1) p.Add($"{at}: needs at least one system.");
            if (!IsHexColour(power.Colour)) p.Add($"{at}: colour '{power.Colour}' is not #rrggbb.");
            if (!Builders.ContainsKey(power.Builder)) p.Add($"{at}: unknown builder '{power.Builder}'.");
        }

        foreach (var style in MusicStyles.Values)
        {
            string at = $"Music style '{style.Id}'";
            if (!Audio.Composer.Scales.TryGetValue(style.Scale, out var scale)) { p.Add($"{at}: scale '{style.Scale}' is unknown. Use one of {Join(Audio.Composer.Scales.Keys)}."); continue; }
            if (style.Tempo.Length != 2 || style.Tempo[0] < 50 || style.Tempo[1] > 120 || style.Tempo[0] > style.Tempo[1]) p.Add($"{at}: tempo needs [min, max] between 50 and 120.");
            if (style.Root is < 33 or > 52) p.Add($"{at}: root should be a MIDI note from 33 to 52 (the bass register).");
            if (style.Progressions.Count == 0 || style.Progressions.Any(pr => pr.Length is not (4 or 8) || pr.Any(d => d < 1 || d > scale.Length)))
                p.Add($"{at}: progressions need four or eight chords, each a degree from 1 to {scale.Length}.");
            if (style.Ninths is < 0 or > 1) p.Add($"{at}: ninths should be 0 to 1.");
            if (style.Swing is < 0 or > 0.35f) p.Add($"{at}: swing should be 0 to 0.35.");
            void Part(string name, Audio.PartDef part, HashSet<string> instruments, HashSet<string>? patterns)
            {
                if (!instruments.Contains(part.Instrument)) p.Add($"{at}: {name} instrument '{part.Instrument}' is unknown. Use {Join(instruments)}.");
                if (patterns != null && part.Pattern != "" && !patterns.Contains(part.Pattern)) p.Add($"{at}: {name} pattern '{part.Pattern}' is unknown. Use {Join(patterns)}.");
                if (part.Volume is < 0 or > 2) p.Add($"{at}: {name} volume should be 0 to 2.");
                if (part.Density is < 0 or > 1 || part.Tone is < 0 or > 1) p.Add($"{at}: {name} density and tone should be 0 to 1.");
            }
            Part("keys", style.Keys, Audio.Lofi.KeysInstruments, Audio.Composer.KeysPatterns);
            Part("bass", style.Bass, Audio.Lofi.BassInstruments, Audio.Composer.BassPatterns);
            Part("lead", style.Lead, Audio.Lofi.LeadInstruments, Audio.Composer.LeadPatterns);
            Part("pad", style.Pad, Audio.Lofi.PadInstruments, null);
            Part("plucks", style.Plucks, Audio.Lofi.PluckInstruments, Audio.Composer.PluckPatterns);
            if (!Audio.Lofi.DrumKits.Contains(style.Drums.Kit)) p.Add($"{at}: drum kit '{style.Drums.Kit}' is unknown. Use {Join(Audio.Lofi.DrumKits)}.");
            if (style.Drums.Busy is < 0 or > 1 || style.Drums.Volume is < 0 or > 2) p.Add($"{at}: drums busy should be 0 to 1 and volume 0 to 2.");
            var sp = style.Space;
            if (new[] { sp.Shimmer, sp.Reverb, sp.Vinyl, sp.Hiss, sp.Wow, sp.Warmth }.Any(v => v is < 0 or > 1)) p.Add($"{at}: space values should be 0 to 1.");
        }

        foreach (var style in Portraits.Values)
        {
            string at = $"Portrait style '{style.Race}'";
            if (!Races.ContainsKey(style.Race)) p.Add($"{at}: unknown race.");
            foreach (var c in style.Skin.Concat(style.Hair).Concat(style.Eyes).Concat(style.Clothing).Where(c => !IsHexColour(c)))
                p.Add($"{at}: colour '{c}' is not #rrggbb.");
            if (style.Skin.Count == 0 || style.Eyes.Count == 0) p.Add($"{at}: needs skin and eyes colours.");
            if (!Galaxus.Core.Portraits.PortraitBuilder.EarTypes.Contains(style.Ears)) p.Add($"{at}: ears must be one of {Join(Galaxus.Core.Portraits.PortraitBuilder.EarTypes)}.");
            foreach (var h in style.HairStyles.Where(h => !Galaxus.Core.Portraits.PortraitBuilder.HairStyleNames.Contains(h))) p.Add($"{at}: hair style '{h}' is unknown.");
            foreach (var m in style.Marks.Keys.Where(m => !Galaxus.Core.Portraits.PortraitBuilder.MarkNames.Contains(m))) p.Add($"{at}: mark '{m}' is unknown.");
            if (style.HeadWidth.Length != 2 || style.HeadWidth[0] < 12 || style.HeadWidth[1] > 24 || style.HeadWidth[0] > style.HeadWidth[1]) p.Add($"{at}: headWidth needs [min, max] between 12 and 24.");
            if (style.Jaw.Length != 2 || style.Jaw[0] < 0.4f || style.Jaw[1] > 0.98f || style.Jaw[0] > style.Jaw[1]) p.Add($"{at}: jaw needs [min, max] between 0.4 and 0.98.");
        }
        foreach (var race in Races.Keys.Where(r => Portraits.Count > 0 && !Portraits.ContainsKey(r))) p.Add($"Race '{race}' has no portrait style.");

        foreach (var role in Roles.Values)
        {
            string at = $"Role '{role.Id}'";
            foreach (var h in role.Headwear.Where(h => !Galaxus.Core.Portraits.Attire.Headwears.Contains(h))) p.Add($"{at}: headwear '{h}' is unknown. Use {Join(Galaxus.Core.Portraits.Attire.Headwears)}.");
            foreach (var e in role.Eyewear.Where(e => !Galaxus.Core.Portraits.Attire.Eyewears.Contains(e))) p.Add($"{at}: eyewear '{e}' is unknown. Use {Join(Galaxus.Core.Portraits.Attire.Eyewears)}.");
            foreach (var c in role.Collar.Where(c => !Galaxus.Core.Portraits.Attire.Collars.Contains(c))) p.Add($"{at}: collar '{c}' is unknown. Use {Join(Galaxus.Core.Portraits.Attire.Collars)}.");
            foreach (var x in role.Extras.Where(x => !Galaxus.Core.Portraits.Attire.ExtraNames.Contains(x))) p.Add($"{at}: extra '{x}' is unknown. Use {Join(Galaxus.Core.Portraits.Attire.ExtraNames)}.");
            foreach (var e in role.Economies.Where(e => !Enum.TryParse<Galaxy.Economy>(e, out _))) p.Add($"{at}: economy '{e}' is unknown.");
        }
        foreach (var required in new[] { "bartender", "dockhand" })
            if (Roles.Count > 0 && !Roles.ContainsKey(required)) p.Add($"Roles: '{required}' is required (every station has one).");

        if (Missions.Count > 0)
            foreach (var place in ContentRules.PlaceGrammars) CheckGrammar("Galaxy", place, "name", p);
    }

    /// <summary>A grammar may only declare variables the game will actually supply.</summary>
    public static readonly HashSet<string> BattleKinds = ["war", "pirate", "synth", "defence", "campaign", "job", "sim"];
    public static readonly HashSet<string> JobBattleTypes = ["bounty", "escort", "patrol", "raid", "claim-jumping", "mining"];
    public static readonly HashSet<string> BattleRoles = ["fighter", "bomber", "escort", "capital", "freighter", "interceptor", "holding"];
    public static readonly HashSet<string> BattlePositions = ["near", "ahead", "behind", "left", "right", "above", "far"];
    public static readonly HashSet<string> ObjectiveTypes = ["destroy", "protect", "survive", "escape", "scan", "rendezvous"];

    private void CheckMoreFactionTypes(List<string> p)
    {
        foreach (var t in FactionTypes.Values)
        {
            string at = $"Faction type '{t.Id}'";
            if (t.Builder != null && !Builders.ContainsKey(t.Builder)) p.Add($"{at}: unknown builder '{t.Builder}'.");
            if (t.StationHull != null && !Hulls.ContainsKey(t.StationHull)) p.Add($"{at}: unknown station hull '{t.StationHull}'.");
            if (t.StationModel != null && t.StationModel.Count == 0) p.Add($"{at}: stationModel needs shapes.");
            if (t.StationModel != null && t.StationRadius <= 0) p.Add($"{at}: a stationModel needs a stationRadius.");
        }
    }

    private void CheckLandmarks(List<string> p)
    {
        foreach (var l in Landmarks.Values)
        {
            string at = $"Landmark '{l.Id}'";
            if (string.IsNullOrWhiteSpace(l.SystemName)) p.Add($"{at}: needs a systemName.");
            if (!ContentRules.Regions.Contains(l.Region)) p.Add($"{at}: region must be one of {string.Join(", ", ContentRules.Regions)}.");
            if (l.Arm != null && !ContentRules.Arms.Contains(l.Arm)) p.Add($"{at}: arm must be one of {string.Join(", ", ContentRules.Arms)}.");
            if (l.OwnerFactionType != null && !Powers.ContainsKey(l.OwnerFactionType) && !FactionTypes.ContainsKey(l.OwnerFactionType))
                p.Add($"{at}: ownerFactionType '{l.OwnerFactionType}' is neither a great power nor a faction type.");
            if (l.Economy != null && !Enum.TryParse<Galaxy.Economy>(l.Economy, out _)) p.Add($"{at}: economy '{l.Economy}' isn't one of {string.Join(", ", Enum.GetNames<Galaxy.Economy>())}.");
        }
        foreach (var dup in Landmarks.Values.GroupBy(l => l.SystemName).Where(g => g.Count() > 1))
            p.Add($"Landmarks {string.Join(", ", dup.Select(l => $"'{l.Id}'"))} share the system name '{dup.Key}'.");
    }

    private void CheckCampaigns(List<string> p)
    {
        foreach (var c in Campaigns.Values)
        {
            string at = $"Campaign '{c.Id}'";
            if (c.FactionType != null)
            {
                if (!FactionTypes.ContainsKey(c.FactionType)) p.Add($"{at}: unknown faction type '{c.FactionType}'.");
                if (c.Power != "") p.Add($"{at}: give either a power or a factionType, not both.");
            }
            else if (!Powers.ContainsKey(c.Power)) p.Add($"{at}: unknown power '{c.Power}'.");
            if (c.BaseLandmark != null && !Landmarks.ContainsKey(c.BaseLandmark)) p.Add($"{at}: unknown base landmark '{c.BaseLandmark}'.");
            if (!Powers.ContainsKey(c.Rival)) p.Add($"{at}: unknown rival power '{c.Rival}'.");
            if (!Hulls.ContainsKey(c.Hull)) p.Add($"{at}: unknown starting hull '{c.Hull}'.");
            if (!Hulls.TryGetValue(c.BaseHull, out var baseHull)) p.Add($"{at}: unknown base hull '{c.BaseHull}'.");
            else if (!baseHull.Dockable) p.Add($"{at}: base hull '{c.BaseHull}' isn't dockable.");
            foreach (var f in c.Fleet.Where(f => !Hulls.ContainsKey(f))) p.Add($"{at}: unknown fleet hull '{f}'.");
            if (c.Rank is < 0 or > 5) p.Add($"{at}: rank must be 0 to 5.");
            if (c.Steps.Count == 0) p.Add($"{at}: needs steps.");
            bool grammar = Grammars.Contains(c.TextGrammar);
            if (!grammar) p.Add($"{at}: unknown text grammar '{c.TextGrammar}'.");
            var rules = grammar ? Grammars.Get(c.TextGrammar).Rules : new Dictionary<string, List<string>>();
            foreach (var rule in new[] { "intro", "outro" }.Where(r => grammar && !rules.ContainsKey(r))) p.Add($"{at}: grammar needs an '{rule}' rule.");
            foreach (var step in c.Steps)
            {
                string sat = $"{at}, step '{step.Id}'";
                if (!Battles.ContainsKey(step.Battle)) p.Add($"{sat}: unknown battle '{step.Battle}'.");
                if (step.Where.StartsWith("landmark:"))
                {
                    if (!Landmarks.ContainsKey(step.Where["landmark:".Length..])) p.Add($"{sat}: unknown landmark in '{step.Where}'.");
                }
                else if (step.Where is not ("base" or "near" or "front")) p.Add($"{sat}: where must be base, near, front or landmark:<id>.");
                if (step.Enemy is not ("rival" or "pirates" or "unbound")) p.Add($"{sat}: enemy must be rival, pirates or unbound.");
                foreach (var rule in new[] { step.Briefing, step.Debrief })
                    if (grammar && !rules.ContainsKey(rule)) p.Add($"{sat}: rule '{rule}' isn't in grammar '{c.TextGrammar}'.");
            }
        }
    }

    private void CheckBattles(List<string> p)
    {
        foreach (var b in Battles.Values)
        {
            string at = $"Battle '{b.Id}'";
            if (!BattleKinds.Contains(b.Kind)) p.Add($"{at}: kind must be one of {string.Join(", ", BattleKinds)}.");
            foreach (var stage in b.Stages.Where(x => x is not ("raids" or "blockade" or "war"))) p.Add($"{at}: stage '{stage}' must be raids, blockade or war.");
            if (b.Kind == "war" && b.Stages.Count == 0) p.Add($"{at}: war battles need at least one stage.");
            if (b.Kind == "job" && (b.JobType == null || !JobBattleTypes.Contains(b.JobType))) p.Add($"{at}: job battles need a jobType, one of {string.Join(", ", JobBattleTypes)}.");
            foreach (var scale in b.Scales.Where(x => x is not ("small" or "skirmish" or "battle" or "war"))) p.Add($"{at}: scale '{scale}' must be small, skirmish, battle or war.");
            if (!Grammars.Contains(b.TextGrammar)) p.Add($"{at}: unknown text grammar '{b.TextGrammar}'.");
            else
                foreach (var rule in new[] { "briefing", "success", "failure" })
                    if (!Grammars.Get(b.TextGrammar).Rules.ContainsKey(rule)) p.Add($"{at}: text grammar '{b.TextGrammar}' needs a '{rule}' rule.");
            if (b.Waves.Count == 0) p.Add($"{at}: needs at least one wave.");
            if (b.Objectives.Count(o => !o.Optional) == 0) p.Add($"{at}: needs at least one objective that isn't optional.");
            var tags = b.Waves.SelectMany(w => w.Ships).Select(g => g.Tag).Where(t => t != null).ToHashSet();
            var waveIds = b.Waves.Select(w => w.Id).ToHashSet();
            var objectiveIds = b.Objectives.Select(o => o.Id).ToHashSet();
            foreach (var w in b.Waves)
            {
                string wat = $"{at}, wave '{w.Id}'";
                if (w.Id == "") p.Add($"{at}: every wave needs an id.");
                if (w.Side is not ("friend" or "enemy" or "neutral")) p.Add($"{wat}: side must be friend, enemy or neutral.");
                if (!BattlePositions.Contains(w.Position)) p.Add($"{wat}: position must be one of {string.Join(", ", BattlePositions)}.");
                if (w.After != null)
                {
                    var parts = w.After.Split(':');
                    bool ok = parts.Length == 2 && ((parts[0] == "objective" && objectiveIds.Contains(parts[1])) || (parts[0] == "wave" && waveIds.Contains(parts[1])));
                    if (!ok) p.Add($"{wat}: after '{w.After}' must be objective:<id> or wave:<id> of this battle.");
                }
                if (w.Radio != null && Grammars.Contains(b.TextGrammar) && !Grammars.Get(b.TextGrammar).Rules.ContainsKey(w.Radio))
                    p.Add($"{wat}: radio rule '{w.Radio}' isn't in grammar '{b.TextGrammar}'.");
                if (w.Ships.Count == 0) p.Add($"{wat}: needs ships.");
                foreach (var g in w.Ships)
                {
                    if (!BattleRoles.Contains(g.Role)) p.Add($"{wat}: role '{g.Role}' must be one of {string.Join(", ", BattleRoles)}.");
                    if (g.Hull != null && !Hulls.ContainsKey(g.Hull)) p.Add($"{wat}: unknown hull '{g.Hull}'.");
                    if (g.Hull == null && (g.Class == null || !HullClasses.Contains(g.Class))) p.Add($"{wat}: each group needs a hull or a hull class.");
                    if (g.Count is < 1 or > 24) p.Add($"{wat}: count must be 1 to 24.");
                    if (g.Target != null && !tags.Contains(g.Target)) p.Add($"{wat}: target '{g.Target}' isn't a tag in this battle.");
                }
            }
            foreach (var o in b.Objectives)
            {
                string oat = $"{at}, objective '{o.Id}'";
                if (!ObjectiveTypes.Contains(o.Type)) p.Add($"{oat}: type must be one of {string.Join(", ", ObjectiveTypes)}.");
                if (o.Type is "destroy" && o.Tag == null && o.Wave == null) p.Add($"{oat}: destroy needs a tag or a wave.");
                if (o.Type is "protect" or "escape" or "scan" or "rendezvous" && o.Tag == null) p.Add($"{oat}: {o.Type} needs a tag.");
                if (o.Type is "scan" or "rendezvous" && (o.Distance <= 0 || o.Seconds <= 0)) p.Add($"{oat}: {o.Type} needs distance and seconds.");
                if (o.Tag != null && !tags.Contains(o.Tag)) p.Add($"{oat}: tag '{o.Tag}' isn't in any wave.");
                if (o.Wave != null && !waveIds.Contains(o.Wave)) p.Add($"{oat}: wave '{o.Wave}' doesn't exist.");
                if (o.Type == "survive" && o.Seconds <= 0) p.Add($"{oat}: survive needs seconds.");
                if (o.Text == "") p.Add($"{oat}: needs text for the HUD.");
            }
        }
    }

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
