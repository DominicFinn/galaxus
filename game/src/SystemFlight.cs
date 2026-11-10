using Godot;
using Galaxus.Core;
using Galaxus.Core.Galaxy;
using NVector3 = System.Numerics.Vector3;

namespace Galaxus;

/// <summary>
/// Flying around a real solar system. Ship physics happen in metres around a local origin;
/// the system itself is in kilometres, with the local origin (the anchor) somewhere in it.
/// Distant bodies are drawn closer but shrunk in proportion, so they look exactly as they would
/// from where you are, and grow as you approach. The cruise drive moves you through the
/// system in kilometres per second and drops you out near your course target.
/// </summary>
public partial class Main
{
    private const double RenderDistanceM = 60_000;
    private const double MaxCruiseKmS = 30_000_000;
    private const double StationDropKm = 4;

    private SolarSystem _solar = null!;
    private readonly List<(int Index, Node3D Node)> _bodyNodes = [];
    private MeshInstance3D? _starNode;
    private (double X, double Y, double Z) _anchorKm, _cruiseKm, _stationKm;
    private int _stationBody = -1;
    private bool _cruising;
    private double _cruiseSpeed;
    /// <summary>Course target: a body index, NavStation for the station, or -1 for none.</summary>
    private int _navTarget = -1;
    private const int NavStation = -2;
    private NVector3 _stationLocal = new(0, 0, -1600);

    private (double X, double Y, double Z) PlayerKm => _cruising
        ? _cruiseKm
        : (_anchorKm.X + _player.Body.Position.X / 1000.0, _anchorKm.Y + _player.Body.Position.Y / 1000.0, _anchorKm.Z + _player.Body.Position.Z / 1000.0);

    private double Day => _session.Galaxy.Day;

    /// <summary>Builds the star and every planet and moon, and puts you beside your station's planet.</summary>
    private void BuildSystemBodies()
    {
        foreach (var (_, node) in _bodyNodes) if (IsInstanceValid(node)) node.QueueFree();
        _bodyNodes.Clear();
        if (_starNode != null && IsInstanceValid(_starNode)) _starNode.QueueFree();
        _starNode = null;

        _solar = SolarSystems.For(_session.Here, _db.Grammars);
        var (disc, _, _, _) = PlanetFactory.StarLook(PlanetFactory.FromStar(_solar.Star.Class));
        _starNode = new MeshInstance3D { Mesh = MeshFactory.Icosphere(1, 3, _ => disc, MeshFactory.Unshaded(disc)) };
        AddChild(_starNode);

        for (int i = 0; i < _solar.Bodies.Count; i++)
        {
            var body = _solar.Bodies[i];
            if (body.Kind == BodyKind.Belt) continue;
            var node = PlanetFactory.BuildPlanet(PlanetFactory.FromBody(body.Kind), new Rng(body.Seed), unit: true);
            AddChild(node);
            _bodyNodes.Add((i, node));
        }

        // Arrive just off the station, with its planet filling the view beyond it.
        _stationBody = _solar.Stations.Count > 0 ? _solar.Stations[0].Body : _solar.Bodies.FindIndex(b => !b.IsMoon && b.Kind != BodyKind.Belt);
        if (_stationBody < 0) _stationBody = 0;
        var planet = _solar.PositionKm(_stationBody, Day);
        double r = Math.Max(_solar.RadiusKm(_stationBody), 2000);
        // Sit on the sunward side a little, so the planet is lit as you look at it.
        var sun = Normalised((-planet.X, -planet.Y, -planet.Z));
        var away = Normalised((sun.X * 0.6, sun.Y * 0.6, 1.0 + sun.Z * 0.6));
        _stationKm = (planet.X + away.X * r * 1.9, planet.Y + away.Y * r * 1.9, planet.Z + away.Z * r * 1.9);
        _stationLocal = new NVector3(0, 0, -1600);
        _anchorKm = (_stationKm.X, _stationKm.Y, _stationKm.Z + 1.6);
        _cruising = false;
        _cruiseSpeed = 0;
        _navTarget = -1;
    }

