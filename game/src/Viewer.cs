using Godot;
using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Kit = Galaxus.Ui.Ui;

namespace Galaxus;

/// <summary>
/// The ship and station viewer: browse every hull, warship and station, turn them round, zoom in,
/// and read what they are. Opened from the main menu, or on its own as the Galaxus Preview build.
/// </summary>
public partial class Viewer : Node3D
{
    private sealed record Entry(string Category, string Name, Func<Node3D> Build, string Info, float Radius, string? Id = null);

    private readonly ContentDatabase _db;
    private readonly Camera3D _camera;
    private readonly Action? _back;
    private readonly List<Entry> _entries = [];
    private readonly List<string> _categories = [];

    private Node3D _pivot = null!;
    private Node3D? _model, _scaleModel;
    private OptionButton _category = null!;
    private ItemList _list = null!;
    private RichTextLabel _info = null!;
    private CheckBox _showScale = null!;
    private PanelContainer _panel = null!;
    private List<Entry> _shown = [];
    private float _distance = 40, _zoom = 1, _yaw = 0.6f, _pitch = 0.25f, _idle;
    private float _radius = 10;

    public Viewer() { _db = null!; _camera = null!; }

    /// <param name="back">Back to the menu, or null in the standalone Preview build (which offers Quit instead).</param>
    /// <param name="startId">A hull to open on, such as one a mod adds.</param>
    public Viewer(ContentDatabase db, Camera3D camera, Action? back, string? startId = null)
    {
        _db = db;
        _camera = camera;
        _back = back;
        _startId = startId;
    }

    private readonly string? _startId;

    public override void _Ready()
    {
        var space = new Space();
        AddChild(space);
        space.Build(20261110, decor: false);
        space.FaceStar(new Vector3(0.6f, 0.5f, 0.6f), new Color(1f, 0.95f, 0.88f), 1.3f);
        _pivot = new Node3D();
        AddChild(_pivot);
        CollectEntries();
        BuildUi();
        // --viewer=ID opens on a particular hull (for screenshots).
        var wanted = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--viewer="))?["--viewer=".Length..] ?? _startId;
        var start = wanted != null ? _entries.FirstOrDefault(e => e.Id == wanted) : null;
        int category = start != null ? _categories.IndexOf(start.Category) : 0;
        _category.Select(category);
        ShowCategory(category);
        if (start != null)
        {
            int index = _shown.IndexOf(start);
            _list.Select(index);
            Show(index);
            _showScale.ButtonPressed = OS.GetCmdlineUserArgs().Contains("--scale");
        }
    }

