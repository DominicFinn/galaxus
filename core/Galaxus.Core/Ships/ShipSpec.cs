using System.Numerics;
using Galaxus.Core.Content;

namespace Galaxus.Core.Ships;

/// <summary>A weapon as fitted to a ship: the part's numbers plus where it sits.</summary>
public sealed record WeaponSpec(
    string PartId,
    string Name,
    float Damage,
    float FireRate,
    float EnergyPerShot,
    float ProjectileSpeed,
    float Range,
    Vector3 Hardpoint,
    string Color,
    /// <summary>laser, pulse, beam, plasma, rail, scatter, flak, missile or torpedo.</summary>
    string Kind = "laser",
    /// <summary>Turrets aim and fire by themselves at enemies in range.</summary>
    bool Turret = false)
{
    public bool Homing => Kind is "missile" or "torpedo";
}

/// <summary>
/// The finished numbers for one ship: hull, builder bias, fitted parts and pilot combined.
/// Built once when a ship is spawned or refitted; power distribution is applied on top each frame.
/// </summary>
public sealed class ShipSpec
{
    public required HullDef Hull { get; init; }
    public required BuilderDef Builder { get; init; }
    public required IReadOnlyList<PartDef> Parts { get; init; }

    public float MaxSpeed { get; init; }
    public float Acceleration { get; init; }
    /// <summary>Turn rates in rad/s.</summary>
    public float PitchRate { get; init; }
    public float YawRate { get; init; }
    public float RollRate { get; init; }
    public float GTolerance { get; init; }
    public float MaxHull { get; init; }
    public float MaxShields { get; init; }
    public float ShieldRecharge { get; init; }
    public float ShieldRechargeDelay { get; init; }
    public float CapacitorMax { get; init; }
    public float CapacitorRecharge { get; init; }
    public int Pips { get; init; }
    public float SensorRange { get; init; }
    public float Radius { get; init; }
    /// <summary>Half the ship's length along its nose-to-tail axis, for hits on long ships.</summary>
    public float HalfLength { get; init; }
    /// <summary>How far the hull reaches out from its spine, for hits: the width of the capsule round the ship.</summary>
    public float HitRadius { get; init; }
    /// <summary>How far the jump drive reaches between stars, in light years.</summary>
    public float JumpRangeLy { get; init; }
    public IReadOnlyList<WeaponSpec> Weapons { get; init; } = [];
}

public static class ShipFactory
{
    private const float DegToRad = MathF.PI / 180f;

    /// <param name="loadout">Part ids to fit. Null uses the hull's default loadout.</param>
    /// <param name="pilotRace">Race id of the pilot, which sets g-tolerance. Null means human.</param>
    public static ShipSpec Build(ContentDatabase db, string hullId, IEnumerable<string>? loadout = null, string? pilotRace = null)
    {
        var hull = db.Hulls[hullId];
        var builder = db.Builders[hull.Builder];
        var parts = (loadout ?? hull.DefaultLoadout).Select(id => db.Parts[id]).ToList();
        float gTolerance = db.Races.TryGetValue(pilotRace ?? "human", out var race) ? race.GTolerance : 9f;

        float Bias(string stat) => builder.StatBias.GetValueOrDefault(stat, 1f);
        float Sum(string type, string stat) => parts.Where(p => p.Type == type).Sum(p => p.Stats.GetValueOrDefault(stat));

        var engines = parts.Where(p => p.Type == "engine").ToList();
        float thrust = engines.Sum(p => p.Stats["thrust"]);
        float speedFactor = engines.Count > 0 ? engines.Average(p => p.Stats["speedFactor"]) : 0f;

        // Weapons go on weapon slots in order; each slot can carry a hardpoint position.
        var weaponSlots = hull.Slots.Where(s => s.Type == "weapon").ToList();
        var weapons = new List<WeaponSpec>();
        foreach (var part in parts.Where(p => p.Type == "weapon"))
        {
            var slot = weapons.Count < weaponSlots.Count ? weaponSlots[weapons.Count] : null;
            var at = slot?.Position is { Length: 3 } pos ? new Vector3(pos[0], pos[1], pos[2]) : Vector3.Zero;
            weapons.Add(new WeaponSpec(part.Id, part.Name, part.Stats["damage"], part.Stats["fireRate"],
                part.Stats["energyPerShot"], part.Stats["projectileSpeed"], part.Stats["range"], at,
                part.Color ?? "#ff5533", part.Kind, slot?.Turret ?? false));
        }

        float agility = Bias(StatKeys.Agility);
        return new ShipSpec
        {
            Hull = hull,
            Builder = builder,
            Parts = parts,
            MaxSpeed = hull.BaseSpeed * speedFactor * Bias(StatKeys.Speed),
            Acceleration = thrust / hull.Mass * Bias(StatKeys.Acceleration),
            PitchRate = hull.PitchRate * DegToRad * agility,
            YawRate = hull.YawRate * DegToRad * agility,
            RollRate = hull.RollRate * DegToRad * agility,
            GTolerance = gTolerance,
            MaxHull = hull.HullPoints * Bias(StatKeys.Hull),
            MaxShields = Sum("shield", "capacity") * Bias(StatKeys.Shields),
            ShieldRecharge = Sum("shield", "recharge") * Bias(StatKeys.ShieldRecharge),
            ShieldRechargeDelay = parts.Where(p => p.Type == "shield").Select(p => p.Stats["rechargeDelay"]).DefaultIfEmpty(0).Max(),
            CapacitorMax = Sum("powerPlant", "capacitor"),
            CapacitorRecharge = Sum("powerPlant", "capacitorRecharge") * Bias(StatKeys.WeaponRecharge),
            Pips = (int)Sum("powerPlant", "pips"),
            SensorRange = MathF.Max(Sum("sensor", "range") * Bias(StatKeys.Sensors), 2000f),
            Radius = hull.Radius,
            HitRadius = hull.Model.Count == 0 ? hull.Radius
                : MathF.Min(hull.Radius, hull.Model.Max(m => MathF.Max(MathF.Abs(m.Position[0]) + MathF.Max(m.Back[0], m.Front[0]) / 2,
                                                                       MathF.Abs(m.Position[1]) + MathF.Max(m.Back[1], m.Front[1]) / 2))),
            HalfLength = hull.Model.Count == 0 ? hull.Radius
                : MathF.Max(hull.Radius, hull.Model.Max(m => MathF.Abs(m.Position[2]) + m.Length / 2)),
            // Explorers are built for long jumps, fighters barely leave their system.
            JumpRangeLy = hull.Class switch
            {
                "explorer" => 24, "courier" => 12, "freighter" => 9, "miner" => 8, "gunship" => 7, "fighter" => 6, _ => 0,
            } * (0.9f + 0.1f * speedFactor) * Bias(StatKeys.Speed),
            Weapons = weapons,
        };
    }
}
