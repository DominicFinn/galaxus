using Godot;

namespace Galaxus;

/// <summary>
/// Input actions, defined in code so the bindings live in one readable place.
/// Pitch follows flight-stick convention: pull back (or the Down arrow) to raise the nose.
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
        Bind(Help, keys: [Key.F1], buttons: [JoyButton.Back]);
        Bind(Dock, keys: [Key.Enter, Key.KpEnter], buttons: [JoyButton.X]);
        Bind(Map, keys: [Key.M], buttons: [JoyButton.Start]);
        Bind(MatchSpeed, keys: [Key.Tab], buttons: [JoyButton.LeftStick]);
        Bind(SystemMapKey, keys: [Key.N]);
        Bind(Cruise, keys: [Key.J]);
        Bind(Quit, keys: [Key.F10]);
        Bind(TimeFaster, keys: [Key.Period]);
        Bind(TimeSlower, keys: [Key.Comma]);
        Bind(NextJob, keys: [Key.L]);
        Bind(Performance, keys: [Key.F3]);
    }

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
