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
    /// <summary>How many light years of direct jumps a full tank covers. Gates and flying within a system use none.</summary>
    public float FuelTankLy { get; init; }
    public IReadOnlyList<WeaponSpec> Weapons { get; init; } = [];
    /// <summary>Homing missiles carried in the rack, refilled at a station. Fighters and gunships carry most.</summary>
    public int Missiles { get; init; }
    /// <summary>The rack's missile: damage, speed and range.</summary>
    public WeaponSpec? Missile { get; init; }

    /// <summary>The part in each of the hull's slots, in slot order, or null where a slot is empty.</summary>
    public IReadOnlyList<PartDef?> Slotted { get; init; } = [];
    /// <summary>Hull points patched per second by repair gear, once the shields are back up.</summary>
    public float HullRepair { get; init; }
    /// <summary>Light years of fuel a scoop gathers per second close to a star.</summary>
    public float FuelScoop { get; init; }
    /// <summary>Decoys carried to break missile locks, and the chance each one works.</summary>
    public int Decoys { get; init; }
    public float DecoyChance { get; init; }
    /// <summary>How far away enemies notice you, as a fraction of normal: under 1 with stealth gear.</summary>
    public float Detection { get; init; } = 1;
    /// <summary>Collision damage taken, as a fraction of normal.</summary>
    public float CollisionDamage { get; init; } = 1;
    /// <summary>Cargo space: the hull's hold, fitted racks and any hidden compartments.</summary>
    public int CargoTonnes { get; init; }
    /// <summary>Of which hidden from customs.</summary>
    public int HiddenTonnes { get; init; }
    /// <summary>Fuel used on a direct jump, as a fraction of the distance.</summary>
    public float JumpFuel { get; init; } = 1;
    /// <summary>An afterburner: top speed and thrust multiplied for a few seconds, then a wait.</summary>
    public float BoostFactor { get; init; } = 1;
    public float BoostSeconds { get; init; }
    public float BoostCooldown { get; init; }
    /// <summary>Off the price of goods bought at Free Machine ports.</summary>
    public float MarketDiscount { get; init; }
    /// <summary>Fitted mining gear's strength, for when mining arrives.</summary>
    public float Mining { get; init; }
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
        var slotted = Assign(db, hull, loadout);
        var parts = slotted.Where(p => p != null).Select(p => p!).ToList();
        float gTolerance = db.Races.TryGetValue(pilotRace ?? "human", out var race) ? race.GTolerance : 9f;

        float Bias(string stat) => builder.StatBias.GetValueOrDefault(stat, 1f);
        float Sum(string type, string stat) => parts.Where(p => p.Type == type).Sum(p => p.Stats.GetValueOrDefault(stat));

        var engines = parts.Where(p => p.Type == "engine").ToList();
        float thrust = engines.Sum(p => p.Stats["thrust"]);
        float speedFactor = engines.Count > 0 ? engines.Average(p => p.Stats["speedFactor"]) : 0f;

        // Each weapon sits on its own slot, which can carry a hardpoint position.
        var weapons = new List<WeaponSpec>();
        for (int i = 0; i < slotted.Length; i++)
        {
            if (slotted[i] is not { Type: "weapon" } part) continue;
            var slot = hull.Slots[i];
            var at = slot?.Position is { Length: 3 } pos ? new Vector3(pos[0], pos[1], pos[2]) : Vector3.Zero;
            weapons.Add(new WeaponSpec(part.Id, part.Name, part.Stats["damage"], part.Stats["fireRate"],
                part.Stats["energyPerShot"], part.Stats["projectileSpeed"], part.Stats["range"], at,
                part.Color ?? "#ff5533", part.Kind, slot?.Turret ?? false));
        }

        // Utility gear. Builders name the same effect in different ways, so they're gathered here.
        var gear = parts.Where(p => p.Type == "utility").ToList();
        float Gear(params string[] stats) => gear.Sum(p => stats.Sum(s => p.Stats.GetValueOrDefault(s)));
        float Product(string stat) => gear.Aggregate(1f, (a, p) => a * p.Stats.GetValueOrDefault(stat, 1f));
        float mass = hull.Mass + Gear("massTonnes");
        int hold = hull.CargoTonnes + (int)Sum("cargo", "tonnes");
        int hidden = (int)Gear("hiddenCargoTonnes");
        float scanners = gear.Where(p => p.Stats.ContainsKey("detail")).Select(p => p.Stats.GetValueOrDefault("range")).DefaultIfEmpty(0).Max();
        float decoyChance = gear.Select(p => p.Stats.GetValueOrDefault("lockBreakChance")).DefaultIfEmpty(0).Max();

        float agility = Bias(StatKeys.Agility);
        return new ShipSpec
        {
            Hull = hull,
            Builder = builder,
            Parts = parts,
            MaxSpeed = hull.BaseSpeed * speedFactor * Bias(StatKeys.Speed),
            Acceleration = thrust / mass * Bias(StatKeys.Acceleration),
            PitchRate = hull.PitchRate * DegToRad * agility,
            YawRate = hull.YawRate * DegToRad * agility,
            RollRate = hull.RollRate * DegToRad * agility,
            GTolerance = gTolerance,
            MaxHull = (hull.HullPoints + Gear("hullPoints")) * Bias(StatKeys.Hull),
            MaxShields = Sum("shield", "capacity") * Bias(StatKeys.Shields),
            ShieldRecharge = Sum("shield", "recharge") * Bias(StatKeys.ShieldRecharge),
            ShieldRechargeDelay = parts.Where(p => p.Type == "shield").Select(p => p.Stats["rechargeDelay"]).DefaultIfEmpty(0).Max(),
            CapacitorMax = Sum("powerPlant", "capacitor"),
            CapacitorRecharge = Sum("powerPlant", "capacitorRecharge") * Bias(StatKeys.WeaponRecharge) * (1 + Gear("starRecharge")),
            Pips = (int)Sum("powerPlant", "pips"),
            SensorRange = MathF.Max(MathF.Max(Sum("sensor", "range") * Bias(StatKeys.Sensors), scanners) * (1 + Gear("sensorRange")), 2000f),
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
            // Generous, so exploring off the gate network feels open: explorers go furthest.
            // Drop tanks add 5 ly a tonne.
            FuelTankLy = (hull.FuelTankLy > 0 ? hull.FuelTankLy : hull.Class switch
            {
                "explorer" => 300, "courier" => 150, "freighter" => 120, "miner" => 120, "gunship" => 90, "fighter" => 60, _ => 0,
            }) + Gear("fuelTonnes") * 5,
            Weapons = weapons,
            Missiles = hull.Class switch { "fighter" => 6, "gunship" => 8, "explorer" => 4, "courier" => 2, _ => 0 },
            Missile = new WeaponSpec("missile-rack", "Homing missile", 70f, 1.2f, 0, 520, 4200, new Vector3(0, -1.2f, -2), "#ffcf70", "missile"),
            Slotted = slotted,
            HullRepair = Gear("repairRate", "repairPerSecond", "repairHullPerSecond", "hullRepairPerSecond"),
            // A scoop rate of 2 fills about 2 ly a second skimming a star.
            FuelScoop = Gear("scoopRate"),
            Decoys = (int)Gear("missileDecoyCharges"),
            DecoyChance = decoyChance,
            Detection = Product("detectionRangeFactor"),
            CollisionDamage = Product("collisionDamageFactor"),
            CargoTonnes = (int)(hold * (1 + Gear("cargoCapacityBonus"))) + hidden,
            HiddenTonnes = hidden,
            JumpFuel = Math.Clamp(1 - Gear("gateFuelSaving"), 0.5f, 1),
            BoostFactor = gear.Select(p => p.Stats.GetValueOrDefault("boostSpeedFactor", 1)).DefaultIfEmpty(1).Max(),
            BoostSeconds = gear.Select(p => p.Stats.GetValueOrDefault("boostSeconds")).DefaultIfEmpty(0).Max(),
            BoostCooldown = gear.Select(p => p.Stats.GetValueOrDefault("cooldownSeconds")).DefaultIfEmpty(0).Max(),
            MarketDiscount = Math.Clamp(Gear("freeMachineMarketDiscount"), 0, 0.3f),
            Mining = Gear("miningRate", "mining", "cuttingRate"),
        };
    }

    /// <summary>
    /// Puts a loadout into the hull's slots: one part per slot, of the slot's type and no bigger.
    /// A loadout saved slot by slot (with empty strings for empty slots) keeps its places; any other
    /// list of parts goes into the smallest slot each fits, in order. Parts that don't fit are left off.
    /// </summary>
    public static PartDef?[] Assign(ContentDatabase db, HullDef hull, IEnumerable<string>? loadout)
    {
        var ids = (loadout ?? hull.DefaultLoadout).ToList();
        var slots = new PartDef?[hull.Slots.Count];
        static bool Fits(SlotDef slot, PartDef part) => slot.Type == part.Type && part.Size <= slot.Size;
        bool positional = ids.Count == slots.Length && ids.Select((id, i) => id == "" || (db.Parts.TryGetValue(id, out var p) && Fits(hull.Slots[i], p))).All(ok => ok);
        if (positional)
        {
            for (int i = 0; i < slots.Length; i++) slots[i] = ids[i] == "" ? null : db.Parts[ids[i]];
            return slots;
        }
        foreach (var id in ids)
        {
            if (!db.Parts.TryGetValue(id, out var part)) continue;
            int best = -1;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == null && Fits(hull.Slots[i], part) && (best < 0 || hull.Slots[i].Size < hull.Slots[best].Size)) best = i;
            if (best >= 0) slots[best] = part;
        }
        return slots;
    }
}
