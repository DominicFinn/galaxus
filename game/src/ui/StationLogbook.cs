using Godot;
using Galaxus.Core.Mining;

namespace Galaxus.Ui;

/// <summary>
/// The logbook tab: what you've found by scanning, the leads you're following, and the survey
/// data you're carrying, which any station buys.
/// </summary>
public partial class StationScreen
{
    private RichTextLabel _logbook = null!;
    private Button _sellSurvey = null!;

    private Control BuildLogbookTab()
    {
        var tab = new VBoxContainer { Name = "Logbook" };
        var actions = new HBoxContainer();
        tab.AddChild(actions);
        _sellSurvey = Ui.Button("Sell survey data", () =>
        {
            int paid = Scanning.SellSurvey(_session.Career);
            AudioManager.Instance?.Ui("paid");
            Refresh($"Sold your survey data for {paid:N0} cr.");
        });
        actions.AddChild(_sellSurvey);
        _logbook = Ui.Text();
        _logbook.SizeFlagsVertical = SizeFlags.ExpandFill;
        tab.AddChild(_logbook);
        return tab;
    }

    private void RefreshLogbook()
    {
        var career = _session.Career;
        var galaxy = _session.Galaxy;
        int worth = Scanning.SurveyWorth(career);
        _sellSurvey.Disabled = worth == 0;
        _sellSurvey.Text = worth == 0 ? "No survey data to sell" : $"Sell survey data: {career.SurveyData.Count} scans, {worth:N0} cr";

        var text = new System.Text.StringBuilder();
        string amber = Ui.Hex(Ui.Amber);
        if (career.Trails.Count > 0)
        {
            text.Append($"[color=#{amber}]LEADS[/color]\n");
            foreach (var trail in career.Trails)
            {
                var system = galaxy.Systems[trail.SystemId];
                var solar = Galaxus.Core.Galaxy.SolarSystems.For(system, _session.Db.Grammars);
                string body = trail.Body >= 0 && trail.Body < solar.Bodies.Count ? solar.Bodies[trail.Body].Name : "the star";
                int jumps = galaxy.Jumps(career.SystemId, system.Id);
                text.Append($"Something waits on {body}, {system.Name}" + (system.Id == career.SystemId ? " (this system)" : jumps < 64 ? $" ({jumps} gates away)" : "") + ". Scan it to find out.\n");
            }
            text.Append('\n');
        }
        text.Append($"[color=#{amber}]FINDS[/color]\n");
        if (career.Logbook.Count == 0)
            text.Append("Nothing yet. Scan worlds, moons, asteroids and ships (K in flight) and anything you find is written here.\n");
        foreach (var entry in Enumerable.Reverse(career.Logbook))
            text.Append($"[color=#{amber}]Day {entry.Day}, {galaxy.Systems[entry.SystemId].Name}: {entry.Title}[/color]\n{entry.Text}\n\n");
        _logbook.Text = text.ToString();
    }
}
