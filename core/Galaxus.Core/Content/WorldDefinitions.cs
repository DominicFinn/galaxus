namespace Galaxus.Core.Content;

// Definitions for the living galaxy: trade goods, faction types, galaxy events, missions and
// their complications. docs/content-guide.md describes each one for writers.

public sealed class CommodityDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>One of ContentRules.CommodityCategories.</summary>
    public string Category { get; set; } = "";
    /// <summary>Credits per tonne at a normal market.</summary>
    public int BasePrice { get; set; }
    /// <summary>legal, restricted (permit needed in some places) or contraband (illegal almost everywhere).</summary>
    public string Legality { get; set; } = "legal";
    public string Description { get; set; } = "";
}

/// <summary>
/// A kind of faction: a mining corporation, a noble house, a pirate clan. The simulation creates
/// hundreds of actual factions from these, each with rolled traits and a generated name.
/// </summary>
public sealed class FactionTypeDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Races that found this type. Empty means any race.</summary>
    public List<string> Races { get; set; } = [];
    /// <summary>Sizes this type can reach: minor (one or two systems), regional, great.</summary>
    public List<string> Tiers { get; set; } = ["minor"];
    /// <summary>Criminal factions hide their bases and never appear on the public nav map.</summary>
    public bool Criminal { get; set; }
    /// <summary>Trait ranges, each [min, max] between 0 and 1. Keys from ContentRules.FactionTraits.</summary>
    public Dictionary<string, float[]> Traits { get; set; } = [];
    /// <summary>Grammar with a "name" rule. Variables: {place}, {founder}, {race}.</summary>
    public string NameGrammar { get; set; } = "";
}

/// <summary>Something happening in the galaxy that creates work: a famine, a border war, a mining rush.</summary>
public sealed class EventDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>How long it usually lasts, [min, max] in days.</summary>
    public int[] DurationDays { get; set; } = [10, 30];
    /// <summary>Demand multipliers by commodity category while the event runs.</summary>
    public Dictionary<string, float> CommodityDemand { get; set; } = [];
    /// <summary>Grammar with "start", "ongoing" and "end" rules for the news feed.</summary>
    public string NewsGrammar { get; set; } = "";
}

public sealed class MissionTemplateDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>One of ContentRules.MissionTypes.</summary>
    public string Type { get; set; } = "";
    /// <summary>small, skirmish, battle or war. Sets how much one captain can sway the outcome.</summary>
    public string Scale { get; set; } = "small";
    public bool Legal { get; set; } = true;
    /// <summary>Faction types that offer it. Empty means any.</summary>
    public List<string> Givers { get; set; } = [];
    /// <summary>Events that create this work. Empty means it's standing work, always on offer somewhere.</summary>
    public List<string> Events { get; set; } = [];
    /// <summary>For cargo work, the commodity categories it can carry.</summary>
    public List<string> CargoCategories { get; set; } = [];
    /// <summary>Rank needed with the giver, 0 (unknown) to 4 (honoured).</summary>
    public int MinRank { get; set; }
    public int BaseReward { get; set; }
    /// <summary>Complications that can happen on this job.</summary>
    public List<string> Complications { get; set; } = [];
    /// <summary>Templates that can follow on success, for short arcs.</summary>
    public List<string> FollowUps { get; set; } = [];
    /// <summary>Grammar with "title", "briefing", "success" and "failure" rules.</summary>
    public string TextGrammar { get; set; } = "";
}

/// <summary>A twist on a job: the client lies, the cargo is stolen, the passenger is a fugitive.</summary>
public sealed class ComplicationDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>The game rule it triggers. One of ContentRules.ComplicationEffects.</summary>
    public string Effect { get; set; } = "";
    /// <summary>Relative chance against other complications on the same job.</summary>
    public float Weight { get; set; } = 1f;
    /// <summary>Grammar with "reveal" and "aftermath" rules.</summary>
    public string TextGrammar { get; set; } = "";
}

