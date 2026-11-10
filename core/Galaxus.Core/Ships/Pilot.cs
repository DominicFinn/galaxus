using System.Numerics;

namespace Galaxus.Core.Ships;

/// <summary>What an AI pilot is for, which sets how it flies and what it picks on.</summary>
public enum PilotRole
{
    /// <summary>Dogfights: goes for the nearest enemy small craft, then anything.</summary>
    Fighter,
    /// <summary>Hunts the player first.</summary>
    Interceptor,
    /// <summary>Goes for warships with missiles and torpedoes, breaking away after each run.</summary>
    Bomber,
    /// <summary>Stays near a ship it guards and engages anything that comes close.</summary>
    Escort,
    /// <summary>A warship: holds a course towards its objective and lets the turrets fight.</summary>
    Capital,
    /// <summary>Flies to a destination and runs from trouble.</summary>
    Freighter,
    /// <summary>Circles a point (a station) and engages enemies that come near.</summary>
    Patrol,
    /// <summary>Lurks, then goes for traders and lone ships.</summary>
    Pirate,
}

/// <summary>
/// An AI pilot. Each frame it picks a target if it needs one, then steers, sets throttle and
/// decides whether to fire. Turrets aim themselves, so warships only have to steer.
/// </summary>
public sealed class Pilot(PilotRole role, Rng rng)
{
    public PilotRole Role { get; } = role;
    /// <summary>The ship an escort guards, or a bomber's or capital's assigned target.</summary>
    public Ship? Assigned { get; set; }
    /// <summary>Where a freighter, capital or patrol is heading or circling.</summary>
    public Vector3 Destination { get; set; }
    /// <summary>The player's ship, which interceptors and pirates like to pick on.</summary>
    public Ship? Player { get; set; }
    /// <summary>Set when a freighter reaches its destination.</summary>
    public bool Arrived { get; private set; }

    private float _breakOff;      // seconds left of turning away after a pass
    private Vector3 _breakDir;
    private float _retarget;      // seconds until it next reconsiders its target
    private float _jinkTime;
    private float _lastHull = -1;
    private float _skill = 0.6f + rng.NextFloat() * 0.4f;

    /// <summary>How good the pilot is, 0 to 1. Better pilots lead shots tighter and jink harder.</summary>
    public float Skill { get => _skill; set => _skill = Math.Clamp(value, 0.1f, 1); }

    public void Think(Ship ship, World world, float dt)
    {
        if (!ship.Alive) return;
        ref var c = ref ship.Controls;
        c.Flight.Assist = true;
        c.Flight.Roll = 0;
        c.Fire = false;

        // Being hit makes a pilot jink for a moment.
        if (_lastHull >= 0 && ship.Hull < _lastHull) _jinkTime = 1.2f + rng.NextFloat();
        _lastHull = ship.Hull;
        _jinkTime = MathF.Max(0, _jinkTime - dt);
        _breakOff = MathF.Max(0, _breakOff - dt);
        _retarget -= dt;

        switch (Role)
        {
            case PilotRole.Capital: FlyCapital(ship, world, dt); break;
            case PilotRole.Freighter: FlyFreighter(ship, world); break;
            case PilotRole.Escort:
                if (Assigned is { Alive: true } ward && Vector3.Distance(ship.Body.Position, ward.Body.Position) > 900
                    && (ship.Target == null || Vector3.Distance(ship.Target.Body.Position, ward.Body.Position) > 2500))
                {
                    ship.Target = null;
                    FlyTo(ship, ward.Body.Position + ward.Body.Velocity, 0.9f);
                    break;
                }
                Dogfight(ship, world, dt, near: Assigned?.Body.Position, radius: 2500);
                break;
            case PilotRole.Patrol:
                if (!Dogfight(ship, world, dt, near: Destination, radius: 4000))
                    Circle(ship, Destination, 1500, dt);
                break;
            default:
                if (!Dogfight(ship, world, dt, near: null, radius: Role == PilotRole.Pirate ? 5000 : 12000))
                {
                    if (Role == PilotRole.Pirate) Circle(ship, Destination, 2500, dt);
                    else FlyTo(ship, Destination, 0.6f);
                }
                break;
        }
    }

