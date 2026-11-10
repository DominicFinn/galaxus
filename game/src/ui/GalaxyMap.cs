using Godot;
using Galaxus.Core;
using Galaxus.Core.Galaxy;
using NVector2 = System.Numerics.Vector2;

namespace Galaxus.Ui;

/// <summary>
/// The galaxy map: every system coloured by owner, the gate network, your position and your
/// jobs' destinations. Click a system to see it and plot a route; jump to travel there.
/// Scroll to zoom, right-drag to pan.
/// </summary>
public partial class GalaxyMap : Control
{
    private Session _session = null!;
    private Action<int> _jump = null!;
    private Action _close = null!;

    private float _zoom = 1.2f;            // pixels per light year
    private NVector2 _centre;              // galaxy point at the middle of the screen
    private int _hovered = -1, _selected = -1;
    private List<int> _route = [];
    private bool _dragging;

    private RichTextLabel _info = null!;
    private Button _jumpButton = null!;

    public void Init(Session session, Action<int> jump, Action close)
    {
        _session = session;
        _jump = jump;
        _close = close;
    }

    public override void _Ready()
    {
        Theme = Ui.Theme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var panel = new PanelContainer { Position = new Vector2(6, 6), CustomMinimumSize = new Vector2(190, 0) };
        AddChild(panel);
        var box = new VBoxContainer();
        panel.AddChild(box);
        _info = Ui.Text();
        _info.FitContent = true;
        _info.ScrollActive = false;
        _info.CustomMinimumSize = new Vector2(180, 0);
        box.AddChild(_info);
        _jumpButton = Ui.Button("Jump", () => { if (_selected >= 0) _jump(_selected); });
        box.AddChild(_jumpButton);
        box.AddChild(Ui.Button("Close map (M)", () => _close()));
    }

    /// <summary>Opens centred on the player with nothing selected.</summary>
    public void Open()
    {
        _centre = _session.Here.Position;
        _zoom = 1.2f;
        _selected = -1;
        _route = [];
        UpdateInfo();
        QueueRedraw();
    }

