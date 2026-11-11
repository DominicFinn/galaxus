using Godot;
using Galaxus.Core.Law;

namespace Galaxus.Ui;

/// <summary>
/// A hail in flight: the calling ship or station turning slowly in a little view, the face of
/// whoever's speaking, what they say, and your replies as buttons. Sits at the top of the screen
/// so you can still see where you're going.
/// </summary>
public partial class HailPanel : PanelContainer
{
    private SubViewport _view = null!;
    private MeshInstance3D _model = null!;
    private Camera3D _camera = null!;
    private PortraitView _face = null!;
    private Label _name = null!, _who = null!;
    private RichTextLabel _text = null!;
    private VBoxContainer _options = null!;

    /// <summary>Called with the action of the reply you pick ("close" when the hail is over).</summary>
    public event Action<string>? Chosen;

    public override void _Ready()
    {
        Theme = Ui.Theme;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.CenterTop);
        CustomMinimumSize = new Vector2(420, 0);
        OffsetLeft = -210;
        OffsetRight = 210;
        OffsetTop = 34;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.05f, 0.04f, 0.94f), BorderColor = Ui.Amber with { A = 0.7f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 5, ContentMarginBottom = 5,
        });

        var row = new HBoxContainer();
        AddChild(row);

        // The calling ship, lit and turning, in its own little world.
        var holder = new SubViewportContainer { CustomMinimumSize = new Vector2(84, 84), Stretch = true };
        row.AddChild(holder);
        _view = new SubViewport { OwnWorld3D = true, TransparentBg = true, Size = new Vector2I(168, 168), RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible };
        holder.AddChild(_view);
        _model = new MeshInstance3D();
        _view.AddChild(_model);
        _camera = new Camera3D { Fov = 40, Current = true };
        _view.AddChild(_camera);
        _view.AddChild(new DirectionalLight3D { Rotation = new Vector3(-0.6f, 0.7f, 0), LightEnergy = 1.3f });
        _view.AddChild(new WorldEnvironment { Environment = new Godot.Environment { AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.35f, 0.4f, 0.45f) } });

        _face = new PortraitView { CustomMinimumSize = new Vector2(72, 84) };
        row.AddChild(_face);

        var words = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(words);
        _name = Ui.Label("", Ui.Amber);
        words.AddChild(_name);
        _who = Ui.Label("", Ui.Dim, wrap: true);
        words.AddChild(_who);
        _text = Ui.Text();
        _text.FitContent = true;
        _text.ScrollActive = false;
        words.AddChild(_text);
        _options = new VBoxContainer();
        words.AddChild(_options);
    }

    /// <summary>Opens on a caller. The mesh is drawn turning; radius frames it.</summary>
    public void Open(string name, string who, Mesh? mesh, float radius, Action<PortraitView> face, HailTurn turn)
    {
        _name.Text = name;
        _who.Text = who;
        _model.Mesh = mesh;
        float distance = Mathf.Max(radius, 1) * 3.2f;
        _camera.Position = new Vector3(0, distance * 0.35f, distance);
        _camera.LookAt(Vector3.Zero);
        _camera.Far = distance * 10;
        face(_face);
        Show(turn);
        Visible = true;
    }

    /// <summary>Shows a turn: their words and your choices, or a close button when it's over.</summary>
    public void Show(HailTurn turn)
    {
        _text.Text = turn.Text;
        foreach (var child in _options.GetChildren()) child.QueueFree();
        var options = turn.Over ? [new HailOption("Close channel", "close")] : turn.Options;
        foreach (var option in options)
        {
            var action = option.Action;
            var button = Ui.Button(option.Label, () => Chosen?.Invoke(action));
            button.Disabled = !option.Enabled;
            button.Alignment = HorizontalAlignment.Left;
            _options.AddChild(button);
        }
    }

    public override void _Process(double delta)
    {
        if (Visible) _model.RotateY((float)delta * 0.4f);
    }
}
