using System.Numerics;
using Galaxus.Core.Content;
using Galaxus.Core.Ships;

namespace Galaxus.Core.Battles;

/// <summary>Who is on each side of a battle, and whose ships they fly.</summary>
public sealed record BattleSides(
    string FriendName, string FriendBuilder, string FriendRace,
    string EnemyName, string EnemyBuilder, string EnemyRace,
    /// <summary>Rogue machines fly the "unbound" designs.</summary>
    bool EnemyUnbound = false);

public enum ObjectiveState { Pending, Done, Failed }

public sealed class BattleObjective(ObjectiveDef def)
{
    public ObjectiveDef Def { get; } = def;
    public ObjectiveState State { get; set; }
    public string Text { get; set; } = def.Text;
}

public abstract record BattleEvent;
public sealed record RadioCall(string Text, string Side) : BattleEvent;
public sealed record ObjectiveChanged(BattleObjective Objective) : BattleEvent;
public sealed record WaveArrived(string WaveId, string Side, int Ships) : BattleEvent;
public sealed record BattleOver(bool Won, string Text) : BattleEvent;
public sealed record ScanComplete(BattleObjective Objective, Ship Ship, int Done, int Of) : BattleEvent;

/// <summary>
/// A set-piece battle running in a World: spawns each wave when its time or trigger comes, flies
/// every AI ship, tracks the objectives and reports radio calls and the outcome. The player's
/// ship flies for the friend side.
/// </summary>
public sealed class Battle
{
    private static int _nextShipId = 100_000;
    private static readonly string[] WarshipNames =
    [
        "Resolute", "Vigilant", "Iron Vow", "Steadfast", "Long Reach", "Gallant", "Reckoner", "Sentinel", "Bright Oath",
        "Harrow", "Warden", "Unbending", "Dauntless", "Grey Tide", "Lodestar", "Indomitable", "Sable", "Meridian Star",
        "Last Word", "Old Patience", "Anvil", "Thunderhead", "Covenant", "Far Light", "Bulwark", "Severance", "Ember",
        "Quiet Hand", "Black Lantern", "Summit", "Torchbearer", "Hammerfall", "Undertow", "Stormcrow", "Cold Iron",
    ];

    private readonly ContentDatabase _db;
    private readonly World _world;
    private readonly Ship _player;
    private readonly Rng _rng;
    private readonly BattleSides _sides;
    private readonly Dictionary<string, string> _variables;
    private readonly Dictionary<string, string> _tagNames;
    private readonly Vector3 _centre, _forward, _right, _up;
    private readonly Dictionary<string, List<Ship>> _tags = [];
    private readonly Dictionary<string, List<Ship>> _waves = [];
    private readonly HashSet<string> _spawned = [];
    private readonly Dictionary<string, float> _triggeredAt = [];

    private float _lastDt;
    public BattleDef Def { get; }
    public int FriendSide { get; }
    public int EnemySide { get; }
    public int NeutralSide { get; }
    public float Time { get; private set; }
    public bool? Won { get; private set; }
    public List<BattleObjective> Objectives { get; }
    public List<(Ship Ship, Pilot Pilot)> Crews { get; } = [];
    public List<BattleEvent> Events { get; } = [];
    /// <summary>Every ship this battle has put into the world, so the game can give them visuals and clear them up.</summary>
    public List<Ship> Spawned { get; } = [];
    public string Name => Def.Name;

    /// <param name="variables">Grammar variables for radio and results: client, enemy, system, captain, station.</param>
    /// <param name="tagNames">Names for tagged warships ("flagship", "target"), fixed when the job was offered.</param>
    public Battle(ContentDatabase db, BattleDef def, World world, Ship player, BattleSides sides, Rng rng,
        IReadOnlyDictionary<string, string> variables, IReadOnlyDictionary<string, string>? tagNames = null)
    {
        _db = db;
        Def = def;
        _world = world;
        _player = player;
        _sides = sides;
        _rng = rng;
        _variables = new(variables);
        _tagNames = tagNames != null ? new(tagNames) : [];
        // A group with a fixed name gives its tag that name, so {flagship} reads right in the radio.
        foreach (var g in def.Waves.SelectMany(w => w.Ships).Where(g => g.Tag != null && g.Name != null))
            _tagNames[g.Tag!] = g.Name!.Replace("{base}", variables.GetValueOrDefault("base", g.Name!));
        foreach (var (tag, name) in _tagNames) _variables[tag] = name;

        FriendSide = player.Side;
        EnemySide = 9000 + rng.Next(900);
        NeutralSide = EnemySide + 1000;
        world.SetHostile(FriendSide, EnemySide);
        world.SetHostile(EnemySide, NeutralSide);

        _centre = player.Body.Position + player.Body.Forward * 1500;
        _forward = player.Body.Forward;
        _right = player.Body.Right;
        _up = player.Body.Up;
        Objectives = def.Objectives.Select(o => new BattleObjective(o)).ToList();
        foreach (var o in Objectives) o.Text = Fill(o.Def.Text);
    }

