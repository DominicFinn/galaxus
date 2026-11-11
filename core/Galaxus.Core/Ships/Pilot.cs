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
    /// <summary>Sits still: a beacon, a stranded ship, a listening post.</summary>
    Holding,
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
    private float _clock;
    /// <summary>Seconds until the pilot has settled on a new target and can open fire.</summary>
    private float _settle;
    /// <summary>How long the target has been held in the sights: firing needs a steady aim.</summary>
    private float _tracking;
    /// <summary>Firing comes in bursts: seconds left of this burst, then of the pause after it.</summary>
    private float _burst, _pause;
    private Ship? _lastTarget;
    private Vector3 _aimWander;

    /// <summary>How good the pilot is, 0 to 1. Better pilots lead shots tighter and jink harder.</summary>
    public float Skill { get => _skill; set => _skill = Math.Clamp(value, 0.1f, 1); }

    /// <summary>Difficulty: scales this pilot's aim assist.</summary>
    public float AimScale { get; set; } = 1f;

    /// <summary>Difficulty: the most pilots on this side that go after the player at once.</summary>
    public int MaxOnPlayer { get; set; } = int.MaxValue;

    /// <summary>How many other pilots on this ship's side are already after the player.</summary>
    private int ChasingPlayer(Ship ship, World world)
    {
        int n = 0;
        if (Player == null) return 0;
        foreach (var s in world.Ships) if (s != ship && s.Alive && s.Side == ship.Side && s.Target == Player) n++;
        return n;
    }

    public void Think(Ship ship, World world, float dt)
    {
        if (!ship.Alive) return;
        ref var c = ref ship.Controls;
        c.Flight.Assist = true;
        c.Flight.Roll = 0;
        c.Fire = false;
        ship.AiAimAssistDegrees = _skill * _skill * 2.2f * AimScale;
        // Too many already after the player: find someone else to fight.
        if (Player != null && ship.Target == Player && ChasingPlayer(ship, world) >= MaxOnPlayer) ship.Target = null;

        // Being hit makes a pilot jink for a moment.
        if (_lastHull >= 0 && ship.Hull < _lastHull) _jinkTime = 1.2f + rng.NextFloat();
        _lastHull = ship.Hull;
        _jinkTime = MathF.Max(0, _jinkTime - dt);
        _breakOff = MathF.Max(0, _breakOff - dt);
        _retarget -= dt;
        _clock += dt;

        switch (Role)
        {
            case PilotRole.Holding:
                c.Flight.Throttle = 0;
                c.Flight.Pitch = 0;
                c.Flight.Yaw = 0;
                break;
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
        if (target != _lastTarget)
        {
            // A new target takes a moment to pick up and line up on.
            _lastTarget = target;
            _settle = 1.2f + (1 - _skill) * 2.5f + rng.NextFloat();
            _tracking = 0;
        }
        _settle = MathF.Max(0, _settle - dt);

        var toTarget = target.Body.Position - ship.Body.Position;
        float distance = toTarget.Length() - target.Spec.Radius;
        var weapon = ship.Spec.Weapons.FirstOrDefault(w => !w.Turret);
        float speed = weapon?.ProjectileSpeed ?? 1000;
        var lead = World.LeadPoint(ship.Body.Position, ship.Body.Velocity, target.Body.Position, target.Body.Velocity, speed) ?? target.Body.Position;
        // Aim wanders, slowly, more for weaker pilots and at longer range: nobody snaps onto a mark.
        if (rng.NextFloat() < dt * 1.5f)
            _aimWander = new Vector3(rng.Range(-1, 1), rng.Range(-1, 1), rng.Range(-1, 1)) * (0.12f + (1 - _skill) * 1.4f);
        lead += _aimWander * MathF.Max(18, distance * 0.03f);

        // Someone on my tail: break hard and jink.
        if (target.Target == ship && distance < 1500 && _breakOff <= 0 && _jinkTime <= 0
            && Vector3.Dot(target.Body.Forward, Vector3.Normalize(ship.Body.Position - target.Body.Position)) > 0.94f
            && rng.NextFloat() < dt * (0.4f + _skill * 0.8f))
        {
            _jinkTime = 1.5f + rng.NextFloat() * 1.5f;
            _breakOff = 1.6f;
            var side = Vector3.Normalize(Vector3.Cross(ship.Body.Forward, ship.Body.Up));
            _breakDir = Vector3.Normalize(side * (rng.NextFloat() < 0.5f ? 1 : -1) + ship.Body.Up * rng.Range(-0.6f, 0.6f));
        }

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
            float cone = weapon is { Homing: true } ? 0.85f : 0.994f - (1 - _skill) * 0.008f;
            bool lined = distance < range * 0.85f && onNose > cone;
            _tracking = lined ? _tracking + dt : MathF.Max(0, _tracking - dt * 2);
            // Fire in bursts once settled and holding the aim, then ease off and re-aim.
            if (_burst > 0) _burst -= dt;
            else if (_pause > 0) _pause -= dt;
            bool steady = _tracking > 0.35f + (1 - _skill) * 0.5f;
            if (_burst <= 0 && _pause <= 0 && steady && _settle <= 0)
            {
                _burst = 0.8f + rng.NextFloat() * 1.2f;
                _pause = 0.8f + rng.NextFloat() * (1.6f - _skill);
            }
            ship.Controls.Fire = lined && _burst > 0 && _settle <= 0;
            // A locked missile now and then, more from bombers and better pilots, and not point blank.
            if (ship.Locked && ship.MissilesLeft > 0 && distance > 700 && _settle <= 0 && (_skill >= 0.45f || Role == PilotRole.Bomber)
                && rng.NextFloat() < dt * (Role == PilotRole.Bomber ? 0.3f : 0.02f + _skill * 0.06f))
                ship.Controls.Missile = true;
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
            if (other == Player && MaxOnPlayer < int.MaxValue && ChasingPlayer(ship, world) >= MaxOnPlayer) continue;
            // Drones are markers and practice targets (scan beacons, the tutorial's targets): left alone unless ordered.
            if (other.Spec.Hull.Class == "drone" && other != Assigned) continue;
            float d = Vector3.Distance(centre, other.Body.Position);
            // Stealth gear shrinks the range a ship is noticed at.
            if (d > radius * other.Spec.Detection) continue;
            bool big = other.Spec.Hull.IsCapital;
            float score = d;
            // Pilots spread out: a target others are already chasing is less attractive.
            int chasers = 0;
            foreach (var s in world.Ships) if (s != ship && s.Alive && s.Target == other && s.Side == ship.Side) chasers++;
            score *= 1 + chasers * 0.7f;
            if (other == Player && Role != PilotRole.Interceptor) score *= 1.3f;
            switch (Role)
            {
                case PilotRole.Bomber: score *= big ? (other == Assigned ? 0.1f : 0.3f) : 3f; break;
                case PilotRole.Interceptor: score *= other == Player ? 0.45f : big ? 4f : 1f; break;
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
