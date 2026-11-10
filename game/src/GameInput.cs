using Godot;

namespace Galaxus;

/// <summary>
/// Input actions, defined in code so the defaults live in one readable place. Players can rebind
/// any action in Settings, separately for keyboard and mouse and for controllers (including flight
/// sticks and HOTAS axes); their changes are saved in the settings and applied over the defaults.
/// Pitch follows flight-stick convention: pull back (or the Down arrow) to raise the nose.
///
/// Controller defaults use Godot's standard layout, which is the same on Mac and Windows: the
/// bottom face button is A on Xbox and Cross on PlayStation, and so on round the face.
/// </summary>
public static class GameInput
{
    public const string PitchUp = "pitch_up", PitchDown = "pitch_down";
    public const string YawLeft = "yaw_left", YawRight = "yaw_right";
    public const string RollLeft = "roll_left", RollRight = "roll_right";
    public const string ThrottleUp = "throttle_up", ThrottleDown = "throttle_down", ThrottleZero = "throttle_zero";
    public const string StrafeUp = "strafe_up", StrafeDown = "strafe_down", StrafeLeft = "strafe_left", StrafeRight = "strafe_right";
    public const string Fire = "fire";
    public const string FlightAssist = "flight_assist";
    public const string PipEngines = "pip_engines", PipShields = "pip_shields", PipWeapons = "pip_weapons", PipBalance = "pip_balance";
    public const string TargetNext = "target_next";
    public const string CameraToggle = "camera_toggle";
    public const string ReleaseMouse = "release_mouse";
    public const string Help = "help";
    public const string Dock = "dock", Map = "map", MatchSpeed = "match_speed", SystemMapKey = "system_map", Cruise = "cruise";
    public const string Quit = "quit";
    public const string TimeFaster = "time_faster", TimeSlower = "time_slower";
    public const string NextJob = "next_job";
    public const string Performance = "performance";
    public const string FireMissile = "fire_missile";
    /// <summary>An absolute throttle axis, for a HOTAS lever that stays where you leave it. Unbound until you set it.</summary>
    public const string ThrottleLever = "throttle_lever";

    /// <summary>The actions players can rebind, in the order Settings lists them, with friendly names.</summary>
    public static readonly (string Action, string Name)[] Rebindable =
    [
        (PitchUp, "Pitch up (nose up)"), (PitchDown, "Pitch down"), (YawLeft, "Yaw left"), (YawRight, "Yaw right"),
        (RollLeft, "Roll left"), (RollRight, "Roll right"), (ThrottleUp, "Throttle up"), (ThrottleDown, "Throttle down"),
        (ThrottleZero, "Throttle to zero"), (ThrottleLever, "Throttle lever (HOTAS axis)"),
        (StrafeUp, "Thrust up"), (StrafeDown, "Thrust down"), (StrafeLeft, "Thrust left"), (StrafeRight, "Thrust right"),
        (Fire, "Fire guns"), (FireMissile, "Fire missile"), (TargetNext, "Next target"), (MatchSpeed, "Match target speed"),
        (FlightAssist, "Flight assist"), (PipEngines, "Power to engines"), (PipShields, "Power to shields"),
        (PipWeapons, "Power to weapons"), (PipBalance, "Balance power"), (Cruise, "Cruise and autopilot"),
        (Dock, "Dock and undock"), (Map, "Galaxy map"), (SystemMapKey, "System map"), (NextJob, "Next job"),
        (CameraToggle, "Cockpit or chase camera"), (TimeFaster, "Time faster"), (TimeSlower, "Time slower"), (Help, "Help"),
    ];

    /// <summary>Stick and trigger deadzone, 0 to 0.5.</summary>
    public static float Deadzone { get; set; } = 0.15f;

