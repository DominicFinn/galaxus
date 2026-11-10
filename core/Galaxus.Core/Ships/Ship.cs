using System.Numerics;
using Galaxus.Core.Flight;

namespace Galaxus.Core.Ships;

/// <summary>What the pilot (player or AI) is asking the ship to do this frame.</summary>
public struct ShipControls
{
    public FlightInput Flight;
    public bool Fire;
    /// <summary>Launch a missile at the locked target, once per press.</summary>
    public bool Missile;
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
    /// <summary>Scales damage this ship takes: the difficulty setting, applied to the player's ship.</summary>
    public float DamageTakenScale { get; set; } = 1f;
    /// <summary>Difficulty scales: how fast this ship's guns fire and how hard its shots hit.</summary>
    public float FireRateScale { get; set; } = 1f;
    public float DamageDealtScale { get; set; } = 1f;
    /// <summary>Difficulty scales for the player: shield recharge and missile lock speed.</summary>
    public float ShieldRechargeScale { get; set; } = 1f;
    /// <summary>How long shields wait after a hit before recharging, as a multiple.</summary>
    public float ShieldDelayScale { get; set; } = 1f;
    public float LockSpeedScale { get; set; } = 1f;
    /// <summary>Difficulty scales for warship turrets: accuracy against fighters, and how much they'd rather shoot someone other than the player.</summary>
    public float TurretAccuracy { get; set; } = 1f;
    public float TurretPlayerWeight { get; set; } = 1f;
    /// <summary>Difficulty scale for this ship's flak, which bursts near fighters and hurts everything in the blast.</summary>
    public float FlakDamageScale { get; set; } = 1f;
    /// <summary>True for the player's own ship.</summary>
    public bool IsPlayer { get; set; }
    /// <summary>The player's aim assist cone in degrees, when AimAssist is on.</summary>
    public float AimAssistCone { get; set; } = AimAssistDegrees;
    public int MissilesLeft { get; private set; }
    /// <summary>How far the missile lock on the current target has built up, 0 to 1. Locked at 1.</summary>
    public float Lock { get; private set; }
    public bool Locked => Lock >= 1;
    private Ship? _lockTarget;
    private float _missileCooldown;
    /// <summary>Seconds of holding the target near the nose to get a lock.</summary>
    public const float LockSeconds = 1.6f;
    /// <summary>The target has to stay within this angle of the nose, and in range, while the lock builds.</summary>
    public const float LockConeDegrees = 16f;
    public Ship? Target { get; set; }

    /// <summary>How far off the nose the guns will swing to meet a target.</summary>
    public const float AimAssistDegrees = 5f;
    /// <summary>Gimballed guns: the full cone for the player.</summary>
    public bool AimAssist { get; set; }
    /// <summary>For AI pilots: a narrower gimbal that grows with their skill, so aces are deadly and novices aren't.</summary>
    public float AiAimAssistDegrees { get; set; }

    public Ship(int id, string name, ShipSpec spec)
    {
        Id = id;
        Name = name;
        Spec = spec;
        Power = new PowerDistributor(spec.Pips);
        Hull = spec.MaxHull;
        Shields = spec.MaxShields;
        Capacitor = spec.CapacitorMax;
        MissilesLeft = spec.Missiles;
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
        else Shields = MathF.Min(Spec.MaxShields, Shields + Spec.ShieldRecharge * ShieldRechargeScale * Power.Multiplier(ShipSystem.Shields) * dt);

        _fireCooldown -= dt;
        if (Controls.Fire && _fixed.Count > 0 && _fireCooldown <= 0) TryFire(shotsFired);
        UpdateMissiles(dt, shotsFired);
    }

    /// <summary>Restocks the missile rack, at a station.</summary>
    public void Resupply() => MissilesLeft = Spec.Missiles;

