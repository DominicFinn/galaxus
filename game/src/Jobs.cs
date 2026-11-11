using Godot;
using Galaxus.Core;

namespace Galaxus;

/// <summary>Following a job in flight: the HUD line, switching jobs, and a course to the job's port.</summary>
public partial class Main
{

    private double _jobHudRefresh;

    /// <summary>Updates the HUD's job lines about twice a second (working out the next step isn't free).</summary>
    private void UpdateJobHud(double delta)
    {
        _jobHudRefresh -= delta;
        if (_jobHudRefresh > 0) return;
        _jobHudRefresh = 0.5;
        if (_session.Career.ActiveJob is { } job)
        {
            _hud.JobTitle = $"{job.Offer.Mission.Title} ({_session.Career.Missions.Count} in hand)";
            _hud.JobStep = _battle is { Won: null } && job == _battleJob
                ? "In battle: complete the objectives. T picks the nearest enemy."
                : _session.NextStep(job);
        }
        else
        {
            _hud.JobTitle = "";
            _hud.JobStep = "";
        }
    }

    private void CycleJob()
    {
        var jobs = _session.Career.Missions.OrderBy(m => m.DeadlineDay).ToList();
        if (jobs.Count == 0) { _hud.Message("NO JOBS. DOCK AND CHECK THE JOBS BOARD"); return; }
        int index = jobs.IndexOf(_session.Career.ActiveJob!);
        var next = jobs[(index + 1) % jobs.Count];
        _session.Career.ActiveJobId = next.Offer.Id;
        _jobHudRefresh = 0;
        _hud.Message($"FOLLOWING: {next.Offer.Mission.Title.ToUpperInvariant()}");
        CourseForJob();
    }

    /// <summary>If the active job ends at a surface port in this system, set a course for it.</summary>
    private void CourseForJob()
    {
        // In a campaign with nothing in hand, the next stop is the flagship.
        if (_flagship != null && Campaigns.OrdersInHand(_session) == null
            && Galaxus.Core.Ships.World.DistanceToShip(_flagship, _player.Body.Position) > 3000)
        {
            _navTarget = NavFleet;
            _hud.Message($"DESTINATION: THE {Campaigns.Current(_session)!.BaseName.ToUpperInvariant()}. J FOR AUTOPILOT");
            return;
        }
        if (_session.Career.ActiveJob is not { } job || job.Offer.DestinationSystemId != _session.Career.SystemId) return;
        if (job.Offer.BattleId != null)
        {
            if (_battle != null) return;
            _navTarget = NavBattle;
            _hud.Message("DESTINATION: THE FIGHTING. J FOR AUTOPILOT");
            return;
        }
        // Work to do first: the world to scan, or the belt to prospect or mine.
        if (!_session.Career.TaskDone(job.Offer) && job.Offer.Task?.Split(':') is { } task)
        {
            int target = task[0] == "scan" ? (int.Parse(task[1]) is var b && b >= 0 ? b : NavStar) : Galaxus.Core.Mining.Asteroids.BeltIndex(_solar);
            if (target == -1) return;
            _navTarget = target;
            _hud.Message($"DESTINATION FOR YOUR JOB: {NavName.ToUpperInvariant()}. J FOR AUTOPILOT");
            return;
        }
        int index = _solar.Stations.FindIndex(s => s.Surface && s.Name == job.Offer.DestinationStation);
        if (index < 0 || _dockedPort?.Name == job.Offer.DestinationStation) return;
        _navTarget = NavPortBase - index;
        _hud.Message($"DESTINATION FOR YOUR JOB: {job.Offer.DestinationStation.ToUpperInvariant()}. J FOR AUTOPILOT");
    }
}
