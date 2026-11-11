using System.Numerics;

namespace Galaxus.Core.Ships;

public sealed class Projectile(Ship owner, Vector3 position, Vector3 velocity, float damage, float life, string color, string kind = "laser")
{
    private static int _nextId;

    public int Id { get; } = Interlocked.Increment(ref _nextId);
    public Ship Owner { get; } = owner;
    public Vector3 Position = position;
    /// <summary>Changes for homing shots as they turn onto their target.</summary>
    public Vector3 Velocity = velocity;
    public float Damage { get; } = damage;
    public float Life = life;
    public string Color { get; } = color;
    /// <summary>The weapon kind it came from, which sets how it flies and looks.</summary>
    public string Kind { get; } = kind;
    /// <summary>What a missile or torpedo is chasing.</summary>
    public Ship? Target { get; set; }

    public bool Homing => Kind is "missile" or "torpedo";
    /// <summary>Degrees per second a homing shot can turn.</summary>
    public float TurnRate => Kind == "missile" ? 110 : 30;
    /// <summary>Flak bursts when it gets this close to an enemy, and hurts everything in the burst.</summary>
    public const float FlakBurst = 70;
}

public abstract record WorldEvent;
public sealed record ShotFired(Projectile Projectile) : WorldEvent;
public sealed record ProjectileExpired(Projectile Projectile) : WorldEvent;
public sealed record ShipHit(Ship Ship, Projectile Projectile, Vector3 Point, DamageResult Damage) : WorldEvent;
public sealed record ShipDestroyed(Ship Ship, Ship? By) : WorldEvent;
/// <summary>A flak shell went off, here.</summary>
public sealed record FlakBurst(Projectile Projectile, Vector3 Point) : WorldEvent;
/// <summary>A decoy drew a missile off the ship it was chasing.</summary>
public sealed record MissileDecoyed(Projectile Projectile, Ship Ship) : WorldEvent;
/// <summary>Someone fired on a side they weren't at war with, and now they are.</summary>
public sealed record SidesTurnedHostile(int Attacker, int Victim) : WorldEvent;

/// <summary>
/// One local space: the ships near the player and the shots between them. The game layer reads
/// Events after each step to drive visuals and sound.
/// </summary>
public sealed class World
{
    /// <summary>Bolts are a few metres across, which makes small fast targets fair to hit.</summary>
    public const float ProjectileRadius = 2.5f;

    private readonly List<Projectile> _newShots = [];

    public List<Ship> Ships { get; } = [];
    public List<Projectile> Projectiles { get; } = [];
    public List<WorldEvent> Events { get; } = [];

    private readonly HashSet<(int, int)> _hostile = [];

    /// <summary>
    /// Sides are at peace unless declared hostile. Shooting a side you're at peace with makes it
    /// hostile to you, so firing on the police or a trader has consequences.
    /// </summary>
    public bool Hostile(int a, int b) => a != b && _hostile.Contains(a < b ? (a, b) : (b, a));

    public void SetHostile(int a, int b, bool hostile = true)
    {
        if (a == b) return;
        var key = a < b ? (a, b) : (b, a);
        if (hostile) _hostile.Add(key); else _hostile.Remove(key);
    }

    public bool Hostile(Ship a, Ship b) => Hostile(a.Side, b.Side);

