using Godot;
using Galaxus.Core.Galaxy;

namespace Galaxus;

/// <summary>
/// A scripted tour for README screenshots: combat, cockpit, the galaxy map, contracts from
/// different races, market, shipyard and news. Run with "-- --showcase=FOLDER".
/// Each step sets the scene, waits some frames for it to settle, then saves a PNG.
/// </summary>
public partial class Main
{
    private sealed record ShowcaseStep(string File, int Frames, Action Setup);

    private string? _showcaseDir;
    private List<ShowcaseStep> _showcaseSteps = [];
    private int _showcaseIndex = -1, _showcaseWait;

    private void StartShowcase()
    {
        var galaxy = _session.Galaxy;
        _showcaseSteps =
        [
            new("flight-combat.png", 330, () => { GoTo(777); SetScreen(Screen.Flight); _demo = true; _cockpit = false; }),
            new("cockpit.png", 100, () => _cockpit = true),
            new("arrival.png", 30, () => { _cockpit = false; _demo = false; GoTo(_session.Career.SystemId == 300 ? 301 : 300); SetScreen(Screen.Flight); }),
            new("system-map.png", 20, () => OpenSystemMap()),
            new("cruise.png", 140, () =>
            {
                CloseSystemMap();
                // Head for the biggest planet that isn't the one we're beside.
                int target = Enumerable.Range(0, _solar.Bodies.Count)
                    .Where(i => !_solar.Bodies[i].IsMoon && _solar.Bodies[i].Kind != BodyKind.Belt && i != _stationBody)
                    .OrderByDescending(i => _solar.Bodies[i].Radius).FirstOrDefault(-1);
                if (target >= 0) SetCourse(target);
                ToggleCruise();
            }),
            new("planet-approach.png", 420, () => { }),
            new("galaxy-map.png", 20, () =>
            {
                _demo = false; _cockpit = false;
                OpenMap();
                _map.SetView(0.17f, System.Numerics.Vector2.Zero);
            }),
            new("map-route.png", 20, () =>
            {
                var target = galaxy.WithinJumps(_session.Career.SystemId, 7).LastOrDefault(s => s.Settled) ?? galaxy.WithinJumps(_session.Career.SystemId, 7).Last();
                _map.SetView(1.5f, (_session.Here.Position + target.Position) / 2, target.Id);
            }),
        ];

        foreach (var race in new[] { "orc", "light-elf", "dwarf", "free-machine", "dark-elf" })
        {
            var system = SystemWithJobFrom(race);
            if (system == null) continue;
            _showcaseSteps.Add(new($"contract-{race}.png", 25, () =>
            {
                GoTo(system.Id);
                Dock();
                _stationScreen.ShowTab(0);
                _stationScreen.SelectJobFrom(race);
            }));
        }

        var yard = galaxy.Systems.FirstOrDefault(s => s.Region == "core" && s.Settled) ?? _session.Here;
        _showcaseSteps.Add(new("market.png", 20, () => { GoTo(yard.Id); Dock(); _stationScreen.ShowTab(2); }));
        _showcaseSteps.Add(new("shipyard.png", 20, () =>
        {
            _stationScreen.ShowTab(3);
            foreach (var hull in new[] { "mc-tribune", "le-windsong", "hd-principal", "kestrel" })
                if (_stationScreen.SelectShip(hull)) break;
        }));
        _showcaseSteps.Add(new("news.png", 20, () => _stationScreen.ShowTab(5)));
        foreach (var race in new[] { "dwarf", "orc" })
        {
            var system = galaxy.Systems.FirstOrDefault(s => s.Stations.Count > 0 && s.Economy == Economy.Mining && galaxy.OwnerOf(s)?.RaceId == race);
            if (system == null) continue;
            _showcaseSteps.Add(new($"people-{race}.png", 25, () => { GoTo(system.Id); Dock(); _stationScreen.ShowTab(4); _stationScreen.SelectPerson(2);
                _stationScreen.TalkAbout(Galaxus.Core.People.Topic.Drink, Galaxus.Core.People.Topic.Rumour, Galaxus.Core.People.Topic.Self); }));
        }
        _showcaseIndex = -1;
        _showcaseWait = 0;
    }

    /// <summary>Moves the player to a system without passing time, for the tour.</summary>
    private void GoTo(int systemId)
    {
        if (_session.Career.SystemId == systemId) return;
        _session.Career.SystemId = systemId;
        EnterSystem();
    }

    private StarSystem? SystemWithJobFrom(string race)
    {
        foreach (var s in _session.Galaxy.Systems.Where(s => s.Stations.Count > 0 && _session.Galaxy.OwnerOf(s)?.RaceId == race))
        {
            var career = _session.Career.SystemId;
            _session.Career.SystemId = s.Id;
            bool found = _session.Board().Any(o => o.Playable && _session.Galaxy.Factions[o.GiverFactionId].RaceId == race);
            _session.Career.SystemId = career;
            if (found) return s;
        }
        return null;
    }

    /// <summary>Runs one frame of the tour. Returns true while it's running.</summary>
    private bool StepShowcase()
    {
        if (_showcaseDir == null) return false;
        if (_showcaseWait-- > 0) return true;

        if (_showcaseIndex >= 0)
        {
            var path = System.IO.Path.Combine(_showcaseDir, _showcaseSteps[_showcaseIndex].File);
            // Headless runs have no image; they still walk the steps, which checks they work.
            var image = GetViewport().GetTexture()?.GetImage();
            if (image != null) { image.SavePng(path); GD.Print($"Saved {path}"); }
        }
        _showcaseIndex++;
        if (_showcaseIndex >= _showcaseSteps.Count)
        {
            GetTree().Quit();
            _showcaseDir = null;
            return false;
        }
        _showcaseSteps[_showcaseIndex].Setup();
        _showcaseWait = _showcaseSteps[_showcaseIndex].Frames;
        return true;
    }
}