/// <summary>A great power from the lore bible, seeded by the galaxy generator in its arm and region.</summary>
public sealed class PowerDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Race { get; set; } = "";
    public string FactionType { get; set; } = "";
    /// <summary>meridian, tolland, anvil or shroud.</summary>
    public string Arm { get; set; } = "";
    /// <summary>core, inner, outer or rim: where its home system is.</summary>
    public string Region { get; set; } = "";
    /// <summary>How many systems it holds at the start.</summary>
    public int Systems { get; set; } = 10;
    /// <summary>Map colour, #rrggbb.</summary>
    public string Colour { get; set; } = "#888888";
    /// <summary>The shipbuilder whose hulls its navy flies.</summary>
    public string Builder { get; set; } = "";
}

/// <summary>The fixed vocabularies content has to use, and the grammar rules the game asks for.</summary>
public static class ContentRules
{
    public static readonly HashSet<string> CommodityCategories =
    [
        "food", "water", "medical", "ore", "metals", "industrial", "machinery", "consumer", "luxury",
        "technology", "weapons", "narcotics", "slaves", "data", "elder-artefacts", "rim-tech", "fuel",
    ];

    public static readonly string[] Regions = ["core", "inner", "outer", "rim"];
    public static readonly string[] Arms = ["meridian", "tolland", "anvil", "shroud"];

    public static readonly HashSet<string> Legalities = ["legal", "restricted", "contraband"];

    public static readonly HashSet<string> FactionTiers = ["minor", "regional", "great"];

    public static readonly HashSet<string> FactionTraits = ["aggression", "greed", "expansion", "paranoia", "honour", "piety"];

    public static readonly HashSet<string> MissionTypes =
    [
        "delivery", "courier", "passenger", "escort", "raid", "smuggling", "bounty", "survey", "salvage",
        "claim-jumping", "blockade-running", "patrol", "rescue", "investigation", "mining",
    ];

    public static readonly HashSet<string> MissionScales = ["small", "skirmish", "battle", "war"];

    public static readonly HashSet<string> ComplicationEffects =
    [
        "ambush",            // hostile ships wait on the route
        "stolen-cargo",      // the cargo was stolen; carrying it makes you wanted
        "client-lies",       // pay or destination isn't what was promised
        "rival-bid",         // another captain races you for the same job
        "fugitive",          // a passenger or contact is wanted
        "double-cross",      // the client tries to kill or rob you at the end
        "hidden-contraband", // something illegal is hidden in the cargo
        "deadline-cut",      // the deadline shortens mid-job
        "extra-stop",        // an unplanned detour is demanded
        "bad-intel",         // the target or site isn't where you were told
        "rival-offer",       // a rival faction offers more to betray the client
        "rim-signal",        // something strange from the rim interferes
    ];

    /// <summary>Variables every mission and complication text can use.</summary>
    public static readonly string[] MissionVariables =
        ["captain", "client", "faction", "system", "destination", "cargo", "tonnes", "reward", "target", "deadline"];

    public static readonly string[] MissionTextRules = ["title", "briefing", "success", "failure"];
    public static readonly string[] ComplicationTextRules = ["reveal", "aftermath"];
    public static readonly string[] EventNewsRules = ["start", "ongoing", "end"];
    public static readonly string[] EventNewsVariables = ["system", "faction", "rival"];
    public static readonly string[] FactionNameVariables = ["place", "founder", "race"];

    /// <summary>
    /// Mission text can say #voice:opener# and the game swaps in the client race's mission voice
    /// grammar, so an orc contract reads differently from an elf one.
    /// </summary>
    public const string VoiceAlias = "voice";
    public static readonly string[] MissionVoiceRules = ["opener", "urgency", "payment", "warning", "closer"];

    /// <summary>Rules every race conversation grammar needs. See game/data/grammars/talk/human.json.</summary>
    public static readonly string[] TalkRules =
        ["greeting-known", "rumour", "no-rumour", "work", "no-work", "self", "drink", "drink-again", "pestered", "cold", "farewell", "haggle-yes", "haggle-no", "haggle-offended"];
    public static readonly string[] TalkVariables = ["captain", "system", "faction", "fact", "role", "name", "place"];

    /// <summary>Grammars the galaxy generator needs, each with a "name" rule.</summary>
    public static readonly string[] PlaceGrammars = ["places-system", "places-station"];
}
