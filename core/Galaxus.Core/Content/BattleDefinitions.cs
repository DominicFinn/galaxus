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
    /// <summary>war (between two powers or factions at war), pirate, synth (rogue machines), or defence (of a station).</summary>
    public string Kind { get; set; } = "war";
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
    /// runs from trouble), interceptor (hunts you first).
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
}

public sealed class ObjectiveDef
{
    public string Id { get; set; } = "";
    /// <summary>destroy (every ship with Tag, or every ship in Wave), protect (keep Tag alive), survive (Seconds), escape (Tag reaches its exit).</summary>
    public string Type { get; set; } = "destroy";
    public string? Tag { get; set; }
    public string? Wave { get; set; }
    public float Seconds { get; set; }
    /// <summary>Shown on the HUD, e.g. "Destroy the carrier Iron Vow".</summary>
    public string Text { get; set; } = "";
    /// <summary>Optional objectives pay a bonus but don't decide the battle.</summary>
    public bool Optional { get; set; }
}
