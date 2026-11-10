namespace Galaxus.Core.Content;

// Content definitions loaded from JSON in game/data. Property names are camelCase in the files.
// docs/content-guide.md describes every field for people (and agents) writing content.

public sealed class RaceDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>"core" for the hand-written story races, "minor" for generated ones.</summary>
    public string Kind { get; set; } = "minor";
    public string Description { get; set; } = "";
    public string HomeEnvironment { get; set; } = "";
    /// <summary>Body tolerance in g. Caps how hard a pilot of this race can turn at speed.</summary>
    public float GTolerance { get; set; } = 9f;
    /// <summary>Needs air and food. Synths and robots don't, which frees cargo space.</summary>
    public bool NeedsLifeSupport { get; set; } = true;
    /// <summary>Stat modifiers this race brings as crew, keyed by stat name (see StatKeys).</summary>
    public Dictionary<string, float> CrewModifiers { get; set; } = [];
    /// <summary>Grammar used for personal names, expanded from its "name" rule.</summary>
    public string NameGrammar { get; set; } = "";
    /// <summary>Grammar for crew chatter from this race.</summary>
    public string VoiceGrammar { get; set; } = "";
    /// <summary>Grammar mission text uses as #voice:rule# when the client is this race.</summary>
    public string MissionVoiceGrammar { get; set; } = "";
    /// <summary>Grammar for conversations at stations. See ContentRules.TalkRules.</summary>
    public string TalkGrammar { get; set; } = "";
    /// <summary>Chance a client of this race agrees to a haggled price, 0 to 1.</summary>
    public float HaggleChance { get; set; } = 0.4f;
    /// <summary>Chance a refused haggle offends them enough to withdraw the job, 0 to 1.</summary>
    public float HaggleOffence { get; set; } = 0.15f;
}

public sealed class BuilderDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Race { get; set; } = "";
    /// <summary>
    /// Where the yards are, from the galactic core outwards: core, inner, outer or rim. Rim designs
    /// are mostly found, salvaged or researched rather than bought.
    /// </summary>
    public string Region { get; set; } = "core";
    /// <summary>The power or culture the builder belongs to, e.g. a human nation or a dwarf hold league. Free text.</summary>
    public string Culture { get; set; } = "";
    public string Style { get; set; } = "";
    public string Bias { get; set; } = "";
    /// <summary>Multipliers applied to every hull this builder makes, keyed by stat name.</summary>
    public Dictionary<string, float> StatBias { get; set; } = [];
    public PaletteDef Palette { get; set; } = new();
    /// <summary>Grammar for model names, expanded from its "model" rule.</summary>
    public string ModelNameGrammar { get; set; } = "";
}

public sealed class PaletteDef
{
    public string Hull { get; set; } = "#8a8f99";
    public string Accent { get; set; } = "#c44b2b";
    public string Trim { get; set; } = "#3a3d44";
    public string Glow { get; set; } = "#7fd4ff";
}

public sealed class HullDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Builder { get; set; } = "";
    /// <summary>courier, freighter, fighter, gunship, explorer, miner or drone.</summary>
    public string Class { get; set; } = "";
    public string Description { get; set; } = "";
    public int Price { get; set; }
    /// <summary>Mass in tonnes. Acceleration is engine thrust divided by mass.</summary>
    public float Mass { get; set; } = 20f;
    /// <summary>Top speed in m/s before engine and power modifiers.</summary>
    public float BaseSpeed { get; set; } = 100f;
    /// <summary>Turn rates in degrees per second.</summary>
    public float PitchRate { get; set; } = 60f;
    public float YawRate { get; set; } = 45f;
    public float RollRate { get; set; } = 120f;
    public float HullPoints { get; set; } = 100f;
    /// <summary>Collision radius in metres.</summary>
    public float Radius { get; set; } = 6f;
    public int CrewBerths { get; set; }
    public int CargoTonnes { get; set; }
    public List<SlotDef> Slots { get; set; } = [];
    /// <summary>Parts fitted when the hull is bought or spawned.</summary>
    public List<string> DefaultLoadout { get; set; } = [];
    public List<ShapeDef> Model { get; set; } = [];
}