    /// <summary>Picks a target and attacks it. Returns false if there's nothing to fight.</summary>
    private bool Dogfight(Ship ship, World world, float dt, Vector3? near, float radius)
    {
        if (ship.Target is not { Alive: true } || !world.Hostile(ship, ship.Target) || _retarget <= 0)
        {
            ship.Target = PickTarget(ship, world, near, radius);
            _retarget = 3 + rng.NextFloat() * 3;
        }
        if (ship.Target is not { } target) return false;

        var toTarget = target.Body.Position - ship.Body.Position;
        float distance = toTarget.Length() - target.Spec.Radius;
        var weapon = ship.Spec.Weapons.FirstOrDefault(w => !w.Turret);
        float speed = weapon?.ProjectileSpeed ?? 1000;
        var lead = World.LeadPoint(ship.Body.Position, ship.Body.Velocity, target.Body.Position, target.Body.Velocity, speed) ?? target.Body.Position;
        // Weaker pilots aim a little off.
        lead += new Vector3(rng.Range(-1, 1), rng.Range(-1, 1), rng.Range(-1, 1)) * (1 - _skill) * 25;

        bool bigTarget = target.Spec.Hull.IsCapital;
        float tooClose = bigTarget ? target.Spec.Radius + 250 : 160;

        if (_breakOff > 0)
        {
            // Peel away after a pass, then come round again.
            FlyTowards(ship, ship.Body.Position + _breakDir * 1000, 1f);
        }
        else if (distance < tooClose)
        {
            _breakOff = bigTarget ? 5f : 2.2f;
            var side = Vector3.Normalize(Vector3.Cross(ship.Body.Forward, Vector3.UnitY) + ship.Body.Up * (rng.NextFloat() - 0.5f));
            _breakDir = Vector3.Normalize(side * (rng.NextFloat() < 0.5f ? 1 : -1) + Vector3.Normalize(ship.Body.Position - target.Body.Position));
        }
        else
        {
            // Slow down to turn tighter when the target is off to the side.
            var local = Vector3.Normalize(ship.Body.ToLocal(lead));
            float onNose = -local.Z;
            float throttle = distance > 1500 ? 1f : onNose > 0.8f ? 0.75f : 0.45f;
            FlyTowards(ship, lead, throttle);

            float range = weapon?.Range ?? 1500;
            float cone = weapon is { Homing: true } ? 0.85f : 0.992f - (1 - _skill) * 0.01f;
            ship.Controls.Fire = distance < range && onNose > cone;
        }

        if (_jinkTime > 0)
        {
            ship.Controls.Flight.Roll = MathF.Sin(_jinkTime * 7) * 0.8f;
            ship.Controls.Flight.Pitch = Math.Clamp(ship.Controls.Flight.Pitch + MathF.Cos(_jinkTime * 5) * 0.5f * _skill, -1, 1);
        }
        return true;
    }

    /// <summary>Who to go after: by role, by distance, and preferring whoever is already close.</summary>
    private Ship? PickTarget(Ship ship, World world, Vector3? near, float radius)
    {
        Ship? best = null;
        float bestScore = float.MaxValue;
        var centre = near ?? ship.Body.Position;
        foreach (var other in world.Ships)
        {
            if (!other.Alive || other == ship || !world.Hostile(ship, other)) continue;
            float d = Vector3.Distance(centre, other.Body.Position);
            if (d > radius) continue;
            bool big = other.Spec.Hull.IsCapital;
            float score = d;
            switch (Role)
            {
                case PilotRole.Bomber: score *= big ? (other == Assigned ? 0.1f : 0.3f) : 3f; break;
                case PilotRole.Interceptor: score *= other == Player ? 0.2f : big ? 4f : 1f; break;
                case PilotRole.Pirate: score *= other.Spec.Hull.Class is "freighter" or "courier" or "miner" ? 0.4f : other == Player ? 0.7f : big ? 6f : 1f; break;
                default: score *= big ? 2.5f : 1f; break;
            }
            if (other == Assigned) score *= 0.5f;
            if (score < bestScore) { bestScore = score; best = other; }
        }
        return best;
    }

    /// <summary>Warships hold a steady course towards their destination or target, and stand off at gun range.</summary>
    private void FlyCapital(Ship ship, World world, float dt)
    {
        var goal = Assigned is { Alive: true } target ? target.Body.Position : Destination;
        float distance = Vector3.Distance(ship.Body.Position, goal);
        float standOff = Assigned != null ? 3500 : 400;
        if (distance > standOff)
        {
            var (pitch, yaw) = Autopilot.SteerTowards(ship, goal, 2f);
            ship.Controls.Flight.Pitch = pitch;
            ship.Controls.Flight.Yaw = yaw;
            ship.Controls.Flight.Throttle = distance > standOff * 3 ? 0.8f : 0.35f;
        }
        else
        {
            // In range: turn broadside-ish and crawl, so the turrets on both flanks can bear.
            var side = Vector3.Normalize(Vector3.Cross(goal - ship.Body.Position, Vector3.UnitY));
            var (pitch, yaw) = Autopilot.SteerTowards(ship, ship.Body.Position + side * 1000, 1f);
            ship.Controls.Flight.Pitch = pitch * 0.5f;
            ship.Controls.Flight.Yaw = yaw * 0.5f;
            ship.Controls.Flight.Throttle = 0.15f;
        }
        // Any forward guns a warship has fire when something lines up.
        ship.Target ??= PickTarget(ship, world, null, 8000);
    }

    private void FlyFreighter(Ship ship, World world)
    {
        float distance = Vector3.Distance(ship.Body.Position, Destination);
        if (distance < 300) { Arrived = true; ship.Controls.Flight.Throttle = 0.2f; return; }
        // Under fire, open the throttle and weave.
        float throttle = _jinkTime > 0 ? 1f : 0.7f;
        FlyTowards(ship, Destination, throttle);
        if (_jinkTime > 0) ship.Controls.Flight.Yaw = Math.Clamp(ship.Controls.Flight.Yaw + MathF.Sin(_jinkTime * 3) * 0.4f, -1, 1);
    }

    private void Circle(Ship ship, Vector3 centre, float radius, float dt)
    {
        var from = ship.Body.Position - centre;
        if (from.LengthSquared() < 1) from = Vector3.UnitX;
        var tangent = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, from));
        var point = centre + Vector3.Normalize(from) * radius + tangent * radius * 0.6f;
        FlyTowards(ship, point, 0.5f);
    }

    private static void FlyTo(Ship ship, Vector3 point, float throttle) => FlyTowards(ship, point, throttle);

    private static void FlyTowards(Ship ship, Vector3 point, float throttle)
    {
        var (pitch, yaw) = Autopilot.SteerTowards(ship, point);
        ship.Controls.Flight.Pitch = pitch;
        ship.Controls.Flight.Yaw = yaw;
        ship.Controls.Flight.Throttle = throttle;
    }
}