    private void CollectEntries()
    {
        string BuilderName(string id) => _db.Builders.TryGetValue(id, out var b) ? b.Name : id;
        foreach (var hull in _db.Hulls.Values.OrderBy(h => BuilderName(h.Builder)).ThenBy(h => h.Radius))
        {
            bool warship = hull.IsCapital || hull.Id.StartsWith("fm-unbound");
            string category = hull.Class == "drone" ? "Drones and beacons" : warship ? $"Warships: {BuilderName(hull.Builder)}" : $"Ships: {BuilderName(hull.Builder)}";
            var h = hull;
            _entries.Add(new Entry(category, hull.Name, () => new MeshInstance3D { Mesh = MeshFactory.BuildShip(h, _db.Builders[h.Builder].Palette) },
                ShipInfo(hull), MathF.Max(hull.Radius, HalfLength(hull)), hull.Id));
        }
        var owner = new Color(0.3f, 0.5f, 0.85f);
        foreach (var (size, scale, label, note) in new[]
        {
            (StationSize.Outpost, 0.6f, "Outpost", "A small station for a few thousand people at the edge of settled space."),
            (StationSize.Station, 1f, "Station", "The standard station: a hub with a docking bay and a habitat ring."),
            (StationSize.Hub, 2.2f, "Hub", "Where billions live: habitat rings stacked along a long spine."),
            (StationSize.Citadel, 4f, "Citadel", "A great power's capital: a great outer ring and four towers."),
        })
        {
            var s = size; var sc = scale;
            _entries.Add(new Entry("Stations", label, () => new MeshInstance3D { Mesh = MeshFactory.BuildStation(owner, s), Scale = Vector3.One * sc },
                $"[color=#{Kit.Hex(Kit.Amber)}]{label.ToUpperInvariant()}[/color]\n{note}", 240 * sc));
        }
        // Every station design, as a full station.
        foreach (var design in Galaxus.Core.Content.StationDesigns.All.Where(d => d != "ring"))
        {
            var d = design;
            float radius = Galaxus.Core.Content.StationDesigns.Build(d, StationSize.Station, 1).Radius;
            _entries.Add(new Entry("Stations", Capital(d) + " station", () => new MeshInstance3D { Mesh = MeshFactory.BuildStation(owner, StationSize.Station, d, 1).Mesh },
                $"[color=#{Kit.Hex(Kit.Amber)}]{d.ToUpperInvariant()}[/color]\n{Galaxus.Core.Content.StationDesigns.Describe(d)}", radius * 1.2f));
        }
        _entries.Add(new Entry("Stations", "Surface port", () => new MeshInstance3D { Mesh = MeshFactory.BuildSurfacePort(owner) },
            $"[color=#{Kit.Hex(Kit.Amber)}]SURFACE PORT[/color]\nA landing pad with lights, a control tower, hangars and fuel tanks, on levelled ground.", 260));
        // Factions with stations of their own design, from mods.
        foreach (var type in _db.FactionTypes.Values.Where(t => t.StationHull != null || t.StationModel is { Count: > 0 }))
        {
            var t = type;
            var palette = t.Builder != null && _db.Builders.TryGetValue(t.Builder, out var builder) ? builder.Palette
                : new PaletteDef { Hull = "#8d939c", Accent = "#4a78d0", Trim = "#3c4048", Glow = "#ffe6a0" };
            HullDef? hull = t.StationHull != null && _db.Hulls.TryGetValue(t.StationHull, out var h) ? h
                : t.StationModel is { Count: > 0 } model ? new HullDef { Id = t.Id + "-station", Model = model, Radius = t.StationRadius } : null;
            if (hull == null) continue;
            var hullPalette = _db.Builders.TryGetValue(hull.Builder, out var hb) ? hb.Palette : palette;
            _entries.Add(new Entry("Stations", $"{t.Name} station", () => new MeshInstance3D { Mesh = MeshFactory.BuildShip(hull, hullPalette) },
                $"[color=#{Kit.Hex(Kit.Amber)}]{t.Name.ToUpperInvariant()} STATION[/color]\n{t.Description}", MathF.Max(hull.Radius, HalfLength(hull))));
        }
        foreach (var e in _entries) if (!_categories.Contains(e.Category)) _categories.Add(e.Category);
        // Ships first, then warships, then everything else.
        _categories.Sort((a, b) => Rank(a).CompareTo(Rank(b)) != 0 ? Rank(a).CompareTo(Rank(b)) : string.CompareOrdinal(a, b));
        static int Rank(string c) => c.StartsWith("Ships") ? 0 : c.StartsWith("Warships") ? 1 : c == "Stations" ? 2 : 3;
    }

    private static float HalfLength(HullDef hull) =>
        hull.Model.Count == 0 ? hull.Radius : hull.Model.Max(m => MathF.Abs(m.Position[2]) + m.Length / 2);

