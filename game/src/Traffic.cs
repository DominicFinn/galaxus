using Godot;
using Galaxus.Core;
using Galaxus.Core.Battles;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Ships;
using NVector3 = System.Numerics.Vector3;

namespace Galaxus;

/// <summary>
/// Everyday traffic: traders coming and going at the station, patrols, miners, and pirates where
/// the law is thin. Ships appear a few kilometres out and leave when they're done or far away, so
/// there's always life around you without simulating every ship in the galaxy.
/// </summary>
public partial class Main
{
    private readonly List<(Ship Ship, Pilot Pilot, bool Departing)> _traffic = [];
    private float _trafficTimer;
    /// <summary>Seconds since you last undocked: pirates leave new arrivals alone for a minute and a half.</summary>
    private float _sinceUndock = 999;
    private int _nextTrafficId = 300_000;
    /// <summary>Lawful ships (traders, patrols) share one side, so firing on a trader brings the police down on you too.</summary>
    private const int LawfulSide = 500, PirateSide = 900;

    /// <summary>Clears all traffic: on arriving in a system, engaging cruise, or when a battle starts.</summary>
    private void ClearTraffic()
    {
        foreach (var (ship, _, _) in _traffic)
        {
            _world.Ships.Remove(ship);
            if (_visuals.Remove(ship, out var visual)) visual.QueueFree();
            if (_player.Target == ship) _player.Target = null;
        }
        _traffic.Clear();
        _trafficTimer = 2;
        _world.SetHostile(PirateSide, _player.Side);
        _world.SetHostile(PirateSide, LawfulSide);
    }

    private void StepTraffic(float dt)
    {
        if (_cruising || _battle != null || !_player.Alive || _profile == null) return;
        foreach (var (ship, pilot, _) in _traffic)
            if (ship.Alive) pilot.Think(ship, _world, dt);

        // Ships that have arrived, died a while ago, or wandered off are taken away.
        for (int i = _traffic.Count - 1; i >= 0; i--)
        {
            var (ship, pilot, departing) = _traffic[i];
            float distance = NVector3.Distance(ship.Body.Position, _player.Body.Position);
            bool done = (pilot.Arrived && !departing) || distance > 30_000 || (!ship.Alive && distance > 2000);
            if (!done) continue;
            _world.Ships.Remove(ship);
            if (_visuals.Remove(ship, out var visual)) visual.QueueFree();
            if (_player.Target == ship) _player.Target = null;
            _traffic.RemoveAt(i);
        }

        _sinceUndock += dt;
        _trafficTimer -= dt;
        if (_trafficTimer > 0) return;
        _trafficTimer = 6 + _rng.NextFloat() * 8;
        bool nearStation = _stationNode != null && NVector3.Distance(_player.Body.Position, _stationLocal) < 25_000;
        int wanted = nearStation
            ? _profile.StationSize switch { StationSize.Outpost => 2, StationSize.Hub => 8, StationSize.Citadel => 11, _ => 5 }
            : 1;
        if (_traffic.Count(t => t.Ship.Alive) >= wanted) return;
        SpawnTraffic(nearStation);
    }