    /// <summary>
    /// The lock builds while the target stays near the nose and in range, and fades when it
    /// doesn't. Once locked, a press launches a missile that homes on the target.
    /// </summary>
    private void UpdateMissiles(float dt, List<Projectile> shotsFired)
    {
        _missileCooldown -= dt;
        var missile = Spec.Missile;
        if (missile == null || Spec.Missiles == 0 || Target is not { Alive: true } target)
        {
            Lock = 0;
            Controls.Missile = false;
            return;
        }
        if (target != _lockTarget) { _lockTarget = target; Lock = 0; }
        var to = target.Body.Position - Body.Position;
        float distance = to.Length();
        bool inCone = distance > 1 && distance < missile.Range
                      && Vector3.Dot(Vector3.Normalize(to), Body.Forward) > MathF.Cos(LockConeDegrees * MathF.PI / 180f);
        Lock = inCone ? MathF.Min(1, Lock + dt * LockSpeedScale / LockSeconds) : MathF.Max(0, Lock - dt / LockSeconds * 2);

        if (Controls.Missile && Locked && MissilesLeft > 0 && _missileCooldown <= 0)
        {
            MissilesLeft--;
            _missileCooldown = 1f / missile.FireRate;
            var origin = Body.Position + Vector3.Transform(missile.Hardpoint, Body.Orientation);
            shotsFired.Add(new Projectile(this, origin, Body.Velocity * 0.5f + Body.Forward * missile.ProjectileSpeed, missile.Damage * DamageDealtScale,
                missile.Range / missile.ProjectileSpeed * 1.6f, missile.Color, "missile") { Target = target });
        }
        Controls.Missile = false;
    }

    private List<WeaponSpec>? _fixedCache;
    /// <summary>Guns that fire where the nose points. Turrets look after themselves.</summary>
    private List<WeaponSpec> _fixed => _fixedCache ??= Spec.Weapons.Where(w => !w.Turret).ToList();
    private float[]? _turretCooldowns;

    /// <summary>Weapons fire in turn, so two cannons give an alternating left-right stream.</summary>
    private void TryFire(List<Projectile> shotsFired)
    {
        var weapons = _fixed;
        var weapon = weapons[_nextWeapon % weapons.Count];
        _nextWeapon++;
        // Missiles need a lock: without a target, skip to the next gun.
        if (weapon.Homing && Target is not { Alive: true }) return;
        if (Capacitor < weapon.EnergyPerShot) return;

        Capacitor -= weapon.EnergyPerShot;
        _fireCooldown = 1f / (weapon.FireRate * weapons.Count * FireRateScale);

        var origin = Body.Position + Vector3.Transform(weapon.Hardpoint, Body.Orientation);
        var aim = Body.Forward;
        // Aim assist: if the target's lead point is within a few degrees of the nose, the guns
        // swing to it. Like gimballed mounts, it rewards getting close to the mark without
        // needing pixel-perfect aim.
        float cone = AimAssist ? AimAssistCone : AiAimAssistDegrees;
        if (cone > 0 && Target is { Alive: true } target &&
            World.LeadPoint(origin, Body.Velocity, target.Body.Position, target.Body.Velocity, weapon.ProjectileSpeed) is { } lead)
        {
            var toLead = Vector3.Normalize(lead - origin);
            if (Vector3.Dot(toLead, Body.Forward) > MathF.Cos(cone * MathF.PI / 180f) && Vector3.Distance(lead, origin) < weapon.Range)
                aim = toLead;
        }
        Launch(weapon, origin, aim, Target, shotsFired);
    }

