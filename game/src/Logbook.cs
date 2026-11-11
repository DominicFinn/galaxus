using Godot;
using Galaxus.Ui;
using UiKit = Galaxus.Ui.Ui;

namespace Galaxus;

/// <summary>
/// The logbook in flight: O opens it over the view without stopping anything, so you can read the
/// news and your lore on the autopilot. The HUD says when something new has gone in.
/// </summary>
public partial class Main
{
    private PanelContainer _logbook = null!;
    private LogbookView _logbookView = null!;
    private int _logbookNoted;

    private void BuildLogbook(CanvasLayer layer)
    {
        _logbook = new PanelContainer { Visible = false, Theme = UiKit.Theme, MouseFilter = Control.MouseFilterEnum.Stop };
        _logbook.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepSize, 40);
        _logbook.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.05f, 0.04f, 0.9f), BorderColor = UiKit.Green with { A = 0.6f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        layer.AddChild(_logbook);
        var box = new VBoxContainer();
        _logbook.AddChild(box);
        var header = new HBoxContainer();
        box.AddChild(header);
        var title = UiKit.Label("LOGBOOK", UiKit.Amber);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);
        header.AddChild(UiKit.Button("Close (O)", ToggleLogbook));
        _logbookView = new LogbookView(_session, OpenMapAt);
        box.AddChild(_logbookView);
        _logbookNoted = _session.Career.LogbookCount;
    }

    private void ToggleLogbook()
    {
        _logbook.Visible = !_logbook.Visible;
        if (_logbook.Visible)
        {
            _logbookView.Refresh();
            _logbookView.MarkRead();
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (_screen == Screen.Flight && !Hailing) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>A note on the HUD when something new goes in the logbook.</summary>
    private void NoteLogbook()
    {
        int count = _session.Career.LogbookCount;
        if (count > _logbookNoted && _screen == Screen.Flight) _hud.Message("NEW IN YOUR LOGBOOK (O)");
        _logbookNoted = count;
    }
}
