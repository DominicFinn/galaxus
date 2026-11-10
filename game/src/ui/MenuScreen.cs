using Godot;

namespace Galaxus.Ui;

/// <summary>What the menu can ask the game to do.</summary>
public sealed class MenuActions
{
    public required Action NewGame { get; init; }
    /// <summary>Starts a campaign by id.</summary>
    public required Action<string> NewCampaign { get; init; }
    /// <summary>The campaigns on offer: id, name and summary.</summary>
    public required Func<IEnumerable<(string Id, string Name, string Summary)>> Campaigns { get; init; }
    /// <summary>Starts a set-piece battle on its own, as a preview.</summary>
    public required Action<string> PreviewBattle { get; init; }
    public required Func<IEnumerable<(string Id, string Name, string Summary)>> PreviewBattles { get; init; }
    public required Action<string> Load { get; init; }
    public required Action<string> Save { get; init; }
    public required Action Resume { get; init; }
    public required Action MainMenu { get; init; }
    public required Action Quit { get; init; }
}

/// <summary>
/// The main menu over a live space backdrop, and the same menu as a pause screen in game:
/// new game, continue, load, save, settings and quit.
/// </summary>
public partial class MenuScreen : Control
{
    private enum Page { Main, Load, Save, Settings, NewGame, Previews, Result }

    private MenuActions _actions = null!;
    private Logo _logo = null!;
    private PanelContainer _panel = null!;
    private VBoxContainer _items = null!;
    private Label _footer = null!;
    private Page _page;
    private Page _back;

    /// <summary>True in game (pause menu), false at the title screen.</summary>
    public bool InGame { get; set; }

    public void Init(MenuActions actions) => _actions = actions;

