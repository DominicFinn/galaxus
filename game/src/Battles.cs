using Godot;
using Galaxus.Core;
using Galaxus.Core.Battles;
using Galaxus.Core.Ships;
using NVector3 = System.Numerics.Vector3;

namespace Galaxus;

/// <summary>
/// Set-piece battles in flight: the battle starts when you reach the fighting in the job's system,
/// its ships get visuals as they arrive, radio calls and objectives go to the HUD, and the job is
/// paid or failed when it's over.
/// </summary>
public partial class Main
{
    private Battle? _battle;
    private AcceptedMission? _battleJob;
    private float _battleCleanup;
    /// <summary>Course target for the battle point.</summary>
    private const int NavBattle = -3;
    /// <summary>The fighting starts when you're this close to the battle point.</summary>
    private const double BattleStartKm = 30;

    private static readonly Dictionary<string, ArrayMesh> HullMeshes = [];

    /// <summary>Ship meshes are built once per hull: warships have dozens of shapes and battles have dozens of ships.</summary>
    private static ArrayMesh MeshFor(Ship ship)
    {
        if (!HullMeshes.TryGetValue(ship.Spec.Hull.Id, out var mesh))
            HullMeshes[ship.Spec.Hull.Id] = mesh = MeshFactory.BuildShip(ship.Spec.Hull, ship.Spec.Builder.Palette);
        return mesh;
    }

    /// <summary>The battle job waiting in this system, if any.</summary>
    private AcceptedMission? BattleJobHere() =>
        _session.Career.Missions.Where(m => m.Offer.BattleId != null && m.Offer.DestinationSystemId == _session.Career.SystemId)
            .OrderBy(m => m == _session.Career.ActiveJob ? 0 : 1).FirstOrDefault();

    /// <summary>Where the fighting is: out from the station, in a direction fixed by the job, so it's the same each visit.</summary>
    private (double X, double Y, double Z) BattlePointKm(AcceptedMission job)
    {
        var rng = new Rng(Rng.Hash(job.Offer.Id));
        var dir = Normalised((rng.Range(-1, 1), rng.Range(-0.3f, 0.3f), rng.Range(-1, 1)));
        return (_stationKm.X + dir.X * 60, _stationKm.Y + dir.Y * 60, _stationKm.Z + dir.Z * 60);
    }

    private void CheckBattleStart()
    {
        if (_battle != null || _cruising || !_player.Alive || BattleJobHere() is not { } job) return;
        if (DistanceKm(PlayerKm, BattlePointKm(job)) > BattleStartKm) return;
        StartBattle(job);
    }

    private void StartBattle(AcceptedMission job)
    {
        var db = _session.Db;
        if (!db.Battles.TryGetValue(job.Offer.BattleId!, out var def)) return;
        var sides = BattleJobs.Sides(db, _session.Galaxy, job.Offer);
        var tags = Battle.NameTags(def, new Rng(Rng.Hash(job.Offer.Id + "tags")));
        var variables = new Dictionary<string, string>(job.Offer.Mission.Variables);
        // Ordinary jobs name the contact as the client; in battle radio it's the faction you fly for.
        variables["client"] = _session.Galaxy.Factions[job.Offer.GiverFactionId].Name;
        variables["enemy"] = sides.EnemyName;
        variables.TryAdd("system", _session.Here.Name);
        variables.TryAdd("station", _session.Here.Stations.FirstOrDefault() ?? _session.Here.Name);
        variables.TryAdd("captain", _session.Career.Captain);
        _battle = new Battle(db, def, _world, _player, sides, new Rng(Rng.Hash(job.Offer.Id + _session.Galaxy.Day)), variables, tags);
        _battle.Difficulty = GameSettings.Current.Level;
        Battle.ApplyToPlayer(_player, GameSettings.Current.Level); ApplyCrew();
        _battleJob = job;
        UpdateAudioForScreen();
        _battleCleanup = 0;
        SetWarp(1, "");
        _player.Target = null;
        ClearTraffic();
        ParkDrones();
        if (_navTarget == NavBattle) _navTarget = -1;
        _hud.Message($"{def.Name.ToUpperInvariant()}: {sides.EnemyName.ToUpperInvariant()} INBOUND");
        _hud.Objectives = _battle.Objectives;
        AudioManager.Instance?.Ui("lock");
    }