    /// <summary>Names for a battle's tagged warships, chosen when the job is offered so the briefing can use them.</summary>
    public static Dictionary<string, string> NameTags(BattleDef def, Rng rng)
    {
        var names = new Dictionary<string, string>();
        foreach (var tag in def.Waves.SelectMany(w => w.Ships).Select(g => g.Tag).Where(t => t != null).Distinct())
            names[tag!] = WarshipNames[rng.Next(WarshipNames.Length)];
        return names;
    }

    public void Step(float dt)
    {
        Events.Clear();
        if (Won != null) { FlyCrews(dt); return; }
        if (Time == 0) SpawnWingmen();
        Time += dt;
        _lastDt = dt;
        foreach (var wave in Def.Waves)
            if (!_spawned.Contains(wave.Id) && Due(wave)) Spawn(wave);
        FlyCrews(dt);
        CheckObjectives();
    }

    private void FlyCrews(float dt)
    {
        foreach (var (ship, pilot) in Crews)
            if (ship.Alive) pilot.Think(ship, _world, dt);
    }

    private bool Due(WaveDef wave)
    {
        if (wave.After == null) return Time >= wave.At;
        if (!_triggeredAt.TryGetValue(wave.After, out float at))
        {
            var parts = wave.After.Split(':');
            bool met = parts[0] == "objective"
                ? Objectives.Any(o => o.Def.Id == parts[1] && o.State == ObjectiveState.Done)
                : _spawned.Contains(parts[1]) && _waves[parts[1]].All(s => !s.Alive);
            if (!met) return false;
            _triggeredAt[wave.After] = at = Time;
        }
        return Time >= at + wave.Delay;
    }

    private void Spawn(WaveDef wave)
    {
        _spawned.Add(wave.Id);
        var ships = _waves[wave.Id] = [];
        int side = wave.Side switch { "friend" => FriendSide, "enemy" => EnemySide, _ => NeutralSide };
        string builder = wave.Side switch { "friend" => _sides.FriendBuilder, "enemy" => _sides.EnemyBuilder, _ => "human-corporate" };
        string race = wave.Side switch { "friend" => _sides.FriendRace, "enemy" => _sides.EnemyRace, _ => "human" };
        bool unbound = wave.Side == "enemy" && _sides.EnemyUnbound;

        var anchor = WhereFrom(wave);
        foreach (var group in wave.Ships)
        {
            var hull = group.Hull ?? PickHull(builder, group.Class ?? "fighter", unbound);
            if (hull == null) continue;
            var def = _db.Hulls[hull];
            for (int i = 0; i < group.Count; i++)
            {
                var spec = ShipFactory.Build(_db, hull, null, race);
                string name = group.Name != null && i == 0 ? Fill(group.Name)
                    : group.Tag != null && _tagNames.TryGetValue(group.Tag, out var tagged) && i == 0 ? tagged
                    : def.IsCapital ? WarshipNames[_rng.Next(WarshipNames.Length)]
                    : $"{def.Name} {ships.Count + 1}";
                var ship = new Ship(Interlocked.Increment(ref _nextShipId), name, spec) { Side = side };
                float spacing = def.IsCapital ? spec.Radius * 3 + 300 : 140;
                var offset = (_right * (i - (group.Count - 1) / 2f) + _up * _rng.Range(-0.3f, 0.3f)) * spacing
                             + new Vector3(_rng.Range(-1, 1), _rng.Range(-1, 1), _rng.Range(-1, 1)) * (def.IsCapital ? 50 : 80);
                ship.Body.Position = anchor + offset;
                // Enemies come in facing the fight; friends face the way the enemy will come from.
                var facing = wave.Side == "friend" ? _forward : Vector3.Normalize(_centre - ship.Body.Position);
                ship.Body.Orientation = LookRotation(facing, _up);
                ship.Body.Velocity = facing * spec.MaxSpeed * 0.5f;

                var pilot = new Pilot(RoleFor(group.Role), _rng.Fork($"pilot-{ship.Id}")) { Player = _player };
                pilot.Destination = group.Role == "freighter" ? Exit(anchor) : wave.Side == "friend" ? _centre + _forward * 2500 : _centre;
                if (group.Target != null && _tags.TryGetValue(group.Target, out var targets) && targets.Count > 0)
                    pilot.Assigned = targets[0];
                if (wave.Side == "enemy") pilot.Skill = 0.3f + 0.08f * Def.MinRank + _rng.NextFloat() * 0.2f;
                if (group.Skill is { } skill) pilot.Skill = skill;

                _world.Ships.Add(ship);
                Spawned.Add(ship);
                Crews.Add((ship, pilot));
                ships.Add(ship);
                if (group.Tag != null)
                {
                    if (!_tags.TryGetValue(group.Tag, out var list)) _tags[group.Tag] = list = [];
                    list.Add(ship);
                }
            }
        }
        // Groups that target a tag in their own wave (an escort for a flagship) get it now.
        foreach (var group in wave.Ships.Where(g => g.Target != null))
            foreach (var (ship, pilot) in Crews.Where(c => ships.Contains(c.Ship) && c.Pilot.Assigned == null))
                if (_tags.TryGetValue(group.Target!, out var t) && t.Count > 0 && RoleFor(group.Role) == pilot.Role) pilot.Assigned = t[0];

        Events.Add(new WaveArrived(wave.Id, wave.Side, ships.Count));
        if (wave.Radio != null) Events.Add(new RadioCall(Say(wave.Radio), wave.Side));
    }

