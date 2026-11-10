using Godot;
using Galaxus.Core;
using Galaxus.Core.Content;
using Galaxus.Core.Ships;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace Galaxus;

/// <summary>
/// First milestone: one flyable fighter, power distribution and target drones to shoot.
///
/// Debug arguments go after "--" on the command line:
///   --demo               fly and shoot automatically
///   --cockpit            start in the cockpit camera
///   --screenshot=PATH    save a screenshot after --frames (default 180) frames, then quit
///   --preview=HULL_ID    show one hull from four angles instead of the game (for content work)
/// </summary>
public partial class Main : Node3D
{
    private const int DroneCount = 4;
    private const float ThrottleRate = 0.7f;
    private const float MouseStickRadius = 60f;
    private const float MouseSensitivity = 0.5f;
    private const float RespawnDelay = 4f;

    private ContentDatabase _db = null!;
    private readonly World _world = new();
    private readonly Rng _rng = new("first-milestone");
    private Ship _player = null!;
    private readonly Dictionary<Ship, DroneBrain> _drones = [];
    private readonly Dictionary<Ship, ShipVisual> _visuals = [];
    private readonly Dictionary<Ship, float> _respawnTimers = [];

    private Space _space = null!;
    private Effects _effects = null!;
    private Hud _hud = null!;
    private Camera3D _camera = null!;
    private bool _cockpit;

    private float _throttle = 0.4f;
    private Vector2 _mouseStick;
    private int _kills;

    private bool _demo;
    private string? _screenshotPath;
    private int _screenshotFrame = 180;
    private int _frame;
    private string? _previewHull;

    public override void _Ready()
    {
        ReadArguments();
        GameInput.Register();

        try
        {
            // The preview only needs one hull, so it shouldn't be blocked by problems elsewhere.
            _db = ContentDatabase.Load(new GodotContentSource(), throwOnProblems: _previewHull == null);
            foreach (var problem in _db.Problems) GD.Print($"Content problem: {problem}");
        }
        catch (ContentException e)
        {
            GD.PushError(e.Message);
            GetTree().Quit(1);
            return;
        }

        if (_previewHull != null)
        {
            BuildPreview(_previewHull);
            return;
        }

        _space = new Space();
        AddChild(_space);
        _space.Build(Rng.Hash("home-system"));

        _effects = new Effects();
        AddChild(_effects);

        _camera = new Camera3D { Fov = 70, Near = 0.3f, Far = 150000, Current = true };
        AddChild(_camera);

        _player = new Ship(1, "Kestrel", ShipFactory.Build(_db, "kestrel")) { Side = 0 };
        _world.Ships.Add(_player);
        AddVisual(_player);

        for (int i = 0; i < DroneCount; i++)
        {
            var drone = new Ship(100 + i, $"Target drone {i + 1}", ShipFactory.Build(_db, "target-drone")) { Side = 1 };
            drone.Body.Position = new NVector3(_rng.Range(-500, 500), _rng.Range(-150, 150), _rng.Range(-1200, -500));
            drone.Body.Orientation = RandomOrientation();
            _world.Ships.Add(drone);
            _drones[drone] = new DroneBrain(_rng.Fork($"drone-{i}"), NVector3.Zero, 600);
            AddVisual(drone);
        }
        _player.Target = NearestTarget();

        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = new Hud { Player = _player, Camera = _camera, MouseStickRadius = MouseStickRadius, Cockpit = _cockpit };
        layer.AddChild(_hud);
        _hud.Message("F1 FOR CONTROLS");

        if (!_demo && _screenshotPath == null) Input.MouseMode = Input.MouseModeEnum.Captured;
        UpdateCamera(1f, snap: true);
    }