    /// <summary>Fires one shot (or a spread, for scatter guns) from a weapon.</summary>
    private void Launch(WeaponSpec weapon, Vector3 origin, Vector3 aim, Ship? target, List<Projectile> shotsFired)
    {
        float life = weapon.Range / weapon.ProjectileSpeed;
        if (weapon.Kind == "scatter")
        {
            // Five pellets in a cone of a few degrees, sharing the damage.
            var right = Vector3.Normalize(Vector3.Cross(aim, MathF.Abs(aim.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
            var up = Vector3.Cross(right, aim);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.2566f;
                var dir = Vector3.Normalize(aim + (right * MathF.Cos(a) + up * MathF.Sin(a)) * (i == 0 ? 0 : 0.035f));
                shotsFired.Add(new Projectile(this, origin, Body.Velocity + dir * weapon.ProjectileSpeed, weapon.Damage / 5 * 1.4f * DamageDealtScale, life, weapon.Color, weapon.Kind));
            }
            return;
        }
        var velocity = (weapon.Homing ? Vector3.Zero : Body.Velocity) + aim * weapon.ProjectileSpeed;
        float damage = weapon.Damage * DamageDealtScale * (weapon.Kind == "flak" ? FlakDamageScale : 1);
        shotsFired.Add(new Projectile(this, origin, velocity, damage, life * (weapon.Homing ? 1.6f : 1), weapon.Color, weapon.Kind)
        {
            Target = weapon.Homing ? target : null,
        });
    }

    /// <summary>
    /// Turrets each pick the nearest enemy in range (flak prefers small craft, torpedoes prefer
    /// warships), lead it, and fire on their own cooldowns, drawing on the ship's capacitor.
    /// </summary>
    public void UpdateTurrets(float dt, World world, List<Projectile> shotsFired)
    {
        var weapons = Spec.Weapons;
        _turretCooldowns ??= new float[weapons.Count];
        for (int i = 0; i < weapons.Count; i++)
        {
            var weapon = weapons[i];
            if (!weapon.Turret) continue;
            _turretCooldowns[i] -= dt;
            if (_turretCooldowns[i] > 0 || Capacitor < weapon.EnergyPerShot) continue;

            var origin = Body.Position + Vector3.Transform(weapon.Hardpoint, Body.Orientation);
            Ship? best = null;
            float bestScore = float.MaxValue;
            foreach (var other in world.Ships)
            {
                if (!other.Alive || other == this || !world.Hostile(this, other)) continue;
                float d = Vector3.Distance(origin, other.Body.Position) - other.Spec.Radius;
                if (d > weapon.Range) continue;
                bool big = other.Spec.Hull.IsCapital;
                if (other.Spec.Hull.Class == "drone") continue;
                // Torpedoes are for warships: they never go after fighters.
                if (weapon.Kind == "torpedo" && !big) continue;
                float score = d * (weapon.Kind == "torpedo" ? 0.3f : weapon.Kind == "flak" ? (big ? 3f : 0.5f) : 1f);
                if (other == Target) score *= 0.5f;
                if (other.IsPlayer) score *= TurretPlayerWeight;
                if (score < bestScore) { bestScore = score; best = other; }
            }
            if (best == null) { _turretCooldowns[i] = 0.25f; continue; }

            var aimAt = World.LeadPoint(origin, Body.Velocity, best.Body.Position, best.Body.Velocity, weapon.ProjectileSpeed) ?? best.Body.Position;
            var aim = Vector3.Normalize(aimAt - origin);
            // Turret crews aren't perfect: a little scatter, more against small fast craft.
            float wobble = best.Spec.Hull.IsCapital ? 0.004f : 0.02f / TurretAccuracy;
            aim = Vector3.Normalize(aim + new Vector3(Jitter(i, 1), Jitter(i, 2), Jitter(i, 3)) * wobble);
            Capacitor -= weapon.EnergyPerShot;
            _turretCooldowns[i] = 1f / (weapon.FireRate * FireRateScale);
            Launch(weapon, origin, aim, best, shotsFired);
        }
    }

    private int _jitterSeed;
    private float Jitter(int a, int b)
    {
        uint h = (uint)(Id * 73856093 ^ a * 19349663 ^ b * 83492791 ^ ++_jitterSeed * 2654435761);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xFFFF) / 32768f - 1;
    }

    /// <summary>Shields soak damage first, and any hit holds off shield recharge for a while.</summary>
    public DamageResult TakeDamage(float amount)
    {
        if (!Alive) return new DamageResult(0, 0, false);
        amount *= DamageTakenScale;
        _shieldDelay = Spec.ShieldRechargeDelay * ShieldDelayScale;
        float toShields = MathF.Min(Shields, amount);
        Shields -= toShields;
        float toHull = amount - toShields;
        Hull = MathF.Max(0, Hull - toHull);
        return new DamageResult(toShields, toHull, !Alive);
    }

    public void Respawn(Vector3 position, Quaternion orientation)
    {
        MissilesLeft = Spec.Missiles;
        Lock = 0;
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
