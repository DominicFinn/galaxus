using Godot;
using Galaxus.Core;

namespace Galaxus;

/// <summary>
/// Moves saves in and out of the game as files, so a game started in the web preview can be
/// carried on in the desktop game, and the other way round. In a browser, exporting downloads the
/// file and importing opens the browser's file picker. On a computer, both use a file dialog.
/// </summary>
public static class SaveTransfer
{
    /// <summary>A file name for an exported save: the captain and the day.</summary>
    public static string FileName(SaveGame.SaveData data)
    {
        var captain = new string(data.Captain.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
        while (captain.Contains("--")) captain = captain.Replace("--", "-");
        return $"galaxus-{(captain.Length > 0 ? captain : "save")}-day-{data.Day}.json";
    }

    /// <summary>Writes a save slot out as a file. Returns false if the slot is empty.</summary>
    public static bool Export(Node host, string slot)
    {
        if (SaveSlots.ReadText(slot) is not { } json || SaveGame.Peek(json) is not { } data) return false;
        string name = FileName(data);
        if (Loading.IsWeb)
        {
            JavaScriptBridge.DownloadBuffer(System.Text.Encoding.UTF8.GetBytes(json), name, "application/json");
            return true;
        }
        var dialog = Dialog(FileDialog.FileModeEnum.SaveFile, "Export save");
        dialog.CurrentFile = name;
        dialog.FileSelected += path =>
        {
            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            file?.StoreString(json);
        };
        Show(host, dialog);
        return true;
    }

    /// <summary>Asks for a save file and hands back its text, or nothing if the player cancels.</summary>
    public static void Import(Node host, Action<string> received)
    {
        if (Loading.IsWeb)
        {
            ImportFromBrowser(received);
            return;
        }
        var dialog = Dialog(FileDialog.FileModeEnum.OpenFile, "Import save");
        dialog.FileSelected += path => received(Godot.FileAccess.GetFileAsString(path));
        Show(host, dialog);
    }

    private static FileDialog Dialog(FileDialog.FileModeEnum mode, string title)
    {
        var dialog = new FileDialog
        {
            FileMode = mode,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = title,
            UseNativeDialog = true,
            Filters = ["*.json ; Galaxus saves"],
            CurrentDir = OS.GetSystemDir(OS.SystemDir.Downloads),
        };
        return dialog;
    }

    private static void Show(Node host, FileDialog dialog)
    {
        host.AddChild(dialog);
        dialog.Canceled += dialog.QueueFree;
        dialog.FileSelected += _ => dialog.QueueFree();
        dialog.PopupCentered(new Vector2I(720, 480));
    }

    // The browser hands the file back through a JavaScript callback. Godot only keeps the callback
    // alive while something holds it, so it lives here.
    private static JavaScriptObject? _callback;
    private static Action<string>? _received;

    private static void ImportFromBrowser(Action<string> received)
    {
        _received = received;
        _callback ??= JavaScriptBridge.CreateCallback(Callable.From<Godot.Collections.Array>(OnBrowserFile));
        JavaScriptBridge.GetInterface("window").Set("galaxusImportedSave", _callback);
        JavaScriptBridge.Eval("""
            (function () {
                const input = document.createElement('input');
                input.type = 'file';
                input.accept = '.json,application/json';
                input.onchange = () => {
                    const file = input.files && input.files[0];
                    if (file) file.text().then((text) => window.galaxusImportedSave(text));
                };
                input.click();
            })();
            """, true);
    }

    private static void OnBrowserFile(Godot.Collections.Array args)
    {
        if (args.Count > 0 && _received is { } received) received(args[0].AsString());
    }
}
