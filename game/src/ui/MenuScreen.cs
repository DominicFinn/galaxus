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
    /// <summary>Opens the ship and station viewer.</summary>
    public required Action OpenViewer { get; init; }
    public required Func<IEnumerable<(string Id, string Name, string Summary)>> PreviewBattles { get; init; }
    public required Func<IEnumerable<(string Id, string Name, string Summary)>> Simulations { get; init; }
    public required Action<string> Load { get; init; }
    /// <summary>Checks a save file brought in from elsewhere before it's loaded.</summary>
    public required Func<string, Galaxus.Core.SaveGame.SaveCheck> CheckSave { get; init; }
    public required Action<string> Save { get; init; }
    /// <summary>The game in progress as a save file, for exporting.</summary>
    public required Func<string> CurrentSave { get; init; }
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
    private enum Page { Main, Load, Save, Settings, NewGame, Previews, Result, Simulator, Campaigns, Import }

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

    /// <summary>A page to open instead of the main one, once (for screenshots).</summary>
    public static string? PendingPage { get; set; }

    public void Open(bool inGame)
    {
        InGame = inGame;
        Visible = true;
        ShowPage(Page.Main);
        if (PendingPage is { } pending) OpenPage(pending);
    }

    /// <summary>Opens a page by name, for screenshots: newgame, campaigns, simulator, previews.</summary>
    public void OpenPage(string name)
    {
        if (Enum.TryParse<Page>(name, ignoreCase: true, out var page)) ShowPage(page);
        else if (name == "newgame") ShowPage(Page.NewGame);
    }

    private void ShowPage(Page page)
    {
        _page = page;
        foreach (var child in _items.GetChildren()) child.QueueFree();
        _panel.CustomMinimumSize = new Vector2(page == Page.Main ? 200 : page is Page.NewGame or Page.Previews or Page.Simulator or Page.Campaigns ? 440 : 360, 0);
        _footer.Text = Loading.IsWeb
            ? "Web preview. The desktop game is free to download, and your game can come with you: pause, then Save game, Export this game to a file."
            : "Galaxus is a working title. Early build.";

        switch (page)
        {
            case Page.Main: BuildMain(); break;
            case Page.Load: BuildSlots(saving: false); break;
            case Page.Save: BuildSlots(saving: true); break;
            case Page.Settings: BuildSettings(); break;
            case Page.NewGame: BuildNewGame(); break;
            case Page.Previews: BuildPreviews(); break;
            case Page.Simulator: BuildSimulator(); break;
            case Page.Campaigns: BuildCampaigns(); break;
            case Page.Result: BuildResult(); break;
            case Page.Import: BuildImport(); break;
        }
        // Centre the panel under the logo once its contents have sized it, and again once wrapped
        // text has settled its height.
        CallDeferred(MethodName.CentrePanel);
        GetTree().CreateTimer(0.05).Timeout += CentrePanel;
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
        if (!InGame) Add(Ui.Button("Ships and stations", () => _actions.OpenViewer()));
        Add(Ui.Button("Settings", () => { _back = Page.Main; ShowPage(Page.Settings); }));
        if (InGame) Add(Ui.Button("Main menu", () => _actions.MainMenu()));
        // A browser tab has nothing to quit to, so the web preview offers the desktop game instead.
        if (Loading.IsWeb) Add(Ui.Button("Get the desktop game", () => OS.ShellOpen(DownloadUrl)));
        else Add(Ui.Button("Quit", () => _actions.Quit()));
    }

    public const string DownloadUrl = "https://galaxus-game.web.app/#download";

    /// <summary>Ways to start: as a free trader with a small ship, or enlisted in a campaign.</summary>
    private void BuildNewGame()
    {
        Add(Ui.Label("NEW GAME", Ui.Amber));
        var tutorial = _actions.Campaigns().FirstOrDefault(c => c.Name.StartsWith("Tutorial"));
        if (tutorial.Id != null)
            Option("Tutorial: First Flight", "Learn to fly, fight and find your way, with an instructor.", () => _actions.NewCampaign(tutorial.Id));
        Option("Free trader", "A small ship, a little money and the whole galaxy. Trade, take jobs, pick sides.", () => _actions.NewGame());
        Option("Campaign", "Enlist with a great power and fly a run of missions from your flagship.", () => ShowPage(Page.Campaigns));
        Option("Dogfight simulator", "Short practice fights, each a little harder than the last.", () => ShowPage(Page.Simulator));
        Option("Battle preview", "Jump straight into one of the big set-piece battles.", () => ShowPage(Page.Previews));
        Add(Ui.Button("Back", () => ShowPage(Page.Main)));
    }

    /// <summary>A choice on the new game page: a button with a line saying what it is underneath.</summary>
    private void Option(string title, string description, Action pressed) => Add(TwoLineButton(title, description, pressed));

    /// <summary>A button with a title and a dimmer line of description under it.</summary>
    private static Button TwoLineButton(string title, string description, Action pressed)
    {
        var button = Ui.Button($"{title}\n{description}", pressed);
        button.Alignment = HorizontalAlignment.Left;
        button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        button.CustomMinimumSize = new Vector2(410, 0);
        return button;
    }

    /// <summary>The campaigns, each with what it is about.</summary>
    private void BuildCampaigns()
    {
        Add(Ui.Label("CAMPAIGNS", Ui.Amber));
        var list = Scrolled();
        foreach (var (id, name, summary) in _actions.Campaigns().Where(c => !c.Name.StartsWith("Tutorial")))
        {
            var campaignId = id;
            list.AddChild(TwoLineButton(name, summary, () => _actions.NewCampaign(campaignId)));
        }
        Add(Ui.Button("Back", () => ShowPage(Page.NewGame)));
    }

    /// <summary>A scrolling list that fits the window under the logo, for pages with more than a few entries.</summary>
    private VBoxContainer Scrolled()
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(420, Mathf.Clamp(Size.Y - 210, 140, 420)), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);
        Add(scroll);
        return list;
    }

    /// <summary>Set pieces to fly on their own: a quick taste of the battles, and a good test of how they run.</summary>
    private void BuildPreviews()
    {
        Add(Ui.Label("BATTLE PREVIEW", Ui.Amber));
        Add(Ui.Label("Start in the middle of a set piece. When it's over, the preview ends. F3 shows performance.", Ui.Dim, wrap: true));
        var list = Scrolled();
        foreach (var (id, name, summary) in _actions.PreviewBattles())
        {
            var battleId = id;
            list.AddChild(TwoLineButton(name, summary, () => _actions.PreviewBattle(battleId)));
        }
        Add(Ui.Button("Back", () => ShowPage(Page.NewGame)));
    }

    /// <summary>The dogfight simulator: short drills, from one slow target up to an ace and a furball.</summary>
    private void BuildSimulator()
    {
        Add(Ui.Label("DOGFIGHT SIMULATOR", Ui.Amber));
        Add(Ui.Label("Short fights to practise in, each a little harder. T targets, the red diamond shows where to aim, Space fires, Q and E roll, slow down to turn tighter.", Ui.Dim, wrap: true));
        var list = Scrolled();
        foreach (var (id, name, summary) in _actions.Simulations())
        {
            var simId = id;
            list.AddChild(TwoLineButton(name, summary, () => _actions.PreviewBattle(simId)));
        }
        Add(Ui.Button("Back", () => ShowPage(Page.NewGame)));
    }

    private string _resultTitle = "", _resultText = "";
    private Action? _again, _home, _next;

    /// <summary>The end of a preview or simulation: how it went, and what next.</summary>
    public void ShowResult(string title, string text, Action again, Action home, Action? next = null)
    {
        _resultTitle = title;
        _resultText = text;
        _again = again;
        _home = home;
        _next = next;
        InGame = true;
        Visible = true;
        ShowPage(Page.Result);
    }

    private void BuildResult()
    {
        Add(Ui.Label(_resultTitle, Ui.Amber));
        Add(Ui.Label(_resultText, Ui.Dim, wrap: true));
        if (_next != null) Add(Ui.Button("Next simulation", () => _next?.Invoke()));
        Add(Ui.Button("Fly it again", () => _again?.Invoke()));
        Add(Ui.Button("Main menu", () => _home?.Invoke()));
    }

    private void BuildSlots(bool saving)
    {
        Add(Ui.Label(saving ? "SAVE GAME" : "LOAD GAME", Ui.Amber));
        var slots = saving ? SaveSlots.Manual : SaveSlots.Loadable.Where(s => s != SaveSlots.Imported || SaveSlots.Peek(s) != null).ToArray();
        foreach (var slot in slots)
        {
            var data = SaveSlots.Peek(slot);
            string label = SlotName(slot) + ": " + (data != null ? SaveSlots.Describe(data) : "empty");
            var button = Ui.Button(label, () =>
            {
                if (saving) { _actions.Save(slot); ShowPage(Page.Save); }
                else if (data != null) _actions.Load(slot);
            });
            button.Alignment = HorizontalAlignment.Left;
            button.Disabled = !saving && data == null;
            Add(button);
        }
        if (saving) Add(Ui.Button("Export this game to a file", ExportGame));
        else Add(Ui.Button("Import a save file", ImportSave));
        if (_transferMessage is { } message) Add(Wrapped(message, Ui.Green));
        _transferMessage = null;
        Add(Ui.Button("Back", () => ShowPage(_back)));
    }

    private static string SlotName(string slot) => slot switch
    {
        SaveSlots.Autosave => "Autosave",
        SaveSlots.Imported => "Imported",
        _ => $"Slot {slot[^1]}",
    };

    private string? _transferMessage;

    private void ExportGame()
    {
        string json = _actions.CurrentSave();
        if (SaveTransfer.Export(this, json))
        {
            _transferMessage = Loading.IsWeb
                ? $"Downloaded {SaveTransfer.FileName(Galaxus.Core.SaveGame.Peek(json)!)}. Load it in the desktop game with Load game, Import a save file."
                : "Choose where to save the file, then load it in the other game with Load game, Import a save file.";
            ShowPage(Page.Save);
        }
    }

    // The import page shows what checking the file found: why it can't be loaded, or a warning
    // to read before loading it.
    private Galaxus.Core.SaveGame.SaveCheck? _import;
    private string? _importJson;

    private void ImportSave()
    {
        SaveTransfer.Import(this, json =>
        {
            var check = _actions.CheckSave(json);
            if (check.Problem == null && check.Warning == null)
            {
                SaveSlots.WriteImported(json);
                _actions.Load(SaveSlots.Imported);
                return;
            }
            _import = check;
            _importJson = json;
            ShowPage(Page.Import);
        });
    }

    private void BuildImport()
    {
        Add(Ui.Label("IMPORT A SAVE", Ui.Amber));
        if (_import is { Problem: { } problem })
            Add(Wrapped(problem, Ui.Red));
        else if (_import is { Data: { } data, Warning: { } warning } && _importJson is { } json)
        {
            Add(Wrapped(SaveSlots.Describe(data), Ui.Green));
            Add(Wrapped(warning, Ui.Amber));
            Add(Ui.Button("Load it anyway", () =>
            {
                SaveSlots.WriteImported(json);
                _actions.Load(SaveSlots.Imported);
            }));
        }
        Add(Ui.Button("Try another file", ImportSave));
        Add(Ui.Button("Back", () => { _import = null; _importJson = null; ShowPage(Page.Load); }));
    }

    private static Label Wrapped(string text, Color colour)
    {
        var label = Ui.Label(text, colour, wrap: true);
        label.CustomMinimumSize = new Vector2(400, 0);
        return label;
    }

    private void BuildSettings()
    {
        var s = GameSettings.Current;
        Add(Ui.Label("SETTINGS", Ui.Amber));
        Add(Choice("Difficulty", ["Easy", "Normal", "Hard"], s.Difficulty, i => s.Difficulty = i));
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
