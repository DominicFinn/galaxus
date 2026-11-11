using Godot;
using Galaxus.Core;
using Galaxus.Core.Galaxy;

namespace Galaxus.Ui;

/// <summary>
/// The system map: the star, every planet on its orbit where it is today, moons around their
/// planets, the asteroid belt and stations. Scroll to zoom, right-drag to pan, click a body for
/// its details and to set a course. Real distances are huge, so moons are drawn on rings around
/// their planet rather than to scale.
/// </summary>
public partial class SystemMap : Control
{
    private Session _session = null!;
    private Action<int> _setCourse = null!;
    private Action _close = null!;
    private Func<(double X, double Y, double Z)> _playerKm = null!;
    private Func<int> _navTarget = null!;

    private StarSystem _system = null!;
    private SolarSystem _solar = null!;
    private bool _current;
    private float _pxPerAu = 100;
    private Vector2 _pan;
    private int _hovered = -1, _selected = -1;
    private bool _dragging;
    private RichTextLabel _info = null!;
    private Button _course = null!;

    public void Init(Session session, Action<int> setCourse, Action close, Func<(double, double, double)> playerKm, Func<int> navTarget)
    {
        _session = session;
        _setCourse = setCourse;
        _close = close;
        _playerKm = playerKm;
        _navTarget = navTarget;
    }

    public override void _Ready()
    {
        Theme = Ui.Theme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var panel = new PanelContainer { Position = new Vector2(6, 6), CustomMinimumSize = new Vector2(200, 0) };
        AddChild(panel);
        var box = new VBoxContainer();
        panel.AddChild(box);
        _info = Ui.Text();
        _info.FitContent = true;
        _info.ScrollActive = false;
        _info.CustomMinimumSize = new Vector2(190, 0);
        box.AddChild(_info);
        _course = Ui.Button("Set as destination", () => { if (_selected != -1) _setCourse(_selected); _close(); });
        box.AddChild(_course);
        box.AddChild(Ui.Button("Close (N)", () => _close()));
    }

    /// <summary>Opens on a system. Courses can only be set in the system you're in.</summary>
    public void Open(StarSystem system, bool current)
    {
        _system = system;
        _current = current;
        _solar = SolarSystems.For(system, _session.Db.Grammars);
        float outer = (float)_solar.Planets.Max(b => b.Orbit + (b.Kind == BodyKind.Belt ? b.Radius : 0));
        _pxPerAu = Mathf.Min(Size.X, Size.Y) * 0.44f / Mathf.Max(outer, 0.1f);
        _pan = new Vector2(60, 0);
        _selected = -1;
        // In the active job's system, start with its station selected, ready to set a course.
        if (current && _session.Career.ActiveJob is { } job && job.Offer.DestinationSystemId == system.Id)
        {
            int index = _solar.Stations.FindIndex(st => st.Name == job.Offer.DestinationStation);
            if (index >= 0) _selected = _solar.Stations[index].Surface ? NavPortBase - index : -2;
            else if (_solar.Stations.Count > 0) _selected = -2;
        }
        UpdateInfo();
        QueueRedraw();
    }

    private Vector2 Centre => Size / 2 + _pan;

    private Vector2 PlanetScreen(int index)
    {
        var (x, _, z) = _solar.PositionKm(index, _session.Galaxy.Day);
        return Centre + new Vector2((float)(x / SolarSystem.KmPerAu), (float)(z / SolarSystem.KmPerAu)) * _pxPerAu;
    }

