using Godot;
using Galaxus.Core;
using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Ships;
using Galaxus.Ui;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace Galaxus;

/// <summary>
/// The game: a living galaxy you travel by gate, stations you dock at for work, trade and ships,
/// and flight with practice drones in each system.
///
/// Debug arguments go after "--" on the command line:
///   --demo               fly and shoot automatically
///   --cockpit            start in the cockpit camera
///   --screen=NAME        skip the menu and start a new game on flight, station or map;
///                        or show the title "menu" without the splash
///   --screenshot=PATH    save a screenshot after --frames (default 180) frames, then quit
///   --showcase=FOLDER    play a scripted tour and save README screenshots into FOLDER, then quit
///   --system=ID          with --screen, start in this system instead (for checking planets)
///   --preview=HULL_ID    show one hull from four angles instead of the game (for content work)
/// </summary>
public partial class Main : Node3D
{
    private const int DroneCount = 4;
    private const float ThrottleRate = 0.7f;
    private const float MouseStickRadius = 60f;
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
    private string? _startScreen;
    private int? _startSystem;

    private enum Screen { Flight, Docked, Map, SystemMap, Menu }

    /// <summary>A session to start when the scene reloads (New game, Continue, Load).</summary>
    private static Session? _pendingSession;
    private static bool _splashShown;
    private MenuScreen _menu = null!;
    private Screen _pauseReturn = Screen.Flight;

    private const float DockRange = 500f, DockSpeed = 80f;

    private Session? _sessionOrNull;
    private Session _session { get => _sessionOrNull!; set => _sessionOrNull = value; }
    private Screen _screen = Screen.Flight;
    private Screen _mapReturn = Screen.Flight;
    private StationScreen _stationScreen = null!;
    private GalaxyMap _map = null!;
    private MeshInstance3D? _stationNode;

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

        SetAppIcon();
        var audio = new AudioManager();
        audio.Init(_db);
        AddChild(audio);
        GameSettings.Load();
        _camera = new Camera3D { Fov = 70, Near = 0.3f, Far = 150000, Current = true };
        AddChild(_camera);

        var menuLayer = new CanvasLayer { Layer = 3 };
        AddChild(menuLayer);
        _menu = new MenuScreen { Visible = false };
        _menu.Init(new MenuActions
        {
            NewGame = () => Restart(Session.StartNew(_db, (ulong)DateTime.Now.Ticks)),
            Load = slot => Restart(SaveSlots.Read(_db, slot)),
            Save = slot => { SaveSlots.Write(slot, _session); },
            Resume = Resume,
            MainMenu = () => Restart(null),
            Quit = () => GetTree().Quit(),
        });
        menuLayer.AddChild(_menu);