    public void Step(float dt)
    {
        Events.Clear();
        _newShots.Clear();

        foreach (var ship in Ships) ship.Update(dt, _newShots);
        foreach (var ship in Ships)
            if (ship.Alive && ship.Spec.Weapons.Any(w => w.Turret)) ship.UpdateTurrets(dt, this, _newShots);
        foreach (var shot in _newShots) Launch(shot);

        for (int i = Projectiles.Count - 1; i >= 0; i--)
        {
            var shot = Projectiles[i];
            if (shot.Homing && shot.Target is { Alive: true } chased) Steer(shot, chased, dt);
            if (shot.Kind == "flak" && FlakTriggered(shot))
            {
                Burst(shot);
                Projectiles.RemoveAt(i);
                continue;
            }
            var start = shot.Position;
            var end = start + shot.Velocity * dt;
            shot.Position = end;
            shot.Life -= dt;

            var hit = FindHit(shot, start, end);
            if (hit != null)
            {
                var (ship, point) = hit.Value;
                if (!Hostile(shot.Owner, ship))
                {
                    SetHostile(shot.Owner.Side, ship.Side);
                    Events.Add(new SidesTurnedHostile(shot.Owner.Side, ship.Side));
                }
                var damage = ship.TakeDamage(shot.Damage);
                Events.Add(new ShipHit(ship, shot, point, damage));
                if (damage.Destroyed) Events.Add(new ShipDestroyed(ship, shot.Owner));
                Projectiles.RemoveAt(i);
            }
            else if (shot.Life <= 0)
            {
                if (shot.Kind == "flak") Burst(shot);
                else Events.Add(new ProjectileExpired(shot));
                Projectiles.RemoveAt(i);
            }
        }
    }

    /// <summary>Puts a new shot into the world. A decoy on its target may pull a missile off as it launches.</summary>
    public void Launch(Projectile shot)
    {
        if (shot.Homing && shot.Target is { DecoysLeft: > 0 } mark)
        {
            mark.DecoysLeft--;
            if ((Rng.Hash("decoy:" + shot.Id) % 1000) / 1000f < mark.Spec.DecoyChance)
            {
                shot.Target = null;
                Events.Add(new MissileDecoyed(shot, mark));
            }
        }
        Projectiles.Add(shot);
        Events.Add(new ShotFired(shot));
    }

