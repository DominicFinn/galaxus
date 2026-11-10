using System.Text.Json;
using Godot;
using Galaxus.Core;
using Galaxus.Core.Content;

namespace Galaxus;

/// <summary>Player settings, kept in user://settings.json.</summary>
public sealed class GameSettings
{
    private const string Path = "user://settings.json";

    public bool Fullscreen { get; set; }
    /// <summary>Mouse virtual stick sensitivity, 0.2 to 1.5.</summary>
    public float MouseSensitivity { get; set; } = 0.5f;
    /// <summary>Flip pitch for the mouse, so moving the mouse up pushes the nose down.</summary>
    public bool InvertMouse { get; set; }
    /// <summary>Flip pitch for keys and sticks, so pushing forward raises the nose.</summary>
    public bool InvertStick { get; set; }
    /// <summary>Window size as a multiple of 640x360.</summary>
    public int WindowScale { get; set; } = 2;
    public bool ShowHints { get; set; } = true;
    /// <summary>0 easy, 1 normal, 2 hard: damage you take and how good enemy pilots are. New players start on easy.</summary>
    public int Difficulty { get; set; } = 0;
    /// <summary>Everything the difficulty level changes (see Galaxus.Core.Difficulty).</summary>
    public Galaxus.Core.Difficulty Level => Galaxus.Core.Difficulty.For(Difficulty);
    /// <summary>Volumes, 0 to 1.</summary>
    public float MasterVolume { get; set; } = 0.8f;
    public float MusicVolume { get; set; } = 0.6f;
    public float SfxVolume { get; set; } = 0.8f;
    /// <summary>Your own control bindings, by action, over the defaults (see GameInput).</summary>
    public Dictionary<string, List<string>> Bindings { get; set; } = [];
    /// <summary>Stick and trigger deadzone, 0 to 0.5.</summary>
    public float Deadzone { get; set; } = 0.15f;

    public static GameSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (Godot.FileAccess.FileExists(Path))
                Current = JsonSerializer.Deserialize<GameSettings>(Godot.FileAccess.GetFileAsString(Path)) ?? new();
        }
        catch (JsonException)
        {
            Current = new();
        }
        Current.Apply();
        GameInput.Apply(Current.Bindings, Current.Deadzone);
    }

    public void Save()
    {
        using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(this));
        Apply();
    }

    public void Apply()
    {
        MouseSensitivity = Mathf.Clamp(MouseSensitivity, 0.2f, 1.5f);
        WindowScale = Math.Clamp(WindowScale, 1, 4);
        MasterVolume = Mathf.Clamp(MasterVolume, 0, 1);
        MusicVolume = Mathf.Clamp(MusicVolume, 0, 1);
        SfxVolume = Mathf.Clamp(SfxVolume, 0, 1);
        AudioManager.Instance?.ApplyVolumes();
        if (DisplayServer.GetName() == "headless") return;
        // Only touch the window when its own settings change, so changing the difficulty (or
        // anything else) doesn't pull you out of a full screen you chose some other way.
        if (_appliedFullscreen == Fullscreen && _appliedScale == WindowScale) return;
        _appliedFullscreen = Fullscreen;
        _appliedScale = WindowScale;
        if (Fullscreen) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        else
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(640 * WindowScale, 360 * WindowScale));
        }
    }

    private static bool? _appliedFullscreen;
    private static int _appliedScale;
}

/// <summary>
/// Save slots in user://saves. Slot "autosave" is written each time you dock; slots 1 to 5 are
/// manual saves; "imported" holds the last save brought in from a file.
/// </summary>
public static class SaveSlots
{
    private const string Folder = "user://saves";
    public const string Autosave = "autosave";
    public const string Imported = "imported";
    public static readonly string[] Manual = ["slot-1", "slot-2", "slot-3", "slot-4", "slot-5"];

    private static string PathFor(string slot) => $"{Folder}/{slot}.json";

    public static void Write(string slot, Session session)
    {
        DirAccess.MakeDirRecursiveAbsolute(Folder);
        using var file = Godot.FileAccess.Open(PathFor(slot), Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(SaveGame.Save(session));
    }

    public static SaveGame.SaveData? Peek(string slot) =>
        Godot.FileAccess.FileExists(PathFor(slot)) ? SaveGame.Peek(Godot.FileAccess.GetFileAsString(PathFor(slot))) : null;

    public static Session Read(ContentDatabase db, string slot) => SaveGame.Load(db, Godot.FileAccess.GetFileAsString(PathFor(slot)));

    /// <summary>A slot's file as it stands, for exporting.</summary>
    public static string? ReadText(string slot) =>
        Godot.FileAccess.FileExists(PathFor(slot)) ? Godot.FileAccess.GetFileAsString(PathFor(slot)) : null;

    /// <summary>Keeps a save brought in from a file in the imported slot.</summary>
    public static void WriteImported(string json)
    {
        DirAccess.MakeDirRecursiveAbsolute(Folder);
        using var file = Godot.FileAccess.Open(PathFor(Imported), Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(json);
    }

    /// <summary>Every slot a game can be loaded from, in the order the load page lists them.</summary>
    public static IEnumerable<string> Loadable => Manual.Prepend(Autosave).Append(Imported);

    /// <summary>The most recently saved slot, for Continue.</summary>
    public static string? Latest() => Loadable
        .Select(slot => (slot, data: Peek(slot)))
        .Where(x => x.data != null)
        .OrderByDescending(x => x.data!.SavedAt, StringComparer.Ordinal)
        .Select(x => x.slot)
        .FirstOrDefault();

    public static string Describe(SaveGame.SaveData data) =>
        $"{data.SystemName}, day {data.Day}, {data.Credits:N0} cr. Saved {data.SavedAt}";
}