    private void SpawnTraffic(bool nearStation)
    {
        var db = _session.Db;
        var galaxy = _session.Galaxy;
        var owner = galaxy.OwnerOf(_session.Here);
        string security = _profile.Security;
        float pirateChance = security switch { "Lawless" => 0.45f, "Low" => 0.3f, "War zone" => 0.15f, "Medium" => 0.06f, _ => 0.02f };
        if (!nearStation) pirateChance *= 1.5f;
        if (_sinceUndock < 90) pirateChance = 0;
        float patrolChance = owner == null ? 0 : security switch { "High" => 0.3f, "War zone" => 0.35f, "Medium" => 0.2f, _ => 0.08f };

        string builder = owner != null ? BattleJobs.BuilderFor(db, owner) : "breakers-guild";
        string race = owner?.RaceId ?? "human";
        var roll = _rng.NextFloat();
        PilotRole role;
        string cls;
        int side = LawfulSide;
        if (roll < pirateChance)
        {
            role = PilotRole.Pirate;
            cls = _rng.NextFloat() < 0.7f ? "fighter" : "gunship";
            builder = "breakers-guild";
            race = "human";
            side = PirateSide;
        }
        else if (roll < pirateChance + patrolChance)
        {
            role = PilotRole.Patrol;
            cls = security == "War zone" && _rng.NextFloat() < 0.25f ? "frigate" : _rng.NextFloat() < 0.6f ? "fighter" : "gunship";
        }
        else
        {
            role = PilotRole.Freighter;
            cls = _rng.Pick(new[] { "freighter", "freighter", "courier", "miner" });
            // Traders come from all over, not just the owner's yards.
            if (_rng.NextFloat() < 0.4f) builder = _rng.Pick(db.Builders.Keys.Where(b => b != "ruin-courts").ToList());
        }

        var hulls = db.Hulls.Values.Where(h => h.Class == cls && !h.Id.StartsWith("fm-unbound") && (h.Builder == builder)).ToList();
        if (hulls.Count == 0) hulls = db.Hulls.Values.Where(h => h.Class == cls && !h.Id.StartsWith("fm-unbound")).ToList();
        if (hulls.Count == 0) return;
        var hull = _rng.Pick(hulls);
        var spec = ShipFactory.Build(db, hull.Id, null, race);
        string name = role == PilotRole.Pirate ? $"{_rng.Pick(PirateNames)} ({hull.Name})" : hull.IsCapital ? $"{hull.Name} patrol" : hull.Name;
        var ship = new Ship(++_nextTrafficId, name, spec) { Side = side };

        // Where it appears and where it's going.
        var randomDir = NVector3.Normalize(new NVector3(_rng.Range(-1, 1), _rng.Range(-0.3f, 0.3f), _rng.Range(-1, 1)));
        bool departing = false;
        var pilot = new Pilot(role, _rng.Fork($"traffic-{ship.Id}")) { Player = _player };
        if (role == PilotRole.Pirate) pilot.Skill *= GameSettings.Current.EnemySkill;
        _player.DamageTakenScale = GameSettings.Current.DamageTaken;
        if (role == PilotRole.Freighter && nearStation)
        {
            departing = _rng.NextFloat() < 0.5f;
            if (departing)
            {
                ship.Body.Position = _stationLocal + randomDir * 400 * StationScale;
                pilot.Destination = _stationLocal + randomDir * 40_000;
            }
            else
            {
                ship.Body.Position = _stationLocal + randomDir * _rng.Range(9_000, 14_000);
                pilot.Destination = _stationLocal;
            }
        }
        else
        {
            ship.Body.Position = _player.Body.Position + randomDir * _rng.Range(6_000, 11_000);
            pilot.Destination = role == PilotRole.Patrol && nearStation ? _stationLocal
                : role == PilotRole.Pirate ? _player.Body.Position
                : _player.Body.Position - randomDir * 40_000;
        }
        var heading = NVector3.Normalize(pilot.Destination - ship.Body.Position);
        ship.Body.Orientation = LookAlong(heading);
        ship.Body.Velocity = heading * spec.MaxSpeed * 0.5f;

        _world.Ships.Add(ship);
        _traffic.Add((ship, pilot, departing));
        AddVisual(ship);
        if (role == PilotRole.Pirate && NVector3.Distance(ship.Body.Position, _player.Body.Position) < 12_000)
            _hud.Message("CONTACT: UNKNOWN SHIP ON AN INTERCEPT COURSE");
    }

    private static readonly string[] PirateNames =
        ["Gutter Rat", "Bad Penny", "Last Laugh", "Rusty Knife", "Quick Pocket", "Bilge Saint", "Long Odds", "Sour Note", "Ninth Life", "Loose Rivet"];

    /// <summary>Bounties: lawful owners pay for pirates you destroy in their space.</summary>
    private void PayBounty(Ship ship)
    {
        if (ship.Side != PirateSide) return;
        var owner = _session.Galaxy.OwnerOf(_session.Here);
        if (owner == null || owner.Criminal) return;
        int bounty = 400 + (int)(ship.Spec.MaxHull * 3) / 100 * 100;
        _session.Career.Credits += bounty;
        _hud.Message($"BOUNTY: {bounty:N0} CR FROM {owner.Name.ToUpperInvariant()}");
        AudioManager.Instance?.Ui("paid");
    }

    private static System.Numerics.Quaternion LookAlong(NVector3 forward)
    {
        var basis = Basis.LookingAt(new Vector3(forward.X, forward.Y, forward.Z), Vector3.Up);
        var q = basis.GetRotationQuaternion();
        return new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W);
    }
}