    /// <summary>Turns a homing shot towards where its target will be, at its turn rate.</summary>
    private static void Steer(Projectile shot, Ship target, float dt)
    {
        float speed = shot.Velocity.Length();
        var aim = LeadPoint(shot.Position, Vector3.Zero, target.Body.Position, target.Body.Velocity, speed) ?? target.Body.Position;
        var want = Vector3.Normalize(aim - shot.Position);
        var now = Vector3.Normalize(shot.Velocity);
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(now, want), -1, 1));
        float max = shot.TurnRate * MathF.PI / 180 * dt;
        var dir = angle <= max ? want : Vector3.Normalize(Slerp(now, want, max / angle));
        shot.Velocity = dir * speed;
    }

    private static Vector3 Slerp(Vector3 a, Vector3 b, float t)
    {
        float dot = Math.Clamp(Vector3.Dot(a, b), -1, 1);
        float theta = MathF.Acos(dot) * t;
        var relative = b - a * dot;
        if (relative.LengthSquared() < 1e-9f) return a;
        return a * MathF.Cos(theta) + Vector3.Normalize(relative) * MathF.Sin(theta);
    }

    private bool FlakTriggered(Projectile shot)
    {
        foreach (var ship in Ships)
            if (ship.Alive && Hostile(shot.Owner, ship) && DistanceToShip(ship, shot.Position) < Projectile.FlakBurst)
                return true;
        return false;
    }

    /// <summary>A flak shell going off hurts every enemy in the burst, less at the edge.</summary>
    private void Burst(Projectile shot)
    {
        Events.Add(new FlakBurst(shot, shot.Position));
        foreach (var ship in Ships)
        {
            if (!ship.Alive || !Hostile(shot.Owner, ship)) continue;
            float d = DistanceToShip(ship, shot.Position);
            if (d > Projectile.FlakBurst * 1.5f) continue;
            float falloff = Math.Clamp(1 - d / (Projectile.FlakBurst * 1.5f), 0.2f, 1);
            var damage = ship.TakeDamage(shot.Damage * falloff);
            Events.Add(new ShipHit(ship, shot, shot.Position, damage));
            if (damage.Destroyed) Events.Add(new ShipDestroyed(ship, shot.Owner));
        }
    }

    /// <summary>
    /// Distance from a point to a ship's hull, treating the ship as a capsule along its length, so
    /// long warships can be hit anywhere along them.
    /// </summary>
    public static float DistanceToShip(Ship ship, Vector3 point)
    {
        float r = ship.Spec.HitRadius;
        float half = MathF.Max(0, ship.Spec.HalfLength - r);
        if (half < 1) return MathF.Max(0, Vector3.Distance(point, ship.Body.Position) - r);
        var axis = Vector3.Transform(Vector3.UnitZ, ship.Body.Orientation) * half;
        var (_, d) = ClosestOnSegment(ship.Body.Position - axis, ship.Body.Position + axis, point);
        return MathF.Max(0, d - r);
    }

    private (Ship, Vector3)? FindHit(Projectile shot, Vector3 start, Vector3 end)
    {
        Ship? best = null;
        Vector3 bestPoint = default;
        float bestT = float.MaxValue;

        foreach (var ship in Ships)
        {
            if (!ship.Alive || ship == shot.Owner || ship.Side == shot.Owner.Side) continue;
            // Torpedoes are heavy anti-ship weapons: they pass small craft by rather than vaporising a fighter in the way.
            if (shot.Kind == "torpedo" && !ship.Spec.Hull.IsCapital) continue;
            float half = MathF.Max(0, ship.Spec.HalfLength - ship.Spec.HitRadius);
            float t, distance;
            if (half < 1) (t, distance) = ClosestOnSegment(start, end, ship.Body.Position);
            else
            {
                // A long ship: closest approach between the shot's path and the ship's spine.
                var axis = Vector3.Transform(Vector3.UnitZ, ship.Body.Orientation) * half;
                (t, distance) = SegmentToSegment(start, end, ship.Body.Position - axis, ship.Body.Position + axis);
            }
            if (distance <= ship.Spec.HitRadius + ProjectileRadius && t < bestT)
            {
                best = ship;
                bestT = t;
                bestPoint = Vector3.Lerp(start, end, t);
            }
        }
        return best == null ? null : (best, bestPoint);
    }

    /// <summary>Closest approach between segments a-b and c-d: the parameter on a-b, and the distance.</summary>
    public static (float T, float Distance) SegmentToSegment(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        var u = b - a; var v = d - c; var w = a - c;
        float A = Vector3.Dot(u, u), B = Vector3.Dot(u, v), C = Vector3.Dot(v, v), D = Vector3.Dot(u, w), E = Vector3.Dot(v, w);
        float den = A * C - B * B;
        float s = den < 1e-9f ? 0 : Math.Clamp((B * E - C * D) / den, 0, 1);
        float t = C < 1e-9f ? 0 : Math.Clamp((B * s + E) / C, 0, 1);
        s = A < 1e-9f ? 0 : Math.Clamp((B * t - D) / A, 0, 1);
        return (s, Vector3.Distance(a + u * s, c + v * t));
    }

    /// <summary>Parameter t in [0, 1] of the closest point to p on segment a-b, and the distance to it.</summary>
    public static (float T, float Distance) ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        var ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared < 1e-9f ? 0 : Math.Clamp(Vector3.Dot(p - a, ab) / lengthSquared, 0, 1);
        return (t, Vector3.Distance(a + ab * t, p));
    }

    /// <summary>
    /// Where to aim to hit a target moving at constant velocity, for the lead indicator.
    /// Returns null if the shot can't catch it.
    /// </summary>
    public static Vector3? LeadPoint(Vector3 shooter, Vector3 shooterVelocity, Vector3 target, Vector3 targetVelocity, float shotSpeed)
    {
        var relativePosition = target - shooter;
        var relativeVelocity = targetVelocity - shooterVelocity;
        float a = relativeVelocity.LengthSquared() - shotSpeed * shotSpeed;
        float b = 2 * Vector3.Dot(relativePosition, relativeVelocity);
        float c = relativePosition.LengthSquared();

        float time;
        if (MathF.Abs(a) < 1e-6f)
        {
            if (MathF.Abs(b) < 1e-6f) return null;
            time = -c / b;
        }
        else
        {
            float discriminant = b * b - 4 * a * c;
            if (discriminant < 0) return null;
            float root = MathF.Sqrt(discriminant);
            float t1 = (-b - root) / (2 * a), t2 = (-b + root) / (2 * a);
            time = MathF.Min(t1, t2) > 0 ? MathF.Min(t1, t2) : MathF.Max(t1, t2);
        }
        return time > 0 ? target + relativeVelocity * time : null;
    }
}
