namespace Galaxus.Core.Content;

/// <summary>
/// A set-piece battle, in the spirit of X-Wing and TIE Fighter missions: ships arrive in waves,
/// there are objectives to win or lose, and command talks to you over the radio. Battles are data,
/// so mods can add more. The game picks hulls for each side from that side's shipbuilder.
/// </summary>
public sealed class BattleDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>war (between two powers or factions at war), pirate, synth (rogue machines), defence (of a station), campaign (only flown as part of a campaign, never posted on boards), sim (a dogfight simulator drill), or job (the fight behind an ordinary job board job; see JobType).</summary>
    public string Kind { get; set; } = "war";
    /// <summary>Wingmen who join you at the start: -1 lets the game decide from the odds, 0 is a solo fight.</summary>
    public int Wingmen { get; set; } = -1;
    /// <summary>For kind "sim": its place in the dogfight simulator's run, from 1.</summary>
    public int Order { get; set; }
    /// <summary>For kind "job": the mission type it plays out (bounty, escort, patrol, raid, claim-jumping, mining).</summary>
    public string? JobType { get; set; }
    /// <summary>For kind "job": the job scales it suits (small, skirmish, battle, war). Empty means any.</summary>
    public List<string> Scales { get; set; } = [];
    /// <summary>For war battles: the conflict stages it's offered at: raids, blockade, war.</summary>
    public List<string> Stages { get; set; } = [];
    /// <summary>Base pay in credits, before the board scales it by danger and rank.</summary>
    public int Reward { get; set; } = 20000;
    /// <summary>Lowest rank (0 to 5) with the client before it's offered.</summary>
    public int MinRank { get; set; }
    /// <summary>Grammar with briefing, success and failure rules, like a mission's text grammar.</summary>
    public string TextGrammar { get; set; } = "";
    public List<WaveDef> Waves { get; set; } = [];
    public List<ObjectiveDef> Objectives { get; set; } = [];
}

public sealed class WaveDef
{
    public string Id { get; set; } = "";
    /// <summary>friend (the side you fly for), enemy, or neutral (traders and civilians caught up in it).</summary>
    public string Side { get; set; } = "enemy";
    /// <summary>Seconds after the battle starts. Ignored if After is set.</summary>
    public float At { get; set; }
    /// <summary>Starts this many seconds after an objective is done or a wave is wiped out: "objective:id" or "wave:id".</summary>
    public string? After { get; set; }
    public float Delay { get; set; }
    /// <summary>Where it arrives relative to you: near, ahead, behind, left, right, above, far.</summary>
    public string Position { get; set; } = "ahead";
    /// <summary>Distance from you in metres.</summary>
    public float Distance { get; set; } = 4000;
    public List<GroupDef> Ships { get; set; } = [];
    /// <summary>Radio rule in the battle's text grammar, read out when the wave arrives.</summary>
    public string? Radio { get; set; }
}

public sealed class GroupDef
{
    /// <summary>
    /// How they fly: fighter (dogfights), bomber (goes for warships), escort (guards Target),
    /// capital (a warship holding course and firing its turrets), freighter (flies a course and
    /// runs from trouble), interceptor (hunts you first), holding (sits still: beacons, a stranded
    /// ship, a listening post).
    /// Use class "drone" for small unmanned things like sensor beacons.
    /// </summary>
    public string Role { get; set; } = "fighter";
    /// <summary>Hull class to pick from the side's builder (fighter, gunship, frigate...). Hull overrides it.</summary>
    public string? Class { get; set; }
    public string? Hull { get; set; }
    public int Count { get; set; } = 1;
    /// <summary>A name for objectives and Target to refer to, like "flagship".</summary>
    public string? Tag { get; set; }
    /// <summary>The tag this group goes after or guards.</summary>
    public string? Target { get; set; }
    /// <summary>How good these pilots are, 0.1 to 1. Unset leaves it to the battle.</summary>
    public float? Skill { get; set; }
    /// <summary>A fixed name for the group's first ship, such as an enemy ace or your base ("{base}"). Variables are filled in.</summary>
    public string? Name { get; set; }
}