    public override void _Ready()
    {
        Theme = Ui.Theme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _logo = new Logo { LogoScale = 0.75f };
        _logo.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _logo.OffsetBottom = 110;
        AddChild(_logo);

        _panel = new PanelContainer { Position = new Vector2(220, 120), CustomMinimumSize = new Vector2(200, 0) };
        AddChild(_panel);
        _items = new VBoxContainer();
        _panel.AddChild(_items);

        _footer = Ui.Label("", Ui.Dim);
        AddChild(_footer);
        _footer.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft, LayoutPresetMode.KeepSize, 8);
    }

    public override void _Process(double delta) => _logo.Spin += (float)delta * 0.08f;

    public void Open(bool inGame)
    {
        InGame = inGame;
        Visible = true;
        ShowPage(Page.Main);
    }

    private void ShowPage(Page page)
    {
        _page = page;
        foreach (var child in _items.GetChildren()) child.QueueFree();
        _panel.CustomMinimumSize = new Vector2(page == Page.Main ? 200 : page is Page.NewGame or Page.Previews ? 440 : 360, 0);
        _footer.Text = "Galaxus is a working title. Early build.";

        switch (page)
        {
            case Page.Main: BuildMain(); break;
            case Page.Load: BuildSlots(saving: false); break;
            case Page.Save: BuildSlots(saving: true); break;
            case Page.Settings: BuildSettings(); break;
            case Page.NewGame: BuildNewGame(); break;
            case Page.Previews: BuildPreviews(); break;
            case Page.Result: BuildResult(); break;
        }
        // Centre the panel under the logo once its contents have sized it.
        CallDeferred(MethodName.CentrePanel);
    }

    private void CentrePanel()
    {
        _panel.ResetSize();
        _panel.Position = new Vector2((Size.X - _panel.Size.X) / 2, 120);
    }

    private void BuildMain()
    {
        if (InGame)
        {
            Add(Ui.Button("Resume", () => _actions.Resume()));
            Add(Ui.Button("Save game", () => { _back = Page.Main; ShowPage(Page.Save); }));
        }
        else
        {
            var latest = SaveSlots.Latest();
            var continueButton = Ui.Button("Continue", () => { if (latest != null) _actions.Load(latest); });
            continueButton.Disabled = latest == null;
            continueButton.TooltipText = latest != null && SaveSlots.Peek(latest) is { } d ? SaveSlots.Describe(d) : "No saved games yet.";
            Add(continueButton);
            Add(Ui.Button("New game", () => { _back = Page.Main; ShowPage(Page.NewGame); }));
        }
        Add(Ui.Button("Load game", () => { _back = Page.Main; ShowPage(Page.Load); }));
        Add(Ui.Button("Settings", () => { _back = Page.Main; ShowPage(Page.Settings); }));
        if (InGame) Add(Ui.Button("Main menu", () => _actions.MainMenu()));
        Add(Ui.Button("Quit", () => _actions.Quit()));
    }

    /// <summary>Ways to start: as a free trader with a small ship, or enlisted in a campaign.</summary>
    private void BuildNewGame()
    {
        Add(Ui.Label("NEW GAME", Ui.Amber));
        Add(Ui.Button("Battle preview: jump straight into a set piece", () => ShowPage(Page.Previews)));
        Add(Ui.Label(" ", Ui.Dim));
        Add(Ui.Label("Free trader: a small ship, a little money and the whole galaxy. Trade, take jobs, pick sides.", Ui.Dim, wrap: true));
        Add(Ui.Button("Start as a free trader", () => _actions.NewGame()));
        foreach (var (id, name, summary) in _actions.Campaigns())
        {
            Add(Ui.Label(" ", Ui.Dim));
            Add(Ui.Label(summary, Ui.Dim, wrap: true));
            var campaignId = id;
            Add(Ui.Button($"Campaign: {name}", () => _actions.NewCampaign(campaignId)));
        }
        Add(Ui.Button("Back", () => ShowPage(_back)));
    }

    /// <summary>Set pieces to fly on their own: a quick taste of the battles, and a good test of how they run.</summary>
    private void BuildPreviews()
    {
        Add(Ui.Label("BATTLE PREVIEW", Ui.Amber));
        Add(Ui.Label("Start in the middle of a set piece. When it's over, the preview ends. F3 shows performance.", Ui.Dim, wrap: true));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(420, 230) };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);
        foreach (var (id, name, summary) in _actions.PreviewBattles())
        {
            var battleId = id;
            var button = Ui.Button($"{name}: {summary}", () => _actions.PreviewBattle(battleId));
            button.Alignment = HorizontalAlignment.Left;
            list.AddChild(button);
        }
        Add(scroll);
        Add(Ui.Button("Back", () => ShowPage(Page.NewGame)));
    }

    private string _resultTitle = "", _resultText = "";
    private Action? _again, _home;

    /// <summary>The end of a preview: how it went, and what next.</summary>
    public void ShowResult(string title, string text, Action again, Action home)
    {
        _resultTitle = title;
        _resultText = text;
        _again = again;
        _home = home;
        InGame = true;
        Visible = true;
        ShowPage(Page.Result);
    }

    private void BuildResult()
    {
        Add(Ui.Label(_resultTitle, Ui.Amber));
        Add(Ui.Label(_resultText, Ui.Dim, wrap: true));
        Add(Ui.Button("Fly it again", () => _again?.Invoke()));
        Add(Ui.Button("Main menu", () => _home?.Invoke()));
    }

    private void BuildSlots(bool saving)
    {
        Add(Ui.Label(saving ? "SAVE GAME" : "LOAD GAME", Ui.Amber));
        var slots = saving ? SaveSlots.Manual : SaveSlots.Manual.Prepend(SaveSlots.Autosave).ToArray();
        foreach (var slot in slots)
        {
            var data = SaveSlots.Peek(slot);
            string label = (slot == SaveSlots.Autosave ? "Autosave" : $"Slot {slot[^1]}") + ": " + (data != null ? SaveSlots.Describe(data) : "empty");
            var button = Ui.Button(label, () =>
            {
                if (saving) { _actions.Save(slot); ShowPage(Page.Save); }
                else if (data != null) _actions.Load(slot);
            });
            button.Alignment = HorizontalAlignment.Left;
            button.Disabled = !saving && data == null;
            Add(button);
        }
        Add(Ui.Button("Back", () => ShowPage(_back)));
    }

    private void BuildSettings()
    {
        var s = GameSettings.Current;
        Add(Ui.Label("SETTINGS", Ui.Amber));
        Add(Toggle("Fullscreen", s.Fullscreen, v => s.Fullscreen = v));
        Add(Choice("Window size", ["640 x 360", "1280 x 720", "1920 x 1080", "2560 x 1440"], s.WindowScale - 1, i => s.WindowScale = i + 1));
        Add(Slider("Mouse sensitivity", s.MouseSensitivity, 0.2f, 1.5f, v => s.MouseSensitivity = v));
        Add(Toggle("Invert mouse pitch", s.InvertMouse, v => s.InvertMouse = v));
        Add(Toggle("Invert keys and stick pitch", s.InvertStick, v => s.InvertStick = v));
        Add(Toggle("Show control hints", s.ShowHints, v => s.ShowHints = v));
        Add(Slider("Master volume", s.MasterVolume, 0, 1, v => { s.MasterVolume = v; AudioManager.Instance?.ApplyVolumes(); }));
        Add(Slider("Music", s.MusicVolume, 0, 1, v => { s.MusicVolume = v; AudioManager.Instance?.ApplyVolumes(); }));
        Add(Slider("Sound effects", s.SfxVolume, 0, 1, v => { s.SfxVolume = v; AudioManager.Instance?.ApplyVolumes(); }));
        Add(Ui.Button("Back", () => { s.Save(); ShowPage(_back); }));
    }

    private Control Toggle(string label, bool value, Action<bool> set)
    {
        var box = new CheckButton { Text = label, ButtonPressed = value, FocusMode = FocusModeEnum.None };
        box.Toggled += v => { set(v); GameSettings.Current.Save(); };
        return box;
    }

    private Control Choice(string label, string[] options, int selected, Action<int> set)
    {
        var row = new HBoxContainer();
        row.AddChild(Ui.Label(label));
        var picker = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var o in options) picker.AddItem(o);
        picker.Selected = Math.Clamp(selected, 0, options.Length - 1);
        picker.ItemSelected += i => { set((int)i); GameSettings.Current.Save(); };
        row.AddChild(picker);
        return row;
    }

    private Control Slider(string label, float value, float min, float max, Action<float> set)
    {
        var row = new HBoxContainer();
        row.AddChild(Ui.Label(label));
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = 0.05, Value = value, SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        slider.ValueChanged += v => set((float)v);
        slider.DragEnded += _ => GameSettings.Current.Save();
        row.AddChild(slider);
        return row;
    }

    private void Add(Control control) => _items.AddChild(control);
}

