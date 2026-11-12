using Godot;
using Galaxus.Core.Law;
using Galaxus.Core.Missions;
using Galaxus.Core.Ships;
using NVector3 = System.Numerics.Vector3;

namespace Galaxus;

/// <summary>
/// People coming after your passengers. Whether anyone comes is decided once as you arrive in a
/// system; if they do, they wait a while, take their turn on the comms, pull you out of cruise if
/// need be, and hail. Their ships shadow you while you talk. Pay, talk them round or outrun them and
/// they go; refuse or fail to outrun them and it's a fight, and beating them all settles it.
/// </summary>
public partial class Main
{
    /// <summary>Side for pursuers who aren't the police, so a fight with them doesn't involve anyone else.</summary>
    private const int PursuerSide = 980;

    /// <summary>Who's coming, before they arrive.</summary>
    private Interception? _interception;
    private float _interceptTimer;
    /// <summary>Who's here: their ships, and whether it's come to a fight.</summary>
    private Interception? _pursuit;
    private readonly List<Ship> _pursuers = [];
    private bool _pursuitHostile;

    /// <summary>A new system: someone may come for your passengers.</summary>
    private void ResetPursuit()
    {
        _pursuers.Clear();
        _pursuit = null;
        _pursuitHostile = false;
        _world.SetHostile(PursuerSide, _player.Side, false);
        _interception = _battle == null ? Passengers.OnArrival(_session) : null;
        _interceptTimer = 40 + _rng.NextFloat() * 50;
        if (OS.GetCmdlineUserArgs().Contains("--passenger-test")) _interceptTimer = Mathf.Min(_interceptTimer, 3);
    }

    private void StepPursuit(float dt)
    {
        if (_solar == null || !_player.Alive || _battle is { Won: null } || _screen != Screen.Flight) return;

        if (_interception is { } coming && !Hailing)
        {
            _interceptTimer -= dt;
            if (_interceptTimer > 0 || _comms.BackingOff("pursuit", _commsClock) || !_comms.TryCall("pursuit", _commsClock)) return;
            if (_cruising) DropFromCruise("PULLED OUT OF CRUISE");
            _interception = null;
            if (SpawnPursuers(coming) is not { } lead) { _comms.Ended(_commsClock); return; }
            _hud.Message($"INCOMING HAIL: {coming.Group.ToUpperInvariant()}");
            OpenHail(_callers[lead], lead);
            return;
        }

        if (_pursuit is not { } x) return;
        // They keep after you, while you talk and when it comes to a fight.
        foreach (var (ship, pilot, _) in _traffic)
            if (_pursuers.Contains(ship)) pilot.Destination = _player.Body.Position;
        if (_pursuitHostile && _pursuers.All(s => !s.Alive))
        {
            _hud.Message(Passengers.Fought(_session, x));
            AudioManager.Instance?.Ui("confirm");
            _pursuit = null;
            _pursuers.Clear();
            _pursuitHostile = false;
            _jobHudRefresh = 0;
        }
    }

    /// <summary>Their ships, three or four kilometres off and closing. Returns the one who does the talking.</summary>
    private Ship? SpawnPursuers(Interception x)
    {
        var hulls = Passengers.Hulls(_session, x);
        bool police = x.Pursuer.Side == "police";
        var faction = x.FactionId >= 0 ? _session.Galaxy.Factions[x.FactionId] : null;
        var dir = NVector3.Normalize(new NVector3(_rng.Range(-1, 1), _rng.Range(-0.2f, 0.2f), _rng.Range(-1, 1)));
        var side = Vector3Side(dir);
        for (int i = 0; i < hulls.Count; i++)
        {
            var hull = _db.Hulls[hulls[i]];
            string race = faction?.RaceId ?? _db.Builders[hull.Builder].Race;
            var at = _player.Body.Position + dir * 3500 + side * (i - (hulls.Count - 1) / 2f) * 260;
            var ship = AddTrafficShip(hull.Id, x.Group, police ? PilotRole.Patrol : PilotRole.Pirate, police ? LawfulSide : PursuerSide, race, at, _player.Body.Position);
            if (ship == null) continue;
            _pursuers.Add(ship);
            _hailed.Add(ship);
            _callers[ship] = new Caller("pursuer", x.Key, x.Group, hull.Id, x.FactionId, race);
        }
        if (_pursuers.Count == 0) return null;
        _pursuit = x;
        return _pursuers[0];
    }

    private static NVector3 Vector3Side(NVector3 dir) => NVector3.Normalize(NVector3.Cross(dir, NVector3.UnitY) + new NVector3(0.001f, 0, 0));

