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
    /// <summary>The star, for skimming fuel.</summary>
    private const int NavStar = -5;
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
        _surface?.QueueFree();
        _surface = null;
        _terrains.Clear();
        foreach (var (_, node) in _portNodes) node.QueueFree();
        _portNodes.Clear();
        if (_starNode != null && IsInstanceValid(_starNode)) _starNode.QueueFree();
        _starNode = null;

        _solar = SolarSystems.For(_session.Here, _db.Grammars);
        var (disc, _, _, _) = PlanetFactory.StarLook(PlanetFactory.FromStar(_solar.Star.Class));
        _starNode = new MeshInstance3D { Mesh = MeshFactory.Icosphere(1, 3, _ => disc, MeshFactory.Unshaded(disc)) };
        AddChild(_starNode);

        // Terrain sampling is the slow part, so every body's surface is built at once across the cores.
        var bodies = _solar.Bodies;
        var triangles = new FlatMeshBuilder?[bodies.Count];
        void BuildOne(int i)
        {
            if (bodies[i].Kind != BodyKind.Belt) triangles[i] = PlanetFactory.BodyTriangles(bodies[i]);
        }
        // In a browser the main thread can't wait on workers (they only start once it yields), so
        // the web build does them one after another.
        if (OperatingSystem.IsBrowser())
            for (int i = 0; i < bodies.Count; i++) BuildOne(i);
        else
            System.Threading.Tasks.Parallel.For(0, bodies.Count, BuildOne);
        for (int i = 0; i < _solar.Bodies.Count; i++)
        {
            var body = _solar.Bodies[i];
            if (body.Kind == BodyKind.Belt) continue;
            var node = PlanetFactory.BuildBody(body, triangles[i]);
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

        // Low over a world, draw it at true scale with terrain around you.
        var (low, altitude) = NearestSolid(player);
        bool surfaceMode = low >= 0 && altitude < SurfaceModeKm(low);
        if (surfaceMode && _surface?.BodyIndex != low)
        {
            _surface?.QueueFree();
            _surface = new PlanetSurface(low, _solar.Bodies[low], TerrainOf(low));
            AddChild(_surface);
        }
        else if (!surfaceMode && _surface != null)
        {
            _surface.QueueFree();
            _surface = null;
        }
        foreach (var (index, node) in _bodyNodes) node.Visible = !(surfaceMode && index == low && _surface!.Ready);
        _surface?.UpdateView(_solar.PositionKm(low, Day), player, eye);
        UpdatePorts(surfaceMode ? low : -1, player, eye);

        // In a world's air: haze and a sky that brightens with the sun.
        float air = 0, daylight = 0;
        Color airColour = Colors.Black;
        if (surfaceMode && PlanetFactory.Atmosphere(PlanetFactory.FromBody(_solar.Bodies[low].Kind)) is { } c)
        {
            var centre = _solar.PositionKm(low, Day);
            var up = Normalised((player.X - centre.X, player.Y - centre.Y, player.Z - centre.Z));
            var sunward = Normalised((-player.X, -player.Y, -player.Z));
            daylight = Mathf.Clamp((float)(up.X * sunward.X + up.Y * sunward.Y + up.Z * sunward.Z) * 2.5f + 0.3f, 0, 1);
            air = Mathf.Clamp(1 - (float)altitude / 120f, 0, 1);
            airColour = new Color(c.R, c.G, c.B);
        }
        _space.SetAir(airColour, air, daylight);

        var toStar = new Vector3((float)-player.X, (float)-player.Y, (float)-player.Z);
        var (_, light, energy, _) = PlanetFactory.StarLook(PlanetFactory.FromStar(_solar.Star.Class));
        _space.FaceStar(toStar, light, energy);

        UpdateFleet();

        // The station is only drawn when you're near it.
        if (_stationNode != null)
        {
            var rel = ((_stationKm.X - _anchorKm.X) * 1000, (_stationKm.Y - _anchorKm.Y) * 1000, (_stationKm.Z - _anchorKm.Z) * 1000);
            _stationLocal = new NVector3((float)rel.Item1, (float)rel.Item2, (float)rel.Item3);
            _stationNode.Position = _stationLocal.ToGodot();
            _stationNode.Visible = !_cruising && _stationLocal.Length() < 80_000;
        }
    }

    /// <summary>
    /// The autopilot: "take me there" within a system. It flies to your destination fast and drops
    /// you out a few kilometres short, facing it, to fly the last bit on thrusters. With no
    /// destination set it picks the obvious one: your job's stop here, or the main station.
    /// Pressing J again, or steering, hands control back.
    /// </summary>
    private void ToggleCruise()
    {
        if (_cruising) { DropFromCruise("AUTOPILOT OFF: YOU HAVE CONTROL"); return; }
        if (_battle is { Won: null })
        {
            _hud.Message("NO AUTOPILOT IN A FIGHT");
            return;
        }
        if (_navTarget == -1) CourseForJob();
        if (_navTarget == -1 && _session.Here.Stations.Count > 0) _navTarget = NavStation;
        if (NavTargetKm() is not { } target)
        {
            _hud.Message("NO DESTINATION: PICK ONE ON THE SYSTEM MAP (N)");
            return;
        }
        double distance = DistanceKm(PlayerKm, target);
        // Already inside the drop distance: for a station or a ship there's nothing to do; for a
        // world or a surface port the autopilot carries on down towards it.
        bool descent = _navTarget >= 0 || _navTarget <= NavPortBase;
        if (distance < CourseDropKm() && !descent)
        {
            _hud.Message($"{NavName.ToUpperInvariant()} IS {distance:0.0} KM AHEAD: FLY IN ON THRUSTERS");
            return;
        }
        _ignoreTargetDrop = descent && distance < CourseDropKm();
        _cruiseKm = PlayerKm;
        ClearTraffic();
        _cruising = true;
        _mouseStick = Vector2.Zero;
        _cruiseSpeed = 5;
        _landed = false;
        _player.Target = null;
        foreach (var drone in _drones.Keys) _visuals[drone].Visible = false;
        foreach (var shot in _world.Projectiles) _effects.RemoveBolt(shot);
        _world.Projectiles.Clear();
        _hud.Message($"AUTOPILOT: {NavName.ToUpperInvariant()}");
        AudioManager.Instance?.Ui("jump");
    }

    private void DropFromCruise(string message = "")
    {
        _cruising = false;
        SetWarp(1, "");
        var forward = _player.Body.Forward;
        bool atStation = _navTarget == NavStation || DistanceKm(_cruiseKm, _stationKm) < 50;
        if (atStation)
        {
            // Come out a few kilometres short of the station, facing it.
            _anchorKm = (_stationKm.X - forward.X * StationDropKm, _stationKm.Y - forward.Y * StationDropKm, _stationKm.Z - forward.Z * StationDropKm);
        }
        else _anchorKm = _cruiseKm;
        _player.Body.Position = NVector3.Zero;
        _player.Body.Velocity = forward * 60;
        _throttle = 0.35f;
        foreach (var drone in _drones.Keys)
        {
            drone.Respawn(forward * 900 + new NVector3(_rng.Range(-300, 300), _rng.Range(-100, 100), _rng.Range(-300, 300)), RandomOrientation());
            _drones[drone].Anchor = forward * 900;
        }
        _navTarget = _navTarget == NavStation ? -1 : _navTarget;
        _player.Target = NearestTarget();
        if (message != "") _hud.Message(message);
        if (OS.GetCmdlineUserArgs().Contains("--autopilot-test")) GD.Print($"AUTOPILOT TEST: dropped: {message}");
        AudioManager.Instance?.Ui("undock");
    }

    /// <summary>The autopilot flying: it turns to the target, builds speed once lined up, eases off as it nears, and drops out on arrival.</summary>
    private void StepCruise(float dt)
    {
        if (NavTargetKm() is not { } target) { DropFromCruise("AUTOPILOT OFF: NO DESTINATION"); return; }
        double distance = DistanceKm(_cruiseKm, target);
        double dropAt = CourseDropKm();

        // Ease off on the way in, but keep a brisk pace to the end rather than creeping.
        double wanted = Math.Clamp((distance - dropAt) / 1.5 + 15, 3, MaxCruiseKmS);
        // Near a world, cruise slows with height so you can come down to the ground.
        var (near, altitude) = NearestSolid(_cruiseKm);
        if (near >= 0) wanted = Math.Min(wanted, Math.Max(1.5, altitude * 2.5));
        _cruiseSpeed = wanted > _cruiseSpeed ? Math.Min(wanted, _cruiseSpeed * (1 + 2.4 * dt) + 5) : wanted;

        // With a course set, the ship turns towards it unless you're steering.
        // With a course set the autopilot flies, unless you clearly take over: keys or a stick held
        // over, or the mouse pushed well off centre. While it flies, the mouse stick drifts back to
        // centre so a leftover offset can't fight it.
        bool keysHeld = Mathf.Abs(Input.GetAxis(GameInput.PitchDown, GameInput.PitchUp)) > 0.2f
                        || Mathf.Abs(Input.GetAxis(GameInput.YawLeft, GameInput.YawRight)) > 0.2f;
        bool mouseHeld = _mouseStick.Length() > MouseStickRadius * 0.45f;
        _autopilot = true;
        if (keysHeld || mouseHeld) { DropFromCruise("AUTOPILOT OFF: YOU HAVE CONTROL"); return; }
        double facing = 1;
        {
            // Fly round anything in the way rather than through it.
            var aim = AimAround(_cruiseKm, target);
            var dir = Normalised((aim.X - _cruiseKm.X, aim.Y - _cruiseKm.Y, aim.Z - _cruiseKm.Z));
            var f = _player.Body.Forward;
            facing = f.X * dir.X + f.Y * dir.Y + f.Z * dir.Z;
            {
                _mouseStick *= Mathf.Exp(-6f * dt);
                var (pitch, yaw) = Galaxus.Core.Ships.Autopilot.SteerTowards(_player,
                    _player.Body.Position + new NVector3((float)dir.X, (float)dir.Y, (float)dir.Z) * 1000, 3f);
                _player.Controls.Flight.Pitch = pitch;
                _player.Controls.Flight.Yaw = yaw;
            }
            // Only build speed once the nose is on the target, so you don't shoot off the wrong way while turning.
            double aligned = Math.Clamp((facing - 0.6) / 0.4, 0, 1);
            wanted = Math.Max(Math.Min(wanted, 5), wanted * aligned * aligned);
            _cruiseSpeed = Math.Min(_cruiseSpeed, Math.Max(wanted, 5));
        }

        var forward = _player.Body.Forward;
        var next = (_cruiseKm.X + forward.X * _cruiseSpeed * dt, _cruiseKm.Y + forward.Y * _cruiseSpeed * dt, _cruiseKm.Z + forward.Z * _cruiseSpeed * dt);
        // A course target is dropped at further out than the safety margin, so it's handled first.
        if (distance < dropAt && !_ignoreTargetDrop) { DropFromCruise($"ARRIVED: {NavName.ToUpperInvariant()}"); return; }
        if (CruiseBlocked(_cruiseKm, next)) return;
        _cruiseKm = next;
    }

    private double CourseDropKm() =>
        _navTarget == NavStation ? StationDropKm * 2
        : _navTarget == NavStar ? _solar.StarRadiusKm * 2.2
        : _navTarget == NavBattle ? 12
        : _navTarget == NavFleet ? 4
        : _navTarget <= NavPortBase ? 25
        : _navTarget >= 0 ? Math.Max(_solar.RadiusKm(_navTarget) * 3, 3000) : 0;

    private (double X, double Y, double Z)? NavTargetKm() => _navTarget switch
    {
        NavStation => _stationKm,
        NavStar => (0, 0, 0),
        NavFleet when _flagship != null => _fleetKm,
        NavBattle when BattleJobHere() is { } battleJob => BattlePointKm(battleJob),
        <= NavPortBase when -_navTarget + NavPortBase < _solar.Stations.Count =>
            _solar.PortPositionKm(_solar.Stations[NavPortBase - _navTarget], Day, TerrainOf(_solar.Stations[NavPortBase - _navTarget].Body)),
        >= 0 when _navTarget < _solar.Bodies.Count => _solar.PositionKm(_navTarget, Day),
        _ => null,
    };

    private string NavName => _navTarget == NavStation ? _session.Here.Stations.FirstOrDefault() ?? "Station"
        : _navTarget == NavStar ? "The star"
        : _navTarget == NavBattle ? "The fighting"
        : _navTarget == NavFleet ? FleetName
        : _navTarget <= NavPortBase ? _solar.Stations[NavPortBase - _navTarget].Name
        : _navTarget >= 0 ? _solar.Bodies[_navTarget].Name : "";

    /// <summary>Sets a course from the system map.</summary>
    private void SetCourse(int target)
    {
        _navTarget = target;
        _hud.Message($"DESTINATION: {NavName.ToUpperInvariant()}. J FOR AUTOPILOT");
    }

    /// <summary>True while cruise is steering you to your course.</summary>
    private bool _autopilot;
    private bool _crashed;
    private bool _lowWarned;
    private bool _landed;
    /// <summary>Set when cruise starts inside the course's drop distance, so it carries on towards the ground.</summary>
    private bool _ignoreTargetDrop;
    private PlanetSurface? _surface;
    /// <summary>Port models on the world you're low over, with the site each stands on.</summary>
    private readonly List<(StationSite Site, MeshInstance3D Node)> _portNodes = [];
    /// <summary>The surface port you're docked at, or null when docked in orbit.</summary>
    private StationSite? _dockedPort;
    /// <summary>Course targets for surface ports are encoded below this: -10 is the first station, -11 the second, and so on.</summary>
    private const int NavPortBase = -10;
    private const float PortDockRange = 220f, PortDockHeight = 45f, PortDockSpeed = 25f;
    private readonly Dictionary<int, PlanetTerrain> _terrains = [];

    private PlanetTerrain TerrainOf(int index)
    {
        if (!_terrains.TryGetValue(index, out var terrain)) _terrains[index] = terrain = _solar.TerrainFor(index);
        return terrain;
    }

    /// <summary>Below this height a world is drawn at true scale with terrain around you.</summary>
    private double SurfaceModeKm(int index) => Math.Min(_solar.RadiusKm(index) * 0.6, 3000);

    /// <summary>The solid body you're closest to the surface of, and your height above its datum.</summary>
    private (int Index, double AltitudeKm) NearestSolid((double X, double Y, double Z) at)
    {
        int best = -1;
        double lowest = double.MaxValue;
        for (int i = 0; i < _solar.Bodies.Count; i++)
        {
            var kind = _solar.Bodies[i].Kind;
            if (kind is BodyKind.Belt or BodyKind.GasGiant or BodyKind.IceGiant) continue;
            double altitude = DistanceKm(at, _solar.PositionKm(i, Day)) - _solar.RadiusKm(i);
            if (altitude < lowest) { lowest = altitude; best = i; }
        }
        return (best, lowest);
    }

    /// <summary>How close cruise lets you get to a body before dropping you out: a margin above the surface.</summary>
    private double CruiseSafetyKm(int index)
    {
        var body = _solar.Bodies[index];
        if (body.Kind is BodyKind.GasGiant or BodyKind.IceGiant) return _solar.RadiusKm(index) + Math.Max(_solar.RadiusKm(index) * 0.15, 300);
        return _solar.RadiusKm(index) + TerrainOf(index).MaxHeightKm + 5;
    }

    /// <summary>
    /// Stops a cruise move short of anything in the way. Checks the whole move, so a fast frame can't
    /// skip through a planet. Returns true if the ship was dropped out of cruise.
    /// </summary>
    private bool CruiseBlocked((double X, double Y, double Z) from, (double X, double Y, double Z) to)
    {
        double bestT = double.MaxValue;
        int blocker = -2;
        if (Galaxus.Core.Ships.Collision.FirstContact(from, to, (0, 0, 0), _solar.StarRadiusKm * 1.6) is { } st) { bestT = st; blocker = -1; }
        for (int i = 0; i < _solar.Bodies.Count; i++)
        {
            if (_solar.Bodies[i].Kind == BodyKind.Belt) continue;
            if (Galaxus.Core.Ships.Collision.FirstContact(from, to, _solar.PositionKm(i, Day), CruiseSafetyKm(i)) is { } t && t < bestT)
            {
                bestT = t;
                blocker = i;
            }
        }
        if (blocker == -2) return false;
        // Stop just short of the margin.
        double stop = Math.Max(0, bestT - 0.01);
        _cruiseKm = (from.X + (to.X - from.X) * stop, from.Y + (to.Y - from.Y) * stop, from.Z + (to.Z - from.Z) * stop);
        if (blocker >= 0 && blocker == DestinationBody())
        {
            DropFromCruise($"ARRIVED: {NavName.ToUpperInvariant()}");
            return true;
        }
        // Anything else is in the way: hold back and let the autopilot steer round it.
        _cruiseSpeed = 3;
        return true;
    }

    /// <summary>The world you're heading down to, if the destination is a world or a port on one.</summary>
    private int DestinationBody() => _navTarget >= 0 ? _navTarget
        : _navTarget <= NavPortBase && NavPortBase - _navTarget < _solar.Stations.Count ? _solar.Stations[NavPortBase - _navTarget].Body
        : -1;

    /// <summary>
    /// Where to point to get to a target without hitting anything: the target itself if the way is
    /// clear, otherwise a point off to the side of whatever's in the way, or straight out if you're
    /// already too close to it. Your destination world never counts as in the way.
    /// </summary>
    private (double X, double Y, double Z) AimAround((double X, double Y, double Z) from, (double X, double Y, double Z) target)
    {
        var aim = target;
        int destination = DestinationBody();
        for (int pass = 0; pass < 4; pass++)
        {
            double bestT = double.MaxValue, radius = 0;
            (double X, double Y, double Z) centre = default;
            void Check((double X, double Y, double Z) c, double r)
            {
                if (Galaxus.Core.Ships.Collision.FirstContact(from, aim, c, r) is { } t && t < bestT) { bestT = t; centre = c; radius = r; }
            }
            if (_navTarget != NavStar) Check((0, 0, 0), _solar.StarRadiusKm * 1.6);
            for (int i = 0; i < _solar.Bodies.Count; i++)
                if (_solar.Bodies[i].Kind != BodyKind.Belt && i != destination) Check(_solar.PositionKm(i, Day), CruiseSafetyKm(i));
            if (bestT == double.MaxValue) return aim;

            var outward = (X: from.X - centre.X, Y: from.Y - centre.Y, Z: from.Z - centre.Z);
            double height = Math.Sqrt(outward.X * outward.X + outward.Y * outward.Y + outward.Z * outward.Z);
            if (height <= radius * 1.02)
            {
                // Too close already: climb straight out first.
                var up = Normalised(outward);
                return (centre.X + up.X * radius * 1.3, centre.Y + up.Y * radius * 1.3, centre.Z + up.Z * radius * 1.3);
            }
            // The point on the line nearest the obstacle's centre, pushed out past its margin.
            var d = (X: aim.X - from.X, Y: aim.Y - from.Y, Z: aim.Z - from.Z);
            double len2 = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
            double s = Math.Clamp(-(outward.X * d.X + outward.Y * d.Y + outward.Z * d.Z) / len2, 0, 1);
            var side = (X: outward.X + d.X * s, Y: outward.Y + d.Y * s, Z: outward.Z + d.Z * s);
            if (side.X * side.X + side.Y * side.Y + side.Z * side.Z < radius * radius * 1e-6)
                side = (d.Z, 0, -d.X); // Dead centre: pick any side.
            var away = Normalised(side);
            aim = (centre.X + away.X * radius * 1.4, centre.Y + away.Y * radius * 1.4, centre.Z + away.Z * radius * 1.4);
        }
        return aim;
    }

    /// <summary>
    /// Gravity near worlds, from their real mass and size. Flight assist pushes back against it like
    /// any other drift, within what the drives can do; with assist off you fall.
    /// </summary>
    private void ApplyGravity(float dt)
    {
        if (_cruising || !_player.Alive || _solar == null || _landed) return;
        var g = GravityAt(PlayerKm);
        _player.Body.Velocity += g * dt;
    }

    /// <summary>Pull towards nearby worlds in m/s², summed, ignoring the star (you're always in orbit of it).</summary>
    private NVector3 GravityAt((double X, double Y, double Z) at)
    {
        var total = NVector3.Zero;
        for (int i = 0; i < _solar.Bodies.Count; i++)
        {
            var body = _solar.Bodies[i];
            if (body.Kind == BodyKind.Belt || body.Mass <= 0) continue;
            var c = _solar.PositionKm(i, Day);
            var rel = (X: c.X - at.X, Y: c.Y - at.Y, Z: c.Z - at.Z);
            double r = Math.Sqrt(rel.X * rel.X + rel.Y * rel.Y + rel.Z * rel.Z);
            double radius = _solar.RadiusKm(i);
            if (r > radius * 12) continue; // far enough to ignore
            double earthRadii = r / SolarSystem.KmPerEarthRadius;
            double accel = 9.81 * body.Mass / (earthRadii * earthRadii);
            total += new NVector3((float)(rel.X / r * accel), (float)(rel.Y / r * accel), (float)(rel.Z / r * accel));
        }
        return total;
    }

    /// <summary>In normal flight, the station is solid and so are planets and the star.</summary>
    private void CheckCollisions()
    {
        if (_cruising || !_player.Alive || _solar == null) return;

        if (_stationNode != null)
        {
            var local = _player.Body.Position - _stationLocal;
            // Bigger stations are the same shape scaled up.
            float distance;
            NVector3 normal;
            if (_customStationRadius > 0)
            {
                // A faction's own station model: a sphere of its radius is close enough.
                float length = local.Length();
                normal = length > 1e-3f ? local / length : NVector3.UnitY;
                distance = length - _customStationRadius * StationScale - _player.Spec.Radius;
            }
            else distance = Galaxus.Core.Ships.Collision.StationDistance(local / StationScale, out normal) * StationScale - _player.Spec.Radius;
            if (distance < 0)
            {
                // Push clear, bounce off with most of the speed lost, and take damage from the impact.
                _player.Body.Position -= normal * distance;
                float closing = -NVector3.Dot(_player.Body.Velocity, normal);
                if (closing > 0)
                {
                    _player.Body.Velocity += normal * closing * 1.3f;
                    var damage = _player.TakeDamage(Galaxus.Core.Ships.Collision.ImpactDamage(closing) * _player.Spec.CollisionDamage);
                    var at = (_player.Body.Position - normal * _player.Spec.Radius).ToGodot();
                    if (damage.ToHull > 0) { _effects.HullSpark(at); AudioManager.Instance?.HullHit(at); }
                    else if (damage.ToShields > 0) { _effects.ShieldFlash(_player.Body.Position.ToGodot(), _player.Spec.Radius); AudioManager.Instance?.ShieldHit(at); }
                    if (damage.Destroyed) Crash("YOU HIT THE STATION");
                    else if (closing > 12) _hud.Message("COLLISION");
                }
            }
        }

        var here = PlayerKm;
        if (DistanceKm(here, (0, 0, 0)) < _solar.StarRadiusKm * 1.05) { Crash("BURNED UP IN THE STAR"); return; }
        for (int i = 0; i < _solar.Bodies.Count; i++)
            if (_solar.Bodies[i].Kind is BodyKind.GasGiant or BodyKind.IceGiant
                && DistanceKm(here, _solar.PositionKm(i, Day)) < _solar.RadiusKm(i) * 0.98)
            { Crash($"CRUSHED IN THE ATMOSPHERE OF {_solar.Bodies[i].Name.ToUpperInvariant()}"); return; }

        var (index, datumKm) = NearestSolid(here);
        if (index < 0 || datumKm > TerrainOf(index).MaxHeightKm + 2) { _landed = false; return; }

        // Close to the ground: check against the real terrain below.
        var centre = _solar.PositionKm(index, Day);
        var upD = Normalised((here.X - centre.X, here.Y - centre.Y, here.Z - centre.Z));
        var up = new NVector3((float)upD.X, (float)upD.Y, (float)upD.Z);
        var ground = TerrainOf(index).Sample(up);
        double groundKm = ground.Wet ? 0 : ground.HeightKm;
        double heightM = (datumKm - groundKm) * 1000 - _player.Spec.Radius;
        string name = _solar.Bodies[index].Name.ToUpperInvariant();
        if (heightM < 300 && !_lowWarned && !_landed) { _hud.Message($"WARNING: GROUND {heightM:0} M BELOW"); _lowWarned = true; }
        if (heightM > 600) _lowWarned = false;
        if (heightM >= 0) { if (heightM > 5) _landed = false; return; }

        float descent = -NVector3.Dot(_player.Body.Velocity, up);
        if (descent > 25 || (ground.Wet && descent > 8))
        {
            Crash(ground.Ground switch
            {
                Ground.Lava => $"LOST IN THE LAVA OF {name}",
                Ground.Sea or Ground.DeepSea => $"CRASHED INTO THE SEA ON {name}",
                _ => $"CRASHED INTO {name}",
            });
            return;
        }
        // A gentle touchdown: stand on the ground, sliding to a stop.
        _player.Body.Position += up * (float)-heightM;
        if (descent > 0) _player.Body.Velocity += up * descent;
        _player.Body.Velocity *= 0.9f;
        if (!_landed)
        {
            _landed = true;
            SetWarp(1, "");
            _hud.Message(ground.Wet ? $"SPLASHDOWN ON {name}" : $"TOUCHDOWN ON {name}");
        }
    }

    /// <summary>Your height above the ground of the world you're low over, or null if you're not low over one.</summary>
    private double? HeightAboveGroundKm()
    {
        if (_surface == null) return null;
        var here = PlayerKm;
        var centre = _solar.PositionKm(_surface.BodyIndex, Day);
        var up = Normalised((here.X - centre.X, here.Y - centre.Y, here.Z - centre.Z));
        double altitude = DistanceKm(here, centre) - _solar.RadiusKm(_surface.BodyIndex);
        return altitude - _surface.GroundKm(new NVector3((float)up.X, (float)up.Y, (float)up.Z));
    }

    /// <summary>Your ship is lost. After a moment you're patched up and put back beside the station.</summary>
    private void Crash(string reason)
    {
        if (_player.Alive) _player.TakeDamage(float.MaxValue);
        _effects.Explosion(_player.Body.Position.ToGodot(), _player.Body.Velocity.ToGodot(), _player.Spec.Radius);
        AudioManager.Instance?.Explosion(_player.Body.Position.ToGodot());
        _hud.Message(reason);
        SetWarp(1, "");
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

    /// <summary>Shows the ports on the world below, standing upright on their levelled ground.</summary>
    private void UpdatePorts(int body, (double X, double Y, double Z) player, Vector3 eye)
    {
        if (_portNodes.Count > 0 && (body < 0 || _portNodes[0].Site.Body != body))
        {
            foreach (var (_, node) in _portNodes) node.QueueFree();
            _portNodes.Clear();
        }
        if (body < 0) return;
        if (_portNodes.Count == 0)
        {
            var owner = _session.Galaxy.OwnerOf(_session.Here);
            var colour = owner != null ? Color.FromHtml(owner.Colour) : new Color(0.6f, 0.6f, 0.6f);
            foreach (var site in _solar.Stations.Where(s => s.Surface && s.Body == body))
            {
                var node = new MeshInstance3D { Mesh = MeshFactory.BuildSurfacePort(colour) };
                AddChild(node);
                _portNodes.Add((site, node));
            }
        }
        foreach (var (site, node) in _portNodes)
        {
            var at = _solar.PortPositionKm(site, Day, TerrainOf(body));
            var offset = new Vector3((float)((at.X - player.X) * 1000), (float)((at.Y - player.Y) * 1000), (float)((at.Z - player.Z) * 1000));
            node.Visible = offset.Length() < 120_000;
            var up = new Vector3(site.Site.X, site.Site.Y, site.Site.Z).Normalized();
            var forward = up.Cross(Mathf.Abs(up.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
            node.Transform = new Transform3D(Basis.LookingAt(forward, up), eye + offset);
        }
    }

    /// <summary>The nearest surface port, how far you are from its pad sideways and above it.</summary>
    private (StationSite Site, float Across, float Above)? NearestPort()
    {
        if (_surface == null || _cruising) return null;
        var player = PlayerKm;
        (StationSite, float, float)? best = null;
        foreach (var site in _solar.Stations.Where(s => s.Surface && s.Body == _surface.BodyIndex))
        {
            var pad = _solar.PortPositionKm(site, Day, TerrainOf(site.Body));
            var rel = new NVector3((float)((player.X - pad.X) * 1000), (float)((player.Y - pad.Y) * 1000), (float)((player.Z - pad.Z) * 1000));
            var up = NVector3.Normalize(site.Site);
            float above = NVector3.Dot(rel, up) - 1.5f;
            float across = (rel - up * NVector3.Dot(rel, up)).Length();
            if (best == null || across < best.Value.Item2) best = (site, across, above);
        }
        return best;
    }

    private string? PortPrompt()
    {
        if (NearestPort() is not { } port || port.Across > 8000) return null;
        string name = port.Site.Name.ToUpperInvariant();
        if (port.Across > PortDockRange || port.Above > PortDockHeight) return $"{name}  {FormatDistance(Math.Sqrt(port.Across * port.Across + port.Above * port.Above) / 1000)}";
        return _player.Body.Speed > PortDockSpeed ? "SLOW DOWN TO LAND" : "ENTER TO DOCK";
    }

    private StationSite? PortToDock =>
        NearestPort() is { } port && port.Across <= PortDockRange && port.Above <= PortDockHeight && _player.Body.Speed <= PortDockSpeed ? port.Site : null;

    /// <summary>Leaving a surface port: on the pad, a little above it, level with the ground.</summary>
    private void LaunchFromPort(StationSite site)
    {
        var pad = _solar.PortPositionKm(site, Day, TerrainOf(site.Body));
        var up = NVector3.Normalize(site.Site);
        _anchorKm = (pad.X + up.X * 0.03, pad.Y + up.Y * 0.03, pad.Z + up.Z * 0.03);
        var godotUp = new Vector3(up.X, up.Y, up.Z);
        var forward = godotUp.Cross(Mathf.Abs(godotUp.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
        var q = Basis.LookingAt(forward, godotUp).GetRotationQuaternion();
        _player.Respawn(NVector3.Zero, new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W));
        _landed = true;
        _navTarget = -1;
    }

    /// <summary>HUD readout for cruise and course.</summary>
    private void UpdateNavHud()
    {
        var (fuel, tank) = FuelGauge();
        _hud.Fuel = tank > 0 ? $"{fuel:0}/{tank:0} LY" : "";
        var target = NavTargetKm();
        string status = _cruising ? $"AUTOPILOT {FormatSpeed(_cruiseSpeed)}" : "";
        if (target is { } t)
        {
            double d = DistanceKm(PlayerKm, t);
            string eta = _cruising && _cruiseSpeed > 1 ? $"  ETA {Math.Max(1, d / Math.Max(_cruiseSpeed, 1) / _timeWarp):0} S" : "";
            status = $"{status}{(status == "" ? "" : "   ")}{NavName.ToUpperInvariant()} {FormatDistance(d)}{eta}";
            _hud.NavPoint = WorldPointTowards(t);
            _hud.NavLabel = NavName.ToUpperInvariant();
        }
        else
        {
            _hud.NavPoint = null;
            if (!_cruising) status = "N SYSTEM MAP   J AUTOPILOT";
        }
        if (_surface != null)
        {
            var here = PlayerKm;
            var (low, altitude) = NearestSolid(here);
            if (low == _surface.BodyIndex)
            {
                // Height above the ground below, not above sea level.
                var centre = _solar.PositionKm(low, Day);
                var up = Normalised((here.X - centre.X, here.Y - centre.Y, here.Z - centre.Z));
                double ground = _surface.GroundKm(new NVector3((float)up.X, (float)up.Y, (float)up.Z));
                float gees = GravityAt(here).Length() / 9.81f;
                status = $"{status}{(status == "" ? "" : "   ")}ALT {FormatDistance(Math.Max(0, altitude - ground))}   GRAV {gees:0.00}G";
            }
        }
        if (_timeWarp > 1) status = $"TIME x{_timeWarp}   {status}";
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