    /// <summary>
    /// Nobody flies alone: a section of fighters joins you on your wing, more when the other side
    /// badly outnumbers yours, and they go for whatever comes after you.
    /// </summary>
    private void SpawnWingmen()
    {
        int enemies = Def.Waves.Where(w => w.Side == "enemy").Sum(w => w.Ships.Sum(g => g.Count));
        int friends = Def.Waves.Where(w => w.Side == "friend").Sum(w => w.Ships.Where(g => g.Role is not ("freighter" or "holding")).Sum(g => g.Count));
        int count = Def.Kind == "war" ? 1 : 2;
        count += Math.Clamp((enemies - friends * 2) / 4, 0, 4);
        if (enemies == 0) count = 0;
        if (Def.Wingmen >= 0) count = Math.Min(Def.Wingmen, 6);
        var hull = PickHull(_sides.FriendBuilder, "fighter", false);
        if (hull == null) return;
        string[] calls = ["Two", "Three", "Four", "Five", "Six", "Seven"];
        for (int i = 0; i < count; i++)
        {
            var spec = ShipFactory.Build(_db, hull, null, _sides.FriendRace);
            var ship = new Ship(Interlocked.Increment(ref _nextShipId), $"Wing {calls[i]}", spec) { Side = FriendSide };
            ship.Body.Position = _player.Body.Position + (_right * (i % 2 == 0 ? 1 : -1) * (60 + 50 * (i / 2)) - _forward * (40 + 40 * (i / 2)));
            ship.Body.Orientation = _player.Body.Orientation;
            ship.Body.Velocity = _player.Body.Velocity;
            var pilot = new Pilot(PilotRole.Escort, _rng.Fork($"wing-{i}")) { Assigned = _player, Player = _player, Skill = 0.65f };
            _world.Ships.Add(ship);
            Spawned.Add(ship);
            Crews.Add((ship, pilot));
        }
        if (count > 0) Events.Add(new RadioCall(count == 1 ? "Wing Two, on your wing." : $"Wing Two to Wing {calls[count - 1]}, formed up on you. We'll keep them off your tail.", "friend"));
    }

    private Vector3 WhereFrom(WaveDef wave) => _centre + wave.Position switch
    {
        "near" => (_right * _rng.Range(-1, 1) + _forward * _rng.Range(-0.5f, 0.5f)) * wave.Distance,
        "ahead" => _forward * wave.Distance,
        "behind" => -_forward * wave.Distance,
        "left" => -_right * wave.Distance,
        "right" => _right * wave.Distance,
        "above" => _up * wave.Distance,
        _ => _forward * wave.Distance * 2,
    };

