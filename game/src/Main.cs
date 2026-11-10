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
///   --battle=BATTLE      start straight into a set-piece battle preview (add --demo to let the autopilot fight)
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
    private MeshInstance3D? _cockpitFrame;

    /// <summary>The cockpit frame for the ship you're flying, hung off the camera.</summary>
    private void BuildCockpit()
    {
        _cockpitFrame?.QueueFree();
        _cockpitFrame = Cockpit.Build(_player.Spec.Builder.Palette);
        _camera.AddChild(_cockpitFrame);
    }
    private static bool _pendingViewer;
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
        // Whatever put up the loading panel (a new game, a load, the web page starting), it comes
        // down once this scene has drawn.
        Loading.Finish(this);

        if (WebBench.Requested())
        {
            WebBench.Run();
            if (!Loading.IsWeb) GetTree().Quit();
            return;
        }

        try
        {
            // The preview only needs one hull, so it shouldn't be blocked by problems elsewhere.
            _db = ModDiscovery.Load(new GodotContentSource(), ModRoots.Find(), throwOnProblems: _previewHull == null, skipBrokenMods: true);
            foreach (var mod in _db.LoadedMods) GD.Print($"Loaded mod: {mod}");
            foreach (var problem in _db.Problems) GD.Print($"Content problem: {problem}");
            foreach (var skipped in _db.SkippedMods) GD.PushWarning(skipped);
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

        // The ship and station viewer: from the menu, with --viewer, or as the Galaxus Preview build.
        bool standaloneViewer = OS.HasFeature("viewer") || OS.GetCmdlineUserArgs().Any(a => a == "--viewer" || a.StartsWith("--viewer="));
        if (_pendingViewer || standaloneViewer)
        {
            _pendingViewer = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            AddChild(new Viewer(_db, _camera, standaloneViewer ? null : () => Restart(null)));
            return;
        }

        var menuLayer = new CanvasLayer { Layer = 3 };
        AddChild(menuLayer);
        _menu = new MenuScreen { Visible = false };
        _menu.Init(new MenuActions
        {
            NewGame = () => Loading.Run(this, "Building the galaxy", () => Restart(Session.StartNew(_db, (ulong)DateTime.Now.Ticks, easy: GameSettings.Current.Difficulty == 0)), finish: false),
            NewCampaign = id => Loading.Run(this, "Building the galaxy", () => Restart(Campaigns.Start(_db, (ulong)DateTime.Now.Ticks, id, easy: GameSettings.Current.Difficulty == 0)), finish: false),
            Campaigns = () => _db.Campaigns.Values.OrderBy(c => c.Tutorial ? 0 : 1).ThenBy(c => c.Name).Select(c => (c.Id, c.Tutorial ? $"Tutorial: {c.Name}" : c.Name, c.Summary)),
            PreviewBattle = PreviewRequested,
            OpenViewer = () => { _pendingViewer = true; Restart(null); },
            PreviewBattles = PreviewBattles,
            Simulations = () => _db.Battles.Values.Where(b => b.Kind == "sim").OrderBy(b => b.Order).Select(b => (b.Id, b.Name, SimSummary(b))),
            Load = slot => Loading.Run(this, "Loading your game", () => Restart(SaveSlots.Read(_db, slot)), finish: false),
            CheckSave = json => SaveGame.Check(_db, json),
            Save = slot => { SaveSlots.Write(slot, _session); },
            CurrentSave = () => SaveGame.Save(_session),
            Resume = Resume,
            MainMenu = () => Restart(null),
            Quit = () => GetTree().Quit(),
        });
        menuLayer.AddChild(_menu);

        if (_cliPreview != null && _pendingSession == null)
        {
            var id = _cliPreview;
            _cliPreview = null;
            if (_db.Battles.ContainsKey(id))
            {
                StartGame(PreviewSession(id), "flight");
                StartPreviewBattle(id);
                return;
            }
            GD.PushError($"No battle '{id}'.");
        }
        if (_pendingSession != null)
        {
            var session = _pendingSession;
            _pendingSession = null;
            if (_pendingPreview is { } preview)
            {
                _pendingPreview = null;
                StartGame(session, "flight");
                StartPreviewBattle(preview);
            }
            else StartGame(session, _startScreen);
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
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--menu-page=")) is { } page) MenuScreen.PendingPage = page["--menu-page=".Length..];
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
        // The difficulty may have changed in Settings.
        _session.Easy = GameSettings.Current.Difficulty == 0;
        Galaxus.Core.Battles.Battle.ApplyToPlayer(_player, GameSettings.Current.Level);
        if (_battle != null) _battle.Difficulty = GameSettings.Current.Level;
        _menu.Visible = false;
        if (_pauseReturn == Screen.Docked) Dock(); else SetScreen(_pauseReturn);
    }

    private void StartGame(Session session, string? startScreen)
    {
        _session = session;
        _session.Easy = GameSettings.Current.Difficulty == 0;

        _effects = new Effects();
        AddChild(_effects);

        _player = new Ship(1, _db.Hulls[_session.Career.HullId].Name, ShipFactory.Build(_db, _session.Career.HullId))
        {
            Side = 0,
            AimAssist = true,
        };
        Galaxus.Core.Battles.Battle.ApplyToPlayer(_player, GameSettings.Current.Level);
        _world.Ships.Add(_player);
        AddVisual(_player);
        BuildCockpit();

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
        _hud.Contacts = () => _world.Ships.Where(sh => sh != _player && sh.Alive && sh.Body.Position.Y < 1e6f)
            .Select(sh => (sh, _world.Hostile(_player, sh) ? -1 : sh.Side == _player.Side ? 1 : 0));
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
        _map.Init(_session, JumpTo, CloseMap, JumpBlocked, CallRescue);
        uiLayer.AddChild(_map);
        _systemMap = new SystemMap();
        _systemMap.Init(_session, SetCourse, CloseSystemMap, () => PlayerKm, () => _navTarget);
        uiLayer.AddChild(_systemMap);

        EnterSystem();
        if (_demo || startScreen == "flight") SetScreen(Screen.Flight);
        else if (startScreen == "map") OpenMap();
        else if (_flagship != null) { _dockedAtFlagship = true; Dock(); _stationScreen.ShowTab(7); }
        else if (_session.Here.Stations.Count > 0) Dock();
        else SetScreen(Screen.Flight);
        UpdateCamera(1f, snap: true);
    }

    /// <summary>Builds the space for the current system: its sky, planet and station.</summary>
    /// <summary>Population, station size and security of the system you're in.</summary>
    private Galaxus.Core.Galaxy.SystemProfile _profile = null!;
    /// <summary>Custom station models are drawn at their own size, at most doubled for the biggest places.</summary>
    private float StationScale => _customStationRadius > 0 ? Mathf.Min(_profile?.StationScale ?? 1f, 2f) : _profile?.StationScale ?? 1f;
    /// <summary>The collision radius of a faction's own station model, or 0 for the standard station.</summary>
    private float _customStationRadius;
    /// <summary>How close you must be to dock: the standard range, or outside a custom station's sphere.</summary>
    private float DockReach => _customStationRadius > 0 ? _customStationRadius * StationScale + 400 : DockRange * StationScale;

    /// <summary>A faction type's own station model, from a hull or a list of shapes, or null for the standard station.</summary>
    private (ArrayMesh Mesh, float Radius)? CustomStation(Galaxus.Core.Galaxy.Faction owner)
    {
        if (!_db.FactionTypes.TryGetValue(owner.TypeId, out var type)) return null;
        var palette = type.Builder != null && _db.Builders.TryGetValue(type.Builder, out var builder) ? builder.Palette
            : new Galaxus.Core.Content.PaletteDef { Hull = "#8d939c", Accent = owner.Colour, Trim = "#3c4048", Glow = "#ffe6a0" };
        if (type.StationHull != null && _db.Hulls.TryGetValue(type.StationHull, out var hull))
            return (MeshFactory.BuildShip(hull, _db.Builders.TryGetValue(hull.Builder, out var b) ? b.Palette : palette), hull.Radius);
        if (type.StationModel is { Count: > 0 } model)
            return (MeshFactory.BuildShip(new Galaxus.Core.Content.HullDef { Id = type.Id + "-station", Model = model, Radius = type.StationRadius }, palette), type.StationRadius);
        return null;
    }

    private void EnterSystem()
    {
        var here = _session.Here;
        _profile = Galaxus.Core.Galaxy.SystemProfiles.For(_session.Galaxy, here, _db);
        _dockedPort = null;
        _timeWarp = 1;
        _space?.QueueFree();
        _space = new Space();
        AddChild(_space);
        _space.Build(here.Seed, here, decor: false);

        _stationNode?.QueueFree();
        _stationNode = null;
        _customStationRadius = 0;
        if (here.Stations.Count > 0)
        {
            var owner = _session.Galaxy.OwnerOf(here);
            var custom = owner != null ? CustomStation(owner) : null;
            _customStationRadius = custom?.Radius ?? 0;
            _stationNode = new MeshInstance3D
            {
                Mesh = custom?.Mesh ?? MeshFactory.BuildStation(owner != null ? Color.FromHtml(owner.Colour) : new Color(0.6f, 0.6f, 0.6f), _profile.StationSize),
                Scale = Vector3.One * StationScale,
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
        ClearBattle();
        ClearTraffic();
        SetupFleet();
        _dockedAtFlagship = false;
        UpdateSystemView();
        _hud.Location = $"{here.Name.ToUpperInvariant()}  DAY {_session.Galaxy.Day}";
        _hud.Message($"{_solar.Star.Description.ToUpperInvariant()}, {_solar.Planets.Count(b => b.Kind != BodyKind.Belt)} PLANETS. N FOR SYSTEM MAP");
        if (_stationBody >= 0) _hud.Message($"IN ORBIT OF {_solar.Bodies[_stationBody].Name.ToUpperInvariant()}");
    }

    /// <summary>
    /// Music follows the local owner's race, and its mood follows what you're doing: calm when
    /// docked, a little more movement in flight, tenser in a battle. Stations add machinery; flight
    /// adds the engine.
    /// </summary>
    private void UpdateAudioForScreen()
    {
        var audio = AudioManager.Instance;
        if (audio == null || _sessionOrNull == null) return;
        var here = _session.Here;
        // Owned systems play their owner's race; nobody's space and the stars between the gates
        // play the general space styles.
        string style = _session.Galaxy.OwnerOf(here)?.RaceId
            ?? (!here.IsGateSystem || here.Region == "rim" || here.Seed % 2 == 0 ? "deep-space" : "drift");
        var mood = _battle != null ? Galaxus.Core.Audio.Mood.Combat
            : _screen == Screen.Docked ? Galaxus.Core.Audio.Mood.Docked
            : Galaxus.Core.Audio.Mood.Flight;
        audio.PlayMusic(style, here.Seed % 3, mood);
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

    private void Dock(string? arrival = null)
    {
        _timeWarp = 1;
        _player.Resupply();
        // On Easy the station tops your tank up as you dock.
        if (GameSettings.Current.Difficulty == 0) _session.Refuel();
        _stationScreen.Port = _dockedAtFlagship && Campaigns.Current(_session) is { } campaign ? campaign.BaseName : _dockedPort?.Name;
        // Docking delivers any jobs for this station.
        string stationName = _dockedPort?.Name ?? _session.Here.Stations.FirstOrDefault() ?? _session.Here.Name;
        var delivered = _session.Dock(stationName);
        string summary = arrival ?? "";
        foreach (var result in delivered)
            summary += $" Job done: {result.Mission.Offer.Mission.Title}, +{result.Payment:N0} cr.";
        if (delivered.Count > 0) AudioManager.Instance?.Ui("paid");
        _stationScreen.Refresh(summary.Trim() == "" ? null : summary.Trim());
        SetScreen(Screen.Docked);
        if (_showcaseDir == null) SaveSlots.Write(SaveSlots.Autosave, _session);
        AudioManager.Instance?.Ui("dock");
    }

    /// <summary>Leave the station pointing away from it, so you're clear of the hub.</summary>
    private void Undock()
    {
        if (_dockedAtFlagship) { LaunchFromFlagship(); _dockedAtFlagship = false; }
        else if (_dockedPort != null) LaunchFromPort(_dockedPort);
        else
        {
            // Out of the hub on the +Z side, turned round so the nose points away from the station
            // (a ship's forward is -Z), and drifting clear.
            var outwards = NQuaternion.CreateFromAxisAngle(NVector3.UnitY, MathF.PI);
            _player.Respawn(_stationLocal + new NVector3(0, 0, _customStationRadius > 0 ? _customStationRadius * StationScale + 320 : 320), outwards);
            _player.Body.Velocity = _player.Body.Forward * 40;
        }
        _throttle = 0.3f;
        _mouseStick = Vector2.Zero;
        SetScreen(Screen.Flight);
        AudioManager.Instance?.Ui("undock");
        _hud.Message($"UNDOCKED FROM {_stationScreen.StationName.ToUpperInvariant()}");
        CourseForJob();
        _sinceUndock = 0;
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
        // Arriving builds the whole system, which takes a while in a browser.
        if (Loading.IsWeb) Loading.Run(this, $"Jumping to {_session.Galaxy.Systems[systemId].Name}", () => JumpNow(systemId));
        else JumpNow(systemId);
    }

    /// <summary>A rescue tug, when you've run dry somewhere you can't get out of.</summary>
    private void CallRescue()
    {
        var (to, fee, days) = _session.CallRescue();
        EnterSystem();
        SetScreen(Screen.Flight);
        ArriveInOpenSpace();
        _hud.Message($"A TUG BROUGHT YOU TO {to.Name.ToUpperInvariant()} AFTER {days} DAYS, FOR {fee:N0} CR");
    }

    private void JumpNow(int systemId)
    {
        var report = _session.Jump(systemId);
        if (report == null) return;
        EnterSystem();
        AudioManager.Instance?.Ui("jump");

        string summary = $"Arrived at {_session.Here.Name} after {report.Days} days.";
        foreach (var result in report.Results.Where(r => !r.Success))
            summary += $" Job failed: {result.Mission.Offer.Mission.Title}.";

        // You arrive in open space and fly in.
        SetScreen(Screen.Flight);
        ArriveInOpenSpace();
        _hud.Message(summary.ToUpperInvariant());
        if (_session.Here.Stations.Count == 0) _hud.Message(_session.Stranded ? "OUT OF FUEL: OPEN THE MAP (M) TO CALL A RESCUE TUG" : "NO STATION IN THIS SYSTEM");
    }

    /// <summary>After buying a ship, swap the player's ship for the new one.</summary>
    private void RefitShip()
    {
        var old = _player;
        _world.Ships.Remove(old);
        _visuals[old].QueueFree();
        _visuals.Remove(old);

        _player = new Ship(1, _db.Hulls[_session.Career.HullId].Name, ShipFactory.Build(_db, _session.Career.HullId)) { Side = 0 };
        _player.AimAssist = true;
        Galaxus.Core.Battles.Battle.ApplyToPlayer(_player, GameSettings.Current.Level);
        BuildCockpit();
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
            else if (arg.StartsWith("--battle=")) _cliPreview = arg["--battle=".Length..];
        }
    }

    private void AddVisual(Ship ship)
    {
        var visual = new ShipVisual(ship, MeshFor(ship));
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
        // A click takes hold of the mouse for flying, but only when you're actually flying: not on the
        // title screen, in the viewer, or in menus.
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right } && Input.MouseMode != Input.MouseModeEnum.Captured
                 && !_demo && _sessionOrNull != null && _screen == Screen.Flight)
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
        if (_demo) DemoPilot(dt);
        else if (_holdStill) _player.Controls.Flight = new Galaxus.Core.Flight.FlightInput { Assist = true };
        else ReadFlightControls(dt);
        // Time acceleration runs the simulation several times a frame, so physics, the autopilot and
        // collisions stay exactly as they would at normal speed.
        int steps = _timeWarp;
        for (int step = 0; step < steps; step++)
        {
            StepBattle(dt);
            StepTraffic(dt);
            if (_battle != null) ParkDrones();
            else if (!_cruising)
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
            ApplyGravity(dt);
            _world.Step(dt);
            if (_cruising)
            {
                _player.Body.Position = NVector3.Zero;
                _player.Body.Velocity = NVector3.Zero;
            }
            HandleEvents();
            CheckCollisions();
            HandleRespawns(dt);
            _fightCooldown = Mathf.Max(0, _fightCooldown - dt);
            if (_timeWarp > 1 && WarpBlocker() is { } reason) SetWarp(1, reason);
            if (_timeWarp == 1) break;
        }
        CheckBattleStart();

        _effects.SyncBolts(_world.Projectiles);
        foreach (var visual in _visuals.Values) visual.Sync();
        _visuals[_player].Visible = !_cockpit;
        foreach (var drone in _drones.Keys) _visuals[drone].Visible = !_cruising;
        UpdateSystemView();
        UpdateNavHud();
        UpdateJobHud(delta);
        UpdateGuidance(dt);
        StepPreview(delta);
        if (_hud.ShowPerformance)
            _hud.Performance = $"{Engine.GetFramesPerSecond():0} FPS   {_world.Ships.Count(sh => sh.Alive)} SHIPS   {_world.Projectiles.Count} SHOTS   {_visuals.Count} MODELS";

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
        if (PortPrompt() is { } port) return port;
        if (FlagshipPrompt() is { } flagship) return flagship;
        if (_stationNode == null) return null;
        if (_cruising) return null;
        float distance = NVector3.Distance(_player.Body.Position, _stationLocal);
        if (distance > DockReach * 3) return null;
        if (distance > DockReach) return $"{_stationScreen.StationName.ToUpperInvariant()}  {distance:0} M";
        return _player.Body.Speed > DockSpeed ? "SLOW DOWN TO DOCK" : "ENTER TO DOCK";
    }

    private bool CanDock => _stationNode != null
                            && !_cruising
                            && NVector3.Distance(_player.Body.Position, _stationLocal) <= DockReach
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
        if (Input.IsActionJustPressed(GameInput.Performance)) _hud.ShowPerformance = !_hud.ShowPerformance;
        if (Input.IsActionJustPressed(GameInput.CameraToggle)) _cockpit = !_cockpit;
        if (Input.IsActionJustPressed(GameInput.TargetNext)) { _player.Target = NextTarget(); AudioManager.Instance?.Ui("lock"); }
        if (Input.IsActionJustPressed(GameInput.Map)) { OpenMap(); return; }
        if (Input.IsActionJustPressed(GameInput.SystemMapKey)) { OpenSystemMap(); return; }
        if (Input.IsActionJustPressed(GameInput.Cruise)) ToggleCruise();
        if (Input.IsActionJustPressed(GameInput.NextJob)) CycleJob();
        if (Input.IsActionJustPressed(GameInput.TimeFaster)) ChangeWarp(+1);
        if (Input.IsActionJustPressed(GameInput.TimeSlower)) ChangeWarp(-1);
        if (Input.IsActionJustPressed(GameInput.MatchSpeed) && _player.Target is { Alive: true } match)
        {
            // Match the target's speed, so closing in is a matter of a little extra throttle.
            _throttle = Mathf.Clamp(match.Body.Speed / Mathf.Max(_player.CurrentFlightStats().MaxSpeed, 1), 0, 1);
            _player.Controls.Flight.Assist = true;
            _hud.Message("MATCHING TARGET SPEED");
        }
        if (Input.IsActionJustPressed(GameInput.Dock) && PortToDock is { } port) { _dockedPort = port; _dockedAtFlagship = false; Dock(); return; }
        if (Input.IsActionJustPressed(GameInput.Dock) && CanDockAtFlagship)
        {
            _dockedPort = null;
            _dockedAtFlagship = true;
            Dock();
            _stationScreen.ShowTab(7);
            return;
        }
        if (Input.IsActionJustPressed(GameInput.Dock) && CanDock) { _dockedPort = null; _dockedAtFlagship = false; Dock(); return; }

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
        if (Input.IsActionJustPressed(GameInput.FireMissile)) controls.Missile = true;
    }

    /// <summary>Chases the current target and fires when lined up. Used for screenshots and soak tests.</summary>
    private void DemoPilot(float dt)
    {
        ref var controls = ref _player.Controls;
        var target = _player.Target is { Alive: true } t && t.Side != _player.Side ? t : (_player.Target = NearestTarget());
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
        if (_player.Locked && distance > 800 && _rng.NextFloat() < 0.01f) controls.Missile = true;
    }

    private void HandleEvents()
    {
        foreach (var e in _world.Events)
        {
            switch (e)
            {
                case ShotFired fired when fired.Projectile.Kind == "missile" && fired.Projectile.Owner == _player:
                    _effects.AddBolt(fired.Projectile);
                    _fightCooldown = 8;
                    AudioManager.Instance?.Ui("jump");
                    _hud.Message($"MISSILE AWAY. {_player.MissilesLeft} LEFT");
                    break;
                case ShotFired fired:
                    if (fired.Projectile.Owner == _player) _fightCooldown = 8;
                    _effects.AddBolt(fired.Projectile);
                    AudioManager.Instance?.Laser(fired.Projectile.Position.ToGodot(), fired.Projectile.Owner.Spec.Builder.Race,
                        fired.Projectile.Velocity.Length(), fired.Projectile.Damage);
                    break;
                case FlakBurst burst:
                    _effects.RemoveBolt(burst.Projectile);
                    _effects.HullSpark(burst.Point.ToGodot());
                    break;
                case ProjectileExpired expired:
                    _effects.RemoveBolt(expired.Projectile);
                    break;
                case ShipHit hit:
                    if (hit.Ship == _player || hit.Projectile.Owner == _player) _fightCooldown = 8;
                    if (hit.Ship == _player && hit.Projectile.Owner != _player) TookFire(hit.Projectile.Owner);
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
                        PayBounty(ship);
                        _kills++;
                        _hud.Message($"{ship.Name.ToUpperInvariant()} DESTROYED");
                    }
                    // Practice drones and you come back; battle ships stay dead.
                    if (ship == _player || _drones.ContainsKey(ship)) _respawnTimers[ship] = RespawnDelay;
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
            if (ship == _player && _crashed) { RecoverFromCrash(); continue; }

            var offset = new NVector3(_rng.Range(-1, 1), _rng.Range(-0.3f, 0.3f), _rng.Range(-1, 1));
            if (offset.LengthSquared() < 1e-3f) offset = NVector3.UnitX;
            ship.Respawn(_player.Body.Position + NVector3.Normalize(offset) * 800, RandomOrientation());
            _player.Target ??= ship;
        }
    }

    /// <summary>The nearest enemy, or failing that the nearest ship that isn't on your side (practice drones, traders). Never a friend.</summary>
    private Ship? NearestTarget() => TargetCandidates().FirstOrDefault(s => s.Side != _player.Side);

    private Ship? NextTarget()
    {
        var alive = TargetCandidates();
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
        if (_cockpitFrame != null) _cockpitFrame.Visible = _cockpit && _screen == Screen.Flight;
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