    /// <summary>Screen position of any body: moons sit on rings around their planet so they're visible.</summary>
    private Vector2 BodyScreen(int index)
    {
        var body = _solar.Bodies[index];
        if (!body.IsMoon) return PlanetScreen(index);
        int nth = _solar.Bodies.Take(index).Count(b => b.Parent == body.Parent);
        float ring = 9 + nth * 5;
        float angle = body.AngleOn(_session.Galaxy.Day);
        return PlanetScreen(body.Parent) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ring;
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
            {
                float factor = wheel.ButtonIndex == MouseButton.WheelUp ? 1.3f : 1 / 1.3f;
                var before = (wheel.Position - Centre) / _pxPerAu;
                _pxPerAu = Mathf.Clamp(_pxPerAu * factor, 2f, 200000f);
                _pan += wheel.Position - (Centre + before * _pxPerAu);
                QueueRedraw();
                break;
            }
            case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } drag:
                _dragging = drag.Pressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                _selected = Nearest(click.Position);
                UpdateInfo();
                QueueRedraw();
                break;
            case InputEventMouseMotion motion:
                if (_dragging) { _pan += motion.Relative; QueueRedraw(); }
                int hovered = Nearest(motion.Position);
                if (hovered != _hovered) { _hovered = hovered; UpdateInfo(); QueueRedraw(); }
                break;
        }
    }

    /// <summary>Nearest body to the cursor; -2 means the station.</summary>
    private int Nearest(Vector2 at)
    {
        int best = -1;
        float bestDistance = 9;
        for (int i = 0; i < _solar.Bodies.Count; i++)
        {
            if (_solar.Bodies[i].Kind == BodyKind.Belt) continue;
            float d = BodyScreen(i).DistanceTo(at);
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        // The belt: anywhere on its ring.
        if (best == -1)
            for (int i = 0; i < _solar.Bodies.Count; i++)
                if (_solar.Bodies[i].Kind == BodyKind.Belt && Mathf.Abs(Centre.DistanceTo(at) - _solar.Bodies[i].Orbit * _pxPerAu) < 5) return i;
        // The star, for skimming with a fuel scoop.
        if (best == -1 && Centre.DistanceTo(at) < 9) return NavStar;
        if (!_current) return best;
        for (int i = 0; i < _solar.Stations.Count; i++)
        {
            var site = _solar.Stations[i];
            if (site.Surface && PortScreen(site.Body).DistanceTo(at) < 6) return NavPortBase - i;
            if (!site.Surface && StationScreen(site.Body).DistanceTo(at) < 6) return -2;
        }
        return best;
    }

    private Vector2 StationScreen(int body) => BodyScreen(body) + new Vector2(7, -7);
    private Vector2 PortScreen(int body) => BodyScreen(body) + new Vector2(-7, 7);
    /// <summary>Surface ports are selected as this minus their station index, matching the flight code's course targets.</summary>
    private const int NavPortBase = -10;
    private const int NavStar = -5;

    private void UpdateInfo()
    {
        int id = _hovered != -1 ? _hovered : _selected;
        var star = _solar.Star;
        if (id == -1)
        {
            var profile = SystemProfiles.For(_session.Galaxy, _system, _session.Db);
            _info.Text = $"[color=#{Ui.Hex(Ui.Amber)}]{_system.Name.ToUpperInvariant()}[/color]\n" +
                         (profile.CapitalOf != null ? $"Capital of {profile.CapitalOf}. " : "") +
                         (_system.LandmarkNote is { Length: > 0 } note ? $"{note}\n" : "") +
                         (profile.Population > 0 ? $"Population {profile.PopulationText}. {profile.Government}. {profile.Security} security.\n" : "") +
                         $"{star.Description}, {star.Temperature:N0} K, {star.Mass:0.00} solar masses, {star.Luminosity:0.###} times the Sun's light.\n" +
                         $"Habitable zone {star.HabitableInner:0.00} to {star.HabitableOuter:0.00} AU. Frost line {star.FrostLine:0.0} AU.\n" +
                         $"{_solar.Planets.Count(b => b.Kind != BodyKind.Belt)} planets, {_solar.Bodies.Count(b => b.IsMoon)} moons.\n\n" +
                         "Click a body for details" + (_current ? " and to make it your destination for the autopilot (J)." : ".") +
                         (_session.Career.ActiveJob is { } job ? $"\n\n[color=#{Ui.Hex(Ui.Amber)}]Job: {job.Offer.Mission.Title}[/color]\n{_session.NextStep(job)}" : "");
            _course.Disabled = true;
            return;
        }
        if (id <= NavPortBase)
        {
            var port = _solar.Stations[NavPortBase - id];
            var world = _solar.Bodies[port.Body];
            double lat = Math.Asin(Math.Clamp(port.Site.Y, -1, 1)) * 180 / Math.PI;
            double lon = Math.Atan2(port.Site.Z, port.Site.X) * 180 / Math.PI;
            _info.Text = $"[color=#{Ui.Hex(Ui.Amber)}]{port.Name.ToUpperInvariant()}[/color]\nSurface port on {world.Name}, " +
                         $"{Math.Abs(lat):0}°{(lat >= 0 ? "N" : "S")} {Math.Abs(lon):0}°{(lon >= 0 ? "E" : "W")}.\n" +
                         "Set it as your destination and use the autopilot (J). It drops you out above the port: fly down and land on the pad.\n" + TradeLines();
            _course.Disabled = !_current || _selected != id;
            return;
        }
        if (id == NavStar)
        {
            _info.Text = $"[color=#{Ui.Hex(Ui.Amber)}]{_system.Name.ToUpperInvariant()}, THE STAR[/color]\n{star.Description}, {star.Temperature:N0} K.\n" +
                         (_session.Career.ShipFuelScoop(_session.Db) ? "Your fuel scoop fills the tank while you skim close. The autopilot takes you to a safe distance." : "With a fuel scoop fitted, you can fill your tank skimming close to a star. Outfitters sell them.");
            _course.Disabled = !_current || _selected != id;
            return;
        }
        if (id == -2)
        {
            var site = _solar.Stations[0];
            _info.Text = $"[color=#{Ui.Hex(Ui.Amber)}]{site.Name.ToUpperInvariant()}[/color]\nStation in orbit of {_solar.Bodies[site.Body].Name}.\n" + TradeLines(shipyard: true);
            _course.Disabled = !_current || _selected != -2;
            return;
        }
        var b = _solar.Bodies[id];
        if (b.Kind == BodyKind.Belt)
        {
            var ore = _session.Db.Commodities[Galaxus.Core.Mining.Asteroids.MainOre(b)].Name.ToLowerInvariant();
            _info.Text = $"[color=#{Ui.Hex(Ui.Amber)}]{b.Name.ToUpperInvariant()}[/color]\nAn asteroid belt {b.Orbit - b.Radius:0.00} to {b.Orbit + b.Radius:0.00} AU from the star, mostly {ore}.\n" +
                         "Set it as your destination and the autopilot (J) takes you into the nearest part of it. Target rocks with T, scan them with K, and cut them with a mining laser.";
            _course.Disabled = !_current || _selected != id;
            return;
        }
        bool surveyed = _session.Career.Scanned.Contains($"{_system.Id}:body:{id}");
        string kind = b.Kind switch
        {
            BodyKind.GasGiant => "Gas giant",
            BodyKind.IceGiant => "Ice giant",
            BodyKind.Molten => "Molten world",
            BodyKind.Barren => "Barren rock",
            BodyKind.Terran => "Earth-like world",
            BodyKind.Ocean => "Ocean world",
            BodyKind.Ice => "Ice world",
            BodyKind.Toxic => "Toxic atmosphere",
            _ => "Desert world",
        };
        string orbit = b.IsMoon ? $"Moon of {_solar.Bodies[b.Parent].Name}, {b.Orbit * 1000:N0} km out" : $"{b.Orbit:0.00} AU from the star";
        var stations = _solar.Stations.Where(s => s.Body == id && !s.Surface).Select(s => s.Name).ToList();
        var ports = _solar.Stations.Where(s => s.Body == id && s.Surface).Select(s => s.Name).ToList();
        _info.Text = $"[color=#{Ui.Hex(Ui.Amber)}]{b.Name.ToUpperInvariant()}[/color]\n{kind}{(b.Habitable ? ", habitable" : "")}.\n" +
                     $"{orbit}. Year {b.PeriodDays:N0} days.\n" +
                     $"Radius {b.Radius * SolarSystem.KmPerEarthRadius:N0} km. {b.Temperature - 273:N0} °C.\n" +
                     (b.IsMoon ? "" : $"{_solar.MoonsOf(id).Count()} moons.\n") +
                     (stations.Count > 0 ? $"Stations in orbit: {string.Join(", ", stations)}\n" : "") +
                     (ports.Count > 0 ? $"[color=#{Ui.Hex(Ui.Amber)}]Surface ports: {string.Join(", ", ports)}[/color]\n" : "") +
                     (surveyed ? "Surveyed." : "Not surveyed: fly close and scan it (K) for survey data.");
        _course.Disabled = !_current || _selected != id;
    }

    /// <summary>
    /// The market (and shipyard) you'd find docking here: live if you're in the system, as you last
    /// saw it otherwise. Ports and stations in a system share its market.
    /// </summary>
    private string TradeLines(bool shipyard = false)
    {
        var db = _session.Db;
        Dictionary<string, (int Buy, int Sell, int Stock, float Trend)> prices;
        string when;
        if (_current)
        {
            prices = Market.Quotes(_session, _system).ToDictionary(q => q.Commodity.Id, q => (q.Buy, q.Sell, q.Stock, q.Trend));
            when = "today";
        }
        else if (_session.Career.SeenMarkets.TryGetValue(_system.Id, out var seen))
        {
            prices = seen.Prices.ToDictionary(p => p.Key, p => (p.Value.Buy, p.Value.Sell, p.Value.Stock, p.Value.Trend));
            when = $"{_session.Galaxy.Day - seen.Day} days ago";
        }
        else return "\nYou haven't seen this market.";
        float Position(string id, int price) { var (low, high) = Market.Range(db.Commodities[id]); return (price - low) / (float)Math.Max(1, high - low); }
        string Trend(float t) => Math.Abs(t) < 0.03f ? "" : t > 0 ? " up" : " down";
        var cheap = prices.Where(p => p.Value.Stock > 0).OrderBy(p => Position(p.Key, p.Value.Buy)).Take(3);
        var dear = prices.OrderByDescending(p => Position(p.Key, p.Value.Sell)).Take(3);
        var shortages = Market.Shortages(db, _session.Galaxy, _session.Galaxy.Day).Where(x => x.SystemId == _system.Id).ToList();
        string text = $"\n[color=#{Ui.Hex(Ui.Amber)}]Market[/color] ({when})\n" +
                      "Cheap: " + string.Join(", ", cheap.Select(p => $"{db.Commodities[p.Key].Name} {p.Value.Buy:N0}{Trend(p.Value.Trend)}")) + "\n" +
                      "Wanted: " + string.Join(", ", dear.Select(p => $"{db.Commodities[p.Key].Name} {p.Value.Sell:N0}{Trend(p.Value.Trend)}")) +
                      string.Concat(shortages.Select(x => $"\n[color=#{Ui.Hex(Ui.Red)}]Short of {db.Commodities[x.CommodityId].Name.ToLowerInvariant()}.[/color]"));
        if (shipyard && _current)
        {
            var offers = Shipyards.Offers(_session, _system);
            text += offers.Count == 0 ? "\n\nShipyard: nothing on sale this week."
                : $"\n\n[color=#{Ui.Hex(Ui.Amber)}]Shipyard[/color] ({offers.Count} ships this week)\n" +
                  string.Join(", ", offers.GroupBy(o => o.Hull.Class).Select(g => $"{g.Count()} {g.Key}{(g.Count() > 1 ? "s" : "")}")) +
                  $", from {offers.Min(o => o.Price):N0} cr";
        }
        return text;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.015f, 0.03f, 0.97f));
        var star = _solar.Star;
        int day = _session.Galaxy.Day;

        // Habitable zone as a faint green band.
        float inner = star.HabitableInner * _pxPerAu, outer = star.HabitableOuter * _pxPerAu;
        DrawArc(Centre, (inner + outer) / 2, 0, Mathf.Tau, 128, new Color(0.3f, 0.9f, 0.4f, 0.06f), Mathf.Max(1, outer - inner));

        for (int i = 0; i < _solar.Bodies.Count; i++)
        {
            var b = _solar.Bodies[i];
            if (b.IsMoon) continue;
            float r = b.Orbit * _pxPerAu;
            if (b.Kind == BodyKind.Belt)
            {
                var rng = new Rng(b.Seed);
                for (int k = 0; k < 260; k++)
                {
                    float a = rng.NextFloat() * Mathf.Tau, rr = r + (rng.NextFloat() - 0.5f) * b.Radius * _pxPerAu * 2;
                    DrawRect(new Rect2(Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr, Vector2.One), new Color(0.6f, 0.55f, 0.5f, 0.6f));
                }
                continue;
            }
            DrawArc(Centre, r, 0, Mathf.Tau, 160, new Color(0.4f, 0.55f, 0.7f, 0.25f), 1);
        }

        var (starColour, _, _, _) = PlanetFactory.StarLook(PlanetFactory.FromStar(star.Class));
        DrawCircle(Centre, 6, starColour);
        DrawArc(Centre, 9, 0, Mathf.Tau, 24, new Color(starColour, 0.4f), 2);

        for (int i = 0; i < _solar.Bodies.Count; i++)
        {
            var b = _solar.Bodies[i];
            if (b.Kind == BodyKind.Belt) continue;
            var p = BodyScreen(i);
            if (b.IsMoon) DrawArc(PlanetScreen(b.Parent), p.DistanceTo(PlanetScreen(b.Parent)), 0, Mathf.Tau, 32, new Color(0.4f, 0.55f, 0.7f, 0.15f), 1);
            float size = b.IsMoon ? 1.5f : Mathf.Clamp(2 + b.Radius * 0.35f, 2.5f, 6f);
            DrawCircle(p, size, BodyColour(b.Kind));
            if (!b.IsMoon && _pxPerAu * b.Orbit > 20)
                DrawString(ThemeDB.FallbackFont, p + new Vector2(6, 12), b.Name, HorizontalAlignment.Left, -1, 8, new Color(1, 1, 1, 0.6f));
        }

        foreach (var site in _solar.Stations)
        {
            if (site.Surface)
            {
                // Surface ports: an amber diamond on the planet's lower left.
                var at = PortScreen(site.Body);
                DrawColoredPolygon([at + new Vector2(0, -3), at + new Vector2(3, 0), at + new Vector2(0, 3), at + new Vector2(-3, 0)], Ui.Amber);
            }
            else DrawRect(new Rect2(StationScreen(site.Body) - Vector2.One * 2, Vector2.One * 4), Ui.Green);
        }

        if (_current)
        {
            int nav = _navTarget();
            if (nav >= 0) DrawArc(BodyScreen(nav), 8, 0, Mathf.Tau, 20, Ui.Amber, 1.5f);
            if (nav <= NavPortBase) DrawArc(PortScreen(_solar.Stations[NavPortBase - nav].Body), 6, 0, Mathf.Tau, 16, Ui.Amber, 1.5f);
        }
        if (_selected >= 0) DrawArc(BodyScreen(_selected), 10, 0, Mathf.Tau, 20, Ui.Green, 1.5f);
        if (_selected <= NavPortBase) DrawArc(PortScreen(_solar.Stations[NavPortBase - _selected].Body), 7, 0, Mathf.Tau, 16, Ui.Green, 1.5f);
        if (_session.Career.ActiveJob is { } activeJob && activeJob.Offer.DestinationSystemId == _system.Id)
        {
            int index = _solar.Stations.FindIndex(st => st.Name == activeJob.Offer.DestinationStation);
            if (index < 0) index = 0;
            if (index < _solar.Stations.Count)
            {
                var site = _solar.Stations[index];
                var at = site.Surface ? PortScreen(site.Body) : StationScreen(site.Body);
                DrawArc(at, 9, 0, Mathf.Tau, 20, Ui.Amber, 2);
                DrawString(ThemeDB.FallbackFont, at + new Vector2(11, -6), $"JOB: {site.Name}", HorizontalAlignment.Left, -1, 9, Ui.Amber);
            }
        }
        if (_current) DrawShip();

        DrawString(ThemeDB.FallbackFont, new Vector2(Size.X - 280, Size.Y - 8), $"Day {day}. Scroll to zoom, right-drag to pan, C to find your ship.", HorizontalAlignment.Left, -1, 8, Ui.Dim);
    }

    private Vector2 ShipScreen()
    {
        var (x, _, z) = _playerKm();
        return Centre + new Vector2((float)(x / SolarSystem.KmPerAu), (float)(z / SolarSystem.KmPerAu)) * _pxPerAu;
    }

    /// <summary>Your ship, drawn last so nothing covers it: a pulsing ring and a label, or an arrow at the edge when it's off screen.</summary>
    private void DrawShip()
    {
        var you = ShipScreen();
        var shipColour = new Color(1f, 0.95f, 0.4f);
        var view = new Rect2(Vector2.One * 12, Size - Vector2.One * 24);
        if (!view.HasPoint(you))
        {
            var from = Size / 2;
            var dir = (you - from).Normalized();
            float t = Mathf.Min(dir.X != 0 ? (Size.X / 2 - 14) / Mathf.Abs(dir.X) : float.MaxValue, dir.Y != 0 ? (Size.Y / 2 - 14) / Mathf.Abs(dir.Y) : float.MaxValue);
            var tip = from + dir * t;
            var side = new Vector2(-dir.Y, dir.X);
            DrawColoredPolygon([tip, tip - dir * 10 + side * 5, tip - dir * 10 - side * 5], shipColour);
            DrawString(ThemeDB.FallbackFont, tip - dir * 22 + new Vector2(-10, 4), "You", HorizontalAlignment.Left, -1, 8, shipColour);
            return;
        }
        float pulse = 9 + 3 * Mathf.Sin(Time.GetTicksMsec() / 250f);
        DrawArc(you, pulse, 0, Mathf.Tau, 24, new Color(shipColour, 0.7f), 1.5f);
        DrawColoredPolygon([you + new Vector2(0, -6), you + new Vector2(5, 5), you + new Vector2(-5, 5)], Colors.Black);
        DrawColoredPolygon([you + new Vector2(0, -4.5f), you + new Vector2(3.5f, 3.8f), you + new Vector2(-3.5f, 3.8f)], shipColour);
        DrawString(ThemeDB.FallbackFont, you + new Vector2(13, -8), "You", HorizontalAlignment.Left, -1, 8, shipColour);
    }

    public override void _Process(double delta)
    {
        if (Visible && _current) QueueRedraw();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        // C centres the map on your ship.
        if (Visible && _current && e is InputEventKey { Pressed: true, Keycode: Key.C })
        {
            _pan += Size / 2 - ShipScreen();
            QueueRedraw();
            GetViewport().SetInputAsHandled();
        }
    }

    private static Color BodyColour(BodyKind kind) => kind switch
    {
        BodyKind.GasGiant => new Color(0.85f, 0.7f, 0.5f),
        BodyKind.IceGiant => new Color(0.55f, 0.8f, 0.9f),
        BodyKind.Terran => new Color(0.4f, 0.7f, 0.45f),
        BodyKind.Ocean => new Color(0.3f, 0.5f, 0.9f),
        BodyKind.Ice => new Color(0.85f, 0.9f, 0.95f),
        BodyKind.Molten => new Color(1f, 0.45f, 0.2f),
        BodyKind.Toxic => new Color(0.75f, 0.75f, 0.3f),
        BodyKind.Desert => new Color(0.8f, 0.6f, 0.4f),
        _ => new Color(0.55f, 0.53f, 0.5f),
    };
}