    private void ParkDrones()
    {
        foreach (var drone in _drones.Keys)
        {
            drone.Body.Position = new NVector3(0, 1e7f, 0);
            drone.Body.Velocity = NVector3.Zero;
        }
    }

    /// <summary>Runs one step of the battle, before the world moves.</summary>
    private void StepBattle(float dt)
    {
        if (_battle == null) return;
        _battle.Step(dt);
        foreach (var e in _battle.Events)
        {
            switch (e)
            {
                case RadioCall radio:
                    _hud.Radio(radio.Text, radio.Side);
                    break;
                case ObjectiveChanged changed:
                    _hud.Message(changed.Objective.State == ObjectiveState.Done
                        ? $"OBJECTIVE COMPLETE: {changed.Objective.Text.ToUpperInvariant()}"
                        : $"OBJECTIVE FAILED: {changed.Objective.Text.ToUpperInvariant()}");
                    AudioManager.Instance?.Ui(changed.Objective.State == ObjectiveState.Done ? "paid" : "error");
                    break;
                case BattleOver over:
                    FinishBattle(over);
                    break;
            }
        }
        foreach (var ship in _battle.Spawned)
            if (!_visuals.ContainsKey(ship)) AddVisual(ship);

        if (_battle.Won != null)
        {
            _battleCleanup += dt;
            if (_battleCleanup > 30) ClearBattle();
        }
    }

    private void FinishBattle(BattleOver over)
    {
        if (_battleJob == null || _battle == null) return;
        var result = _session.Career.FinishBattle(_battleJob, over.Won, _battle.BonusesEarned);
        Campaigns.Record(_session, _battleJob, over.Won);
        if (_session.RecordBattle(_battleJob, over.Won) is { } action && action.Won)
            _hud.Message("WORD OF THIS WILL SPREAD. CHECK THE NEWS WHEN YOU DOCK");
        if (Campaigns.Current(_session) != null && over.Won)
            _hud.Message(Campaigns.Finished(_session) ? "CAMPAIGN COMPLETE. REPORT TO THE FLAGSHIP" : "RETURN TO THE FLAGSHIP FOR YOUR DEBRIEF");
        _hud.Radio(over.Text, "friend");
        _hud.Message(over.Won
            ? $"VICTORY. +{result.Payment:N0} CR{(_battle.BonusesEarned > 0 ? $" WITH {_battle.BonusesEarned} BONUS" : "")}"
            : "THE BATTLE IS LOST. THE JOB HAS FAILED");
        AudioManager.Instance?.Ui(over.Won ? "paid" : "error");
        _battleJob = null;
        _jobHudRefresh = 0;
    }

    /// <summary>Removes the battle's ships once the dust has settled.</summary>
    private void ClearBattle()
    {
        if (_battle == null) return;
        foreach (var ship in _battle.Spawned)
        {
            _world.Ships.Remove(ship);
            if (_visuals.Remove(ship, out var visual)) visual.QueueFree();
            if (_player.Target == ship) _player.Target = null;
        }
        _hud.Objectives = null;
        _battle = null;
        UpdateAudioForScreen();
    }

    /// <summary>Everything worth pointing a gun at: enemies first, nearest first, then anyone else.</summary>
    private List<Ship> TargetCandidates() =>
        _world.Ships.Where(s => s != _player && s.Alive && NVector3.Distance(s.Body.Position, _player.Body.Position) < 30_000)
            .OrderBy(s => _world.Hostile(_player, s) ? 0 : 1)
            .ThenBy(s => NVector3.DistanceSquared(s.Body.Position, _player.Body.Position))
            .ToList();
}