    public static void Register()
    {
        Bind(PitchUp, keys: [Key.Down], axes: [(JoyAxis.LeftY, 1)]);
        Bind(PitchDown, keys: [Key.Up], axes: [(JoyAxis.LeftY, -1)]);
        Bind(YawLeft, keys: [Key.A, Key.Left], axes: [(JoyAxis.LeftX, -1)]);
        Bind(YawRight, keys: [Key.D, Key.Right], axes: [(JoyAxis.LeftX, 1)]);
        Bind(RollLeft, keys: [Key.Q], buttons: [JoyButton.LeftShoulder], axes: [(JoyAxis.RightX, -1)]);
        Bind(RollRight, keys: [Key.E], buttons: [JoyButton.RightShoulder], axes: [(JoyAxis.RightX, 1)]);
        Bind(ThrottleUp, keys: [Key.W], axes: [(JoyAxis.RightY, -1)]);
        Bind(ThrottleDown, keys: [Key.S], axes: [(JoyAxis.RightY, 1)]);
        Bind(ThrottleZero, keys: [Key.X], buttons: [JoyButton.RightStick]);
        Bind(StrafeUp, keys: [Key.R]);
        Bind(StrafeDown, keys: [Key.F]);
        Bind(StrafeLeft, keys: [Key.Z]);
        Bind(StrafeRight, keys: [Key.C]);
        Bind(Fire, keys: [Key.Space], mouse: [MouseButton.Left], axes: [(JoyAxis.TriggerRight, 1)]);
        Bind(FlightAssist, keys: [Key.G], buttons: [JoyButton.B]);
        Bind(PipEngines, keys: [Key.Key1], buttons: [JoyButton.DpadUp]);
        Bind(PipShields, keys: [Key.Key2], buttons: [JoyButton.DpadLeft]);
        Bind(PipWeapons, keys: [Key.Key3], buttons: [JoyButton.DpadRight]);
        Bind(PipBalance, keys: [Key.Key4], buttons: [JoyButton.DpadDown]);
        Bind(TargetNext, keys: [Key.T], buttons: [JoyButton.A]);
        Bind(CameraToggle, keys: [Key.V], buttons: [JoyButton.Y]);
        Bind(ReleaseMouse, keys: [Key.Escape]);
        Bind(Help, keys: [Key.F1]);
        Bind(Dock, keys: [Key.Enter, Key.KpEnter], buttons: [JoyButton.X]);
        Bind(Map, keys: [Key.M], buttons: [JoyButton.Start]);
        Bind(MatchSpeed, keys: [Key.Tab]);
        Bind(SystemMapKey, keys: [Key.N], buttons: [JoyButton.Back]);
        Bind(Cruise, keys: [Key.J], buttons: [JoyButton.LeftStick]);
        Bind(Quit, keys: [Key.F10]);
        Bind(TimeFaster, keys: [Key.Period]);
        Bind(TimeSlower, keys: [Key.Comma]);
        Bind(NextJob, keys: [Key.L]);
        Bind(Performance, keys: [Key.F3]);
        Bind(FireMissile, keys: [Key.B], mouse: [MouseButton.Right], axes: [(JoyAxis.TriggerLeft, 1)]);
        Bind(ThrottleLever);
    }

    // Player bindings --------------------------------------------------------------------------

    /// <summary>Puts the defaults back, then the player's own bindings over them.</summary>
    public static void Apply(Dictionary<string, List<string>> bindings, float deadzone)
    {
        Register();
        Deadzone = Math.Clamp(deadzone, 0, 0.5f);
        foreach (var (action, _) in Rebindable) InputMap.ActionSetDeadzone(action, Deadzone);
        foreach (var (action, codes) in bindings)
        {
            if (!InputMap.HasAction(action)) continue;
            InputMap.ActionEraseEvents(action);
            foreach (var e in codes.Select(Decode).OfType<InputEvent>()) InputMap.ActionAddEvent(action, e);
        }
    }

    /// <summary>True for controller inputs (buttons and axes), false for keyboard and mouse.</summary>
    public static bool IsController(InputEvent e) => e is InputEventJoypadButton or InputEventJoypadMotion;

    /// <summary>
    /// Replaces an action's keyboard-and-mouse or controller binding with a new input, keeping the
    /// other kind as it was, and returns the action's bindings to save.
    /// </summary>
    public static List<string> Rebind(string action, InputEvent input)
    {
        bool controller = IsController(input);
        var keep = InputMap.ActionGetEvents(action).Where(e => IsController(e) != controller).ToList();
        InputMap.ActionEraseEvents(action);
        foreach (var e in keep) InputMap.ActionAddEvent(action, e);
        if (Encode(input) is { } code && Decode(code) is { } clean) InputMap.ActionAddEvent(action, clean);
        return [.. InputMap.ActionGetEvents(action).Select(Encode).OfType<string>()];
    }

    /// <summary>What an action is bound to, for one kind of device, in words.</summary>
    public static string Describe(string action, bool controller)
    {
        var names = InputMap.ActionGetEvents(action).Where(e => IsController(e) == controller).Select(Name).ToList();
        return names.Count == 0 ? "not set" : string.Join(", ", names);
    }

