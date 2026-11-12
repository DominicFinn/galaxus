using Godot;
using Galaxus.Core.Ships;

namespace Galaxus;

/// <summary>
/// Cockpit HUD drawn directly with 2D primitives at the game's 640x360 resolution, so it scales
/// up with sharp pixels like the 3D view.
/// </summary>
public partial class Hud : Control
{
    private static readonly Color Green = new(0.45f, 1f, 0.55f);
    private static readonly Color Dim = new(0.55f, 1f, 0.62f, 0.62f);
    private static readonly Color Amber = new(1f, 0.75f, 0.3f);
    private static readonly Color Red = new(1f, 0.35f, 0.3f);
    private static readonly Color Blue = new(0.45f, 0.75f, 1f);
    private static readonly Color Panel = new(0f, 0.05f, 0.02f, 0.68f);
    private const int FontSize = 8;

    private readonly List<(string Text, float Age)> _messages = [];
    private Font _font = null!;

    public Ship? Player { get; set; }
    public Camera3D? Camera { get; set; }
    public Vector2 MouseStick { get; set; }
    public float MouseStickRadius { get; set; } = 60;
    public float Throttle { get; set; }
    public int Kills { get; set; }
    public bool ShowHelp { get; set; }
    public bool Cockpit { get; set; }
    /// <summary>Docking hint shown under the crosshair, or null.</summary>
    public string? Prompt { get; set; }
    /// <summary>Cruise and course readout, shown under the location.</summary>
    public string Cruise { get; set; } = "";
    /// <summary>Fuel for direct jumps, e.g. "120/150 LY".</summary>
    public string Fuel { get; set; } = "";
    /// <summary>Clear to jump: in open space, away from stations, the fleet and planets.</summary>
    public bool JumpReady { get; set; }
    /// <summary>The active job and what to do next, shown top right. Empty for none.</summary>
    public string JobTitle { get; set; } = "";
    public bool ShowPerformance { get; set; }
    /// <summary>Things to point at in space: objective targets and threats, each with a label.</summary>
    public List<(Vector3 Position, string Label, Color Colour)> Marks { get; } = [];
    /// <summary>Extra detail after each objective: distance, scan progress, time left.</summary>
    public Dictionary<Galaxus.Core.Battles.BattleObjective, string> ObjectiveNotes { get; } = [];
    /// <summary>What's attacking you and what to do about it, shown under the job lines.</summary>
    public string Threat { get; set; } = "";
    public string Performance { get; set; } = "";
    /// <summary>The battle's objectives while one is on, or null.</summary>
    public List<Galaxus.Core.Battles.BattleObjective>? Objectives { get; set; }
    /// <summary>All ships to mark on screen, and whether each is an enemy, a friend or neither.</summary>
    public Func<IEnumerable<(Ship Ship, int Stance)>>? Contacts { get; set; }
    private readonly List<(string Text, string Side, float Age)> _radio = [];

