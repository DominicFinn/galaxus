namespace Galaxus.Core;

/// <summary>
/// Everything the difficulty setting changes, in one place. Battles stay big at every level:
/// difficulty changes how dangerous the crowd is to you, not how many ships are in it. Easy is
/// generous, Normal fair, Hard as the battles were designed.
/// </summary>
public sealed record Difficulty
{
    public string Name { get; init; } = "Hard";
    /// <summary>Damage you take, as a multiple.</summary>
    public float DamageTaken { get; init; } = 1;
    /// <summary>Enemy pilot skill, as a multiple.</summary>
    public float EnemySkill { get; init; } = 1;
    /// <summary>How fast enemy guns fire, as a multiple.</summary>
    public float EnemyFireRate { get; init; } = 1;
    /// <summary>Damage enemy shots do, as a multiple.</summary>
    public float EnemyDamage { get; init; } = 1;
    /// <summary>Enemy aim assist, as a multiple.</summary>
    public float EnemyAim { get; init; } = 1;
    /// <summary>Enemy warship turrets: accuracy against fighters, and how much they'd rather shoot someone other than you.</summary>
    public float TurretAccuracy { get; init; } = 1;
    public float TurretPlayerWeight { get; init; } = 1;
    /// <summary>How much punishment enemy warships take before they go down, as a multiple.</summary>
    public float EnemyWarshipToughness { get; init; } = 1;
    /// <summary>Damage from enemy flak, as a multiple: the warships' answer to fighters.</summary>
    public float FlakDamage { get; init; } = 1;
    /// <summary>The most enemy pilots that go after you at once; the rest fight your side.</summary>
    public int MaxAttackers { get; init; } = 99;
    /// <summary>Allied fighter groups are this much bigger, so the battle stays even around you.</summary>
    public float AllyScale { get; init; } = 1;
    /// <summary>Extra wingmen on your wing, and how good they are.</summary>
    public int ExtraWingmen { get; init; }
    public float WingmanSkill { get; init; } = 0.65f;
    /// <summary>Hull and shields of ships you must protect or get away, as a multiple.</summary>
    public float ProtectedToughness { get; init; } = 1;
    /// <summary>"Survive until" timers, as a multiple: shorter is easier.</summary>
    public float SurviveTime { get; init; } = 1;
    /// <summary>Your aim assist cone in degrees.</summary>
    public float PlayerAimAssist { get; init; } = 5;
    /// <summary>Your shield recharge, as a multiple.</summary>
    public float ShieldRecharge { get; init; } = 1;
    /// <summary>How fast your missiles lock, as a multiple.</summary>
    public float LockSpeed { get; init; } = 1;
    /// <summary>
    /// The second wind: how much the enemies near you ease off when you're clearly losing (low on
    /// hull and shields, outnumbered, or about to lose a ship you're protecting), 0 to 1. It works
    /// at every level, gently on Hard.
    /// </summary>
    public float SecondWind { get; init; } = 0.35f;

    public static readonly Difficulty Easy = new()
    {
        Name = "Easy", DamageTaken = 0.4f, EnemySkill = 0.55f, EnemyFireRate = 0.6f, EnemyDamage = 0.65f, EnemyAim = 0.3f,
        TurretAccuracy = 0.3f, TurretPlayerWeight = 6f, FlakDamage = 0.2f, EnemyWarshipToughness = 0.55f,
        MaxAttackers = 2, AllyScale = 1.6f, ExtraWingmen = 2, WingmanSkill = 0.85f, ProtectedToughness = 2f, SurviveTime = 0.7f,
        PlayerAimAssist = 9, ShieldRecharge = 1.7f, LockSpeed = 1.7f, SecondWind = 1f,
    };

    public static readonly Difficulty Normal = new()
    {
        Name = "Normal", DamageTaken = 0.75f, EnemySkill = 0.8f, EnemyFireRate = 0.8f, EnemyDamage = 0.85f, EnemyAim = 0.7f,
        TurretAccuracy = 0.55f, TurretPlayerWeight = 2.5f, FlakDamage = 0.5f, EnemyWarshipToughness = 0.8f,
        MaxAttackers = 4, AllyScale = 1.25f, ExtraWingmen = 1, WingmanSkill = 0.75f, ProtectedToughness = 1.4f, SurviveTime = 0.85f,
        PlayerAimAssist = 6.5f, ShieldRecharge = 1.25f, LockSpeed = 1.3f, SecondWind = 0.7f,
    };

    /// <summary>Hard: the battles as designed, with sharper enemy pilots.</summary>
    public static readonly Difficulty Hard = new() { Name = "Hard", EnemySkill = 1.1f, EnemyFireRate = 1.1f, TurretPlayerWeight = 0.8f };

    /// <summary>0 easy, 1 normal, 2 hard.</summary>
    public static Difficulty For(int level) => level switch { 0 => Easy, 1 => Normal, _ => Hard };
}