public sealed class SlotDef
{
    /// <summary>engine, powerPlant, shield, weapon, sensor, cargo or utility.</summary>
    public string Type { get; set; } = "";
    public int Size { get; set; } = 1;
    /// <summary>Hardpoint position for weapons, in metres in ship space (forward is -Z).</summary>
    public float[]? Position { get; set; }
}

/// <summary>
/// One primitive in a ship model. A tapered box runs along Z from the back face to the
/// front face, so a fuselage, a wing or an engine pod is each one shape.
/// </summary>
public sealed class ShapeDef
{
    /// <summary>"taperedBox" (the only shape so far).</summary>
    public string Shape { get; set; } = "taperedBox";
    /// <summary>Centre in metres, ship space. Forward is -Z, up is +Y, right is +X.</summary>
    public float[] Position { get; set; } = [0, 0, 0];
    /// <summary>Width and height of the rear face.</summary>
    public float[] Back { get; set; } = [1, 1];
    /// <summary>Width and height of the front face. Use small values for a point.</summary>
    public float[] Front { get; set; } = [1, 1];
    public float Length { get; set; } = 1f;
    /// <summary>Offset of the front face from the back face, in X and Y, for swept wings and raked noses.</summary>
    public float[] Skew { get; set; } = [0, 0];
    /// <summary>Rotation in degrees about the Z axis, for angled fins.</summary>
    public float Roll { get; set; }
    /// <summary>hull, accent, trim, glow, or a hex colour like "#aabbcc".</summary>
    public string Color { get; set; } = "hull";
    /// <summary>Also place a copy mirrored across X.</summary>
    public bool Mirror { get; set; }
}

public sealed class PartDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>engine, powerPlant, shield, weapon, sensor, cargo or utility.</summary>
    public string Type { get; set; } = "";
    public int Size { get; set; } = 1;
    /// <summary>Equipment grade, 1 to 5 (marks I to V). 0 means a rare or one-off part.</summary>
    public int Mark { get; set; } = 1;
    public string Builder { get; set; } = "";
    public string Description { get; set; } = "";
    public int Price { get; set; }
    /// <summary>Numbers that depend on the part type. See docs/content-guide.md.</summary>
    public Dictionary<string, float> Stats { get; set; } = [];
    /// <summary>Weapons only: bolt colour.</summary>
    public string? Color { get; set; }
}

/// <summary>Stat names used in modifier dictionaries.</summary>
public static class StatKeys
{
    public const string Speed = "speed";
    public const string Acceleration = "acceleration";
    public const string Agility = "agility";
    public const string Hull = "hull";
    public const string Shields = "shields";
    public const string ShieldRecharge = "shieldRecharge";
    public const string WeaponRecharge = "weaponRecharge";
    public const string EnginePower = "enginePower";
    public const string Sensors = "sensors";
    public const string Stealth = "stealth";
    public const string StarRecharge = "starRecharge";
    public const string Repair = "repair";
    public const string Mining = "mining";

    public static readonly HashSet<string> All =
    [
        Speed, Acceleration, Agility, Hull, Shields, ShieldRecharge, WeaponRecharge,
        EnginePower, Sensors, Stealth, StarRecharge, Repair, Mining,
    ];
}

/// <summary>Stats each part type must define.</summary>
public static class PartStats
{
    public static readonly Dictionary<string, string[]> Required = new()
    {
        ["engine"] = ["thrust", "speedFactor"],
        ["powerPlant"] = ["pips", "capacitor", "capacitorRecharge"],
        ["shield"] = ["capacity", "recharge", "rechargeDelay"],
        ["weapon"] = ["damage", "fireRate", "energyPerShot", "projectileSpeed", "range"],
        ["sensor"] = ["range"],
        ["cargo"] = ["tonnes"],
        ["utility"] = [],
    };
}
