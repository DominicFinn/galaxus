using Godot;
using Galaxus.Core.People;

namespace Galaxus.Ui;

/// <summary>The crew tab: who works for you, what they cost, what they add, and letting them go.</summary>
public partial class StationScreen
{
    private Tree _crew = null!;
    private RichTextLabel _crewText = null!;
    private Button _dismiss = null!;

    private Control BuildCrewTab()
    {
        var tab = new HBoxContainer { Name = "Crew" };
        _crew = Ui.Table(("Name", 0), ("Job", 60), ("Skill", 36), ("Wage", 44), ("Kills", 32));
        _crew.CustomMinimumSize = new Vector2(280, 0);
        _crew.ItemSelected += () => _dismiss.Disabled = false;
        tab.AddChild(_crew);
        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tab.AddChild(side);
        _crewText = Ui.Text();
        side.AddChild(_crewText);
        _dismiss = Ui.Button("Let them go", () =>
        {
            if (_crew.GetSelected() is not { } item) return;
            var member = _session.Career.Crew[item.GetMetadata(0).AsInt32()];
            CrewRules.Dismiss(_session.Career, member);
            Refresh($"{member.Name} has left your crew.");
        });
        side.AddChild(_dismiss);
        return tab;
    }

    private void RefreshCrew()
    {
        var career = _session.Career;
        _crew.Clear();
        var root = _crew.CreateItem();
        for (int i = 0; i < career.Crew.Count; i++)
        {
            var c = career.Crew[i];
            var item = _crew.CreateItem(root);
            item.SetText(0, c.Name + (c.Injured ? " (hurt)" : ""));
            item.SetText(1, CrewRules.JobName(c.Job));
            item.SetText(2, $"{c.Skill * 100:0}%");
            item.SetText(3, $"{c.Wage}/day");
            item.SetText(4, c.Job == "pilot" ? c.Kills.ToString() : "");
            item.SetMetadata(0, i);
        }
        var bonus = CrewRules.Bonus(career);
        int wages = career.Crew.Sum(c => c.Wage);
        int berths = CrewRules.Berths(_session);
        _crewText.Text = career.Crew.Count == 0
            ? $"Nobody works for you yet. Pilots and mercenaries in station bars will fly as wingmen in their own fighters (up to {CrewRules.MaxWingmen}); engineers, gunners and navigators take a berth aboard. Your ship has {berths} berth{(berths == 1 ? "" : "s")}. Look for the Hire button on the People tab."
            : $"Wages: {wages:N0} cr a day, paid when you dock. Anyone you can't pay leaves.\n" +
              $"Wingmen: {career.Crew.Count(c => c.Job == "pilot")} of {CrewRules.MaxWingmen}. Berths aboard: {career.Crew.Count(c => c.Job != "pilot")} of {berths}.\n\n" +
              $"Aboard, your crew add: shields recharge {(bonus.Shields - 1) * 100:0}% faster, guns hit {(bonus.Damage - 1) * 100:0}% harder, jump range {(bonus.JumpRange - 1) * 100:0}% further and direct jumps use {(1 - bonus.JumpFuel) * 100:0}% less fuel.\n\n" +
              "In flight: F5 sends your wing after your target, F6 has them defend you, F7 forms them up. They get better with every kill. A wingman shot down may not get out.";
        _dismiss.Disabled = true;
    }
}