    /// <summary>Where escaping ships head: far out on the opposite side from where they arrived.</summary>
    private Vector3 Exit(Vector3 from)
    {
        var away = from - _centre;
        if (away.LengthSquared() < 1) away = -_forward;
        return _centre - Vector3.Normalize(away) * 9000;
    }

    private string? PickHull(string builder, string cls, bool unbound)
    {
        bool Fits(HullDef h) => h.Class == cls && (cls == "drone" || h.Class != "drone") && (cls == "drone" || h.Id.StartsWith("fm-unbound") == unbound);
        var options = _db.Hulls.Values.Where(h => h.Builder == builder && Fits(h)).ToList();
        if (options.Count == 0) options = _db.Hulls.Values.Where(Fits).ToList();
        if (options.Count == 0 && unbound) options = _db.Hulls.Values.Where(h => h.Builder == builder && h.Class == cls).ToList();
        if (options.Count == 0)
        {
            // No such class here: step down to the next size that exists.
            string? smaller = cls switch { "carrier" or "battlecruiser" => "cruiser", "cruiser" => "frigate", "frigate" => "gunship", _ => null };
            return smaller == null ? null : PickHull(builder, smaller, unbound);
        }
        return options.OrderBy(h => h.Id, StringComparer.Ordinal).ElementAt(_rng.Next(options.Count)).Id;
    }

    private static PilotRole RoleFor(string role) => role switch
    {
        "bomber" => PilotRole.Bomber,
        "escort" => PilotRole.Escort,
        "capital" => PilotRole.Capital,
        "freighter" => PilotRole.Freighter,
        "interceptor" => PilotRole.Interceptor,
        "holding" => PilotRole.Holding,
        _ => PilotRole.Fighter,
    };

    private readonly Dictionary<(BattleObjective, Ship), float> _held = [];
    private readonly HashSet<(BattleObjective, Ship)> _scanned = [];

    /// <summary>
    /// Scans and rendezvous: time spent close to each marked ship counts up; scans need every
    /// marked ship, a rendezvous needs you matched in speed too. Losing an unscanned ship fails it.
    /// </summary>
    private void Progress(BattleObjective o, List<Ship> marks, float dt)
    {
        bool rendezvous = o.Def.Type == "rendezvous";
        foreach (var ship in marks)
        {
            var key = (o, ship);
            if (_scanned.Contains(key)) continue;
            if (!ship.Alive)
            {
                o.State = ObjectiveState.Failed;
                return;
            }
            bool close = World.DistanceToShip(ship, _player.Body.Position) <= o.Def.Distance;
            bool matched = !rendezvous || Vector3.Distance(ship.Body.Velocity, _player.Body.Velocity) < 15;
            float held = _held.GetValueOrDefault(key);
            held = close && matched ? held + dt : rendezvous ? MathF.Max(0, held - dt * 2) : held;
            _held[key] = held;
            if (held >= o.Def.Seconds)
            {
                _scanned.Add(key);
                Events.Add(new ScanComplete(o, ship, marks.Count(m => _scanned.Contains((o, m))), marks.Count));
            }
        }
        if (marks.All(m => _scanned.Contains((o, m)))) o.State = ObjectiveState.Done;
    }

    /// <summary>The ships an objective is about that still matter: alive, and not yet scanned for scans.</summary>
    public IEnumerable<Ship> TargetsOf(BattleObjective o)
    {
        var ships = o.Def.Tag != null ? _tags.GetValueOrDefault(o.Def.Tag) : o.Def.Wave != null ? _waves.GetValueOrDefault(o.Def.Wave) : null;
        if (ships == null) return [];
        return ships.Where(s => s.Alive && !_scanned.Contains((o, s)));
    }

    /// <summary>Seconds left on a survive objective.</summary>
    public float TimeLeft(BattleObjective o) => MathF.Max(0, o.Def.Seconds - Time);

    /// <summary>How far through a scan or rendezvous you are with the nearest marked ship, 0 to 1, or null when you're not at one.</summary>
    public float? HoldProgress(BattleObjective o) =>
        _tags.TryGetValue(o.Def.Tag ?? "", out var marks)
            ? marks.Where(m => !_scanned.Contains((o, m)) && World.DistanceToShip(m, _player.Body.Position) <= o.Def.Distance)
                .Select(m => (float?)(_held.GetValueOrDefault((o, m)) / o.Def.Seconds)).FirstOrDefault()
            : null;

