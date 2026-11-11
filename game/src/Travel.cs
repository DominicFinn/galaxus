using Godot;
using Galaxus.Core;
using NVector3 = System.Numerics.Vector3;

namespace Galaxus;

/// <summary>
/// Jumping like flying a ship: you jump from open space (not docked, clear of stations, the fleet and
/// planets) and arrive in open space, then fly in. Fuel for direct jumps shows on the HUD and the map.
/// </summary>
public partial class Main
{
    /// <summary>How far from a station or the fleet you must be to jump, in km.</summary>
    private const double MinJumpKm = 5;

    /// <summary>Why you can't jump from where you are, or null if you can.</summary>
    private string? JumpBlocked()
    {
        bool docked = _screen == Screen.Docked || (_screen is Screen.Map or Screen.SystemMap && _mapReturn == Screen.Docked);
        if (docked) return "Undock first: you can't jump from a station.";
        if (_solar == null) return null;
        var me = PlayerKm;
        if (_session.Here.Stations.Count > 0)
        {
            double d = DistanceKm(me, _stationKm);
            if (d < MinJumpKm) return $"Too close to {_session.Here.Stations[0]}: {d:0.0} km, need {MinJumpKm:0} km.";
        }
        if (_flagship != null)
        {
            double d = DistanceKm(me, _fleetKm);
            if (d < MinJumpKm) return $"Too close to the fleet: {d:0.0} km, need {MinJumpKm:0} km.";
        }
        if (_landed) return "You're on the ground: take off and climb away first.";
        for (int i = 0; i < _solar.Bodies.Count; i++)
        {
            var body = _solar.Bodies[i];
            if (body.Kind == Galaxus.Core.Galaxy.BodyKind.Belt) continue;
            double radius = _solar.RadiusKm(i);
            double altitude = DistanceKm(me, _solar.PositionKm(i, Day)) - radius;
            double well = radius * 0.5 + 200;
            if (altitude < well) return $"Too deep in {body.Name}'s gravity: {Math.Max(0, altitude):N0} km up, climb to {well:N0} km.";
        }
        return null;
    }

    /// <summary>
    /// After a jump: out in open space some way from the station (or the fleet), facing it, with the
    /// course set, so you fly in. Where exactly comes from the system and the day, so it's repeatable.
    /// </summary>
    private void ArriveInOpenSpace()
    {
        var rng = new Rng(_session.Here.Seed ^ (ulong)(uint)_session.Galaxy.Day ^ 0xa441);
        bool easy = GameSettings.Current.Difficulty == 0;
        double distance = easy ? rng.Range(20, 30) : rng.Range(35, 60);
        var (aim, label) = _flagship != null && _campaignFleet ? (_fleetKm, FleetName) : (_stationKm, _session.Here.Stations.FirstOrDefault() ?? "");
        // A random direction, kept out of the station's planet.
        (double X, double Y, double Z) dir = Normalised((rng.Range(-1, 1), rng.Range(-0.4f, 0.4f), rng.Range(-1, 1)));
        if (_stationBody >= 0)
        {
            var planet = _solar.PositionKm(_stationBody, Day);
            var at = (aim.X + dir.X * distance, aim.Y + dir.Y * distance, aim.Z + dir.Z * distance);
            if (DistanceKm(at, planet) < _solar.RadiusKm(_stationBody) * 1.3) dir = (-dir.X, -dir.Y, -dir.Z);
        }
        _anchorKm = (aim.X + dir.X * distance, aim.Y + dir.Y * distance, aim.Z + dir.Z * distance);
        var facing = new Vector3((float)-dir.X, (float)-dir.Y, (float)-dir.Z);
        var q = Basis.LookingAt(facing, Mathf.Abs(facing.Y) > 0.95f ? Vector3.Right : Vector3.Up).GetRotationQuaternion();
        _player.Respawn(NVector3.Zero, new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W));
        _player.Body.Velocity = NVector3.Zero;
        _throttle = 0;
        _cruising = false;
        _landed = false;
        _navTarget = _session.Here.Stations.Count > 0 ? NavStation : -1;
        _sinceUndock = 0;
        if (label != "") _hud.Message($"{label.ToUpperInvariant()} {distance:0} KM AHEAD: J FOR AUTOPILOT");
    }

    /// <summary>Fuel in the tank and its size, in light years, for the HUD and map.</summary>
    private (float Fuel, float Tank) FuelGauge()
    {
        float tank = _player.Spec.FuelTankLy;
        return (_session.Career.FuelLy < 0 ? tank : MathF.Min(_session.Career.FuelLy, tank), tank);
    }
}

/// <summary>
/// A launch option for checking the autopilot without a player: "-- --screen=flight --autopilot-test"
/// picks the furthest world in the system as the destination, engages the autopilot, logs its
/// progress every second, and quits when it arrives (or after two minutes).
/// </summary>
public partial class Main
{
    private float _autopilotTestTime = -1;

    private void StepAutopilotTest(float dt)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--autopilot-test") || _solar == null || _screen != Screen.Flight) return;
        if (_autopilotTestTime < 0)
        {
            int furthest = Enumerable.Range(0, _solar.Bodies.Count).Where(i => _solar.Bodies[i].Kind != Galaxus.Core.Galaxy.BodyKind.Belt)
                .MaxBy(i => DistanceKm(PlayerKm, _solar.PositionKm(i, Day)));
            _navTarget = furthest;
            ToggleCruise();
            _autopilotTestTime = 0;
            GD.Print($"AUTOPILOT TEST: to {NavName}, {DistanceKm(PlayerKm, NavTargetKm()!.Value):N0} km, engaged {_cruising}");
            return;
        }
        float before = _autopilotTestTime;
        _autopilotTestTime += dt;
        double left = NavTargetKm() is { } t ? DistanceKm(PlayerKm, t) : -1;
        if ((int)_autopilotTestTime != (int)before)
            GD.Print($"AUTOPILOT TEST: t={_autopilotTestTime:0}s on={_cruising} speed={_cruiseSpeed:N0} km/s left={left:N0} km");
        if (!_cruising || _autopilotTestTime > 240)
        {
            GD.Print($"AUTOPILOT TEST: {(_cruising ? "still flying" : "dropped out")} after {_autopilotTestTime:0.0}s, {left:N0} km from {NavName}");
            GetTree().Quit();
        }
    }
}

/// <summary>Skimming a star with a fuel scoop fitted fills the tank.</summary>
public partial class Main
{
    private bool _scooping;

    private void StepFuelScoop(float dt)
    {
        if (_solar == null || !_player.Alive || _player.Spec.FuelScoop <= 0) { _scooping = false; return; }
        double distance = DistanceKm(PlayerKm, (0, 0, 0)) / _solar.StarRadiusKm;
        float tank = _session.FuelTankLy, fuel = _session.FuelLy;
        bool close = distance < 3;
        if (close && fuel < tank - 0.05f)
        {
            // Strongest skimming low, nothing beyond three star radii.
            float rate = _player.Spec.FuelScoop * (float)Math.Clamp((3 - distance) / 1.2, 0.2, 1);
            _session.Career.FuelLy = MathF.Min(tank, fuel + rate * dt);
            if (!_scooping) _hud.Message("FUEL SCOOP OPEN");
            _scooping = true;
        }
        else if (_scooping)
        {
            _hud.Message(close ? "FUEL SCOOP: TANK FULL" : "FUEL SCOOP CLOSED");
            _scooping = false;
        }
    }
}
