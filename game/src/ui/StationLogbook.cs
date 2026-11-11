using Godot;
using Galaxus.Core.Mining;

namespace Galaxus.Ui;

/// <summary>
/// The logbook tab: the same logbook as in flight (news, lore, clues and finds), plus selling the
/// survey data you're carrying, which any station buys.
/// </summary>
public partial class StationScreen
{
    private Button _sellSurvey = null!;
    private LogbookView _logbookView = null!;
    /// <summary>Opens the galaxy map on a system, for links in the logbook.</summary>
    public Action<int> ShowOnMap { get; set; } = _ => { };

    private Control BuildLogbookTab()
    {
        var tab = new VBoxContainer { Name = "Logbook" };
        _sellSurvey = Ui.Button("Sell survey data", () =>
        {
            int paid = Scanning.SellSurvey(_session.Career);
            AudioManager.Instance?.Ui("paid");
            Refresh($"Sold your survey data for {paid:N0} cr.");
        });
        tab.AddChild(_sellSurvey);
        _logbookView = new LogbookView(_session, id => ShowOnMap(id));
        tab.AddChild(_logbookView);
        return tab;
    }

    private void RefreshLogbook()
    {
        int worth = Scanning.SurveyWorth(_session.Career);
        _sellSurvey.Disabled = worth == 0;
        _sellSurvey.Text = worth == 0 ? "No survey data to sell" : $"Sell survey data: {_session.Career.SurveyData.Count} scans, {worth:N0} cr";
        _logbookView.Refresh();
    }
}
