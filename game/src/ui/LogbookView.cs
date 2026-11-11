using Godot;
using Galaxus.Core;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Lore;

namespace Galaxus.Ui;

/// <summary>
/// The logbook: the news (filtered by nearby, your jobs or unread, each item linked to the map),
/// the lore you've been told, and your clues, leads and finds. Used in the flight overlay (O) and
/// on the station's Logbook tab. Quick to skim on the autopilot.
/// </summary>
public partial class LogbookView : VBoxContainer
{
    private readonly Session _session;
    private readonly Action<int> _showOnMap;
    private TabContainer _tabs = null!;
    private RichTextLabel _news = null!, _lore = null!, _clues = null!;
    private OptionButton _filter = null!;

    public LogbookView(Session session, Action<int> showOnMap)
    {
        _session = session;
        _showOnMap = showOnMap;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    public override void _Ready()
    {
        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(_tabs);

        var news = new VBoxContainer { Name = "News" };
        _tabs.AddChild(news);
        var bar = new HBoxContainer();
        news.AddChild(bar);
        bar.AddChild(Ui.Label("Show:", Ui.Dim));
        _filter = new OptionButton();
        foreach (var name in new[] { "Everything", "Within four jumps", "Where your jobs are", "Unread" }) _filter.AddItem(name);
        _filter.ItemSelected += _ => Refresh();
        bar.AddChild(_filter);
        _news = Text();
        news.AddChild(_news);

        _lore = Text();
        _lore.Name = "Lore";
        _tabs.AddChild(_lore);
        _clues = Text();
        _clues.Name = "Clues and finds";
        _tabs.AddChild(_clues);
        _tabs.TabChanged += _ => MarkRead();
    }

    private RichTextLabel Text()
    {
        var text = Ui.Text();
        text.SizeFlagsVertical = SizeFlags.ExpandFill;
        text.MetaClicked += meta => { if (int.TryParse(meta.AsString(), out int id)) _showOnMap(id); };
        return text;
    }

    /// <summary>Counts things not yet read, for the tab names and the HUD.</summary>
    public static int UnreadNews(Session session) => session.Galaxy.News.Count(n => n.Day > session.Career.NewsReadDay);

    /// <summary>Marks what's on the open tab as read.</summary>
    public void MarkRead()
    {
        if (_tabs.CurrentTab == 0) _session.Career.NewsReadDay = _session.Galaxy.Day;
        else _session.Career.LogbookSeen = _session.Career.LogbookCount;
        Titles();
    }

    private void Titles()
    {
        int news = UnreadNews(_session);
        int log = Math.Max(0, _session.Career.LogbookCount - _session.Career.LogbookSeen);
        _tabs.SetTabTitle(0, news > 0 ? $"News ({news} new)" : "News");
        _tabs.SetTabTitle(1, $"Lore ({_session.Career.Lore.Count})");
        _tabs.SetTabTitle(2, log > 0 ? $"Clues and finds ({log} new)" : "Clues and finds");
    }

    public void Refresh()
    {
        if (_tabs == null) return;
        var galaxy = _session.Galaxy;
        var career = _session.Career;
        string amber = Ui.Hex(Ui.Amber), dim = Ui.Hex(Ui.Dim);
        string Link(int id) => $"[url={id}]{galaxy.Systems[id].Name}[/url]";

        // News, newest first.
        var near = galaxy.WithinJumps(career.SystemId, 4).Select(s => s.Id).Append(career.SystemId).ToHashSet();
        var jobs = career.Missions.Select(m => m.Offer.DestinationSystemId).ToHashSet();
        var items = Enumerable.Reverse(galaxy.News).Where(n => _filter.Selected switch
        {
            1 => near.Contains(n.SystemId),
            2 => jobs.Contains(n.SystemId),
            3 => n.Day > career.NewsReadDay,
            _ => true,
        }).Take(80).ToList();
        var sb = new System.Text.StringBuilder();
        if (items.Count == 0) sb.Append("Nothing to show. News travels on ships, so it arrives late and patchy.");
        foreach (var n in items)
            sb.Append($"{(n.Day > career.NewsReadDay ? $"[color=#{amber}]NEW[/color] " : "")}[color=#{dim}]Day {n.Day}, [/color]{Link(n.SystemId)}[color=#{dim}]:[/color] {n.Text}\n");
        _news.Text = sb.ToString();

        // Lore, by kind.
        sb.Clear();
        if (career.Lore.Count == 0) sb.Append("Nothing yet. People in station bars tell stories once they like you: buy a round and ask what's new.");
        foreach (var group in career.Lore.Where(l => _session.Db.Lore.ContainsKey(l.Id)).GroupBy(l => _session.Db.Lore[l.Id].Category).OrderBy(g => g.Key))
        {
            sb.Append($"[color=#{amber}]{group.Key.ToUpperInvariant()}[/color]\n\n");
            foreach (var record in group)
            {
                var def = _session.Db.Lore[record.Id];
                sb.Append($"[color=#{amber}]{def.Title}[/color]  [color=#{dim}]heard from {record.From} at {Link(record.SystemId)}, day {record.Day}[/color]\n{def.Text}\n\n");
            }
        }
        _lore.Text = sb.ToString();

        // Clue chains, then leads, then finds.
        sb.Clear();
        var chains = _session.Db.Clues.Values.Where(c => career.Clues.ContainsKey(c.Id)).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        if (chains.Count > 0)
        {
            sb.Append($"[color=#{amber}]TREASURE HUNTS[/color]\n\n");
            foreach (var clue in chains)
            {
                int known = career.Clues[clue.Id];
                var target = Tales.Target(_session, clue);
                string state = known switch
                {
                    1 => "You know the region. Two more pieces would pin it down.",
                    2 when target is { } t => $"You know the system: {Link(t.System.Id)}. One more piece names the body.",
                    _ when target is { } t2 && career.Trails.Any(tr => tr.CacheId == clue.Payoff) => $"You know where it is. Go to {Link(t2.System.Id)} and scan it.",
                    _ => "Found.",
                };
                sb.Append($"[color=#{amber}]{clue.Title}[/color] ({known} of 3)  {state}\n");
                for (int i = 0; i < known; i++) sb.Append($"[color=#{dim}]{i + 1}.[/color] {Tales.Fragment(_session, clue, i)}\n");
                sb.Append('\n');
            }
        }
        if (career.Trails.Count > 0)
        {
            sb.Append($"[color=#{amber}]LEADS[/color]\n");
            foreach (var trail in career.Trails)
            {
                var system = galaxy.Systems[trail.SystemId];
                var solar = SolarSystems.For(system, _session.Db.Grammars);
                string body = trail.Body >= 0 && trail.Body < solar.Bodies.Count ? solar.Bodies[trail.Body].Name : "the star";
                sb.Append($"Something waits on {body}, {Link(system.Id)}. Fly there and scan it (K).\n");
            }
            sb.Append('\n');
        }
        sb.Append($"[color=#{amber}]FINDS[/color]\n");
        if (career.Logbook.Count == 0) sb.Append("Nothing yet. Scan worlds, moons, asteroids and ships (K in flight); anything you find is written here.\n");
        foreach (var entry in Enumerable.Reverse(career.Logbook))
            sb.Append($"[color=#{amber}]{entry.Title}[/color]  [color=#{dim}]day {entry.Day}, {Link(entry.SystemId)}[/color]\n{entry.Text}\n\n");
        _clues.Text = sb.ToString();
        Titles();
    }
}