    private void ReadArguments()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--demo") _demo = true;
            else if (arg == "--cockpit") _cockpit = true;
            else if (arg.StartsWith("--screenshot=")) _screenshotPath = arg["--screenshot=".Length..];
            else if (arg.StartsWith("--frames=")) _screenshotFrame = int.Parse(arg["--frames=".Length..]);
            else if (arg.StartsWith("--preview=")) _previewHull = arg["--preview=".Length..];
        }
    }

    private void AddVisual(Ship ship)
    {
        var visual = new ShipVisual(ship, MeshFactory.BuildShip(ship.Spec.Hull, ship.Spec.Builder.Palette));
        AddChild(visual);
        _visuals[ship] = visual;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            // Virtual joystick: the mouse moves a dot inside a ring, and the dot's offset is the stick.
            _mouseStick += new Vector2(motion.Relative.X, motion.Relative.Y) * MouseSensitivity;
            if (_mouseStick.Length() > MouseStickRadius) _mouseStick = _mouseStick.Normalized() * MouseStickRadius;
        }
        else if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured && !_demo)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (_db == null) return;
        if (_previewHull != null)
        {
            CaptureScreenshotIfAsked();
            return;
        }
        float dt = Mathf.Min((float)delta, 1 / 30f);

        HandleCommands();
        if (_demo) DemoPilot(dt); else ReadFlightControls(dt);
        foreach (var (drone, brain) in _drones)
        {
            // Drones loosely follow the player so there's always something nearby to shoot.
            brain.Anchor = NVector3.Lerp(brain.Anchor, _player.Body.Position + _player.Body.Forward * 600, dt * 0.2f);
            brain.Think(drone, dt);
        }

        _world.Step(dt);
        HandleEvents();
        HandleRespawns(dt);

        _effects.SyncBolts(_world.Projectiles);
        foreach (var visual in _visuals.Values) visual.Sync();
        _visuals[_player].Visible = !_cockpit;

        UpdateCamera(dt);
        _space.UpdateDust(_camera.GlobalPosition, _player.Body.Velocity.ToGodot(), _player.Body.Up.ToGodot());

        _hud.MouseStick = _mouseStick;
        _hud.Throttle = _throttle;
        _hud.Kills = _kills;
        _hud.Cockpit = _cockpit;

        CaptureScreenshotIfAsked();
    }

    private void HandleCommands()
    {
        if (Input.IsActionJustPressed(GameInput.Quit)) GetTree().Quit();
        if (Input.IsActionJustPressed(GameInput.ReleaseMouse)) Input.MouseMode = Input.MouseModeEnum.Visible;
        if (Input.IsActionJustPressed(GameInput.Help)) _hud.ShowHelp = !_hud.ShowHelp;
        if (Input.IsActionJustPressed(GameInput.CameraToggle)) _cockpit = !_cockpit;
        if (Input.IsActionJustPressed(GameInput.TargetNext)) _player.Target = NextTarget();

        if (Input.IsActionJustPressed(GameInput.PipEngines)) _player.Power.Raise(ShipSystem.Engines);
        if (Input.IsActionJustPressed(GameInput.PipShields)) _player.Power.Raise(ShipSystem.Shields);
        if (Input.IsActionJustPressed(GameInput.PipWeapons)) _player.Power.Raise(ShipSystem.Weapons);
        if (Input.IsActionJustPressed(GameInput.PipBalance)) _player.Power.Balance();

        if (Input.IsActionJustPressed(GameInput.FlightAssist))
        {
            ref var flight = ref _player.Controls.Flight;
            flight.Assist = !flight.Assist;
            if (flight.Assist)
            {
                // Pick up the throttle where the ship already is, so switching back doesn't lurch.
                _throttle = Mathf.Clamp(_player.Body.ForwardSpeed / _player.CurrentFlightStats().MaxSpeed, 0, 1);
            }
            _hud.Message(flight.Assist ? "FLIGHT ASSIST ON" : "FLIGHT ASSIST OFF");
        }
    }

    private void ReadFlightControls(float dt)
    {
        ref var controls = ref _player.Controls;
        var stick = _mouseStick / MouseStickRadius;
        if (stick.Length() < 0.06f) stick = Vector2.Zero;

        controls.Flight.Pitch = Mathf.Clamp(Input.GetAxis(GameInput.PitchDown, GameInput.PitchUp) - stick.Y, -1, 1);
        controls.Flight.Yaw = Mathf.Clamp(Input.GetAxis(GameInput.YawLeft, GameInput.YawRight) + stick.X, -1, 1);
        controls.Flight.Roll = Input.GetAxis(GameInput.RollLeft, GameInput.RollRight);
        controls.Flight.Strafe = new System.Numerics.Vector2(
            Input.GetAxis(GameInput.StrafeLeft, GameInput.StrafeRight),
            Input.GetAxis(GameInput.StrafeDown, GameInput.StrafeUp));

        float throttleInput = Input.GetAxis(GameInput.ThrottleDown, GameInput.ThrottleUp);
        if (controls.Flight.Assist)
        {
            _throttle = Mathf.Clamp(_throttle + throttleInput * ThrottleRate * dt, 0, 1);
            if (Input.IsActionJustPressed(GameInput.ThrottleZero)) _throttle = 0;
            controls.Flight.Throttle = _throttle;
            controls.Flight.Thrust = 0;
        }
        else
        {
            controls.Flight.Thrust = throttleInput;
        }

        controls.Fire = Input.IsActionPressed(GameInput.Fire);
    }

    /// <summary>Chases the current target and fires when lined up. Used for screenshots and soak tests.</summary>
    private void DemoPilot(float dt)
    {
        ref var controls = ref _player.Controls;
        var target = _player.Target is { Alive: true } t ? t : (_player.Target = NearestTarget());
        if (target == null) return;

        var weapon = _player.Spec.Weapons[0];
        var aim = World.LeadPoint(_player.Body.Position, _player.Body.Velocity, target.Body.Position, target.Body.Velocity, weapon.ProjectileSpeed)
                  ?? target.Body.Position;
        var (pitch, yaw) = Autopilot.SteerTowards(_player, aim, 4f);
        controls.Flight.Pitch = pitch;
        controls.Flight.Yaw = yaw;
        controls.Flight.Roll = 0;
        float distance = NVector3.Distance(_player.Body.Position, target.Body.Position);
        _throttle = distance > 500 ? 0.8f : 0.35f;
        controls.Flight.Throttle = _throttle;
        controls.Flight.Assist = true;
        var local = NVector3.Normalize(_player.Body.ToLocal(aim));
        controls.Fire = local.Z < -0.995f && distance < weapon.Range;
    }

    private void HandleEvents()
    {
        foreach (var e in _world.Events)
        {
            switch (e)
            {
                case ShotFired fired:
                    _effects.AddBolt(fired.Projectile);
                    break;
                case ProjectileExpired expired:
                    _effects.RemoveBolt(expired.Projectile);
                    break;
                case ShipHit hit:
                    _effects.RemoveBolt(hit.Projectile);
                    if (hit.Damage.ToHull > 0) _effects.HullSpark(hit.Point.ToGodot());
                    else _effects.ShieldFlash(hit.Ship.Body.Position.ToGodot(), hit.Ship.Spec.Radius);
                    break;
                case ShipDestroyed destroyed:
                    var ship = destroyed.Ship;
                    _effects.Explosion(ship.Body.Position.ToGodot(), ship.Body.Velocity.ToGodot(), ship.Spec.Radius);
                    if (destroyed.By == _player)
                    {
                        _kills++;
                        _hud.Message($"{ship.Name.ToUpperInvariant()} DESTROYED");
                    }
                    _respawnTimers[ship] = RespawnDelay;
                    if (_player.Target == ship) _player.Target = NearestTarget();
                    break;
            }
        }
    }

    private void HandleRespawns(float dt)
    {
        foreach (var ship in _respawnTimers.Keys.ToList())
        {
            _respawnTimers[ship] -= dt;
            if (_respawnTimers[ship] > 0) continue;
            _respawnTimers.Remove(ship);

            var offset = new NVector3(_rng.Range(-1, 1), _rng.Range(-0.3f, 0.3f), _rng.Range(-1, 1));
            if (offset.LengthSquared() < 1e-3f) offset = NVector3.UnitX;
            ship.Respawn(_player.Body.Position + NVector3.Normalize(offset) * 1400, RandomOrientation());
            _player.Target ??= ship;
        }
    }

    private Ship? NearestTarget() =>
        _drones.Keys.Where(d => d.Alive)
            .OrderBy(d => NVector3.DistanceSquared(d.Body.Position, _player.Body.Position))
            .FirstOrDefault();

    private Ship? NextTarget()
    {
        var alive = _drones.Keys.Where(d => d.Alive)
            .OrderBy(d => NVector3.DistanceSquared(d.Body.Position, _player.Body.Position)).ToList();
        if (alive.Count == 0) return null;
        int index = _player.Target == null ? -1 : alive.IndexOf(_player.Target);
        return alive[(index + 1) % alive.Count];
    }

    private NQuaternion RandomOrientation() =>
        NQuaternion.CreateFromYawPitchRoll(_rng.Range(0, MathF.Tau), _rng.Range(-0.4f, 0.4f), 0);

    /// <summary>
    /// Chase camera sits behind and above and eases after the ship, so turns read clearly.
    /// Cockpit camera is fixed at the pilot's eye.
    /// </summary>
    private void UpdateCamera(float dt, bool snap = false)
    {
        var shipTransform = Interop.TransformOf(_player.Body);
        if (_cockpit)
        {
            _camera.GlobalTransform = shipTransform.TranslatedLocal(new Vector3(0, 1.0f, -1.6f));
            return;
        }

        var target = shipTransform.TranslatedLocal(new Vector3(0, 4.5f, 19f));
        target = target.LookingAt(shipTransform * new Vector3(0, 2f, -30f), shipTransform.Basis.Y);
        // Ease rotation only; position follows exactly so speed doesn't stretch the camera out.
        float weight = snap ? 1 : 1 - Mathf.Exp(-dt * 7f);
        var basis = new Basis(_camera.GlobalBasis.GetRotationQuaternion().Slerp(target.Basis.GetRotationQuaternion(), weight));
        var behind = shipTransform.Origin + basis * new Vector3(0, 4.5f, 19f);
        _camera.GlobalTransform = new Transform3D(basis, behind);
    }

    /// <summary>
    /// Shows a hull four times: front three-quarter, side, top and rear three-quarter, with a 5 m grid
    /// cube for scale and its collision radius drawn as a ring. Used to check ship models.
    /// </summary>
    private void BuildPreview(string hullId)
    {
        if (!_db.Hulls.TryGetValue(hullId, out var hull))
        {
            GD.PushError($"No hull '{hullId}'. Known hulls: {string.Join(", ", _db.Hulls.Keys)}");
            GetTree().Quit(1);
            return;
        }

        // Same lighting as the game, on a plain background.
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.08f, 0.09f, 0.12f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.35f, 0.4f, 0.55f),
                AmbientLightEnergy = 0.35f,
            },
        });
        AddChild(new DirectionalLight3D
        {
            Transform = Transform3D.Identity.LookingAt(new Vector3(-0.5f, -0.6f, -0.6f), Vector3.Up),
            LightEnergy = 1.3f,
        });

        var mesh = MeshFactory.BuildShip(hull, _db.Builders[hull.Builder].Palette);
        float spacing = hull.Radius * 2.6f;
        var views = new (Vector2 Cell, Vector3 Rotation, string Label)[]
        {
            (new Vector2(-1, 1), new Vector3(20, 215, 0), "FRONT 3/4"),
            (new Vector2(1, 1), new Vector3(0, 90, 0), "SIDE (NOSE LEFT)"),
            (new Vector2(-1, -1), new Vector3(90, 0, 0), "TOP (NOSE UP)"),
            (new Vector2(1, -1), new Vector3(20, 35, 0), "REAR 3/4"),
        };

        var layer = new CanvasLayer();
        AddChild(layer);
        foreach (var (cell, rotation, label) in views)
        {
            var origin = new Vector3(cell.X * spacing / 2, cell.Y * spacing / 2, 0);
            AddChild(new MeshInstance3D { Mesh = mesh, Position = origin, RotationDegrees = rotation });
            var ring = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = hull.Radius - 0.05f, OuterRadius = hull.Radius, Rings = 48, RingSegments = 4 },
                MaterialOverride = MeshFactory.Unshaded(new Color(0.3f, 0.6f, 0.3f)),
                Position = origin,
                RotationDegrees = new Vector3(90, 0, 0),
            };
            AddChild(ring);
            var screen = new Vector2(320 + cell.X * 160, 180 - cell.Y * 90);
            layer.AddChild(new Label { Text = label, Position = screen + new Vector2(-150, -86) });
        }
        layer.AddChild(new Label { Text = $"{hull.Name} ({hull.Id}), {hull.Class}, radius {hull.Radius} m. Ring shows the collision radius.", Position = new Vector2(8, 344) });

        _camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = spacing * 2.05f,
            Near = 0.1f,
            Far = 1000,
            Position = new Vector3(0, 0, 200),
            Current = true,
        };
        AddChild(_camera);
        _screenshotFrame = Math.Min(_screenshotFrame, 20);
    }

    private void CaptureScreenshotIfAsked()
    {
        if (_screenshotPath == null) return;
        _frame++;
        if (_frame < _screenshotFrame) return;
        var image = GetViewport().GetTexture().GetImage();
        image.SavePng(_screenshotPath);
        GD.Print($"Saved screenshot to {_screenshotPath}");
        GetTree().Quit();
    }
}

/// <summary>Draws a core Ship and keeps it in step with the simulation.</summary>
public partial class ShipVisual : Node3D
{
    private readonly MeshInstance3D _mesh = new();

    // Godot needs a parameterless constructor for script classes.
    public ShipVisual() { }

    public ShipVisual(Ship ship, Mesh mesh)
    {
        Ship = ship;
        _mesh.Mesh = mesh;
    }

    public Ship Ship { get; } = null!;

    public override void _Ready() => AddChild(_mesh);

    public void Sync()
    {
        _mesh.Visible = Ship.Alive;
        GlobalTransform = Interop.TransformOf(Ship.Body);
    }
}