    /// <summary>Places bodies, star, sunlight and station for the player's current position.</summary>
    private void UpdateSystemView()
    {
        if (_solar == null) return;
        var player = PlayerKm;
        var eye = _player.Body.Position.ToGodot();

        void Place(Node3D node, (double X, double Y, double Z) km, double radiusKm)
        {
            var rel = (X: km.X - player.X, Y: km.Y - player.Y, Z: km.Z - player.Z);
            double distM = Math.Sqrt(rel.X * rel.X + rel.Y * rel.Y + rel.Z * rel.Z) * 1000;
            if (distM < 1) return;
            double shown = Math.Min(distM, RenderDistanceM);
            var dir = new Vector3((float)(rel.X * 1000 / distM), (float)(rel.Y * 1000 / distM), (float)(rel.Z * 1000 / distM));
            // Same apparent size as the real thing, but never smaller than a dot.
            float scale = (float)Math.Max(radiusKm * 1000 * shown / distM, shown * 0.0012);
            node.Position = eye + dir * (float)shown;
            node.Scale = Vector3.One * scale;
        }

        Place(_starNode!, (0, 0, 0), _solar.StarRadiusKm);
        foreach (var (index, node) in _bodyNodes) Place(node, _solar.PositionKm(index, Day), _solar.RadiusKm(index));

        var toStar = new Vector3((float)-player.X, (float)-player.Y, (float)-player.Z);
        var (_, light, energy, _) = PlanetFactory.StarLook(PlanetFactory.FromStar(_solar.Star.Class));
        _space.FaceStar(toStar, light, energy);

        // The station is only drawn when you're near it.
        if (_stationNode != null)
        {
            var rel = ((_stationKm.X - _anchorKm.X) * 1000, (_stationKm.Y - _anchorKm.Y) * 1000, (_stationKm.Z - _anchorKm.Z) * 1000);
            _stationLocal = new NVector3((float)rel.Item1, (float)rel.Item2, (float)rel.Item3);
            _stationNode.Position = _stationLocal.ToGodot();
            _stationNode.Visible = !_cruising && _stationLocal.Length() < 80_000;
        }
    }

    private void ToggleCruise()
    {
        if (_cruising) { DropFromCruise(); return; }
        _cruiseKm = PlayerKm;
        _cruising = true;
        _cruiseSpeed = 30;
        _player.Target = null;
        foreach (var drone in _drones.Keys) _visuals[drone].Visible = false;
        foreach (var shot in _world.Projectiles) _effects.RemoveBolt(shot);
        _world.Projectiles.Clear();
        _hud.Message("CRUISE DRIVE ENGAGED");
        AudioManager.Instance?.Ui("jump");
    }

    private void DropFromCruise()
    {
        _cruising = false;
        var forward = _player.Body.Forward;
        if (_navTarget == NavStation || DistanceKm(_cruiseKm, _stationKm) < 50)
        {
            // Come out a few kilometres short of the station, facing it.
            _anchorKm = (_stationKm.X - forward.X * StationDropKm, _stationKm.Y - forward.Y * StationDropKm, _stationKm.Z - forward.Z * StationDropKm);
        }
        else _anchorKm = _cruiseKm;
        _player.Body.Position = NVector3.Zero;
        _player.Body.Velocity = forward * 80;
        _throttle = 0.5f;
        foreach (var drone in _drones.Keys)
        {
            drone.Respawn(forward * 900 + new NVector3(_rng.Range(-300, 300), _rng.Range(-100, 100), _rng.Range(-300, 300)), RandomOrientation());
            _drones[drone].Anchor = forward * 900;
        }
        _navTarget = _navTarget == NavStation ? -1 : _navTarget;
        _player.Target = NearestTarget();
        _hud.Message("DROPPED FROM CRUISE");
        AudioManager.Instance?.Ui("undock");
    }

    /// <summary>Cruise flight: speed builds up quickly, eases off near the target, and drops you out on arrival.</summary>
    private void StepCruise(float dt)
    {
        var target = NavTargetKm();
        double distance = target is { } t ? DistanceKm(_cruiseKm, t) : double.MaxValue;
        double dropAt = _navTarget == NavStation ? StationDropKm * 2 : _navTarget >= 0 ? Math.Max(_solar.RadiusKm(_navTarget) * 3, 3000) : 0;

        double wanted = target != null
            ? Math.Clamp((distance - dropAt) / 4.0, 20, MaxCruiseKmS)
            : Math.Max(0.05f, _throttle) * 2_000_000;
        _cruiseSpeed = wanted > _cruiseSpeed ? Math.Min(wanted, _cruiseSpeed * (1 + 2.4 * dt) + 5) : wanted;

        // With a course set, the ship turns towards it unless you're steering.
        var stickIdle = Mathf.Abs(_player.Controls.Flight.Pitch) < 0.08f && Mathf.Abs(_player.Controls.Flight.Yaw) < 0.08f;
        if (target is { } aim && stickIdle)
        {
            var dir = Normalised((aim.X - _cruiseKm.X, aim.Y - _cruiseKm.Y, aim.Z - _cruiseKm.Z));
            var (pitch, yaw) = Galaxus.Core.Ships.Autopilot.SteerTowards(_player,
                _player.Body.Position + new NVector3((float)dir.X, (float)dir.Y, (float)dir.Z) * 1000, 3f);
            _player.Controls.Flight.Pitch = pitch;
            _player.Controls.Flight.Yaw = yaw;
        }

        var forward = _player.Body.Forward;
        var next = (_cruiseKm.X + forward.X * _cruiseSpeed * dt, _cruiseKm.Y + forward.Y * _cruiseSpeed * dt, _cruiseKm.Z + forward.Z * _cruiseSpeed * dt);
        // A course target is dropped at further out than the safety margin, so it's handled first.
        if (target != null && distance < dropAt) { DropFromCruise(); return; }
        if (CruiseBlocked(_cruiseKm, next)) return;
        _cruiseKm = next;
    }

