using System.Numerics;
using Galaxus.Core.Flight;

namespace Galaxus.Core.Ships;

/// <summary>What the pilot (player or AI) is asking the ship to do this frame.</summary>
public struct ShipControls
{
    public FlightInput Flight;
    public bool Fire;
}

/// <summary>A ship in flight: its spec, physics body, power and the state of its systems.</summary>
public sealed class Ship
{
    private int _nextWeapon;
    private float _fireCooldown;
    private float _shieldDelay;

    public int Id { get; }
    public string Name { get; set; }
    /// <summary>Ships on the same side don't damage each other.</summary>
    public int Side { get; set; }
    public ShipSpec Spec { get; }
    public FlightBody Body { get; } = new();
    public PowerDistributor Power { get; }
    public ShipControls Controls;

    public float Hull { get; private set; }
    public float Shields { get; private set; }
    public float Capacitor { get; private set; }
    public bool Alive => Hull > 0;
    public Ship? Target { get; set; }

    /// <summary>How far off the nose the guns will swing to meet a target.</summary>
    public const float AimAssistDegrees = 5f;

    public Ship(int id, string name, ShipSpec spec)
    {
        Id = id;
        Name = name;
        Spec = spec;
        Power = new PowerDistributor(spec.Pips);
        Hull = spec.MaxHull;
        Shields = spec.MaxShields;
        Capacitor = spec.CapacitorMax;
        Controls.Flight.Assist = true;
    }

    /// <summary>Flight numbers for this frame, after engine power is applied.</summary>
    public FlightStats CurrentFlightStats()
    {
        float pips = Power.Pips(ShipSystem.Engines);
        float turn = 0.9f + 0.05f * pips;
        return new FlightStats
        {
            MaxSpeed = Spec.MaxSpeed * (0.8f + 0.1f * pips),
            Acceleration = Spec.Acceleration * Power.Multiplier(ShipSystem.Engines),
            PitchRate = Spec.PitchRate * turn,
            YawRate = Spec.YawRate * turn,
            RollRate = Spec.RollRate * turn,
            AngularAcceleration = MathF.Max(Spec.PitchRate, Spec.RollRate) * 5f,
            GTolerance = Spec.GTolerance,
        };
    }

    /// <summary>Advances flight and systems. Returns any shots fired this frame.</summary>
    public void Update(float dt, List<Projectile> shotsFired)
    {
        if (!Alive) return;

        var stats = CurrentFlightStats();
        FlightModel.Step(Body, Controls.Flight, stats, dt);

        Capacitor = MathF.Min(Spec.CapacitorMax,
            Capacitor + Spec.CapacitorRecharge * Power.Multiplier(ShipSystem.Weapons) * dt);

        if (_shieldDelay > 0) _shieldDelay -= dt;
        else Shields = MathF.Min(Spec.MaxShields, Shields + Spec.ShieldRecharge * Power.Multiplier(ShipSystem.Shields) * dt);

        _fireCooldown -= dt;
        if (Controls.Fire && Spec.Weapons.Count > 0 && _fireCooldown <= 0) TryFire(shotsFired);
    }

    /// <summary>Weapons fire in turn, so two cannons give an alternating left-right stream.</summary>
    private void TryFire(List<Projectile> shotsFired)
    {
        var weapon = Spec.Weapons[_nextWeapon % Spec.Weapons.Count];
        if (Capacitor < weapon.EnergyPerShot) return;

        Capacitor -= weapon.EnergyPerShot;
        _nextWeapon++;
        _fireCooldown = 1f / (weapon.FireRate * Spec.Weapons.Count);

        var origin = Body.Position + Vector3.Transform(weapon.Hardpoint, Body.Orientation);
        var aim = Body.Forward;
        // Aim assist: if the target's lead point is within a few degrees of the nose, the guns
        // swing to it. Like gimballed mounts, it rewards getting close to the mark without
        // needing pixel-perfect aim.
        if (Target is { Alive: true } target &&
            World.LeadPoint(origin, Body.Velocity, target.Body.Position, target.Body.Velocity, weapon.ProjectileSpeed) is { } lead)
        {
            var toLead = Vector3.Normalize(lead - origin);
            if (Vector3.Dot(toLead, Body.Forward) > MathF.Cos(AimAssistDegrees * MathF.PI / 180f) && Vector3.Distance(lead, origin) < weapon.Range)
                aim = toLead;
        }
        var velocity = Body.Velocity + aim * weapon.ProjectileSpeed;
        shotsFired.Add(new Projectile(this, origin, velocity, weapon.Damage, weapon.Range / weapon.ProjectileSpeed, weapon.Color));
    }

    /// <summary>Shields soak damage first, and any hit holds off shield recharge for a while.</summary>
    public DamageResult TakeDamage(float amount)
    {
        if (!Alive) return new DamageResult(0, 0, false);
        _shieldDelay = Spec.ShieldRechargeDelay;
        float toShields = MathF.Min(Shields, amount);
        Shields -= toShields;
        float toHull = amount - toShields;
        Hull = MathF.Max(0, Hull - toHull);
        return new DamageResult(toShields, toHull, !Alive);
    }

    public void Respawn(Vector3 position, Quaternion orientation)
    {
        Hull = Spec.MaxHull;
        Shields = Spec.MaxShields;
        Capacitor = Spec.CapacitorMax;
        Body.Position = position;
        Body.Orientation = orientation;
        Body.Velocity = Vector3.Zero;
        Body.AngularVelocity = Vector3.Zero;
    }
}

public readonly record struct DamageResult(float ToShields, float ToHull, bool Destroyed);
