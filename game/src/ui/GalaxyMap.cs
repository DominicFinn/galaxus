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
    private readonly List<(Galaxus.Core.Galaxy.DeepStar Star, Vector2 Screen)> _visibleDeep = [];
    /// <summary>Zoom (pixels per galaxy unit) at which nearby deep-space stars appear.</summary>
    private const float DeepZoom = 180f;
    private readonly Dictionary<(int, int), List<(Galaxus.Core.Galaxy.DeepStar Star, Color Colour)>> _columnCache = [];

    private List<(Galaxus.Core.Galaxy.DeepStar Star, Color Colour)> CachedColumn(ulong seed, int cx, int cy)
    {
        if (_columnCache.TryGetValue((cx, cy), out var cached)) return cached;
        if (_columnCache.Count > 6000) _columnCache.Clear();
        var list = new List<(Galaxus.Core.Galaxy.DeepStar, Color)>();
        foreach (var star in Galaxus.Core.Galaxy.DeepSpace.Column(seed, cx, cy))
        {
            var (colour, _, _, _) = PlanetFactory.StarLook(PlanetFactory.FromStar(Galaxus.Core.Galaxy.SolarSystems.StarFor(star.Seed).Class));
            // Fainter for stars far above or below the plane, for a sense of depth.
            colour.A = 0.9f - MathF.Abs(star.PositionLy.Z) / 150f;
            list.Add((star, colour));
        }
        return _columnCache[(cx, cy)] = list;
    }
    private Button _jumpButton = null!;

    private Func<string?> _jumpBlocked = () => null;
    private Action _rescue = () => { };

    /// <param name="jumpBlocked">Why you can't jump from where you are (docked, too close to a station or a world), or null.</param>
    /// <param name="rescue">Calls a rescue tug when you're stranded.</param>
    public void Init(Session session, Action<int> jump, Action close, Func<string?>? jumpBlocked = null, Action? rescue = null)
    {
        _session = session;
        _jump = jump;
        _close = close;
        if (jumpBlocked != null) _jumpBlocked = jumpBlocked;
        if (rescue != null) _rescue = rescue;
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

        // Factions, or the trade overlay with a goods picker.
        var modes = new HBoxContainer();
        box.AddChild(modes);
        _modePicker = new OptionButton { FocusMode = FocusModeEnum.None };
        _modePicker.AddItem("Factions");
        _modePicker.AddItem("Trade");
        _modePicker.AddItem("Gates");
        _modePicker.ItemSelected += i =>
        {
            _tradeMode = i == 1;
            _gatesMode = i == 2;
            _goodsPicker.Visible = _tradeMode;
            if (_gatesMode) _jumpsFromHere = JumpsFrom(_session.Career.SystemId);
            RefreshTrade();
        };
        modes.AddChild(_modePicker);
        _goodsPicker = new OptionButton { FocusMode = FocusModeEnum.None, Visible = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _goodsPicker.AddItem("Best runs");
        foreach (var c in _session.Db.Commodities.Values.OrderBy(c => c.Name, StringComparer.Ordinal)) _goodsPicker.AddItem(c.Name);
        _goodsPicker.ItemSelected += i =>
        {
            _tradeGoods = i == 0 ? null : _session.Db.Commodities.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ElementAt((int)i - 1).Id;
            RefreshTrade();
        };
        modes.AddChild(_goodsPicker);
        // For screenshots: --map-mode=trade opens the trade overlay.
        if (OS.GetCmdlineUserArgs().Contains("--map-mode=trade")) { _modePicker.Select(1); _tradeMode = true; _goodsPicker.Visible = true; }
        if (OS.GetCmdlineUserArgs().Contains("--map-mode=gates")) { _modePicker.Select(2); _gatesMode = true; }

        _jumpButton = Ui.Button("Jump", () =>
        {
            if (_session.Stranded) { _rescue(); return; }
            if (_selected >= 0) _jump(_selected);
        });
        box.AddChild(_jumpButton);
        _jumpReason = Ui.Label("", Ui.Amber, wrap: true);
        _jumpReason.CustomMinimumSize = new Vector2(180, 0);
        box.AddChild(_jumpReason);
        box.AddChild(Ui.Button("Close map (M)", () => _close()));
        _jobs = new VBoxContainer();
        box.AddChild(_jobs);

        // The trade panel: the market there against the market here, for whichever system you point at.
        _tradePanel = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(230, 0) };
        _tradePanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.KeepSize, 6);
        _tradePanel.GrowHorizontal = GrowDirection.Begin;
        AddChild(_tradePanel);
        _tradeText = Ui.Text();
        _tradeText.FitContent = true;
        _tradeText.ScrollActive = false;
        _tradeText.CustomMinimumSize = new Vector2(220, 0);
        _tradePanel.AddChild(_tradeText);
    }

    private Label _jumpReason = null!;
    private PanelContainer _tradePanel = null!;
    private RichTextLabel _tradeText = null!;
    private bool _gatesMode;
    private Dictionary<int, int> _jumpsFromHere = [];

    /// <summary>Gate jumps from a system to every other it can reach.</summary>
    private Dictionary<int, int> JumpsFrom(int start)
    {
        var jumps = new Dictionary<int, int> { [start] = 0 };
        var queue = new Queue<int>([start]);
        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            foreach (var n in _session.Galaxy.Systems[node].Gates)
                if (jumps.TryAdd(n, jumps[node] + 1)) queue.Enqueue(n);
        }
        return jumps;
    }

    private VBoxContainer _jobs = null!;

    // Trade overlay --------------------------------------------------------------------------

    private OptionButton _modePicker = null!, _goodsPicker = null!;
    private bool _tradeMode;
    private string? _tradeGoods;
    private List<TradeRun> _runs = [];
    private List<TradeDeal> _rumours = [];
    private Dictionary<int, (SeenPrice Price, int Age)> _known = [];

    /// <summary>Works out the overlay from what you know: prices seen, runs from here, word of jackpots.</summary>
    private void RefreshTrade()
    {
        _runs = _tradeMode ? TradeIntel.Routes(_session, _tradeGoods) : [];
        _rumours = _tradeMode ? TradeIntel.Rumours(_session) : [];
        _known = _tradeMode && _tradeGoods != null
            ? TradeIntel.Known(_session, _tradeGoods).ToDictionary(k => k.System.Id, k => (k.Price, k.AgeDays))
            : [];
        UpdateInfo();
        QueueRedraw();
    }

    private string GoodsName(string id) => _session.Db.Commodities[id].Name;

    private static string TrendText(float trend) =>
        Math.Abs(trend) < 0.03f ? "steady" : $"{(trend > 0 ? "up" : "down")} {Math.Abs(trend) * 100:0}%";

    /// <summary>The trade overlay's own panel text when nothing is picked: the best runs from here and any rumours.</summary>
    private string TradeSummary()
    {
        var galaxy = _session.Galaxy;
        var lines = new List<string>
        {
            $"[color=#{Ui.Hex(Ui.Amber)}]TRADE MAP[/color]",
            _tradeGoods == null
                ? "Lines show the best runs from here to markets you've seen. Pick goods to colour the map by price."
                : $"{GoodsName(_tradeGoods)}: blue is cheap, amber is dear, at markets you've seen. Arrows show which way the price was moving.",
        };
        if (_runs.Count == 0) lines.Add("\nNo runs from here yet. Visit more markets to learn their prices.");
        else
        {
            lines.Add("\nBest runs from here:");
            foreach (var r in _runs)
                lines.Add($"{GoodsName(r.CommodityId)} to {galaxy.Systems[r.To].Name}: {r.Tonnes} t, about {r.Profit:N0} cr in {r.Days} days" + (r.AgeDays > 7 ? $" ({r.AgeDays} days old)" : ""));
        }
        if (_rumours.Count > 0)
        {
            lines.Add($"\n[color=#{Ui.Hex(Ui.Amber)}]Word on the docks:[/color]");
            foreach (var d in _rumours)
                lines.Add($"{galaxy.Systems[d.Source].Name} has {GoodsName(d.CommodityId).ToLowerInvariant()} going cheap, and {galaxy.Systems[d.Destination].Name} is paying well for it. For a few more days.");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The trade panel for a system: how its market compares with where you are, what's higher and
    /// lower, the best run between the two, and its own cheap and wanted goods.
    /// </summary>
    private void UpdateTradePanel(int id)
    {
        var galaxy = _session.Galaxy;
        if (id < 0 || galaxy.Systems[id].Stations.Count == 0)
        {
            _tradePanel.Visible = false;
            return;
        }
        var there = galaxy.Systems[id];
        var lines = new List<string> { $"[color=#{Ui.Hex(Ui.Amber)}]TRADE: {there.Name.ToUpperInvariant()}[/color]" };
        if (id == _session.Career.SystemId)
            lines.Add("Your market today. Pick another system to compare.");
        else if (_session.Here.Stations.Count == 0)
            lines.Add("No market here to compare with.");
        else
        {
            var c = TradeIntel.Compare(_session, there);
            if (c.AgeDays == null) lines.Add("You haven't seen this market. Visit it, or listen in bars, to learn its prices.");
            else
            {
                lines.Add($"Against {_session.Here.Name}, from prices {(c.AgeDays == 0 ? "today" : $"{c.AgeDays} days old")}.");
                lines.Add($"\n[color=#{Ui.Hex(Ui.Amber)}]Higher there[/color] (take from here):");
                lines.AddRange(c.Higher.Count == 0 ? ["  nothing worth the trip"]
                    : c.Higher.Select(g => $"  {GoodsName(g.CommodityId)}: {g.Buy:N0} here, {g.Sell:N0} there, +{g.Margin:N0}/t, {TrendText(g.Trend)}"));
                lines.Add($"\n[color=#{Ui.Hex(Ui.Amber)}]Lower there[/color] (bring back here):");
                lines.AddRange(c.Lower.Count == 0 ? ["  nothing worth bringing"]
                    : c.Lower.Select(g => $"  {GoodsName(g.CommodityId)}: {g.Buy:N0} there, {g.Sell:N0} here, +{g.Margin:N0}/t, {TrendText(g.Trend)}"));
                if (c.Best is { } best)
                    lines.Add($"\nBest run: {best.Tonnes} t of {GoodsName(best.CommodityId).ToLowerInvariant()}, about {best.Profit:N0} cr in {best.Days} days.");
            }
        }
        lines.Add(MarketLines(there));
        _tradeText.Text = string.Join("\n", lines);
        _tradePanel.Visible = true;
    }

    /// <summary>A system's market as you know it: what's cheap there and what it pays well for.</summary>
    private string MarketLines(StarSystem s)
    {
        var galaxy = _session.Galaxy;
        SeenMarket? market = s.Id == _session.Career.SystemId && s.Stations.Count > 0
            ? new SeenMarket(galaxy.Day, Market.Quotes(_session, s).ToDictionary(q => q.Commodity.Id, q => new SeenPrice(q.Buy, q.Sell, q.Stock, q.Wanted, q.Trend, q.Deal)))
            : _session.Career.SeenMarkets.GetValueOrDefault(s.Id);
        if (market == null) return s.Stations.Count > 0 ? "\nYou haven't seen this market. Visit it to learn its prices.\n" : "";
        float Position(string id, int price) { var (low, high) = Market.Range(_session.Db.Commodities[id]); return (price - low) / (float)Math.Max(1, high - low); }
        var cheap = market.Prices.Where(p => p.Value.Stock > 0).OrderBy(p => Position(p.Key, p.Value.Buy)).Take(4);
        var dear = market.Prices.OrderByDescending(p => Position(p.Key, p.Value.Sell)).Take(4);
        int age = galaxy.Day - market.Day;
        return $"\n[color=#{Ui.Hex(Ui.Amber)}]Market[/color] ({(age == 0 ? "today" : $"{age} days ago")})\n" +
               "Cheap: " + string.Join(", ", cheap.Select(p => $"{GoodsName(p.Key)} {p.Value.Buy:N0} ({TrendText(p.Value.Trend)})")) + "\n" +
               "Pays well: " + string.Join(", ", dear.Select(p => $"{GoodsName(p.Key)} {p.Value.Sell:N0} ({TrendText(p.Value.Trend)})")) + "\n";
    }

    /// <summary>Population, government and security, and whether it's a capital.</summary>
    private string ProfileLine(StarSystem s)
    {
        var p = SystemProfiles.For(_session.Galaxy, s, _session.Db);
        if (p.Population == 0) return "";
        string security = p.Security switch
        {
            "Lawless" or "War zone" => $"[color=#{Ui.Hex(Ui.Red)}]{p.Security}[/color]",
            "Low" => $"[color=#{Ui.Hex(Ui.Amber)}]Low security[/color]",
            _ => $"{p.Security} security",
        };
        return (p.CapitalOf != null ? $"[color=#{Ui.Hex(Ui.Amber)}]Capital of {p.CapitalOf}.[/color]\n" : "") +
               $"Population {p.PopulationText}. {p.Government}. {security}.\n";
    }

    /// <summary>Your jobs as buttons: click one to make it the active job and plot its route.</summary>
    private void RebuildJobs()
    {
        foreach (var child in _jobs.GetChildren()) child.QueueFree();
        var career = _session.Career;
        if (career.Missions.Count == 0) return;
        _jobs.AddChild(Ui.Label("Your jobs (click to follow):", Ui.Amber));
        var active = career.ActiveJob;
        foreach (var job in career.Missions.OrderBy(m => m.DeadlineDay))
        {
            var system = _session.Galaxy.Systems[job.Offer.DestinationSystemId];
            int left = job.DeadlineDay - _session.Galaxy.Day;
            string title = job.Offer.Mission.Title.Length > 24 ? job.Offer.Mission.Title[..23] + "…" : job.Offer.Mission.Title;
            string text = $"{(job == active ? "> " : "  ")}{title}: {system.Name}, {left}d";
            var j = job;
            var button = Ui.Button(text, () =>
            {
                career.ActiveJobId = j.Offer.Id;
                RebuildJobs();
                if (j.Offer.DestinationSystemId != career.SystemId) Select(j.Offer.DestinationSystemId);
                else { _selected = -1; _route = []; UpdateInfo(); QueueRedraw(); }
            });
            button.Alignment = HorizontalAlignment.Left;
            _jobs.AddChild(button);
        }
    }

    private void Select(int id)
    {
        _selected = id;
        _route = id >= 0 ? _session.Galaxy.Route(_session.Career.SystemId, id) : [];
        UpdateInfo();
        QueueRedraw();
    }

    /// <summary>Opens centred on the player with nothing selected.</summary>
    /// <summary>Opens centred on the player, with the active job's destination already selected and routed.</summary>
    public void Open()
    {
        _centre = _session.Here.Position;
        _zoom = 1.2f;
        _selected = -1;
        _route = [];
        RebuildJobs();
        if (_gatesMode) _jumpsFromHere = JumpsFrom(_session.Career.SystemId);
        RefreshTrade();
        if (_session.Career.ActiveJob is { } job && job.Offer.DestinationSystemId != _session.Career.SystemId)
        {
            Select(job.Offer.DestinationSystemId);
            _centre = (_session.Here.Position + _session.Galaxy.Systems[job.Offer.DestinationSystemId].Position) / 2;
        }
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
                _zoom = Mathf.Clamp(_zoom * (wheel.ButtonIndex == MouseButton.WheelUp ? 1.25f : 0.8f), 0.12f, 1200f);
                var after = ToGalaxy(wheel.Position);
                _centre += before - after; // keep the point under the cursor fixed
                QueueRedraw();
                break;
            }
            case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } drag:
                _dragging = drag.Pressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                _selected = Nearest(click.Position, register: true);
                _route = _selected >= 0 ? _session.Galaxy.Route(_session.Career.SystemId, _selected) : [];
                UpdateInfo();
                QueueRedraw();
                break;
            case InputEventMouseMotion motion:
                if (_dragging) { _centre -= new NVector2(motion.Relative.X, motion.Relative.Y) / _zoom; QueueRedraw(); }
                int hovered = Nearest(motion.Position);
                if (hovered <= -1000) hovered = -1; // an unregistered deep star: only show it once clicked
                if (hovered != _hovered) { _hovered = hovered; UpdateInfo(); QueueRedraw(); }
                break;
        }
    }

    /// <summary>For the showcase: set the view and selection directly.</summary>
    public void SetView(float zoom, NVector2 centre, int selected = -1)
    {
        _zoom = zoom;
        _centre = centre;
        _selected = selected;
        _hovered = -1;
        _route = selected >= 0 ? _session.Galaxy.Route(_session.Career.SystemId, selected) : [];
        UpdateInfo();
        QueueRedraw();
    }

    /// <summary>Nearest system to the cursor. A deep-space star is registered so it can be selected and jumped to.</summary>
    private int Nearest(Vector2 screen, bool register = false)
    {
        int best = -1;
        float bestDistance = 8f;
        foreach (var s in _session.Galaxy.Systems)
        {
            float d = ToScreen(s.Position).DistanceTo(screen);
            if (d < bestDistance) { bestDistance = d; best = s.Id; }
        }
        Galaxus.Core.Galaxy.DeepStar? deep = null;
        foreach (var (star, at) in _visibleDeep)
        {
            float d = at.DistanceTo(screen);
            if (d < bestDistance) { bestDistance = d; deep = star; best = -1; }
        }
        if (deep is { } found)
            return register || _session.Galaxy.IsRegistered(found.Key) ? _session.Galaxy.Register(found.Key) : -1000 - _visibleDeep.FindIndex(v => v.Star.Key == found.Key);
        return best;
    }

    private void UpdateInfo()
    {
        var galaxy = _session.Galaxy;
        int id = _hovered >= 0 ? _hovered : _selected;
        UpdateJumpButton();
        UpdateTradePanel(id);
        var (fuel, tank) = (_session.FuelLy, _session.FuelTankLy);
        string fuelLine = tank > 0 ? $"Fuel {fuel:0} of {tank:0} ly for direct jumps. Gates are free.\n" : "";
        if (id < 0 && _tradeMode)
        {
            _info.Text = TradeSummary();
            return;
        }
        if (id < 0)
        {
            _info.Text = $"[color=#{Ui.Hex(Ui.Amber)}]GALAXY MAP[/color]\nDay {galaxy.Day}. You are at {_session.Here.Name}.\n" + fuelLine +
                         (_gatesMode ? "Bright lines are the main gate routes; amber ones are bridges, the only way into whole parts of the galaxy. Numbers are gate jumps from here.\n\n"
                                     : "Click a system to plot a route. Scroll to zoom, right-drag to pan.\n\n") + Legend();
            return;
        }

        var s = galaxy.Systems[id];
        float distanceLy = galaxy.DistanceLy(_session.Career.SystemId, id);
        var plan = _session.PlanJump(id);
        string travel = id == _session.Career.SystemId ? "You are here."
            : plan.Kind == JumpKind.Direct ? $"{distanceLy:0.0} ly. A direct jump: {plan.Days} days, {plan.FuelLy:0.0} ly of fuel."
            : plan.Kind == JumpKind.Gates ? GateRouteText(id)
            : $"{distanceLy:0.0} ly. [color=#{Ui.Hex(Ui.Red)}]{plan.Reason}[/color]";

        if (!s.IsGateSystem)
        {
            var star = Galaxus.Core.Galaxy.SolarSystems.StarFor(s.Seed);
            _info.Text =
                $"[color=#{Ui.Hex(Ui.Amber)}]{s.Name.ToUpperInvariant()}[/color]\n" +
                $"A star between the gates: {star.Description}.\n" +
                $"{Capitalise(s.Region)}, {s.Arm} arm. Only reached by direct jump.\n" + travel;
            return;
        }

        var owner = galaxy.OwnerOf(s);
        var events = galaxy.EventsIn(id).Select(e => _session.Db.Events.TryGetValue(e.EventId, out var d) ? d.Name : e.EventId).ToList();
        var jobs = _session.Career.Missions.Where(m => m.Offer.DestinationSystemId == id).ToList();
        var news = galaxy.News.Where(n => n.SystemId == id).TakeLast(3).Reverse().ToList();
        var wars = galaxy.Conflicts.Where(c => c.FrontSystem == id).ToList();
        var bridges = s.Gates.Select(g => GateNetwork.Role(galaxy, id, g)).Where(r => r is { Bridge: true }).ToList();
        string Faction(int f) => f >= 0 && f < galaxy.Factions.Count ? $"[color=#{galaxy.Factions[f].Colour.TrimStart('#')}]{galaxy.Factions[f].Name}[/color]" : "nobody";

        var lines = new List<string>
        {
            $"[color=#{Ui.Hex(Ui.Amber)}]{s.Name.ToUpperInvariant()}[/color]",
            $"{Capitalise(s.Region)}, {s.Arm} arm. {s.State}{(s.Economy != Economy.None ? $", {s.Economy}" : "")}.",
            owner != null ? $"Held by {Faction(owner.Id)}." : "Unclaimed.",
            ProfileLine(s).TrimEnd('\n'),
            s.Stations.Count > 0 ? $"Stations: {string.Join(", ", s.Stations)}" : "No stations.",
        };
        // History and lore: landmarks, takeovers and wars.
        if (s.LandmarkNote is { Length: > 0 } note) lines.Add($"[color=#{Ui.Hex(Ui.Amber)}]{note}[/color]");
        if (WarFleets.At(_session.Db, galaxy, s) is { } fleet)
            lines.Add($"[color=#{Ui.Hex(Ui.Amber)}]{Faction(fleet.Faction.Id)}'s war fleet holds the front here. Its flagship, the {fleet.FlagshipName}, takes visitors.[/color]");
        if (s.PreviousOwner >= 0) lines.Add($"Taken from {Faction(s.PreviousOwner)} on day {s.OwnerSince}" + (galaxy.Day - s.OwnerSince < 21 ? ", and still unsettled." : "."));
        foreach (var w in wars) lines.Add($"[color=#{Ui.Hex(Ui.Red)}]On the front between {Faction(w.A)} and {Faction(w.B)}.[/color]");
        if (events.Count > 0) lines.Add($"[color=#{Ui.Hex(Ui.Red)}]{string.Join(", ", events)}[/color]");
        // Intel: your jobs, leads and the news from here.
        if (jobs.Count > 0) lines.Add($"[color=#{Ui.Hex(Ui.Amber)}]{jobs.Count} of your jobs end here: {string.Join(", ", jobs.Select(j => j.Offer.DestinationStation).Distinct())}.[/color]");
        lines.AddRange(_session.Career.KnownLeads.Where(l => l.EndsWith($":{id}")).Select(l => $"[color=#{Ui.Hex(Ui.Red)}]Ask around here for {galaxy.Factions[int.Parse(l.Split(':')[0])].Name}.[/color]"));
        if (news.Count > 0)
        {
            lines.Add("\nNews from here:");
            lines.AddRange(news.Select(n => $"  Day {n.Day}: {n.Text}"));
        }
        // Gates: bridges out of here lead to whole parts of the galaxy.
        foreach (var b in bridges)
        {
            int other = b!.A == id ? b.B : b.A;
            lines.Add($"[color=#{Ui.Hex(Ui.Amber)}]A bridge gate to {galaxy.Systems[other].Name}: the only way to {b.Beyond} systems beyond.[/color]");
        }
        lines.Add("\n" + travel);
        _info.Text = string.Join("\n", lines.Where(l => l.Length > 0));
    }

    /// <summary>A gate route in words: jumps, days, and any bridges it crosses.</summary>
    private string GateRouteText(int id)
    {
        var galaxy = _session.Galaxy;
        var route = id == _selected && _route.Count > 1 ? _route : galaxy.Route(_session.Career.SystemId, id);
        if (route.Count < 2) return "No gate route.";
        var bridges = GateNetwork.BridgesOn(galaxy, route);
        string text = $"{route.Count - 1} gates, {galaxy.RouteDays(route)} days, no fuel needed.";
        if (bridges.Count > 0)
            text += " Crosses " + string.Join(", ", bridges.Select(b => $"the {galaxy.Systems[b.A].Name}-{galaxy.Systems[b.B].Name} bridge")) + ".";
        return text;
    }

    /// <summary>The Jump button: what it would do for the selected system, or why it can't.</summary>
    private void UpdateJumpButton()
    {
        _jumpReason.Text = "";
        if (_session.Stranded)
        {
            _jumpButton.Disabled = false;
            _jumpButton.Text = "Call a rescue tug";
            _jumpReason.Text = "Out of fuel with no station in reach. A tug will take you to the nearest station for a fee.";
            return;
        }
        if (_selected < 0 || _selected == _session.Career.SystemId)
        {
            _jumpButton.Disabled = true;
            _jumpButton.Text = "Jump";
            if (_jumpBlocked() is { } why) _jumpReason.Text = why;
            return;
        }
        var target = _session.Galaxy.Systems[_selected];
        var plan = _session.PlanJump(_selected);
        var blocked = _jumpBlocked();
        _jumpButton.Disabled = plan.Kind == JumpKind.None || blocked != null;
        _jumpButton.Text = plan.Kind switch
        {
            JumpKind.Direct => $"Jump to {target.Name} ({plan.Days} days)",
            JumpKind.Gates => $"Gate to {target.Name} ({plan.Days} days)",
            _ => "Jump",
        };
        _jumpReason.Text = plan.Reason ?? blocked ?? "";
    }

    private string Legend() => string.Join("\n", _session.Galaxy.Factions.Where(f => f.Tier == "great")
        .Select(f => $"[color=#{f.Colour.TrimStart('#')}]{f.Name}[/color]"));

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>
    /// A system's colour in the trade overlay: by the chosen good's price where you know it (blue
    /// cheap, amber dear), green for any market you know when no good is picked, grey otherwise.
    /// </summary>
    private Color TradeColour(StarSystem s)
    {
        var grey = new Color(0.45f, 0.45f, 0.5f, s.Stations.Count > 0 ? 0.35f : 0.15f);
        if (_tradeGoods == null)
            return s.Id == _session.Career.SystemId || _session.Career.SeenMarkets.ContainsKey(s.Id) ? new Color(Ui.Green, 0.9f) : grey;
        if (!_known.TryGetValue(s.Id, out var seen)) return grey;
        var (low, high) = Market.Range(_session.Db.Commodities[_tradeGoods]);
        float t = Math.Clamp((seen.Price.Sell - low) / (float)Math.Max(1, high - low), 0, 1);
        var cheap = new Color(0.4f, 0.7f, 1f);
        var dear = new Color(1f, 0.6f, 0.2f);
        var c = cheap.Lerp(dear, t);
        c.A = seen.Age > 14 ? 0.6f : 1f;
        return c;
    }

    public override void _Draw()
    {
        var galaxy = _session.Galaxy;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.015f, 0.03f, 0.97f));

        // Region rings, faint, so the core-to-rim layout reads at a glance.
        var origin = ToScreen(NVector2.Zero);
        foreach (var r in new[] { 180f, 450f, 800f })
            DrawArc(origin, r * _zoom, 0, Mathf.Tau, 96, new Color(0.3f, 0.4f, 0.5f, 0.15f), 1);
        // Region names on the rings, so "the far reaches" means something.
        if (_zoom < 3)
            foreach (var (r, name) in new[] { (90f, "CORE"), (315f, "INNER"), (625f, "OUTER"), (900f, "RIM") })
                DrawString(ThemeDB.FallbackFont, origin + new Vector2(-14, -r * _zoom), name, HorizontalAlignment.Left, -1, 9, new Color(0.55f, 0.65f, 0.75f, 0.45f));

        // Gates: the main routes bright and thick, bridges to the far reaches in amber, local links faint.
        var roles = GateNetwork.Roles(galaxy);
        float faint = _zoom > 0.6f ? 0.16f : 0.07f;
        foreach (var role in roles.Values)
        {
            var a = ToScreen(galaxy.Systems[role.A].Position);
            var b = ToScreen(galaxy.Systems[role.B].Position);
            if (!new Rect2(-50, -50, Size.X + 100, Size.Y + 100).HasPoint(a) && !new Rect2(-50, -50, Size.X + 100, Size.Y + 100).HasPoint(b)) continue;
            Color colour;
            float width;
            if (role.Bridge) { colour = new Color(Ui.Amber, _gatesMode ? 0.95f : 0.6f); width = _gatesMode ? 2.5f : 1.6f; }
            else if (role.Main) { colour = new Color(0.55f, 0.75f, 0.95f, _gatesMode ? 0.75f : 0.3f + 0.25f * role.Weight); width = 1 + role.Weight * (_gatesMode ? 2.2f : 1.2f); }
            else { colour = new Color(0.4f, 0.55f, 0.7f, _gatesMode ? faint * 1.8f : faint); width = 1; }
            DrawLine(a, b, colour, width);
            // A small gate mark at each end of a bridge.
            if (role.Bridge && _zoom > 0.3f)
                foreach (var end in new[] { a, b })
                    DrawPolyline([end + new Vector2(0, -5), end + new Vector2(5, 0), end + new Vector2(0, 5), end + new Vector2(-5, 0), end + new Vector2(0, -5)], Ui.Amber, 1);
        }

        if (_selected >= 0 && _route.Count < 2 && _selected != _session.Career.SystemId)
            DrawLine(ToScreen(_session.Here.Position), ToScreen(galaxy.Systems[_selected].Position), Ui.Green, 1.5f);
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
            if (_tradeMode) c = TradeColour(s);
            DrawRect(new Rect2(p - Vector2.One * dot / 2, Vector2.One * dot), c);
            if (_tradeMode && _known.TryGetValue(s.Id, out var seen) && Math.Abs(seen.Price.Trend) >= 0.03f)
            {
                // A small arrow: up for rising, down for falling.
                float y = seen.Price.Trend > 0 ? -1 : 1, h = dot + 4;
                var tip = p + new Vector2(dot / 2 + 4, y * h * 0.6f);
                DrawColoredPolygon([tip, tip + new Vector2(-3, -y * 4), tip + new Vector2(3, -y * 4)], seen.Price.Trend > 0 ? Ui.Amber : new Color(0.55f, 0.8f, 1f));
            }
            if (galaxy.EventsIn(s.Id).Any() && _zoom > 0.5f) DrawArc(p, dot + 2, 0, Mathf.Tau, 12, new Color(Ui.Red, 0.6f), 1);
        }

        if (_tradeMode)
        {
            // The best runs from here, as cyan lines with what they'd make.
            var cyan = new Color(0.4f, 0.9f, 1f, 0.85f);
            foreach (var r in _runs)
            {
                var to = ToScreen(galaxy.Systems[r.To].Position);
                DrawLine(ToScreen(_session.Here.Position), to, cyan, 1.5f);
                DrawString(ThemeDB.FallbackFont, to + new Vector2(dot + 4, 10), $"+{r.Profit:N0} {GoodsName(r.CommodityId).ToLowerInvariant()}", HorizontalAlignment.Left, -1, 8, cyan);
            }
            // Jackpots you've heard of: amber rings at both ends.
            foreach (var d in _rumours)
                foreach (var (id, label) in new[] { (d.Source, "SURPLUS"), (d.Destination, "WANTED") })
                {
                    var at = ToScreen(galaxy.Systems[id].Position);
                    DrawArc(at, dot + 6, 0, Mathf.Tau, 16, Ui.Amber, 1.5f);
                    DrawString(ThemeDB.FallbackFont, at + new Vector2(dot + 8, -6), $"{label} {GoodsName(d.CommodityId).ToLowerInvariant()}", HorizontalAlignment.Left, -1, 8, Ui.Amber);
                }
        }

        // Underworld leads you've been told about: a red diamond on the system.
        foreach (var lead in _session.Career.KnownLeads)
            if (lead.Split(':') is [_, var sys] && int.TryParse(sys, out int id) && id < galaxy.Systems.Count)
            {
                var p = ToScreen(galaxy.Systems[id].Position);
                DrawColoredPolygon([p + new Vector2(0, -dot - 4), p + new Vector2(dot + 4, 0), p + new Vector2(0, dot + 4), p + new Vector2(-dot - 4, 0)], new Color(Ui.Red, 0.55f));
            }

        // Close in, the billions of stars between the gates come into view.
        _visibleDeep.Clear();
        if (_zoom >= DeepZoom)
        {
            float perLy = _zoom / Galaxus.Core.Galaxy.DeepSpace.LyPerUnit;
            var topLeft = ToGalaxy(Vector2.Zero) * Galaxus.Core.Galaxy.DeepSpace.LyPerUnit;
            var bottomRight = ToGalaxy(Size) * Galaxus.Core.Galaxy.DeepSpace.LyPerUnit;
            float column = Galaxus.Core.Galaxy.DeepSpace.ColumnLy;
            for (int cx = (int)MathF.Floor(topLeft.X / column); cx <= (int)MathF.Floor(bottomRight.X / column); cx++)
            for (int cy = (int)MathF.Floor(topLeft.Y / column); cy <= (int)MathF.Floor(bottomRight.Y / column); cy++)
                foreach (var (star, colour) in CachedColumn(galaxy.Seed, cx, cy))
                {
                    var p = ToScreen(new NVector2(star.PositionLy.X, star.PositionLy.Y) / Galaxus.Core.Galaxy.DeepSpace.LyPerUnit);
                    _visibleDeep.Add((star, p));
                    DrawRect(new Rect2(p - Vector2.One, Vector2.One * 2), colour);
                }
            var here = ToScreen(_session.Here.Position);
            DrawArc(here, _session.JumpRangeLy * perLy, 0, Mathf.Tau, 64, new Color(Ui.Green, 0.5f), 1);
            DrawString(ThemeDB.FallbackFont, here + new Vector2(_session.JumpRangeLy * perLy + 4, 0), $"jump range {_session.JumpRangeLy:0} ly", HorizontalAlignment.Left, -1, 8, Ui.Dim);
            // How far your fuel takes you by direct jumps, one jump at a time.
            float fuel = _session.FuelLy;
            if (fuel > _session.JumpRangeLy)
            {
                DrawArc(here, fuel * perLy, 0, Mathf.Tau, 96, new Color(Ui.Amber, 0.3f), 1);
                DrawString(ThemeDB.FallbackFont, here + new Vector2(fuel * perLy + 4, 12), $"fuel {fuel:0} ly", HorizontalAlignment.Left, -1, 8, new Color(Ui.Amber, 0.6f));
            }
        }
        else if (_zoom >= DeepZoom / 4)
            DrawString(ThemeDB.FallbackFont, new Vector2(Size.X - 230, Size.Y - 8), "Zoom in further to see the stars between the gates.", HorizontalAlignment.Left, -1, 8, Ui.Dim);

        if (_gatesMode && _zoom >= 1.5f)
            foreach (var (id, jumps) in _jumpsFromHere)
            {
                if (jumps == 0 || jumps > 12) continue;
                var p = ToScreen(galaxy.Systems[id].Position);
                if (p.X < 0 || p.Y < 0 || p.X > Size.X || p.Y > Size.Y) continue;
                DrawString(ThemeDB.FallbackFont, p + new Vector2(dot, dot + 8), jumps.ToString(), HorizontalAlignment.Left, -1, 8, new Color(0.7f, 0.85f, 1f, 0.8f));
            }

        foreach (var job in _session.Career.Missions)
            DrawArc(ToScreen(galaxy.Systems[job.Offer.DestinationSystemId].Position), dot + 4, 0, Mathf.Tau, 16, Ui.Amber, 1);
        if (_session.Career.ActiveJob is { } activeJob)
        {
            // The active job: a heavier ring and a label, so it stands out from the rest.
            var at = ToScreen(galaxy.Systems[activeJob.Offer.DestinationSystemId].Position);
            DrawArc(at, dot + 10, 0, Mathf.Tau, 24, Ui.Amber, 2);
            DrawString(ThemeDB.FallbackFont, at + new Vector2(dot + 12, 4), "JOB", HorizontalAlignment.Left, -1, 9, Ui.Amber);
        }

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