/// <summary>Logo fades in over the stars, holds, and fades out. Any key or click skips it.</summary>
public partial class Splash : Control
{
    private Logo _logo = null!;
    private float _time;
    private const float FadeIn = 1.2f, Hold = 1.6f, FadeOut = 0.7f;

    public event Action? Finished;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        _logo = new Logo { LogoScale = 1f, Alpha = 0 };
        _logo.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_logo);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        _logo.Spin += (float)delta * 0.08f;
        _logo.Alpha = _time < FadeIn ? _time / FadeIn
            : _time < FadeIn + Hold ? 1
            : 1 - (_time - FadeIn - Hold) / FadeOut;
        QueueRedraw();
        if (_time > FadeIn + Hold + FadeOut) Finish();
    }

    public override void _Draw()
    {
        // Black that clears with the logo, revealing the space backdrop behind.
        float a = _time < FadeIn + Hold ? 1 : Mathf.Clamp(1 - (_time - FadeIn - Hold) / FadeOut, 0, 1);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, a));
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } or InputEventKey { Pressed: true }) Finish();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } or InputEventJoypadButton { Pressed: true }) Finish();
    }

    private void Finish()
    {
        if (!Visible) return;
        Visible = false;
        Finished?.Invoke();
        QueueFree();
    }
}
