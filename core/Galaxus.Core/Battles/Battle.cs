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

/// <summary>Where to pick a lost battle up again: the enemy wave you were fighting, and what you'd already done.</summary>
/// <param name="WaveId">The enemy wave to fight again.</param>
/// <param name="Time">The battle clock when it arrived.</param>
/// <param name="Cleared">Enemy waves already beaten, which don't come back.</param>
/// <param name="Done">Objectives already done, by index, which stay done.</param>
public sealed record RetryPoint(string WaveId, float Time, IReadOnlyList<string> Cleared, IReadOnlyList<int> Done, int Attempt);

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
    private readonly Dictionary<string, float> _spawnedAt = [];
    private bool _wingmenOut;
    private string? _lostBecause;
    /// <summary>Which go at the battle this is: 0 first, then one more for each retry.</summary>
    public int Attempt { get; private set; }

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
    /// <summary>The difficulty: how dangerous the enemy is to you, how strong your side is, how long timers run.</summary>
    public Difficulty Difficulty { get; set; } = Difficulty.Hard;

    /// <summary>
    /// How much the second wind is helping you right now, 0 to 1. It rises when you're clearly
    /// losing and fades as you recover. Hidden from the player: it should feel like you pulled it back.
    /// </summary>
    public float SecondWind { get; private set; }

    /// <summary>How badly you seem to be losing right now, 0 to 1, from your health, the odds near you and your charges.</summary>
    public float Pressure { get; private set; }

    private float _sinceAssessed;
    private float _playerShieldBase = -1;

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
        if (!_wingmenOut) { _wingmenOut = true; SpawnWingmen(); }
        Time += dt;
        _lastDt = dt;
        foreach (var wave in Def.Waves)
            if (!_spawned.Contains(wave.Id) && Due(wave)) Spawn(wave);
        FlyCrews(dt);
        UpdateSecondWind(dt);
        CheckObjectives();
    }

    /// <summary>
    /// Works out twice a second how badly you're losing, and eases the enemies near you off while
    /// you are: slower, softer, less accurate fire, fewer of them on you, and quicker shields for you.
    /// It builds over a few seconds and fades more slowly, so it never flickers.
    /// </summary>
    private void UpdateSecondWind(float dt)
    {
        if (_playerShieldBase < 0) _playerShieldBase = _player.ShieldRechargeScale;
        _sinceAssessed += dt;
        if (_sinceAssessed >= 0.5f)
        {
            _sinceAssessed = 0;
            Pressure = AssessPressure();
        }
        float target = Pressure * Difficulty.SecondWind;
        // Builds quickly (a fight can turn in a few seconds), fades slowly.
        SecondWind = target > SecondWind ? MathF.Min(target, SecondWind + dt * 0.6f) : MathF.Max(target, SecondWind - dt * 0.05f);

        float ease = SecondWind;
        _player.ShieldRechargeScale = _playerShieldBase * (1 + ease);
        _player.ShieldDelayScale = 1 - 0.6f * ease;
        var me = _player.Body.Position;
        foreach (var (ship, pilot) in Crews)
        {
            if (ship.Side != EnemySide || !ship.Alive) continue;
            // The ones in your fight ease off most; the wider battle hardly changes.
            float near = Vector3.Distance(ship.Body.Position, me) < 4000 ? 1 : 0.3f;
            float e = ease * near;
            ship.FireRateScale = Difficulty.EnemyFireRate * (1 - 0.45f * e);
            ship.DamageDealtScale = Difficulty.EnemyDamage * (1 - 0.4f * e);
            ship.TurretAccuracy = Difficulty.TurretAccuracy * (1 - 0.5f * e);
            ship.TurretPlayerWeight = Difficulty.TurretPlayerWeight * (1 + 5 * e);
            ship.FlakDamageScale = Difficulty.FlakDamage * (1 - 0.5f * e);
            pilot.AimScale = Difficulty.EnemyAim * (1 - 0.6f * e);
            // When you're nearly done for, the ones near you leave you a moment to get clear.
            pilot.MaxOnPlayer = e > 0.75f ? 0 : e > 0.4f ? Math.Max(1, Math.Min(Difficulty.MaxAttackers, 3) - 1) : Difficulty.MaxAttackers;
        }
    }

    /// <summary>
    /// How badly you're losing, 0 to 1: low hull and shields count most, then being outnumbered near
    /// you, then a ship you're protecting close to going down.
    /// </summary>
    private float AssessPressure()
    {
        if (!_player.Alive) return 0;
        var spec = _player.Spec;
        float health = (_player.Hull + _player.Shields) / MathF.Max(1, spec.MaxHull + spec.MaxShields);
        // Starts to count once your shields are mostly gone, and is full when you're down to a third of your hull.
        float hurt = Math.Clamp((0.7f - health) / 0.45f, 0, 1);

        // The odds within 3 km: warships count for more than fighters.
        float enemies = 0, friends = 1;
        var me = _player.Body.Position;
        foreach (var ship in _world.Ships)
        {
            if (!ship.Alive || ship == _player || Vector3.Distance(ship.Body.Position, me) > 3000) continue;
            float weight = ship.Spec.Hull.IsCapital ? 3 : 1;
            if (ship.Side == EnemySide) enemies += weight;
            else if (ship.Side == FriendSide) friends += weight;
        }
        float outnumbered = Math.Clamp((enemies / friends - 1.2f) / 1.8f, 0, 1);

        // A ship you must protect, nearly gone.
        float charge = 0;
        foreach (var o in Objectives.Where(o => o.State == ObjectiveState.Pending && o.Def.Type is "protect" or "escape" && o.Def.Tag != null))
            foreach (var ward in _tags.GetValueOrDefault(o.Def.Tag!) ?? [])
                if (ward.Alive)
                    charge = MathF.Max(charge, Math.Clamp((0.35f - ward.Hull / MathF.Max(1, ward.Spec.MaxHull)) / 0.25f, 0, 1));

        // Being hurt matters most; being hurt and outnumbered is the clearest sign you're losing.
        return Math.Clamp(MathF.Max(hurt * (0.5f + 0.5f * outnumbered), charge * 0.7f), 0, 1);
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
        _spawnedAt[wave.Id] = Time;
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
            // Your side's fighter groups grow with the difficulty, so the fight stays even around you.
            int count = wave.Side == "friend" && !def.IsCapital && group.Role is not ("freighter" or "holding")
                ? (int)MathF.Round(group.Count * Difficulty.AllyScale) : group.Count;
            // Ships the objectives need alive (to protect, get away, scan or meet) are tougher on easier levels.
            bool protectedShip = group.Tag != null && Def.Objectives.Any(o => o.Tag == group.Tag && o.Type is "protect" or "escape" or "scan" or "rendezvous");
            for (int i = 0; i < count; i++)
            {
                var spec = ShipFactory.Build(_db, hull, null, race);
                string name = group.Name != null && i == 0 ? Fill(group.Name)
                    : group.Tag != null && _tagNames.TryGetValue(group.Tag, out var tagged) && i == 0 ? tagged
                    : def.IsCapital ? WarshipNames[_rng.Next(WarshipNames.Length)]
                    : $"{def.Name} {ships.Count + 1}";
                var ship = new Ship(Interlocked.Increment(ref _nextShipId), name, spec) { Side = side };
                if (wave.Side == "enemy")
                {
                    ship.FireRateScale = Difficulty.EnemyFireRate;
                    ship.DamageDealtScale = Difficulty.EnemyDamage;
                    ship.TurretAccuracy = Difficulty.TurretAccuracy;
                    ship.TurretPlayerWeight = Difficulty.TurretPlayerWeight;
                    ship.FlakDamageScale = Difficulty.FlakDamage;
                    if (def.IsCapital) ship.DamageTakenScale = 1 / Difficulty.EnemyWarshipToughness;
                }
                // Ships the objectives need alive are tougher on easier levels, whichever side they're on
                // (an enemy you must scan shouldn't be shot out from under you).
                if (protectedShip) ship.DamageTakenScale = 1 / Difficulty.ProtectedToughness;
                float spacing = def.IsCapital ? spec.Radius * 3 + 300 : MathF.Max(140, spec.Radius * 2.5f + 60);
                var offset = (_right * (i - (count - 1) / 2f) + _up * _rng.Range(-0.3f, 0.3f)) * spacing
                             + new Vector3(_rng.Range(-1, 1), _rng.Range(-1, 1), _rng.Range(-1, 1)) * (def.IsCapital ? 50 : 80);
                ship.Body.Position = ClearSpot(anchor + offset, spec.Radius);
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
                if (wave.Side == "enemy")
                {
                    pilot.Skill *= Difficulty.EnemySkill;
                    pilot.AimScale = Difficulty.EnemyAim;
                    pilot.MaxOnPlayer = Difficulty.MaxAttackers;
                }

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
    /// Moves a new ship off anyone it would spawn inside or right beside, so groups that share an
    /// arrival point don't pile up. It tries the spot first, then rings further and further out.
    /// </summary>
    private Vector3 ClearSpot(Vector3 spot, float radius)
    {
        bool Clear(Vector3 p) => !_world.Ships.Any(s => s.Alive
            && Vector3.Distance(s.Body.Position, p) < radius + s.Spec.Radius + 60);
        if (Clear(spot)) return spot;
        float step = radius * 2 + 120;
        for (int ring = 1; ring <= 12; ring++)
            for (int k = 0; k < 8; k++)
            {
                float a = MathF.Tau * k / 8 + ring * 0.4f;
                var p = spot + (_right * MathF.Cos(a) + _up * MathF.Sin(a) * 0.6f - _forward * (k % 2) * 0.5f) * step * ring;
                if (Clear(p)) return p;
            }
        return spot + _up * step * 13;
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
        if (enemies > 0) count = Math.Min(count + Difficulty.ExtraWingmen, 6);
        var hull = PickHull(_sides.FriendBuilder, "fighter", false);
        if (hull == null) return;
        string[] calls = ["Two", "Three", "Four", "Five", "Six", "Seven"];
        for (int i = 0; i < count; i++)
        {
            var spec = ShipFactory.Build(_db, hull, null, _sides.FriendRace);
            var ship = new Ship(Interlocked.Increment(ref _nextShipId), $"Wing {calls[i]}", spec) { Side = FriendSide };
            ship.Body.Position = ClearSpot(_player.Body.Position + (_right * (i % 2 == 0 ? 1 : -1) * (60 + 50 * (i / 2)) - _forward * (40 + 40 * (i / 2))), spec.Radius);
            ship.Body.Orientation = _player.Body.Orientation;
            ship.Body.Velocity = _player.Body.Velocity;
            var pilot = new Pilot(PilotRole.Escort, _rng.Fork($"wing-{i}")) { Assigned = _player, Player = _player, Skill = Difficulty.WingmanSkill };
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
    public float TimeLeft(BattleObjective o) => MathF.Max(0, SurviveSeconds(o) - Time);

    /// <summary>How long a survive objective runs at this difficulty.</summary>
    private float SurviveSeconds(BattleObjective o) => o.Def.Seconds * Difficulty.SurviveTime;

    /// <summary>Applies the difficulty to the player's ship: damage taken, aim assist, shields and missile lock.</summary>
    public static void ApplyToPlayer(Ship player, Difficulty difficulty)
    {
        player.IsPlayer = true;
        player.DamageTakenScale = difficulty.DamageTaken;
        player.AimAssistCone = difficulty.PlayerAimAssist;
        player.ShieldRechargeScale = difficulty.ShieldRecharge;
        player.LockSpeedScale = difficulty.LockSpeed;
    }

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
                    if (Time >= SurviveSeconds(o)) o.State = ObjectiveState.Done;
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
            var failed = Objectives.FirstOrDefault(o => !o.Def.Optional && o.State == ObjectiveState.Failed);
            _lostBecause = !_player.Alive ? "shot-down" : failed?.Def.Type switch { "protect" => "protect", "escape" => "escape", _ => "objective" };
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

    /// <summary>
    /// After a loss, where a retry would start: the earliest enemy wave still fighting, or the last
    /// one that came if they were all beaten. Null unless the battle is lost.
    /// </summary>
    public RetryPoint? Retry()
    {
        if (Won != false) return null;
        var enemyWaves = Def.Waves.Where(w => w.Side == "enemy" && _spawned.Contains(w.Id)).ToList();
        if (enemyWaves.Count == 0) return new RetryPoint("", 0, [], [], Attempt + 1);
        var fighting = enemyWaves.FirstOrDefault(w => _waves[w.Id].Any(s => s.Alive)) ?? enemyWaves[^1];
        var cleared = enemyWaves.TakeWhile(w => w != fighting).Select(w => w.Id).ToList();
        var done = Objectives.Select((o, i) => (o, i)).Where(x => x.o.State == ObjectiveState.Done).Select(x => x.i).ToList();
        return new RetryPoint(fighting.Id, _spawnedAt[fighting.Id], cleared, done, Attempt + 1);
    }

    /// <summary>Starts a fresh battle from a retry point: beaten waves stay beaten, finished objectives stay finished.</summary>
    public void ResumeFrom(RetryPoint point)
    {
        Attempt = point.Attempt;
        // A few seconds before the wave, to get your bearings.
        Time = MathF.Max(0, point.Time - 6);
        foreach (var id in point.Cleared)
        {
            _spawned.Add(id);
            _waves[id] = [];
            _spawnedAt[id] = 0;
        }
        foreach (int i in point.Done.Where(i => i < Objectives.Count)) Objectives[i].State = ObjectiveState.Done;
    }

    /// <summary>
    /// How close you came, and what to try next: objectives done, waves seen, enemies left, and a
    /// tip from what went wrong.
    /// </summary>
    public string Debrief()
    {
        var required = Objectives.Where(o => !o.Def.Optional).ToList();
        int done = required.Count(o => o.State == ObjectiveState.Done);
        int enemyWaves = Def.Waves.Count(w => w.Side == "enemy");
        int seen = Def.Waves.Count(w => w.Side == "enemy" && _spawned.Contains(w.Id));
        int left = EnemiesLeft();
        var lines = new List<string>();
        lines.Add(required.Count > 0 ? $"Objectives: {done} of {required.Count} done." : "");
        lines.Add(enemyWaves > 0 ? $"Enemy waves: {seen} of {enemyWaves} came in, {left} enemy ship{(left == 1 ? "" : "s")} still fighting." : "");
        float progress = (required.Count > 0 ? (float)done / required.Count : 0) * 0.5f + (enemyWaves > 0 ? (float)seen / enemyWaves : 0) * 0.5f;
        lines.Add(progress >= 0.7f ? "You were nearly there." : progress >= 0.4f ? "You got a good way in." : "It went wrong early.");
        lines.Add(_lostBecause switch
        {
            "shot-down" => "You were shot down. Keep power on your shields (2) in a crowd, and break away to recharge when they drop; warship flak hurts most up close.",
            "protect" => "A ship you were protecting went down. Stay near it, and take the fighters attacking it first.",
            "escape" => "The ships you were escorting didn't get away. Clear their path and stay with them.",
            "time" => "Time ran out. Go straight for the objectives and leave stragglers.",
            _ => "An objective failed. Read the objectives (top left) and take them in order.",
        });
        return string.Join(" ", lines.Where(l => l != ""));
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
