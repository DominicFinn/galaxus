using System.Numerics;

namespace Galaxus.Core.Ships;

public sealed class Projectile(Ship owner, Vector3 position, Vector3 velocity, float damage, float life, string color)
{
    private static int _nextId;

    public int Id { get; } = Interlocked.Increment(ref _nextId);
    public Ship Owner { get; } = owner;
    public Vector3 Position = position;
    public Vector3 Velocity { get; } = velocity;
    public float Damage { get; } = damage;
    public float Life = life;
    public string Color { get; } = color;
}

public abstract record WorldEvent;
public sealed record ShotFired(Projectile Projectile) : WorldEvent;
public sealed record ProjectileExpired(Projectile Projectile) : WorldEvent;
public sealed record ShipHit(Ship Ship, Projectile Projectile, Vector3 Point, DamageResult Damage) : WorldEvent;
public sealed record ShipDestroyed(Ship Ship, Ship? By) : WorldEvent;

/// <summary>
/// One local space: the ships near the player and the shots between them. The game layer reads
/// Events after each step to drive visuals and sound.
/// </summary>
public sealed class World
{
    private readonly List<Projectile> _newShots = [];

    public List<Ship> Ships { get; } = [];
    public List<Projectile> Projectiles { get; } = [];
    public List<WorldEvent> Events { get; } = [];

    public void Step(float dt)
    {
        Events.Clear();
        _newShots.Clear();

        foreach (var ship in Ships) ship.Update(dt, _newShots);
        foreach (var shot in _newShots)
        {
            Projectiles.Add(shot);
            Events.Add(new ShotFired(shot));
        }

        for (int i = Projectiles.Count - 1; i >= 0; i--)
        {
            var shot = Projectiles[i];
            var start = shot.Position;
            var end = start + shot.Velocity * dt;
            shot.Position = end;
            shot.Life -= dt;

            var hit = FindHit(shot, start, end);
            if (hit != null)
            {
                var (ship, point) = hit.Value;
                var damage = ship.TakeDamage(shot.Damage);
                Events.Add(new ShipHit(ship, shot, point, damage));
                if (damage.Destroyed) Events.Add(new ShipDestroyed(ship, shot.Owner));
                Projectiles.RemoveAt(i);
            }
            else if (shot.Life <= 0)
            {
                Events.Add(new ProjectileExpired(shot));
                Projectiles.RemoveAt(i);
            }
        }
    }

    private (Ship, Vector3)? FindHit(Projectile shot, Vector3 start, Vector3 end)
    {
        Ship? best = null;
        Vector3 bestPoint = default;
        float bestT = float.MaxValue;

        foreach (var ship in Ships)
        {
            if (!ship.Alive || ship == shot.Owner || ship.Side == shot.Owner.Side) continue;
            var (t, distance) = ClosestOnSegment(start, end, ship.Body.Position);
            if (distance <= ship.Spec.Radius && t < bestT)
            {
                best = ship;
                bestT = t;
                bestPoint = Vector3.Lerp(start, end, t);
            }
        }
        return best == null ? null : (best, bestPoint);
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
