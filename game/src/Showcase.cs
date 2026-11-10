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
            new("system-map-ports.png", 30, () =>
            {
                if (PortWorld() is { } port) GoTo(port.System.Id);
                SetScreen(Screen.Flight);
                OpenSystemMap();
            }),
            new("descent-high.png", 240, () =>
            {
                CloseSystemMap();
                if (PortWorld() is { } port) PlaceAbove(port.Body, SunlitSite(port.Body), 260, 35);
            }),
            new("descent-low.png", 300, () =>
            {
                if (PortWorld() is { } port) PlaceAbove(port.Body, SunlitSite(port.Body), 3, 30);
            }),
            new("port-approach.png", 300, () =>
            {
                if (PortWorld() is { } port) ApproachPort(port.Body, port.Site, 2.2, 0.7);
            }),
            new("citadel.png", 900, () =>
            {
                // A great power's capital: the citadel station, with traffic about it.
                var capital = galaxy.Factions.First(f => f.PowerId == "meridian-concord").HomeSystem;
                _holdStill = false;
                _demo = false;
                GoTo(capital);
                SetScreen(Screen.Flight);
                _player.Respawn(_stationLocal + new System.Numerics.Vector3(600, 400, 3200), System.Numerics.Quaternion.CreateFromYawPitchRoll(0.15f, -0.1f, 0));
                _holdStill = true;
                _trafficTimer = 0;
            }),
            new("battle-opening.png", 1500, () => StartShowcaseBattle("fleet-engagement")),
            new("battle-fleet.png", 1500, () => { _cockpit = false; }),
            new("battle-cockpit.png", 600, () => { _cockpit = true; }),
            new("campaign-fleet.png", 400, () =>
            {
                // A campaign start: out beside the flagship and its fleet.
                var id = _session.Db.Campaigns.Keys.OrderBy(k => k).FirstOrDefault();
                if (id == null) return;
                _demo = false;
                _holdStill = false;
                var campaign = _session.Db.Campaigns[id];
                _session.Career.CampaignId = id;
                _session.Career.CampaignStep = 0;
                _session.Career.CampaignBase = galaxy.Factions.First(f => f.PowerId == campaign.Power).HomeSystem;
                _session.Career.CampaignDebrief = "Showcase.";
                GoTo(_session.Career.CampaignBase);
                EnterSystem();
                SetScreen(Screen.Flight);
                if (_flagship != null)
                    _player.Respawn(_flagship.Body.Position + new System.Numerics.Vector3(-_flagship.Spec.HalfLength * 1.2f, _flagship.Spec.HalfLength * 0.3f, _flagship.Spec.HalfLength * 1.6f),
                        System.Numerics.Quaternion.CreateFromYawPitchRoll(-0.6f, -0.12f, 0));
                _holdStill = true;
            }),
            new("campaign-orders.png", 30, () =>
            {
                _holdStill = false;
                if (_flagship == null) return;
                _dockedAtFlagship = true;
                Dock();
                _stationScreen.ShowTab(7);
            }),
            new("galaxy-deep-stars.png", 20, () =>
            {
                _holdStill = false;
                OpenMap();
                _map.SetView(420f, _session.Here.Position);
            }),
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

    /// <summary>Ignores the controls, so a fixed view isn't steered by wherever the mouse happens to be.</summary>
    private bool _holdStill;

    private (StarSystem System, int Body, System.Numerics.Vector3 Site)? _portWorld;

    /// <summary>Hires you into a battle between two great powers, right here, with the demo pilot flying.</summary>
    private void StartShowcaseBattle(string battleId)
    {
        var galaxy = _session.Galaxy;
        if (!_session.Db.Battles.TryGetValue(battleId, out var def)) return;
        var friend = galaxy.Factions.First(f => f.PowerId == "meridian-concord");
        var enemy = galaxy.Factions.First(f => f.PowerId == "grav-khanates");
        _holdStill = false;
        _cockpit = false;
        GoTo(friend.HomeSystem);
        SetScreen(Screen.Flight);
        var station = _session.Here.Stations.FirstOrDefault() ?? _session.Here.Name;
        var variables = new Dictionary<string, string>
        {
            ["client"] = friend.Name, ["enemy"] = enemy.Name, ["system"] = _session.Here.Name,
            ["captain"] = _session.Career.Captain, ["station"] = station,
        };
        var mission = new Galaxus.Core.Missions.Mission(Galaxus.Core.Battles.BattleJobs.Template(def), def.Name, "", "", "", def.Reward,
            null, 0, 10, null, null, null, variables);
        var offer = new Galaxus.Core.Missions.MissionOffer("showcase-battle", mission, friend.Id, _session.Career.SystemId,
            _session.Career.SystemId, station, galaxy.Day, null, def.Id, enemy.Id);
        var job = new Galaxus.Core.AcceptedMission(offer, galaxy.Day, galaxy.Day + 10);
        _session.Career.Missions.Add(job);
        _player.Body.Position = new System.Numerics.Vector3(0, 0, 3000);
        StartBattle(job);
        _demo = true;
    }

    /// <summary>A system with a surface port on an Earth-like world, for the descent shots.</summary>
    private (StarSystem System, int Body, System.Numerics.Vector3 Site)? PortWorld()
    {
        if (_portWorld != null) return _portWorld;
        foreach (var s in _session.Galaxy.Systems.Where(s => s.Stations.Count > 1
                     && SolarSystems.StarFor(s.Seed).Class is StarClass.G or StarClass.F or StarClass.K))
        {
            var solar = SolarSystems.For(s, _db.Grammars);
            var port = solar.Stations.FirstOrDefault(p => p.Surface && solar.Bodies[p.Body].Kind == BodyKind.Terran)
                    ?? solar.Stations.FirstOrDefault(p => p.Surface && solar.Bodies[p.Body].Kind is BodyKind.Ocean or BodyKind.Desert);
            if (port != null) return _portWorld = (s, port.Body, port.Site);
        }
        return null;
    }

    /// <summary>A point on a world with the sun fairly low in the sky, so the ground shows its shape.</summary>
    private System.Numerics.Vector3 SunlitSite(int body)
    {
        var c = _solar.PositionKm(body, Day);
        var toStar = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3((float)-c.X, (float)-c.Y, (float)-c.Z));
        var side = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(toStar, System.Numerics.Vector3.UnitY));
        var start = System.Numerics.Vector3.Normalize(toStar + side * 1.1f + System.Numerics.Vector3.UnitY * 0.3f);
        // Look nearby for land with some height to it, rather than open sea.
        var terrain = new PlanetTerrain(_solar.Bodies[body]);
        var rng = new Galaxus.Core.Rng(9);
        for (int i = 0; i < 400; i++)
        {
            var dir = System.Numerics.Vector3.Normalize(start + new System.Numerics.Vector3(rng.Range(-1, 1), rng.Range(-1, 1), rng.Range(-1, 1)) * (0.05f + i * 0.002f));
            var sample = terrain.Sample(dir);
            if (!sample.Wet && sample.HeightKm > 0.8f && sample.Ground is not (Ground.Snow or Ground.Ice)) return dir;
        }
        return start;
    }

    /// <summary>Puts the ship a little way back from a surface port and above it, looking at the pad.</summary>
    private void ApproachPort(int body, System.Numerics.Vector3 site, double backKm, double heightKm)
    {
        _cruising = false;
        var terrain = TerrainOf(body);
        var port = _solar.Stations.First(s => s.Surface && s.Site == site);
        var pad = _solar.PortPositionKm(port, Day, terrain);
        var centre = _solar.PositionKm(body, Day);
        var up = System.Numerics.Vector3.Normalize(site);
        var along = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(up, System.Numerics.Vector3.UnitY));
        double r = _solar.RadiusKm(body);
        var startDir = System.Numerics.Vector3.Normalize(up * (float)r - along * (float)backKm);
        double ground = Math.Max(0, terrain.HeightKm(startDir));
        double k = r + Math.Max(ground, terrain.HeightKm(up)) + heightKm;
        _anchorKm = (centre.X + startDir.X * k, centre.Y + startDir.Y * k, centre.Z + startDir.Z * k);
        var forward = new Vector3((float)(pad.X - _anchorKm.X), (float)(pad.Y - _anchorKm.Y), (float)(pad.Z - _anchorKm.Z)).Normalized();
        var q = Basis.LookingAt(forward, new Vector3(startDir.X, startDir.Y, startDir.Z)).GetRotationQuaternion();
        _player.Respawn(System.Numerics.Vector3.Zero, new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W));
        _throttle = 0;
        _player.Target = null;
        _holdStill = true;
        foreach (var drone in _drones.Keys) drone.Body.Position = new System.Numerics.Vector3(0, 1e7f, 0);
    }

    /// <summary>Puts the ship above a point on a world, looking out towards the horizon.</summary>
    private void PlaceAbove(int body, System.Numerics.Vector3 site, double altitudeKm, float pitchDownDegrees)
    {
        _cruising = false;
        var centre = _solar.PositionKm(body, Day);
        var terrain = new PlanetTerrain(_solar.Bodies[body]);
        var sample = terrain.Sample(site);
        double r = _solar.RadiusKm(body) + (sample.Wet ? 0 : sample.HeightKm) + altitudeKm;
        _anchorKm = (centre.X + site.X * r, centre.Y + site.Y * r, centre.Z + site.Z * r);
        var up = new Vector3(site.X, site.Y, site.Z).Normalized();
        var along = up.Cross(Mathf.Abs(up.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
        var forward = along.Rotated(up.Cross(along).Normalized(), Mathf.DegToRad(pitchDownDegrees));
        var q = Basis.LookingAt(forward, up).GetRotationQuaternion();
        _player.Respawn(System.Numerics.Vector3.Zero, new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W));
        _throttle = 0;
        _player.Controls.Flight.Throttle = 0;
        _player.Target = null;
        _holdStill = true;
        foreach (var drone in _drones.Keys) drone.Body.Position = new System.Numerics.Vector3(0, 1e7f, 0);
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