        if (_pendingSession != null)
        {
            var session = _pendingSession;
            _pendingSession = null;
            StartGame(session, _startScreen);
        }
        else if (_demo || _startScreen is "flight" or "station" or "map")
        {
            var session = Session.StartNew(_db, 2026);
            if (_startSystem is { } id && id >= 0 && id < session.Galaxy.Systems.Count) session.Career.SystemId = id;
            StartGame(session, _startScreen);
            if (_showcaseDir != null) StartShowcase();
        }
        else
            ShowTitle(splash: !_splashShown && _startScreen != "menu");
    }

    /// <summary>
    /// Shows the Galaxus icon in the dock and taskbar. Godot does this from the project icon too,
    /// but only once the image has been imported, so load it straight from disk as a fallback.
    /// </summary>
    private static void SetAppIcon()
    {
        if (DisplayServer.GetName() == "headless") return;
        var image = ResourceLoader.Exists("res://icon.png") ? GD.Load<Texture2D>("res://icon.png")?.GetImage() : null;
        image ??= Image.LoadFromFile(ProjectSettings.GlobalizePath("res://icon.png"));
        if (image != null && !image.IsEmpty()) DisplayServer.SetIcon(image);
    }

    /// <summary>The title screen: a random system as a slowly turning backdrop, with the splash first time round.</summary>
    private void ShowTitle(bool splash)
    {
        _space = new Space();
        AddChild(_space);
        _space.Build((ulong)DateTime.Now.Ticks);
        _camera.Rotation = new Vector3(0, 0, 0);
        _menu.Open(inGame: false);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        AudioManager.Instance?.PlayMusic("title", 1);

        if (splash)
        {
            _splashShown = true;
            _menu.Visible = false;
            var splashLayer = new CanvasLayer { Layer = 4 };
            AddChild(splashLayer);
            var splashScreen = new Splash();
            splashScreen.Finished += () => _menu.Open(inGame: false);
            splashLayer.AddChild(splashScreen);
        }
    }

    /// <summary>Starts or switches games by reloading the scene with the session waiting.</summary>
    private void Restart(Session? session)
    {
        _pendingSession = session;
        _startScreen = null;
        GetTree().ReloadCurrentScene();
    }

    private void OpenPause()
    {
        _pauseReturn = _screen == Screen.Menu ? _pauseReturn : _screen;
        SetScreen(Screen.Menu);
        _menu.Open(inGame: true);
    }

    private void Resume()
    {
        _menu.Visible = false;
        if (_pauseReturn == Screen.Docked) Dock(); else SetScreen(_pauseReturn);
    }

    private void StartGame(Session session, string? startScreen)
    {
        _session = session;

        _effects = new Effects();
        AddChild(_effects);

        _player = new Ship(1, _db.Hulls[_session.Career.HullId].Name, ShipFactory.Build(_db, _session.Career.HullId)) { Side = 0 };
        _world.Ships.Add(_player);
        AddVisual(_player);

        for (int i = 0; i < DroneCount; i++)
        {
            var drone = new Ship(100 + i, $"Target drone {i + 1}", ShipFactory.Build(_db, "target-drone")) { Side = 1 };
            drone.Body.Position = new NVector3(_rng.Range(-500, 500), _rng.Range(-150, 150), _rng.Range(-1200, -500));
            drone.Body.Orientation = RandomOrientation();
            _world.Ships.Add(drone);
            _drones[drone] = new DroneBrain(_rng.Fork($"drone-{i}"), NVector3.Zero, 380);
            AddVisual(drone);
        }
        _player.Target = NearestTarget();

        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = new Hud { Player = _player, Camera = _camera, MouseStickRadius = MouseStickRadius, Cockpit = _cockpit };
        layer.AddChild(_hud);
        _hud.Message("F1 FOR CONTROLS");

        // Station and map screens sit above the HUD.
        var uiLayer = new CanvasLayer { Layer = 2 };
        AddChild(uiLayer);
        _stationScreen = new StationScreen();
        _stationScreen.Init(_session, Undock, () => OpenMap(), RefitShip);
        _stationScreen.OpenSystemMapRequested += OpenSystemMap;
        uiLayer.AddChild(_stationScreen);
        _map = new GalaxyMap();
        _map.Init(_session, JumpTo, CloseMap);
        uiLayer.AddChild(_map);
        _systemMap = new SystemMap();
        _systemMap.Init(_session, SetCourse, CloseSystemMap, () => PlayerKm, () => _navTarget);
        uiLayer.AddChild(_systemMap);

        EnterSystem();
        if (_demo || startScreen == "flight") SetScreen(Screen.Flight);
        else if (startScreen == "map") OpenMap();
        else if (_session.Here.Stations.Count > 0) Dock();
        else SetScreen(Screen.Flight);
        UpdateCamera(1f, snap: true);
    }

    /// <summary>Builds the space for the current system: its sky, planet and station.</summary>
    private void EnterSystem()
    {
        var here = _session.Here;
        _space?.QueueFree();
        _space = new Space();
        AddChild(_space);
        _space.Build(here.Seed, here, decor: false);

        _stationNode?.QueueFree();
        _stationNode = null;
        if (here.Stations.Count > 0)
        {
            var owner = _session.Galaxy.OwnerOf(here);
            _stationNode = new MeshInstance3D
            {
                Mesh = MeshFactory.BuildStation(owner != null ? Color.FromHtml(owner.Colour) : new Color(0.6f, 0.6f, 0.6f)),
                Position = _stationLocal.ToGodot(),
            };
            AddChild(_stationNode);
        }

        // Arrive facing the station, at rest, with the practice drones nearby.
        _player.Respawn(NVector3.Zero, NQuaternion.Identity);
        _throttle = 0;
        foreach (var drone in _drones.Keys)
        {
            drone.Respawn(new NVector3(_rng.Range(-600, 600), _rng.Range(-200, 200), _rng.Range(-900, 300)), RandomOrientation());
            _drones[drone].Anchor = NVector3.Zero;
        }
        foreach (var shot in _world.Projectiles) _effects.RemoveBolt(shot);
        _world.Projectiles.Clear();
        BuildSystemBodies();
        UpdateSystemView();
        _hud.Location = $"{here.Name.ToUpperInvariant()}  DAY {_session.Galaxy.Day}";
        _hud.Message($"{_solar.Star.Description.ToUpperInvariant()}, {_solar.Planets.Count(b => b.Kind != BodyKind.Belt)} PLANETS. N FOR SYSTEM MAP");
        if (_stationBody >= 0) _hud.Message($"IN ORBIT OF {_solar.Bodies[_stationBody].Name.ToUpperInvariant()}");
    }

    /// <summary>Music follows the local owner's race; stations add machinery; flight adds the engine.</summary>
    private void UpdateAudioForScreen()
    {
        var audio = AudioManager.Instance;
        if (audio == null || _sessionOrNull == null) return;
        var here = _session.Here;
        string style = _session.Galaxy.OwnerOf(here)?.RaceId ?? (here.Region == "rim" ? "dark-elf" : "human");
        audio.PlayMusic(style, here.Seed % 3);
        audio.SetAmbience(_screen == Screen.Docked ? here.Economy.ToString() : null, here.Seed);
        if (_screen != Screen.Flight) audio.SetEngine(null, 0, 0);
    }

    private static string SpacedName(PlanetKind kind) =>
        string.Concat(kind.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString())).ToUpperInvariant();

    private void SetScreen(Screen screen)
    {
        _screen = screen;
        _stationScreen.Visible = screen == Screen.Docked;
        _map.Visible = screen == Screen.Map;
        _systemMap.Visible = screen == Screen.SystemMap;
        _hud.Visible = screen == Screen.Flight;
        if (screen != Screen.Menu) _menu.Visible = false;
        UpdateAudioForScreen();
        if (!_demo && _screenshotPath == null)
            Input.MouseMode = screen == Screen.Flight ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }

    private void Dock()
    {
        _stationScreen.Refresh();
        SetScreen(Screen.Docked);
        if (_showcaseDir == null) SaveSlots.Write(SaveSlots.Autosave, _session);
        AudioManager.Instance?.Ui("dock");
    }

    /// <summary>Leave the station pointing away from it, so you're clear of the hub.</summary>
    private void Undock()
    {
        _player.Respawn(_stationLocal + new NVector3(0, 0, 320), NQuaternion.Identity);
        _throttle = 0.3f;
        _mouseStick = Vector2.Zero;
        SetScreen(Screen.Flight);
        AudioManager.Instance?.Ui("undock");
        _hud.Message($"UNDOCKED FROM {_stationScreen.StationName.ToUpperInvariant()}");
    }

    private void OpenMap()
    {
        _mapReturn = _screen == Screen.Map ? _mapReturn : _screen;
        _map.Open();
        SetScreen(Screen.Map);
    }

    private SystemMap _systemMap = null!;

    private void OpenSystemMap()
    {
        _mapReturn = _screen is Screen.Map or Screen.SystemMap ? _mapReturn : _screen;
        _systemMap.Open(_session.Here, current: true);
        SetScreen(Screen.SystemMap);
    }

    private void CloseSystemMap()
    {
        if (_mapReturn == Screen.Docked) Dock(); else SetScreen(Screen.Flight);
    }

    private void CloseMap()
    {
        if (_mapReturn == Screen.Docked) Dock(); else SetScreen(Screen.Flight);
    }

    /// <summary>Travel along the plotted route. Days pass, the galaxy moves on, and jobs settle on arrival.</summary>
    private void JumpTo(int systemId)
    {
        var report = _session.Jump(systemId);
        if (report == null) return;
        EnterSystem();
        AudioManager.Instance?.Ui(report.Results.Any(r => r.Success) ? "paid" : "jump");

        string summary = $"Arrived at {_session.Here.Name} after {report.Days} days.";
        foreach (var result in report.Results)
            summary += result.Success
                ? $" Job done: {result.Mission.Offer.Mission.Title}, +{result.Payment:N0} cr."
                : $" Job failed: {result.Mission.Offer.Mission.Title}.";

        if (_session.Here.Stations.Count > 0)
        {
            _stationScreen.Refresh(summary);
            SetScreen(Screen.Docked);
        }
        else
        {
            SetScreen(Screen.Flight);
            _hud.Message(summary.ToUpperInvariant());
            _hud.Message("NO STATION IN THIS SYSTEM");
        }
    }

    /// <summary>After buying a ship, swap the player's ship for the new one.</summary>
    private void RefitShip()
    {
        var old = _player;
        _world.Ships.Remove(old);
        _visuals[old].QueueFree();
        _visuals.Remove(old);

        _player = new Ship(1, _db.Hulls[_session.Career.HullId].Name, ShipFactory.Build(_db, _session.Career.HullId)) { Side = 0 };
        _player.Body.Position = old.Body.Position;
        _player.Body.Orientation = old.Body.Orientation;
        _player.Target = old.Target;
        _world.Ships.Add(_player);
        AddVisual(_player);
        _hud.Player = _player;
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
            else if (arg.StartsWith("--screen=")) _startScreen = arg["--screen=".Length..];
            else if (arg.StartsWith("--system=")) _startSystem = int.Parse(arg["--system=".Length..]);
            else if (arg.StartsWith("--showcase=")) { _showcaseDir = arg["--showcase=".Length..]; _startScreen = "flight"; }
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
            _mouseStick += new Vector2(motion.Relative.X, motion.Relative.Y) * GameSettings.Current.MouseSensitivity;
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
        StepShowcase();

        if (_sessionOrNull == null)
        {
            // Title screen: drift the camera slowly round the backdrop.
            _camera.Rotation += new Vector3(0, dt * 0.015f, 0);
            CaptureScreenshotIfAsked();
            return;
        }

        if (_screen != Screen.Flight)
        {
            HandleScreenKeys();
            CaptureScreenshotIfAsked();
            return;
        }

        HandleCommands();
        if (_demo) DemoPilot(dt); else ReadFlightControls(dt);
        if (!_cruising)
            foreach (var (drone, brain) in _drones)
            {
                // Drones loosely follow the player so there's always something nearby to shoot.
                brain.Anchor = NVector3.Lerp(brain.Anchor, _player.Body.Position + _player.Body.Forward * 450, dt * 0.2f);
                brain.Think(drone, dt);
            }

        if (_cruising)
        {
            // In cruise the ship only turns; the system moves past instead.
            StepCruise(dt);
            _player.Controls.Fire = false;
            _player.Controls.Flight.Throttle = 0;
            foreach (var drone in _drones.Keys) drone.Body.Position = new NVector3(0, 1e7f, 0);
        }
        _world.Step(dt);
        if (_cruising)
        {
            _player.Body.Position = NVector3.Zero;
            _player.Body.Velocity = NVector3.Zero;
        }
        HandleEvents();
        HandleRespawns(dt);

        _effects.SyncBolts(_world.Projectiles);
        foreach (var visual in _visuals.Values) visual.Sync();
        _visuals[_player].Visible = !_cockpit;
        foreach (var drone in _drones.Keys) _visuals[drone].Visible = !_cruising;
        UpdateSystemView();
        UpdateNavHud();

        UpdateCamera(dt);
        var dustVelocity = _cruising ? _player.Body.Forward.ToGodot() * 600 : _player.Body.Velocity.ToGodot();
        _space.UpdateDust(_camera.GlobalPosition, dustVelocity, _player.Body.Up.ToGodot());

        _hud.MouseStick = _mouseStick;
        _hud.Throttle = _throttle;
        _hud.Kills = _kills;
        _hud.Cockpit = _cockpit;
        _hud.Prompt = DockPrompt();
        var flightStats = _player.CurrentFlightStats();
        AudioManager.Instance?.SetEngine(_player.Spec.Builder.Race,
            _player.Controls.Flight.Assist ? _throttle : Mathf.Abs(_player.Controls.Flight.Thrust),
            _player.Body.Speed / Mathf.Max(flightStats.MaxSpeed, 1));

        CaptureScreenshotIfAsked();
    }

    private string? DockPrompt()
    {
        if (_stationNode == null) return null;
        if (_cruising) return null;
        float distance = NVector3.Distance(_player.Body.Position, _stationLocal);
        if (distance > DockRange * 3) return null;
        if (distance > DockRange) return $"{_stationScreen.StationName.ToUpperInvariant()}  {distance:0} M";
        return _player.Body.Speed > DockSpeed ? "SLOW DOWN TO DOCK" : "ENTER TO DOCK";
    }

    private bool CanDock => _stationNode != null
                            && !_cruising
                            && NVector3.Distance(_player.Body.Position, _stationLocal) <= DockRange
                            && _player.Body.Speed <= DockSpeed;

    private void HandleScreenKeys()
    {
        if (Input.IsActionJustPressed(GameInput.Quit)) GetTree().Quit();
        if (_screen == Screen.Menu && Input.IsActionJustPressed(GameInput.ReleaseMouse)) Resume();
        else if (_screen == Screen.Map && (Input.IsActionJustPressed(GameInput.Map) || Input.IsActionJustPressed(GameInput.ReleaseMouse))) CloseMap();
        else if (_screen == Screen.Docked && Input.IsActionJustPressed(GameInput.ReleaseMouse)) OpenPause();
        else if (_screen == Screen.SystemMap && (Input.IsActionJustPressed(GameInput.SystemMapKey) || Input.IsActionJustPressed(GameInput.ReleaseMouse))) CloseSystemMap();
        else if (_screen == Screen.Docked && Input.IsActionJustPressed(GameInput.SystemMapKey)) OpenSystemMap();
        else if (_screen == Screen.Docked && Input.IsActionJustPressed(GameInput.Map)) OpenMap();
        else if (_screen == Screen.Docked && Input.IsActionJustPressed(GameInput.Dock)) Undock();
    }

    private void HandleCommands()
    {
        if (Input.IsActionJustPressed(GameInput.Quit)) GetTree().Quit();
        if (Input.IsActionJustPressed(GameInput.ReleaseMouse)) { OpenPause(); return; }
        if (Input.IsActionJustPressed(GameInput.Help)) _hud.ShowHelp = !_hud.ShowHelp;
        if (Input.IsActionJustPressed(GameInput.CameraToggle)) _cockpit = !_cockpit;
        if (Input.IsActionJustPressed(GameInput.TargetNext)) { _player.Target = NextTarget(); AudioManager.Instance?.Ui("lock"); }
        if (Input.IsActionJustPressed(GameInput.Map)) { OpenMap(); return; }
        if (Input.IsActionJustPressed(GameInput.SystemMapKey)) { OpenSystemMap(); return; }
        if (Input.IsActionJustPressed(GameInput.Cruise)) ToggleCruise();
        if (Input.IsActionJustPressed(GameInput.MatchSpeed) && _player.Target is { Alive: true } match)
        {
            // Match the target's speed, so closing in is a matter of a little extra throttle.
            _throttle = Mathf.Clamp(match.Body.Speed / Mathf.Max(_player.CurrentFlightStats().MaxSpeed, 1), 0, 1);
            _player.Controls.Flight.Assist = true;
            _hud.Message("MATCHING TARGET SPEED");
        }
        if (Input.IsActionJustPressed(GameInput.Dock) && CanDock) { Dock(); return; }

        if (Input.IsActionJustPressed(GameInput.PipEngines)) { _player.Power.Raise(ShipSystem.Engines); AudioManager.Instance?.Ui("pip-0"); }
        if (Input.IsActionJustPressed(GameInput.PipShields)) { _player.Power.Raise(ShipSystem.Shields); AudioManager.Instance?.Ui("pip-1"); }
        if (Input.IsActionJustPressed(GameInput.PipWeapons)) { _player.Power.Raise(ShipSystem.Weapons); AudioManager.Instance?.Ui("pip-2"); }
        if (Input.IsActionJustPressed(GameInput.PipBalance)) { _player.Power.Balance(); AudioManager.Instance?.Ui("pip-1"); }

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

        var settings = GameSettings.Current;
        float keyPitch = Input.GetAxis(GameInput.PitchDown, GameInput.PitchUp) * (settings.InvertStick ? -1 : 1);
        float mousePitch = -stick.Y * (settings.InvertMouse ? -1 : 1);
        controls.Flight.Pitch = Mathf.Clamp(keyPitch + mousePitch, -1, 1);
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
                    AudioManager.Instance?.Laser(fired.Projectile.Position.ToGodot(), fired.Projectile.Owner.Spec.Builder.Race,
                        fired.Projectile.Velocity.Length(), fired.Projectile.Damage);
                    break;
                case ProjectileExpired expired:
                    _effects.RemoveBolt(expired.Projectile);
                    break;
                case ShipHit hit:
                    _effects.RemoveBolt(hit.Projectile);
                    if (hit.Damage.ToHull > 0) { _effects.HullSpark(hit.Point.ToGodot()); AudioManager.Instance?.HullHit(hit.Point.ToGodot()); }
                    else { _effects.ShieldFlash(hit.Ship.Body.Position.ToGodot(), hit.Ship.Spec.Radius); AudioManager.Instance?.ShieldHit(hit.Point.ToGodot()); }
                    break;
                case ShipDestroyed destroyed:
                    var ship = destroyed.Ship;
                    _effects.Explosion(ship.Body.Position.ToGodot(), ship.Body.Velocity.ToGodot(), ship.Spec.Radius);
                    AudioManager.Instance?.Explosion(ship.Body.Position.ToGodot());
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
            ship.Respawn(_player.Body.Position + NVector3.Normalize(offset) * 800, RandomOrientation());
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