    private (double X, double Y, double Z)? NavTargetKm() => _navTarget switch
    {
        NavStation => _stationKm,
        >= 0 when _navTarget < _solar.Bodies.Count => _solar.PositionKm(_navTarget, Day),
        _ => null,
    };

    private string NavName => _navTarget == NavStation ? _session.Here.Stations.FirstOrDefault() ?? "Station"
        : _navTarget >= 0 ? _solar.Bodies[_navTarget].Name : "";

    /// <summary>Sets a course from the system map.</summary>
    private void SetCourse(int target)
    {
        _navTarget = target;
        _hud.Message($"COURSE SET: {NavName.ToUpperInvariant()}. J FOR CRUISE");
    }

    private bool _crashed;
    private bool _lowWarned;

    /// <summary>How close cruise lets you get to a body before dropping you out: a margin above the surface.</summary>
    private double CruiseSafetyKm(int index) => _solar.RadiusKm(index) + Math.Max(_solar.RadiusKm(index) * 0.15, 300);

    /// <summary>
    /// Stops a cruise move short of anything in the way. Checks the whole move, so a fast frame can't
    /// skip through a planet. Returns true if the ship was dropped out of cruise.
    /// </summary>
    private bool CruiseBlocked((double X, double Y, double Z) from, (double X, double Y, double Z) to)
    {
        double bestT = double.MaxValue;
        string? blocker = null;
        if (Galaxus.Core.Ships.Collision.FirstContact(from, to, (0, 0, 0), _solar.StarRadiusKm * 1.6) is { } st) { bestT = st; blocker = "STAR"; }
        for (int i = 0; i < _solar.Bodies.Count; i++)
        {
            if (_solar.Bodies[i].Kind == BodyKind.Belt) continue;
            if (Galaxus.Core.Ships.Collision.FirstContact(from, to, _solar.PositionKm(i, Day), CruiseSafetyKm(i)) is { } t && t < bestT)
            {
                bestT = t;
                blocker = _solar.Bodies[i].Name.ToUpperInvariant();
            }
        }
        if (blocker == null) return false;
        _cruiseKm = (from.X + (to.X - from.X) * bestT, from.Y + (to.Y - from.Y) * bestT, from.Z + (to.Z - from.Z) * bestT);
        DropFromCruise();
        _hud.Message($"CRUISE DROPPED: TOO CLOSE TO {blocker}");
        return true;
    }

