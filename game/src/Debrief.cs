using Godot;
using Galaxus.Core.Battles;
using Galaxus.Core;
using UiKit = Galaxus.Ui.Ui;

namespace Galaxus;

/// <summary>
/// When a battle is lost: how close you came and why, with the choice to go again from the wave
/// you were fighting (beaten waves stay beaten, finished objectives stay finished) or take the loss.
/// </summary>
public partial class Main
{
    private PanelContainer? _debrief;
    private RichTextLabel _debriefText = null!;
    private Button _retryButton = null!;
    private (AcceptedMission Job, RetryPoint Point, string Text, int Bonuses)? _pendingLoss;

    private void BuildDebrief(CanvasLayer layer)
    {
        _debrief = new PanelContainer { Visible = false, Theme = UiKit.Theme, MouseFilter = Control.MouseFilterEnum.Stop };
        _debrief.SetAnchorsPreset(Control.LayoutPreset.Center);
        _debrief.CustomMinimumSize = new Vector2(320, 0);
        _debrief.OffsetLeft = -160;
        _debrief.OffsetRight = 160;
        _debrief.OffsetTop = -90;
        _debrief.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.03f, 0.03f, 0.95f), BorderColor = UiKit.Red with { A = 0.7f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        layer.AddChild(_debrief);
        var box = new VBoxContainer();
        _debrief.AddChild(box);
        box.AddChild(UiKit.Label("THE BATTLE IS LOST", UiKit.Red));
        _debriefText = UiKit.Text();
        _debriefText.FitContent = true;
        _debriefText.ScrollActive = false;
        _debriefText.CustomMinimumSize = new Vector2(300, 0);
        box.AddChild(_debriefText);
        var row = new HBoxContainer();
        box.AddChild(row);
        _retryButton = UiKit.Button("Retry from the last wave", RetryBattle);
        row.AddChild(_retryButton);
        row.AddChild(UiKit.Button("Accept defeat", AcceptDefeat));
    }

    private void ShowDebrief(AcceptedMission job, RetryPoint point, string text, string debrief, int bonuses)
    {
        _pendingLoss = (job, point, text, bonuses);
        _debriefText.Text = $"{text}\n\n{debrief}" + (point.Attempt > 1 ? $"\n\n[color=#{UiKit.Hex(UiKit.Dim)}]Attempt {point.Attempt}.[/color]" : "");
        _retryButton.Text = point.WaveId == "" ? "Try again" : "Retry from the last wave";
        _debrief!.Visible = true;
        _timeWarp = 1;
        FreeMouse();
    }

    private void RetryBattle()
    {
        if (_pendingLoss is not { } loss) return;
        _pendingLoss = null;
        _debrief!.Visible = false;
        ClearBattle();
        // Back in the fight, whole, where you were.
        _respawnTimers.Remove(_player);
        _crashed = false;
        _player.Respawn(_player.Body.Position, _player.Body.Orientation);
        Galaxus.Core.Battles.Battle.ApplyToPlayer(_player, GameSettings.Current.Level); ApplyCrew();
        StartBattle(loss.Job, loss.Point);
        _hud.Message(loss.Point.WaveId == "" ? "AGAIN, FROM THE TOP" : "BACK INTO IT: FROM THE LAST WAVE");
        if (_screen == Screen.Flight) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void AcceptDefeat()
    {
        if (_pendingLoss is not { } loss) return;
        _pendingLoss = null;
        _debrief!.Visible = false;
        ConcludeBattle(loss.Job, false, loss.Bonuses, loss.Text);
        if (_screen == Screen.Flight) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private bool DebriefOpen => _debrief?.Visible == true;
}