public sealed class ObjectiveDef
{
    public string Id { get; set; } = "";
    /// <summary>
    /// destroy (every ship with Tag, or every ship in Wave), protect (keep Tag alive), survive
    /// (Seconds), escape (Tag reaches its exit), scan (fly within Distance of every ship with Tag
    /// and hold there for Seconds each: reconnaissance), rendezvous (match speed within Distance of
    /// Tag and hold for Seconds: refuelling, transfers, boarding).
    /// </summary>
    public string Type { get; set; } = "destroy";
    public string? Tag { get; set; }
    public string? Wave { get; set; }
    public float Seconds { get; set; }
    /// <summary>For scan and rendezvous: how close you have to be, in metres from the ship's hull.</summary>
    public float Distance { get; set; }
    /// <summary>Shown on the HUD, e.g. "Destroy the carrier Iron Vow".</summary>
    public string Text { get; set; } = "";
    /// <summary>Optional objectives pay a bonus but don't decide the battle.</summary>
    public bool Optional { get; set; }
}

/// <summary>
/// A campaign: you start enlisted with a great power and fly a run of missions for it, returning
/// to your base (a flagship with its fleet) between each for a debrief and new orders. It's a
/// guided cut of the game in the same galaxy; when it's done, play carries on freely.
/// </summary>
public sealed class CampaignDef
{
    public string Id { get; set; } = "";
    /// <summary>A tutorial is listed first on the new game screen and teaches the controls as it goes.</summary>
    public bool Tutorial { get; set; }
    public string Name { get; set; } = "";
    /// <summary>The great power you fly for. Leave empty and set FactionType to fly for a smaller faction.</summary>
    public string Power { get; set; } = "";
    /// <summary>A faction type to fly for instead of a great power. The largest faction of that type is used; the galaxy founds one if there's none.</summary>
    public string? FactionType { get; set; }
    /// <summary>A landmark id: the campaign's base is there instead of at the power's capital.</summary>
    public string? BaseLandmark { get; set; }
    /// <summary>The great power you're mostly fighting, for steps whose enemy is "rival".</summary>
    public string Rival { get; set; } = "";
    /// <summary>A paragraph for the new game screen.</summary>
    public string Summary { get; set; } = "";
    /// <summary>The ship you start in.</summary>
    public string Hull { get; set; } = "";
    public int Credits { get; set; } = 5000;
    /// <summary>Your starting standing with the power, 0 to 5.</summary>
    public int Rank { get; set; } = 1;
    /// <summary>Your base: a dockable warship (flagship or carrier) and its name.</summary>
    public string BaseHull { get; set; } = "";
    public string BaseName { get; set; } = "";
    /// <summary>Other warships parked with the base, so it reads as a fleet.</summary>
    public List<string> Fleet { get; set; } = [];
    /// <summary>Grammar with "intro" and "outro" rules, and each step's briefing and debrief rules.</summary>
    public string TextGrammar { get; set; } = "";
    public List<CampaignStepDef> Steps { get; set; } = [];
}

public sealed class CampaignStepDef
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>The battle flown for this step (any kind, usually "campaign").</summary>
    public string Battle { get; set; } = "";
    /// <summary>Where it happens: base (in the base's system), near (a system a jump or two away), front (towards the rival), or landmark:id (a landmark's system).</summary>
    public string Where { get; set; } = "base";
    /// <summary>Who you're up against: rival, pirates or unbound.</summary>
    public string Enemy { get; set; } = "rival";
    public int Reward { get; set; } = 10000;
    /// <summary>Rules in the campaign grammar for the orders and the debrief afterwards.</summary>
    public string Briefing { get; set; } = "";
    public string Debrief { get; set; } = "";
}
