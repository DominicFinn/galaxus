using Godot;
using Kit = Galaxus.Ui.Ui;

namespace Galaxus;

/// <summary>
/// The loading panel for slow steps: building a galaxy, a system or a battle. The work runs on
/// the main thread, so the panel goes up first and the work starts two frames later, once it's on
/// screen. The panel lives on the scene tree's root, so it stays up through the scene reload that
/// starts a game, and <see cref="Finish"/> takes it down once the new screen has drawn.
/// In a browser the page's own overlay is shown as well, because its spinner keeps turning while
/// the game is busy.
/// </summary>
public static class Loading
{
    public static bool IsWeb => OS.HasFeature("web");

    /// <summary>What the web build says under every loading message.</summary>
    public const string WebNote = "This takes a moment.";

    private static CanvasLayer? _layer;
    private static bool _busy;

    /// <summary>
    /// Shows the panel, then runs the work once it's on screen. Work that reloads the scene passes
    /// <paramref name="finish"/> false, and the new scene calls <see cref="Finish"/> when it's ready.
    /// </summary>
    public static async void Run(Node host, string message, Action work, bool finish = true)
    {
        if (_busy) return;
        _busy = true;
        var tree = host.GetTree();
        Show(tree, message);
        await host.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await host.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        try
        {
            work();
        }
        catch (Exception e)
        {
            GD.PushError($"Loading failed: {e}");
            _busy = false;
            Hide();
            return;
        }
        if (finish) Finish(host);
    }

    /// <summary>Takes the panel down after the next frame, so the new screen is drawn behind it first.</summary>
    public static async void Finish(Node host)
    {
        var tree = host.GetTree();
        await host.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await host.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        _busy = false;
        Hide();
    }

    private static void Show(SceneTree tree, string message)
    {
        if (IsWeb)
            JavaScriptBridge.Eval($"window.galaxusLoading && window.galaxusLoading({Js(message + "…")}, {Js(WebNote)})");
        if (DisplayServer.GetName() == "headless") return;

        Hide();
        _layer = new CanvasLayer { Layer = 50, Name = "LoadingPanel" };
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = Control.MouseFilterEnum.Stop };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(shade);

        var panel = new PanelContainer { Theme = Kit.Theme };
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        box.AddThemeConstantOverride("separation", 8);
        var title = Kit.Label(message.ToUpperInvariant() + "…", Kit.Amber);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);
        var note = Kit.Label(IsWeb ? WebNote : "This takes a moment.", Kit.Dim);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(note);
        panel.AddChild(box);
        _layer.AddChild(panel);
        tree.Root.AddChild(_layer);
    }

    private static void Hide()
    {
        if (IsWeb) JavaScriptBridge.Eval("window.galaxusLoaded && window.galaxusLoaded()");
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) _layer.QueueFree();
        _layer = null;
    }

    /// <summary>A C# string as a JavaScript string literal.</summary>
    private static string Js(string text) => System.Text.Json.JsonSerializer.Serialize(text);
}