    /// <summary>A radio call: kept on screen for a while, coloured by who's talking.</summary>
    public void Radio(string text, string side)
    {
        // Wrap long calls so they fit the panel.
        var words = text.Split(' ');
        var line = "";
        var lines = new List<string>();
        foreach (var w in words)
        {
            if (line.Length + w.Length > 64) { lines.Add(line.TrimEnd()); line = ""; }
            line += w + " ";
        }
        if (line.Trim() != "") lines.Add(line.TrimEnd());
        foreach (var l in lines) _radio.Add((l, side, 0));
        while (_radio.Count > 7) _radio.RemoveAt(0);
    }
    public string JobStep { get; set; } = "";
    /// <summary>The targeted asteroid: where it is, its label and its size, or null.</summary>
    public (Vector3 Position, string Label, float Radius)? RockTarget { get; set; }
    /// <summary>"SCANNING X 40%" while a scan runs.</summary>
    public string ScanProgress { get; set; } = "";
    /// <summary>Your wing's order and how each wingman is doing.</summary>
    public string Wing { get; set; } = "";
    /// <summary>The last scan's report, shown for a while on the left.</summary>
    public string ScanReport { get; set; } = "";
    public float ScanReportTime { get; set; }
    /// <summary>What the mining laser is doing, while you hold fire on a rock.</summary>
    public string CutStatus { get; set; } = "";
    /// <summary>Where the course target is, in render space, for the nav marker. Null hides it.</summary>
    public Vector3? NavPoint { get; set; }
    public string NavLabel { get; set; } = "";
    /// <summary>Current system and day, shown top centre.</summary>
    public string Location { get; set; } = "";

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _font = ThemeDB.FallbackFont;
    }

    public void Message(string text) => _messages.Insert(0, (text, 0));

    public override void _Process(double delta)
    {
        for (int i = _radio.Count - 1; i >= 0; i--)
        {
            var (t, side, a) = _radio[i];
            a += (float)delta;
            if (a > 14) _radio.RemoveAt(i); else _radio[i] = (t, side, a);
        }
        if (ScanReportTime > 0) ScanReportTime -= (float)delta;
        for (int i = _messages.Count - 1; i >= 0; i--)
        {
            var (text, age) = _messages[i];
            age += (float)delta;
            if (age > 4 || i > 4) _messages.RemoveAt(i);
            else _messages[i] = (text, age);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Player == null || Camera == null) return;
        var size = GetViewportRect().Size;
        var centre = size / 2;

        DrawCrosshair(centre);
        DrawMouseStick(centre);
        DrawTarget(size);
        DrawFlightPanel(size);
        DrawPowerPanel(size);
        DrawDefencePanel(size);
        DrawContacts(size);
        DrawTargetPanel();
        DrawMessages(size);
        DrawRadio(size);
        DrawObjectives(size);
        Text(new Vector2(size.X / 2, 12), Location, Dim, HorizontalAlignment.Center);
        Text(new Vector2(size.X / 2, 24), Cruise, Amber, HorizontalAlignment.Center);
        DrawNavMarker(size);
        DrawMarks(size);
        DrawRockTarget(size);
        if (Wing != "") Text(new Vector2(size.X / 2, 36), Wing, Green, HorizontalAlignment.Center);
        if (ScanProgress != "") Text(new Vector2(size.X / 2, size.Y / 2 + 48), ScanProgress, Blue, HorizontalAlignment.Center);
        if (CutStatus != "") Text(new Vector2(size.X / 2, size.Y / 2 + 60), CutStatus, Amber, HorizontalAlignment.Center);
        if (ScanReportTime > 0 && ScanReport != "") DrawScanReport(size);
        if (Threat != "") Text(new Vector2(size.X / 2, 84), Threat, Red, HorizontalAlignment.Center);
        if (Prompt != null) Text(new Vector2(size.X / 2, size.Y / 2 + 36), Prompt, Amber, HorizontalAlignment.Center);
        Text(new Vector2(size.X - 6, 12), $"KILLS {Kills}", Dim, HorizontalAlignment.Right);
        if (GameSettings.Current.ShowHints) Text(new Vector2(size.X - 6, 22), "F1 CONTROLS", Dim, HorizontalAlignment.Right);
        if (JobTitle != "")
        {
            Text(new Vector2(size.X - 6, 38), $"JOB: {JobTitle.ToUpperInvariant()}", Amber, HorizontalAlignment.Right);
            Text(new Vector2(size.X - 6, 48), JobStep.ToUpperInvariant(), Green, HorizontalAlignment.Right);
            Text(new Vector2(size.X - 6, 58), "L NEXT JOB   M GALAXY MAP   N SYSTEM MAP", Dim, HorizontalAlignment.Right);
        }
        if (ShowPerformance) Text(new Vector2(size.X / 2, size.Y - 132), Performance, Amber, HorizontalAlignment.Center);
        if (ShowHelp) DrawHelp(size);
    }

    /// <summary>Small marks on every ship in view: red for enemies, green for friends, white for neither.</summary>
    private void DrawContacts(Vector2 size)
    {
        if (Contacts == null || Camera == null || Player == null) return;
        foreach (var (ship, stance) in Contacts())
        {
            if (ship == Player.Target || !ship.Alive) continue;
            var at = ship.Body.Position.ToGodot();
            if (Camera.IsPositionBehind(at)) continue;
            var p = Camera.UnprojectPosition(at);
            if (!OnScreen(p, size, 4)) continue;
            var colour = stance < 0 ? Red : stance > 0 ? Green : new Color(1, 1, 1, 0.7f);
            float r = ship.Spec.Hull.IsCapital ? 6 : 3;
            DrawPolyline([p + new Vector2(0, -r), p + new Vector2(r, 0), p + new Vector2(0, r), p + new Vector2(-r, 0), p + new Vector2(0, -r)], colour);
            // Name warships only when they're close enough to tell apart, so a distant line doesn't become a smear of text.
            if (ship.Spec.Hull.IsCapital && at.DistanceTo(Player.Body.Position.ToGodot()) < 4000)
                Text(p + new Vector2(0, r + 9), ship.Name.ToUpperInvariant(), colour, HorizontalAlignment.Center);
        }
    }

    private void DrawRadio(Vector2 size)
    {
        if (_radio.Count == 0) return;
        var origin = new Vector2(8, size.Y - 150 - _radio.Count * 10);
        DrawRect(new Rect2(origin - new Vector2(4, 10), new Vector2(330, _radio.Count * 10 + 4)), Panel);
        for (int i = 0; i < _radio.Count; i++)
        {
            var (text, side, age) = _radio[i];
            float alpha = Mathf.Clamp(14 - age, 0, 1);
            var colour = side == "enemy" ? Red : side == "neutral" ? new Color(0.85f, 0.85f, 0.85f) : Blue;
            Text(origin + new Vector2(0, i * 10), text, new Color(colour, alpha));
        }
    }

    private void DrawObjectives(Vector2 size)
    {
        if (Objectives == null) return;
        var origin = new Vector2(8, 96);
        DrawRect(new Rect2(origin - new Vector2(4, 10), new Vector2(300, Objectives.Count * 10 + 16)), Panel);
        Text(origin, "OBJECTIVES", Amber);
        for (int i = 0; i < Objectives.Count; i++)
        {
            var o = Objectives[i];
            var (mark, colour) = o.State switch
            {
                Galaxus.Core.Battles.ObjectiveState.Done => ("[X]", Green),
                Galaxus.Core.Battles.ObjectiveState.Failed => ("[!]", Red),
                _ => ("[ ]", o.Def.Optional ? Dim : Colors.White),
            };
            string note = ObjectiveNotes.TryGetValue(o, out var n) && o.State == Galaxus.Core.Battles.ObjectiveState.Pending ? $"  {n}" : "";
            Text(origin + new Vector2(0, 11 + i * 10), $"{mark} {o.Text.ToUpperInvariant()}{(o.Def.Optional ? " (BONUS)" : "")}{note}", colour);
        }
    }

    private void DrawCrosshair(Vector2 c)
    {
        DrawLine(c + new Vector2(-8, 0), c + new Vector2(-3, 0), Green);
        DrawLine(c + new Vector2(3, 0), c + new Vector2(8, 0), Green);
        DrawLine(c + new Vector2(0, -8), c + new Vector2(0, -3), Green);
        DrawLine(c + new Vector2(0, 3), c + new Vector2(0, 8), Green);
    }

    private void DrawMouseStick(Vector2 c)
    {
        if (MouseStick.LengthSquared() < 1) return;
        DrawArc(c, MouseStickRadius, 0, Mathf.Tau, 48, Dim, 1);
        DrawRect(new Rect2(c + MouseStick - new Vector2(1.5f, 1.5f), new Vector2(3, 3)), Green);
    }

    private void DrawTarget(Vector2 size)
    {
        var target = Player!.Target;
        if (target is not { Alive: true }) return;

        var targetPos = target.Body.Position.ToGodot();
        float distance = targetPos.DistanceTo(Player.Body.Position.ToGodot());

        if (Camera!.IsPositionBehind(targetPos) || !OnScreen(Camera.UnprojectPosition(targetPos), size, 10))
        {
            DrawOffscreenArrow(targetPos, size);
            return;
        }

        var p = Camera.UnprojectPosition(targetPos);
        float half = Mathf.Clamp(2400f / distance, 5, 30);
        int stance = Contacts?.Invoke().FirstOrDefault(c => c.Ship == target).Stance ?? 0;
        var colour = stance < 0 ? Red : stance > 0 ? Green : Amber;
        // Corner brackets.
        foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            var corner = p + new Vector2(sx * half, sy * half);
            DrawLine(corner, corner - new Vector2(sx * 4, 0), colour);
            DrawLine(corner, corner - new Vector2(0, sy * 4), colour);
        }
        Text(p + new Vector2(0, half + 10), $"{distance:0} m", colour, HorizontalAlignment.Center);
        // Missile lock: a ring closes in on the target as the lock builds, then turns solid.
        if (Player.Spec.Missiles > 0 && Player.MissilesLeft > 0 && Player.Lock > 0)
        {
            float ring = Mathf.Lerp(half * 3, half + 4, Player.Lock);
            var lockColour = Player.Locked ? Red : Amber;
            DrawArc(p, ring, 0, Mathf.Tau, 24, lockColour, Player.Locked ? 2.5f : 1.2f);
            Text(p + new Vector2(0, -half - 8), Player.Locked ? "LOCKED" : "LOCKING", lockColour, HorizontalAlignment.Center);
        }

        // Lead indicator: where to aim so the bolts meet the target.
        var weapon = Player.Spec.Weapons.Count > 0 ? Player.Spec.Weapons[0] : null;
        if (weapon != null && distance < weapon.Range * 1.2f)
        {
            var lead = World.LeadPoint(Player.Body.Position, Player.Body.Velocity, target.Body.Position, target.Body.Velocity, weapon.ProjectileSpeed);
            if (lead != null && !Camera.IsPositionBehind(lead.Value.ToGodot()))
            {
                var l = Camera.UnprojectPosition(lead.Value.ToGodot());
                DrawPolyline([l + new Vector2(0, -4), l + new Vector2(4, 0), l + new Vector2(0, 4), l + new Vector2(-4, 0), l + new Vector2(0, -4)], Red);
                DrawLine(p, l, new Color(Red, 0.3f));
            }
        }
    }

    /// <summary>Brackets on the targeted asteroid, with its ore once scanned.</summary>
    private void DrawRockTarget(Vector2 size)
    {
        if (RockTarget is not { } rock || Camera == null) return;
        if (Camera.IsPositionBehind(rock.Position) || !OnScreen(Camera.UnprojectPosition(rock.Position), size, 10))
        {
            DrawOffscreenArrow(rock.Position, size, Amber);
            return;
        }
        var p = Camera.UnprojectPosition(rock.Position);
        float distance = Mathf.Max(1, Camera.GlobalPosition.DistanceTo(rock.Position));
        float half = Mathf.Clamp(rock.Radius * 500f / distance, 5, 60);
        foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            var corner = p + new Vector2(sx * half, sy * half);
            DrawLine(corner, corner - new Vector2(sx * 4, 0), Amber);
            DrawLine(corner, corner - new Vector2(0, sy * 4), Amber);
        }
        Text(p + new Vector2(0, half + 10), rock.Label, Amber, HorizontalAlignment.Center);
    }

    /// <summary>The last scan's report in a panel on the left, wrapped to fit.</summary>
    private void DrawScanReport(Vector2 size)
    {
        var lines = new List<string>();
        foreach (var paragraph in ScanReport.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                if (line.Length + word.Length + 1 > 52) { lines.Add(line); line = word; }
                else line = line.Length == 0 ? word : line + " " + word;
            }
            lines.Add(line);
        }
        var origin = new Vector2(8, 90);
        DrawRect(new Rect2(origin - new Vector2(4, 10), new Vector2(214, lines.Count * 10 + 6)), Panel);
        for (int i = 0; i < lines.Count; i++) Text(origin + new Vector2(0, i * 10), lines[i], i == 0 ? Amber : Green);
    }

    /// <summary>A diamond on the course target, or an arrow at the screen edge pointing to it.</summary>
    private void DrawNavMarker(Vector2 size)
    {
        if (NavPoint is not { } point || Camera == null) return;
        if (Camera.IsPositionBehind(point) || !OnScreen(Camera.UnprojectPosition(point), size, 10))
        {
            DrawOffscreenArrow(point, size, Blue);
            return;
        }
        var p = Camera.UnprojectPosition(point);
        DrawPolyline([p + new Vector2(0, -7), p + new Vector2(7, 0), p + new Vector2(0, 7), p + new Vector2(-7, 0), p + new Vector2(0, -7)], Blue, 1.5f);
        Text(p + new Vector2(0, 18), NavLabel, Blue, HorizontalAlignment.Center);
    }

    /// <summary>Objective and threat markers: a labelled bracket on screen, a labelled arrow at the edge when off it.</summary>
    private void DrawMarks(Vector2 size)
    {
        if (Camera == null) return;
        foreach (var (position, label, colour) in Marks)
        {
            if (Camera.IsPositionBehind(position) || !OnScreen(Camera.UnprojectPosition(position), size, 12))
            {
                var local = Camera.GlobalTransform.AffineInverse() * position;
                var dir = new Vector2(local.X, -local.Y);
                if (dir.LengthSquared() < 1e-4f) dir = Vector2.Down;
                dir = dir.Normalized();
                var tip = size / 2 + dir * (Mathf.Min(size.X, size.Y) / 2 - 48);
                var side = new Vector2(-dir.Y, dir.X);
                DrawColoredPolygon([tip + dir * 8, tip - dir * 5 + side * 6, tip - dir * 5 - side * 6], colour);
                Text(tip - dir * 18 + new Vector2(0, 4), label, colour, HorizontalAlignment.Center);
                continue;
            }
            var p = Camera.UnprojectPosition(position);
            const float half = 11;
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var corner = p + new Vector2(sx * half, sy * half);
                DrawLine(corner, corner - new Vector2(sx * 5, 0), colour, 1.5f);
                DrawLine(corner, corner - new Vector2(0, sy * 5), colour, 1.5f);
            }
            Text(p + new Vector2(0, -half - 4), label, colour, HorizontalAlignment.Center);
        }
    }

    private void DrawOffscreenArrow(Vector3 targetPos, Vector2 size)
    {
        // Direction to the target in camera space, flattened onto the screen.
        var local = Camera!.GlobalTransform.AffineInverse() * targetPos;
        var dir = new Vector2(local.X, -local.Y);
        if (dir.LengthSquared() < 1e-4f) dir = Vector2.Down;
        dir = dir.Normalized();
        var centre = size / 2;
        var tip = centre + dir * (Mathf.Min(size.X, size.Y) / 2 - 20);
        var side = new Vector2(-dir.Y, dir.X);
        DrawColoredPolygon([tip + dir * 6, tip - dir * 4 + side * 5, tip - dir * 4 - side * 5], Amber);
    }

    private void DrawOffscreenArrow(Vector3 targetPos, Vector2 size, Color colour)
    {
        var local = Camera!.GlobalTransform.AffineInverse() * targetPos;
        var dir = new Vector2(local.X, -local.Y);
        if (dir.LengthSquared() < 1e-4f) dir = Vector2.Down;
        dir = dir.Normalized();
        var tip = size / 2 + dir * (Mathf.Min(size.X, size.Y) / 2 - 34);
        var side = new Vector2(-dir.Y, dir.X);
        DrawColoredPolygon([tip + dir * 7, tip - dir * 5 + side * 6, tip - dir * 5 - side * 6], colour);
    }

    private void DrawFlightPanel(Vector2 size)
    {
        var origin = new Vector2(8, size.Y - 98);
        DrawRect(new Rect2(origin - new Vector2(4, 12), new Vector2(96, 104)), Panel);
        var stats = Player!.CurrentFlightStats();
        bool assist = Player.Controls.Flight.Assist;

        Text(origin, assist ? "FLIGHT ASSIST ON" : "FLIGHT ASSIST OFF", assist ? Green : Amber);
        Text(origin + new Vector2(0, 14), $"SPD {Player.Body.Speed,4:0} m/s", Green);
        Text(origin + new Vector2(0, 26), $"MAX {stats.MaxSpeed,4:0} m/s", Dim);
        if (Fuel != "") Text(origin + new Vector2(0, 62), $"FUEL {Fuel}", Dim);
        if (JumpReady) Text(origin + new Vector2(0, 74), "JUMP READY (M)", Green);

        // Throttle bar: the setting with assist on, live thrust with it off.
        var bar = new Rect2(origin + new Vector2(76, -4), new Vector2(8, 60));
        DrawRect(bar, Dim, false);
        float fill = assist ? Throttle : Mathf.Abs(Player.Controls.Flight.Thrust);
        float h = bar.Size.Y * fill;
        DrawRect(new Rect2(bar.Position.X + 1, bar.End.Y - h, bar.Size.X - 2, h), assist ? Green : Amber);
        // Speed marker against top speed.
        float speedMark = Mathf.Clamp(Player.Body.ForwardSpeed / stats.MaxSpeed, 0, 1);
        float y = bar.End.Y - bar.Size.Y * speedMark;
        DrawLine(new Vector2(bar.Position.X - 4, y), new Vector2(bar.Position.X - 1, y), Blue, 2);

        // Best turn speed: g-tolerance caps turning above this.
        float bestTurn = Galaxus.Core.Flight.FlightModel.TurnRateLimit(1, stats.GTolerance) / MathF.Max(stats.PitchRate, 0.01f);
        Text(origin + new Vector2(0, 38), $"TURN <{bestTurn:0} m/s", Dim);
        Text(origin + new Vector2(0, 50), $"G-LIMIT {stats.GTolerance:0}g", Dim);
    }

    private void DrawPowerPanel(Vector2 size)
    {
        var origin = new Vector2(size.X / 2 - 48, size.Y - 62);
        DrawRect(new Rect2(origin - new Vector2(4, 4), new Vector2(104, 58)), Panel);
        (string Label, ShipSystem System, Color Colour)[] systems =
        [
            ("ENG", ShipSystem.Engines, Amber),
            ("SHD", ShipSystem.Shields, Blue),
            ("WEP", ShipSystem.Weapons, Red),
        ];
        for (int s = 0; s < 3; s++)
        {
            var (label, system, colour) = systems[s];
            var column = origin + new Vector2(8 + s * 32, 0);
            int halves = Player!.Power.HalfPips(system);
            for (int pip = 0; pip < 4; pip++)
            {
                var rect = new Rect2(column + new Vector2(0, 30 - pip * 9), new Vector2(18, 7));
                DrawRect(rect, Dim, false);
                int filled = Mathf.Clamp(halves - pip * 2, 0, 2);
                if (filled == 2) DrawRect(rect, colour);
                else if (filled == 1) DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X / 2, rect.Size.Y)), colour);
            }
            Text(column + new Vector2(9, 48), $"{s + 1} {label}", colour, HorizontalAlignment.Center);
        }
    }

    private void DrawDefencePanel(Vector2 size)
    {
        var origin = new Vector2(size.X - 108, size.Y - 62);
        DrawRect(new Rect2(origin - new Vector2(4, 12), new Vector2(104, Player!.Spec.Missiles > 0 ? 78 : 66)), Panel);
        Bar(origin, "SHIELD", Player!.Shields / MathF.Max(Player.Spec.MaxShields, 1), Blue);
        Bar(origin + new Vector2(0, 16), "HULL", Player.Hull / Player.Spec.MaxHull, Green);
        Bar(origin + new Vector2(0, 32), "WEAPON", Player.Capacitor / MathF.Max(Player.Spec.CapacitorMax, 1), Red);
        if (Player.Spec.Missiles > 0)
            Text(origin + new Vector2(0, 48), $"MISSILES {Player.MissilesLeft}/{Player.Spec.Missiles}" + (Player.Spec.Decoys > 0 ? $"  DECOYS {Player.DecoysLeft}" : ""), Player.MissilesLeft > 0 ? Amber : Dim);
    }

    private void DrawTargetPanel()
    {
        var target = Player!.Target;
        var origin = new Vector2(8, 14);
        DrawRect(new Rect2(origin - new Vector2(4, 10), new Vector2(120, 58)), Panel);
        if (target is not { Alive: true })
        {
            Text(origin, "NO TARGET  (T)", Dim);
            return;
        }
        float distance = Vector3.Zero.DistanceTo((target.Body.Position - Player.Body.Position).ToGodot());
        Text(origin, target.Name.ToUpperInvariant(), Amber);
        Text(origin + new Vector2(0, 10), $"{target.Spec.Hull.Class.ToUpperInvariant()}  {distance:0} m", Dim);
        Bar(origin + new Vector2(0, 22), "SHIELD", target.Shields / MathF.Max(target.Spec.MaxShields, 1), Blue);
        Bar(origin + new Vector2(0, 36), "HULL", target.Hull / target.Spec.MaxHull, Green);
    }

    private void DrawMessages(Vector2 size)
    {
        for (int i = 0; i < _messages.Count; i++)
        {
            var (text, age) = _messages[i];
            float alpha = Mathf.Clamp(4 - age, 0, 1);
            Text(new Vector2(size.X / 2, 60 + i * 11), text, new Color(Green, alpha), HorizontalAlignment.Center);
        }
    }

    private void DrawHelp(Vector2 size)
    {
        string[] lines =
        [
            "MOUSE / ARROWS / LEFT STICK   pitch and yaw (down arrow raises the nose)",
            "A D                           yaw",
            "Q E / BUMPERS / RIGHT STICK X roll",
            "W S / RIGHT STICK Y           throttle (assist on) or thrust (assist off)",
            "X                             zero throttle",
            "R F Z C                       thrusters up, down, left, right",
            "SPACE / LEFT CLICK / RT       fire",
            "G / B                         flight assist on or off",
            "1 2 3 / D-PAD UP LEFT RIGHT   power to engines, shields, weapons",
            "4 / D-PAD DOWN                balance power",
            "T / A                         next target",
            "TAB                           match target speed",
            "V / Y                         chase or cockpit camera",
            "ENTER / X                     dock when close and slow",
            "M / START                     galaxy map",
            "N / VIEW                      system map: pick a destination",
            "J / LEFT STICK CLICK          autopilot to your destination",
            "B / RIGHT CLICK / LT          fire a missile once locked",
            "H                             afterburner, if one is fitted",
            "K                             scan your target, or the world you're near",
            "Y                             hail your target",
            "O                             logbook: news, lore, clues, finds",
            "F5 / F6 / F7                  wing: attack my target, defend me, form up",
            "L                             cycle which job you're following",
            ".  ,                          speed time up, slow it down",
            "F1  this list    F3  frame rate and ship count    C  (system map) find your ship",
            "ESC                           pause menu, save and load      F10  quit",
        ];
        var rect = new Rect2(size.X / 2 - 200, 70, 400, lines.Length * 11 + 16);
        DrawRect(rect, new Color(0, 0.03f, 0.01f, 0.85f));
        DrawRect(rect, Dim, false);
        for (int i = 0; i < lines.Length; i++)
            Text(rect.Position + new Vector2(10, 16 + i * 11), lines[i], Green);
    }

    private void Bar(Vector2 origin, string label, float fraction, Color colour)
    {
        Text(origin, label, Dim);
        var rect = new Rect2(origin + new Vector2(44, -7), new Vector2(52, 8));
        DrawRect(rect, Dim, false);
        DrawRect(new Rect2(rect.Position + Vector2.One, new Vector2((rect.Size.X - 2) * Mathf.Clamp(fraction, 0, 1), rect.Size.Y - 2)), colour);
    }

    private void Text(Vector2 position, string text, Color colour, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        float width = align == HorizontalAlignment.Left ? -1 : 400;
        var at = align switch
        {
            HorizontalAlignment.Center => position - new Vector2(200, 0),
            HorizontalAlignment.Right => position - new Vector2(400, 0),
            _ => position,
        };
        DrawString(_font, at, text, align, width, FontSize, colour);
    }

    private static bool OnScreen(Vector2 p, Vector2 size, float margin) =>
        p.X >= margin && p.Y >= margin && p.X <= size.X - margin && p.Y <= size.Y - margin;
}