    private Vector2 ToScreen(NVector2 p) => Size / 2 + new Vector2(p.X - _centre.X, p.Y - _centre.Y) * _zoom;
    private NVector2 ToGalaxy(Vector2 s) => _centre + new NVector2((s.X - Size.X / 2) / _zoom, (s.Y - Size.Y / 2) / _zoom);

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
            {
                var before = ToGalaxy(wheel.Position);
                _zoom = Mathf.Clamp(_zoom * (wheel.ButtonIndex == MouseButton.WheelUp ? 1.25f : 0.8f), 0.12f, 8f);
                var after = ToGalaxy(wheel.Position);
                _centre += before - after; // keep the point under the cursor fixed
                QueueRedraw();
                break;
            }
            case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } drag:
                _dragging = drag.Pressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                _selected = Nearest(click.Position);
                _route = _selected >= 0 ? _session.Galaxy.Route(_session.Career.SystemId, _selected) : [];
                UpdateInfo();
                QueueRedraw();
                break;
            case InputEventMouseMotion motion:
                if (_dragging) { _centre -= new NVector2(motion.Relative.X, motion.Relative.Y) / _zoom; QueueRedraw(); }
                int hovered = Nearest(motion.Position);
                if (hovered != _hovered) { _hovered = hovered; UpdateInfo(); QueueRedraw(); }
                break;
        }
    }

    private int Nearest(Vector2 screen)
    {
        int best = -1;
        float bestDistance = 8f;
        foreach (var s in _session.Galaxy.Systems)
        {
            float d = ToScreen(s.Position).DistanceTo(screen);
            if (d < bestDistance) { bestDistance = d; best = s.Id; }
        }
        return best;
    }

    private void UpdateInfo()
    {
        var galaxy = _session.Galaxy;
        int id = _hovered >= 0 ? _hovered : _selected;
        if (id < 0)
        {
            _info.Text = $"[color=#{Ui.Hex(Ui.Amber)}]GALAXY MAP[/color]\nDay {galaxy.Day}. You are at {_session.Here.Name}.\n" +
                         "Click a system to plot a route. Scroll to zoom, right-drag to pan.\n\n" + Legend();
            _jumpButton.Disabled = true;
            _jumpButton.Text = "Jump";
            return;
        }

        var s = galaxy.Systems[id];
        var owner = galaxy.OwnerOf(s);
        var events = galaxy.EventsIn(id).Select(e => _session.Db.Events.TryGetValue(e.EventId, out var d) ? d.Name : e.EventId);
        var route = id == _selected ? _route : galaxy.Route(_session.Career.SystemId, id);
        int days = galaxy.RouteDays(route);
        var jobs = _session.Career.Missions.Where(m => m.Offer.DestinationSystemId == id).ToList();

        _info.Text =
            $"[color=#{Ui.Hex(Ui.Amber)}]{s.Name.ToUpperInvariant()}[/color]\n" +
            $"{Capitalise(s.Region)}, {s.Arm} arm. {s.State}{(s.Economy != Economy.None ? $", {s.Economy}" : "")}.\n" +
            (owner != null ? $"Held by [color=#{owner.Colour.TrimStart('#')}]{owner.Name}[/color].\n" : "Unclaimed.\n") +
            (s.Stations.Count > 0 ? $"Stations: {string.Join(", ", s.Stations)}\n" : "No stations.\n") +
            (events.Any() ? $"[color=#{Ui.Hex(Ui.Red)}]{string.Join(", ", events)}[/color]\n" : "") +
            (jobs.Count > 0 ? $"[color=#{Ui.Hex(Ui.Amber)}]{jobs.Count} of your jobs end here.[/color]\n" : "") +
            (id == _session.Career.SystemId ? "You are here." : $"{route.Count - 1} jumps, {days} days.");

        bool canJump = _selected >= 0 && _selected != _session.Career.SystemId && _route.Count > 1;
        _jumpButton.Disabled = !canJump;
        _jumpButton.Text = canJump ? $"Jump to {galaxy.Systems[_selected].Name} ({galaxy.RouteDays(_route)} days)" : "Jump";
    }

    private string Legend() => string.Join("\n", _session.Galaxy.Factions.Where(f => f.Tier == "great")
        .Select(f => $"[color=#{f.Colour.TrimStart('#')}]{f.Name}[/color]"));

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public override void _Draw()
    {
        var galaxy = _session.Galaxy;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.015f, 0.03f, 0.97f));

        // Region rings, faint, so the core-to-rim layout reads at a glance.
        var origin = ToScreen(NVector2.Zero);
        foreach (var r in new[] { 180f, 450f, 800f })
            DrawArc(origin, r * _zoom, 0, Mathf.Tau, 96, new Color(0.3f, 0.4f, 0.5f, 0.15f), 1);

        var gateColour = new Color(0.4f, 0.55f, 0.7f, _zoom > 0.6f ? 0.18f : 0.08f);
        foreach (var s in galaxy.Systems)
        foreach (var g in s.Gates)
            if (g > s.Id) DrawLine(ToScreen(s.Position), ToScreen(galaxy.Systems[g].Position), gateColour);

        for (int i = 0; i + 1 < _route.Count; i++)
            DrawLine(ToScreen(galaxy.Systems[_route[i]].Position), ToScreen(galaxy.Systems[_route[i + 1]].Position), Ui.Green, 2);

        float dot = Mathf.Clamp(_zoom * 2f, 1.5f, 4f);
        foreach (var s in galaxy.Systems)
        {
            var p = ToScreen(s.Position);
            if (p.X < -10 || p.Y < -10 || p.X > Size.X + 10 || p.Y > Size.Y + 10) continue;
            Color c = s.Owner >= 0 ? Color.FromHtml(galaxy.Factions[s.Owner].Colour) : new Color(0.5f, 0.5f, 0.55f);
            if (s.Owner < 0) c.A = s.State == SystemState.Surveyed ? 0.5f : 0.25f;
            else if (!s.Settled) c.A = 0.6f;
            DrawRect(new Rect2(p - Vector2.One * dot / 2, Vector2.One * dot), c);
            if (galaxy.EventsIn(s.Id).Any() && _zoom > 0.5f) DrawArc(p, dot + 2, 0, Mathf.Tau, 12, new Color(Ui.Red, 0.6f), 1);
        }

        foreach (var job in _session.Career.Missions)
            DrawArc(ToScreen(galaxy.Systems[job.Offer.DestinationSystemId].Position), dot + 4, 0, Mathf.Tau, 16, Ui.Amber, 1);

        DrawArc(ToScreen(_session.Here.Position), dot + 5, 0, Mathf.Tau, 20, Colors.White, 1.5f);
        if (_selected >= 0) DrawArc(ToScreen(galaxy.Systems[_selected].Position), dot + 7, 0, Mathf.Tau, 20, Ui.Green, 1.5f);

        if (_hovered >= 0 && _zoom < 3f)
        {
            var p = ToScreen(galaxy.Systems[_hovered].Position);
            DrawString(ThemeDB.FallbackFont, p + new Vector2(6, -4), galaxy.Systems[_hovered].Name, HorizontalAlignment.Left, -1, 8, Colors.White);
        }
        else if (_zoom >= 3f)
        {
            foreach (var s in galaxy.Systems)
            {
                var p = ToScreen(s.Position);
                if (p.X > 0 && p.Y > 0 && p.X < Size.X && p.Y < Size.Y)
                    DrawString(ThemeDB.FallbackFont, p + new Vector2(5, -3), s.Name, HorizontalAlignment.Left, -1, 8, new Color(1, 1, 1, 0.6f));
            }
        }
    }
}
