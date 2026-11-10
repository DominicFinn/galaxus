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
            _hud.Message($"COURSE SET FOR THE {Campaigns.Current(_session)!.BaseName.ToUpperInvariant()}. J FOR AUTOPILOT");
            return;
        }
        if (_session.Career.ActiveJob is not { } job || job.Offer.DestinationSystemId != _session.Career.SystemId) return;
        if (job.Offer.BattleId != null)
        {
            if (_battle != null) return;
            _navTarget = NavBattle;
            _hud.Message("COURSE SET FOR THE FIGHTING. J FOR AUTOPILOT");
            return;
        }
        int index = _solar.Stations.FindIndex(s => s.Surface && s.Name == job.Offer.DestinationStation);
        if (index < 0 || _dockedPort?.Name == job.Offer.DestinationStation) return;
        _navTarget = NavPortBase - index;
        _hud.Message($"COURSE SET FOR YOUR JOB: {job.Offer.DestinationStation.ToUpperInvariant()}. J FOR AUTOPILOT");
    }
}