    private static string Name(InputEvent e) => e switch
    {
        InputEventKey k => OS.GetKeycodeString(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode),
        InputEventMouseButton m => m.ButtonIndex switch { MouseButton.Left => "Left click", MouseButton.Right => "Right click", MouseButton.Middle => "Middle click", _ => $"Mouse {(int)m.ButtonIndex}" },
        InputEventJoypadButton b => b.ButtonIndex switch
        {
            JoyButton.A => "A / Cross", JoyButton.B => "B / Circle", JoyButton.X => "X / Square", JoyButton.Y => "Y / Triangle",
            JoyButton.LeftShoulder => "LB / L1", JoyButton.RightShoulder => "RB / R1", JoyButton.LeftStick => "Left stick click",
            JoyButton.RightStick => "Right stick click", JoyButton.Back => "View / Share", JoyButton.Start => "Menu / Options",
            JoyButton.DpadUp => "D-pad up", JoyButton.DpadDown => "D-pad down", JoyButton.DpadLeft => "D-pad left", JoyButton.DpadRight => "D-pad right",
            _ => $"Button {(int)b.ButtonIndex}",
        },
        InputEventJoypadMotion a => a.Axis switch
        {
            JoyAxis.LeftX => a.AxisValue < 0 ? "Left stick left" : "Left stick right",
            JoyAxis.LeftY => a.AxisValue < 0 ? "Left stick up" : "Left stick down",
            JoyAxis.RightX => a.AxisValue < 0 ? "Right stick left" : "Right stick right",
            JoyAxis.RightY => a.AxisValue < 0 ? "Right stick up" : "Right stick down",
            JoyAxis.TriggerLeft => "LT / L2",
            JoyAxis.TriggerRight => "RT / R2",
            _ => $"Axis {(int)a.Axis} {(a.AxisValue < 0 ? "-" : "+")}",
        },
        _ => e.AsText(),
    };

    /// <summary>An input as a short code for the settings file, e.g. "key:W", "button:0", "axis:1:-".</summary>
    public static string? Encode(InputEvent e) => e switch
    {
        InputEventKey k => $"key:{(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode)}",
        InputEventMouseButton m => $"mouse:{m.ButtonIndex}",
        InputEventJoypadButton b => $"button:{(int)b.ButtonIndex}",
        InputEventJoypadMotion a => $"axis:{(int)a.Axis}:{(a.AxisValue < 0 ? "-" : "+")}",
        _ => null,
    };

    public static InputEvent? Decode(string code)
    {
        var parts = code.Split(':');
        return parts[0] switch
        {
            "key" when Enum.TryParse<Key>(parts[1], out var key) => new InputEventKey { PhysicalKeycode = key },
            "mouse" when Enum.TryParse<MouseButton>(parts[1], out var mouse) => new InputEventMouseButton { ButtonIndex = mouse },
            "button" when int.TryParse(parts[1], out var button) => new InputEventJoypadButton { ButtonIndex = (JoyButton)button, Device = -1 },
            "axis" when parts.Length == 3 && int.TryParse(parts[1], out var axis) =>
                new InputEventJoypadMotion { Axis = (JoyAxis)axis, AxisValue = parts[2] == "-" ? -1 : 1, Device = -1 },
            _ => null,
        };
    }

    /// <summary>The axis bound as a throttle lever, if any, and whether pushing it the bound way means more throttle.</summary>
    public static (JoyAxis Axis, bool Reversed)? Lever =>
        InputMap.ActionGetEvents(ThrottleLever).OfType<InputEventJoypadMotion>().Select(a => ((JoyAxis, bool)?)(a.Axis, a.AxisValue > 0)).FirstOrDefault();

    private static void Bind(string action, Key[]? keys = null, JoyButton[]? buttons = null,
        (JoyAxis Axis, float Direction)[]? axes = null, MouseButton[]? mouse = null)
    {
        if (InputMap.HasAction(action)) InputMap.EraseAction(action);
        InputMap.AddAction(action, 0.15f);
        foreach (var key in keys ?? []) InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        foreach (var button in buttons ?? []) InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button, Device = -1 });
        foreach (var (axis, direction) in axes ?? [])
            InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = axis, AxisValue = direction, Device = -1 });
        foreach (var button in mouse ?? []) InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }
}