    /// <summary>What their answer means in flight.</summary>
    private void ApplyPursuitOutcome(HailOutcome outcome)
    {
        if (_pursuit is not { } x) return;
        switch (outcome)
        {
            case HailOutcome.Hostile:
                _pursuitHostile = true;
                if (x.Pursuer.Side == "police") { _world.SetHostile(LawfulSide, _player.Side); _hud.Message("THE POLICE ARE COMING FOR YOU"); }
                else _world.SetHostile(PursuerSide, _player.Side);
                AudioManager.Instance?.Ui("missile");
                break;
            case HailOutcome.Leave:
                // They peel away and leave you be.
                foreach (var (ship, pilot, _) in _traffic)
                    if (_pursuers.Contains(ship)) pilot.Destination = ship.Body.Position - NVector3.Normalize(_player.Body.Position - ship.Body.Position + NVector3.UnitX * 0.01f) * 40_000;
                if (x.JobId != null && _session.Career.Missions.All(m => m.Offer.Id != x.JobId)) _hud.Message($"{x.Passenger?.Name.ToUpperInvariant() ?? "PASSENGER"} HANDED OVER");
                _pursuit = null;
                _pursuers.Clear();
                break;
        }
        _jobHudRefresh = 0;
    }
}

/// <summary>
/// A launch option for checking passengers without a player: "-- --screen=flight --passenger-test"
/// puts a dodgy passenger with creditors after them aboard, arrives in a quiet system, waits for
/// the hail, refuses, and fights until the pursuers are beaten or time runs out. Logs and quits.
/// </summary>
public partial class Main
{
    private float _passengerTestTime;
    private int _passengerTestStage;
    private float _passengerTestFight;

    private void PassengerTestStart(Galaxus.Core.Session session)
    {
        var galaxy = session.Galaxy;
        var giver = galaxy.Factions.First(f => f.Alive && !f.Criminal);
        var context = new MissionContext(new FactionInfo(giver.Name, giver.TypeId, giver.RaceId), session.Here.Name, "Somewhere", session.Career.Captain);
        var mission = MissionGenerator.Build(session.Db, session.Db.Missions["no-questions-passage"], context, new Galaxus.Core.Rng(11));
        mission = mission with { Passenger = mission.Passenger! with { Look = "dodgy", Trouble = "debtor" } };
        var here = session.Here;
        var offer = new MissionOffer("passenger-test", mission, giver.Id, here.Id, here.Id, here.Stations.FirstOrDefault() ?? here.Name, galaxy.Day);
        session.Career.Missions.Add(new Galaxus.Core.AcceptedMission(offer, galaxy.Day, galaxy.Day + 30));
        session.Career.HullId = session.Db.Hulls.Values.Where(h => h.ForSale && h.Class == "gunship" && h.CrewBerths >= 1).OrderBy(h => h.Id).First().Id;
        session.Career.Loadout = null;
    }

    private void StepPassengerTest(float dt)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--passenger-test") || _solar == null || _screen != Screen.Flight) return;
        _passengerTestTime += dt;
        void Log(string text) => GD.Print($"PASSENGER TEST: t={_passengerTestTime:0}s {text}");
        void Done(string text) { Log(text); GetTree().Quit(); }
        if (_passengerTestTime > 240) { Done($"gave up at stage {_passengerTestStage}, pursuers alive {_pursuers.Count(s => s.Alive)}"); return; }
        var job = _session.Career.Missions.FirstOrDefault(m => m.Offer.Id == "passenger-test");
        switch (_passengerTestStage)
        {
            case 0:
                // Force the interception rather than rely on the dice.
                if (job != null && _interception == null && _pursuit == null) _interception = Passengers.Intercept(_session, job, _db.Pursuers["creditors"]);
                Log($"in {_session.Here.Name}; carrying {job?.Offer.Mission.Passenger?.Name} ({job?.Offer.Mission.Passenger?.Look}, {job?.Offer.Mission.Passenger?.Trouble}); coming: {_interception?.Group}");
                _passengerTestStage = 1;
                return;
            case 1:
                if (!Hailing) return;
                Log($"hailed by {_hailCaller!.ShipName}: {_hailPanel.Visible}. Options: {string.Join(" | ", Passengers.Open(_session, _pursuit!).Options.Select(o => o.Label))}");
                Log($"{_pursuers.Count} ships at {NVector3.Distance(_pursuers[0].Body.Position, _player.Body.Position):N0} m");
                OnHailChosen("px:fight");
                Log($"refused; hostile {_pursuitHostile}, world hostile {_world.Hostile(PursuerSide, _player.Side)}");
                OnHailChosen("close");
                _passengerTestFight = _passengerTestTime;
                _passengerTestStage = 2;
                return;
            case 2:
            {
                // Let them attack for a while, then finish them off.
                var alive = _pursuers.Where(s => s.Alive).ToList();
                if (alive.Count == 0 || _pursuit == null)
                {
                    Done($"pursuers beaten: outcome {_session.Career.PassengerOutcomes.GetValueOrDefault("passenger-test")}, grudges {_session.Career.Grudges.Count} ({string.Join(", ", _session.Career.Grudges.Select(g => g.Group))}), my shields and hull {_player.Shields:0}/{_player.Hull:0}");
                    return;
                }
                if ((int)_passengerTestTime % 4 == 0 && (int)(_passengerTestTime - dt) % 4 != 0)
                    Log($"{alive.Count} attacking, nearest {alive.Min(s => NVector3.Distance(s.Body.Position, _player.Body.Position)):N0} m, my shields and hull {_player.Shields:0}/{_player.Hull:0}");
                if (_passengerTestTime - _passengerTestFight > 30) foreach (var s in alive) s.TakeDamage(1_000_000);
                return;
            }
        }
    }
}