    /// <summary>In normal flight, the station is solid and so are planets and the star.</summary>
    private void CheckCollisions()
    {
        if (_cruising || !_player.Alive || _solar == null) return;

        if (_stationNode != null)
        {
            var local = _player.Body.Position - _stationLocal;
            float distance = Galaxus.Core.Ships.Collision.StationDistance(local, out var normal) - _player.Spec.Radius;
            if (distance < 0)
            {
                // Push clear, bounce off with most of the speed lost, and take damage from the impact.
                _player.Body.Position -= normal * distance;
                float closing = -NVector3.Dot(_player.Body.Velocity, normal);
                if (closing > 0)
                {
                    _player.Body.Velocity += normal * closing * 1.3f;
                    var damage = _player.TakeDamage(Galaxus.Core.Ships.Collision.ImpactDamage(closing));
                    var at = (_player.Body.Position - normal * _player.Spec.Radius).ToGodot();
                    if (damage.ToHull > 0) { _effects.HullSpark(at); AudioManager.Instance?.HullHit(at); }
                    else if (damage.ToShields > 0) { _effects.ShieldFlash(_player.Body.Position.ToGodot(), _player.Spec.Radius); AudioManager.Instance?.ShieldHit(at); }
                    if (damage.Destroyed) Crash("YOU HIT THE STATION");
                    else if (closing > 12) _hud.Message("COLLISION");
                }
            }
        }

        var here = PlayerKm;
        double lowest = double.MaxValue;
        string lowestName = "";
        for (int i = 0; i < _solar.Bodies.Count; i++)
        {
            if (_solar.Bodies[i].Kind == BodyKind.Belt) continue;
            double altitude = DistanceKm(here, _solar.PositionKm(i, Day)) - _solar.RadiusKm(i);
            if (altitude < lowest) { lowest = altitude; lowestName = _solar.Bodies[i].Name; }
        }
        if (DistanceKm(here, (0, 0, 0)) < _solar.StarRadiusKm * 1.05) { Crash("BURNED UP IN THE STAR"); return; }
        if (lowest < 0) { Crash($"CRASHED INTO {lowestName.ToUpperInvariant()}"); return; }
        if (lowest < 20 && !_lowWarned) { _hud.Message($"WARNING: {lowestName.ToUpperInvariant()} {FormatDistance(lowest)} BELOW"); _lowWarned = true; }
        if (lowest > 40) _lowWarned = false;
    }

    /// <summary>Your ship is lost. After a moment you're patched up and put back beside the station.</summary>
    private void Crash(string reason)
    {
        if (_player.Alive) _player.TakeDamage(float.MaxValue);
        _effects.Explosion(_player.Body.Position.ToGodot(), _player.Body.Velocity.ToGodot(), _player.Spec.Radius);
        AudioManager.Instance?.Explosion(_player.Body.Position.ToGodot());
        _hud.Message(reason);
        _crashed = true;
        _player.Target = null;
        _respawnTimers[_player] = RespawnDelay;
    }

    /// <summary>After a crash: back beside the station, where you started.</summary>
    private void RecoverFromCrash()
    {
        _crashed = false;
        _anchorKm = (_stationKm.X, _stationKm.Y, _stationKm.Z + 1.6);
        _player.Respawn(NVector3.Zero, System.Numerics.Quaternion.Identity);
        _throttle = 0;
        _navTarget = -1;
        _hud.Message("SALVAGE CREW HAS PATCHED YOU UP");
    }

    /// <summary>HUD readout for cruise and course.</summary>
    private void UpdateNavHud()
    {
        var target = NavTargetKm();
        string status = _cruising ? $"CRUISE {FormatSpeed(_cruiseSpeed)}" : "";
        if (target is { } t)
        {
            double d = DistanceKm(PlayerKm, t);
            string eta = _cruising && _cruiseSpeed > 1 ? $"  ETA {Math.Max(1, d / Math.Max(_cruiseSpeed, 1)):0} S" : "";
            status = $"{status}{(status == "" ? "" : "   ")}{NavName.ToUpperInvariant()} {FormatDistance(d)}{eta}";
            _hud.NavPoint = WorldPointTowards(t);
            _hud.NavLabel = NavName.ToUpperInvariant();
        }
        else
        {
            _hud.NavPoint = null;
            if (!_cruising) status = "N SYSTEM MAP   J CRUISE";
        }
        _hud.Cruise = status;
    }

    /// <summary>A point in render space in the direction of something in the system, for HUD markers.</summary>
    private Vector3 WorldPointTowards((double X, double Y, double Z) km)
    {
        var p = PlayerKm;
        var dir = Normalised((km.X - p.X, km.Y - p.Y, km.Z - p.Z));
        return _player.Body.Position.ToGodot() + new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z) * 5000;
    }

    public static string FormatSpeed(double kmS) => kmS switch
    {
        >= 299_792 => $"{kmS / 299_792.458:0.0} C",
        >= 1000 => $"{kmS / 1000:0.0}K KM/S",
        _ => $"{kmS:0} KM/S",
    };

    public static string FormatDistance(double km) => km switch
    {
        >= SolarSystem.KmPerAu * 0.1 => $"{km / SolarSystem.KmPerAu:0.00} AU",
        >= 10_000 => $"{km / 1000:N0}K KM",
        >= 10 => $"{km:0} KM",
        _ => $"{km * 1000:0} M",
    };

    private static double DistanceKm((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

    private static (double X, double Y, double Z) Normalised((double X, double Y, double Z) v)
    {
        double l = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return l < 1e-12 ? (0, 0, -1) : (v.X / l, v.Y / l, v.Z / l);
    }
}