    private void CheckObjectives()
    {
        foreach (var o in Objectives.Where(o => o.State == ObjectiveState.Pending))
        {
            var before = o.State;
            float dt = _lastDt;
            switch (o.Def.Type)
            {
                case "destroy":
                    var targets = o.Def.Tag != null ? _tags.GetValueOrDefault(o.Def.Tag) : o.Def.Wave != null ? _waves.GetValueOrDefault(o.Def.Wave) : null;
                    if (targets is { Count: > 0 } && targets.All(s => !s.Alive)) o.State = ObjectiveState.Done;
                    break;
                case "protect":
                    if (_tags.TryGetValue(o.Def.Tag!, out var ward) && ward.Count > 0 && ward.All(s => !s.Alive)) o.State = ObjectiveState.Failed;
                    break;
                case "survive":
                    if (Time >= o.Def.Seconds) o.State = ObjectiveState.Done;
                    break;
                case "scan":
                case "rendezvous":
                    if (_tags.TryGetValue(o.Def.Tag!, out var marks) && marks.Count > 0) Progress(o, marks, dt);
                    break;
                case "escape":
                    if (_tags.TryGetValue(o.Def.Tag!, out var runners) && runners.Count > 0)
                    {
                        var alive = runners.Where(s => s.Alive).ToList();
                        if (alive.Count == 0) o.State = ObjectiveState.Failed;
                        else if (alive.All(s => Crews.First(c => c.Ship == s).Pilot.Arrived)) o.State = ObjectiveState.Done;
                    }
                    break;
            }
            if (o.State != before) Events.Add(new ObjectiveChanged(o));
        }

        if (!_player.Alive || Objectives.Any(o => !o.Def.Optional && o.State == ObjectiveState.Failed))
        {
            Finish(false);
            return;
        }
        // Won once everything that isn't optional or a protect objective is done; protected ships
        // still alive at that point count as saved.
        var deciding = Objectives.Where(o => !o.Def.Optional && o.Def.Type != "protect").ToList();
        bool allWavesIn = Def.Waves.All(w => _spawned.Contains(w.Id) || w.Side != "enemy");
        if ((deciding.Count == 0 ? allWavesIn && EnemiesLeft() == 0 : deciding.All(o => o.State == ObjectiveState.Done)))
        {
            foreach (var o in Objectives.Where(o => o.Def.Type == "protect" && o.State == ObjectiveState.Pending))
            {
                o.State = ObjectiveState.Done;
                Events.Add(new ObjectiveChanged(o));
            }
            Finish(true);
        }
    }

    public int EnemiesLeft() => Crews.Count(c => c.Ship.Alive && c.Ship.Side == EnemySide);

    private void Finish(bool won)
    {
        Won = won;
        Events.Add(new BattleOver(won, Say(won ? "success" : "failure")));
        if (won)
            // The enemy breaks off: survivors turn for home.
            foreach (var (ship, pilot) in Crews.Where(c => c.Ship.Side == EnemySide && c.Ship.Alive))
            {
                ship.Target = null;
                pilot.Destination = Exit(ship.Body.Position);
            }
    }

    /// <summary>Optional objectives done, for a bonus.</summary>
    public int BonusesEarned => Objectives.Count(o => o.Def.Optional && o.State == ObjectiveState.Done);

    private string Say(string rule) =>
        Tidy(_db.Grammars.Expand(Def.TextGrammar, rule, _rng.Fork(rule + Time), _variables));

    private string Fill(string text)
    {
        foreach (var (k, v) in _variables) text = text.Replace("{" + k + "}", v);
        return Tidy(text);
    }

    /// <summary>Faction names carry their own "The", so "the {enemy}" would read "the The Grav Khanates".</summary>
    public static string Tidy(string text) => text.Replace("the The ", "the ").Replace("The The ", "The ");

    private static Quaternion LookRotation(Vector3 forward, Vector3 up)
    {
        var f = Vector3.Normalize(forward);
        var r = Vector3.Normalize(Vector3.Cross(up, -f));
        if (float.IsNaN(r.X)) r = Vector3.UnitX;
        var u = Vector3.Cross(-f, r);
        var m = new Matrix4x4(r.X, r.Y, r.Z, 0, u.X, u.Y, u.Z, 0, -f.X, -f.Y, -f.Z, 0, 0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }
}