    private string ShipInfo(HullDef hull)
    {
        var spec = Galaxus.Core.Ships.ShipFactory.Build(_db, hull.Id);
        int guns = spec.Weapons.Count(w => !w.Turret), turrets = spec.Weapons.Count(w => w.Turret);
        string builder = _db.Builders.TryGetValue(hull.Builder, out var b) ? b.Name : hull.Builder;
        var lines = new List<string>
        {
            $"[color=#{Kit.Hex(Kit.Amber)}]{hull.Name.ToUpperInvariant()}[/color]  {Capital(hull.Class)}, {builder}",
            hull.Description,
            "",
            $"Length {HalfLength(hull) * 2:N0} m.  Mass {hull.Mass:N0} t.",
            $"Top speed {spec.MaxSpeed:0} m/s.  Hull {spec.MaxHull:N0}.  Shields {spec.MaxShields:N0}.",
            $"{(guns > 0 ? $"{guns} forward {(guns == 1 ? "gun" : "guns")}" : "No forward guns")}{(turrets > 0 ? $", {turrets} turrets" : "")}{(spec.Missiles > 0 ? $", {spec.Missiles} missiles" : "")}.",
        };
        if (spec.Weapons.Count > 0)
            lines.Add("Weapons: " + string.Join(", ", spec.Weapons.GroupBy(w => $"{w.Name} ({w.Kind})").Select(g => g.Count() > 1 ? $"{g.Count()} x {g.Key}" : g.Key)) + ".");
        if (hull.CargoTonnes > 0) lines.Add(spec.JumpRangeLy > 0 ? $"Hold {hull.CargoTonnes:N0} t.  Jump range {spec.JumpRangeLy:0} ly." : $"Hold {hull.CargoTonnes:N0} t.");
        if (hull.Hangar > 0) lines.Add($"Flight deck: {hull.Hangar} {(_db.Hulls.TryGetValue(hull.HangarHull ?? "", out var f) ? f.Name : "fighters")}.");
        if (hull.Dockable) lines.Add("Big enough to dock with, like a station.");
        lines.Add(hull.ForSale ? $"For sale: {hull.Price:N0} cr." : "Not for sale.");
        return string.Join("\n", lines);
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var root = new Control { Theme = Kit.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);

        _panel = new PanelContainer { Position = new Vector2(6, 6), CustomMinimumSize = new Vector2(230, 0) };
        root.AddChild(_panel);
        var box = new VBoxContainer();
        _panel.AddChild(box);
        box.AddChild(Kit.Label("GALAXUS PREVIEW", Kit.Amber));
        _category = new OptionButton();
        foreach (var c in _categories) _category.AddItem(c);
        _category.ItemSelected += i => ShowCategory((int)i);
        box.AddChild(_category);
        _list = new ItemList { CustomMinimumSize = new Vector2(220, 150), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _list.ItemSelected += i => Show((int)i);
        box.AddChild(_list);
        _showScale = new CheckBox { Text = "Show a fighter for scale" };
        _showScale.Toggled += _ => Show(_list.GetSelectedItems().FirstOrDefault());
        box.AddChild(_showScale);
        box.AddChild(Kit.Label("Drag to turn, scroll to zoom. Up and down arrows for the next model.", Kit.Dim, wrap: true));
        box.AddChild(_back != null ? Kit.Button("Back to the menu", () => _back()) : Kit.Button("Quit", () => GetTree().Quit()));

        var infoPanel = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
        infoPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        infoPanel.Position = new Vector2(-306, 6);
        infoPanel.GrowHorizontal = Control.GrowDirection.Begin;
        root.AddChild(infoPanel);
        _info = Kit.Text();
        _info.FitContent = true;
        _info.CustomMinimumSize = new Vector2(290, 0);
        infoPanel.AddChild(_info);
        infoPanel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        infoPanel.OffsetLeft = -306;
        infoPanel.OffsetRight = -6;
        infoPanel.OffsetTop = 6;
    }

    private void ShowCategory(int index)
    {
        _shown = _entries.Where(e => e.Category == _categories[index]).ToList();
        _list.Clear();
        foreach (var e in _shown) _list.AddItem(e.Name);
        if (_shown.Count > 0)
        {
            _list.Select(0);
            Show(0);
        }
    }

    private void Show(int index)
    {
        if (index < 0 || index >= _shown.Count) return;
        var entry = _shown[index];
        _model?.QueueFree();
        _scaleModel?.QueueFree();
        _scaleModel = null;
        _model = entry.Build();
        _pivot.AddChild(_model);
        _radius = entry.Radius;
        _zoom = 1;
        _info.Text = entry.Info;

        // A fighter alongside, so a warship's size means something.
        if (_showScale.ButtonPressed && _db.Hulls.TryGetValue("mc-sentinel", out var fighter))
        {
            _scaleModel = new MeshInstance3D
            {
                Mesh = MeshFactory.BuildShip(fighter, _db.Builders[fighter.Builder].Palette),
                Position = new Vector3(_radius * 1.15f, _radius * 0.2f, 0),
            };
            _pivot.AddChild(_scaleModel);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion motion when (motion.ButtonMask & MouseButtonMask.Left) != 0 && motion.Position.X > _panel.Size.X + 10:
                _yaw -= motion.Relative.X * 0.008f;
                _pitch = Mathf.Clamp(_pitch + motion.Relative.Y * 0.006f, -1.4f, 1.4f);
                _idle = 0;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _zoom = Mathf.Clamp(_zoom * 0.88f, 0.2f, 4f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _zoom = Mathf.Clamp(_zoom * 1.14f, 0.2f, 4f);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Down or Key.Up } key:
                int current = _list.GetSelectedItems().FirstOrDefault();
                int next = Mathf.Clamp(current + (key.Keycode == Key.Down ? 1 : -1), 0, _shown.Count - 1);
                _list.Select(next);
                _list.EnsureCurrentIsVisible();
                Show(next);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                if (_back != null) _back(); else GetTree().Quit();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (Input.MouseMode != Input.MouseModeEnum.Visible) Input.MouseMode = Input.MouseModeEnum.Visible;
        // Turns slowly on its own until you take hold of it.
        _idle += (float)delta;
        if (_idle > 3) _yaw += (float)delta * 0.15f;
        float frame = _scaleModel != null ? 3.4f : 2.8f;
        _distance = Mathf.Lerp(_distance, _radius * frame * _zoom + 6, (float)delta * 6);
        var offset = new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _distance;
        var centre = Vector3.Zero;
        _camera.GlobalPosition = centre + offset;
        _camera.LookAt(centre, Vector3.Up);
        // Shift the picture right so the model sits in the open space beside the list, not behind it.
        _camera.HOffset = -_distance * 0.2f;
        _camera.VOffset = _distance * 0.07f;
        _camera.Far = MathF.Max(150000, _distance * 10);
        _camera.Near = MathF.Max(0.3f, _distance * 0.002f);
    }
}
