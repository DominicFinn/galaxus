using Godot;
using Galaxus.Core;
using Galaxus.Core.Battles;
using Galaxus.Core.Missions;

namespace Galaxus;

/// <summary>
/// Battle previews: pick a set piece from the new game screen and start right in it, in a good
/// ship. When it's over you see how it went and can fly it again or go back to the menu.
/// </summary>
public partial class Main
{
    private static string? _pendingPreview;
    private static string? _cliPreview;
    private string? _previewBattle;
    private float _previewEndTimer = -1;

    /// <summary>Set pieces worth previewing: every battle except job and campaign fights.</summary>
    private IEnumerable<(string Id, string Name, string Summary)> PreviewBattles() =>
        _db.Battles.Values.Where(b => b.Kind is "war" or "pirate" or "synth" or "defence")
            .OrderBy(b => b.Kind switch { "war" => 0, "defence" => 1, "pirate" => 2, _ => 3 }).ThenByDescending(b => b.Reward)
            .Select(b => (b.Id, b.Name, Summary(b)));

    private string Summary(Galaxus.Core.Content.BattleDef b)
    {
        int ships = b.Waves.Sum(w => w.Ships.Sum(g => g.Count));
        int warships = b.Waves.Sum(w => w.Ships.Where(g => g.Class is "frigate" or "cruiser" or "battlecruiser" or "carrier").Sum(g => g.Count));
        string kind = b.Kind switch { "war" => "War", "pirate" => "Pirates", "synth" => "Rogue machines", _ => "Station defence" };
        return $"{kind}. About {ships} ships{(warships > 0 ? $", {warships} of them warships" : "")}. {b.Objectives.Count(o => !o.Optional)} objectives.";
    }

    private void PreviewRequested(string battleId)
    {
        _pendingPreview = battleId;
        Restart(PreviewSession(battleId));
    }

    /// <summary>A fresh galaxy with you at the friendly side's capital, in their best fighter.</summary>
    private Session PreviewSession(string battleId)
    {
        // Sides decide the ship you fly, so they're settled before the game starts.
        var session = Session.StartNew(_db, (ulong)DateTime.Now.Ticks);
        var (friend, _) = PreviewSides(session, _db.Battles[battleId]);
        var builder = BattleJobs.BuilderFor(_db, friend);
        // The best fighter that side's yard builds.
        var hull = _db.Hulls.Values.Where(h => h.Builder == builder && h.Class == "fighter" && h.ForSale).OrderByDescending(h => h.Price).FirstOrDefault()
                   ?? _db.Hulls.Values.Where(h => h.Class == "fighter" && h.ForSale).OrderByDescending(h => h.Price).First();
        session.Career.HullId = hull.Id;
        session.Career.SystemId = friend.HomeSystem;
        return session;
    }

    /// <summary>Who fights whom in a preview: two great powers for wars, a power against pirates or the Unbound otherwise.</summary>
    private static (Galaxus.Core.Galaxy.Faction Friend, int Enemy) PreviewSides(Session session, Galaxus.Core.Content.BattleDef def)
    {
        var galaxy = session.Galaxy;
        Galaxus.Core.Galaxy.Faction Power(string id) => galaxy.Factions.First(f => f.PowerId == id);
        return def.Kind switch
        {
            "war" => (Power("meridian-concord"), Power("grav-khanates").Id),
            "defence" => (Power("hold-league"), Power("grav-khanates").Id),
            "synth" => (Power("tolland-compact"), BattleJobs.RogueMachines),
            _ => (Power("hesper-directorate"), BattleJobs.Pirates),
        };
    }

    /// <summary>Starts the chosen set piece where you are, as a job so the HUD, radio and result all work as normal.</summary>
    private void StartPreviewBattle(string battleId)
    {
        if (!_db.Battles.TryGetValue(battleId, out var def)) return;
        _previewBattle = battleId;
        var galaxy = _session.Galaxy;
        var (friend, enemy) = PreviewSides(_session, def);
        string station = _session.Here.Stations.FirstOrDefault() ?? _session.Here.Name;
        var variables = new Dictionary<string, string>
        {
            ["client"] = friend.Name, ["enemy"] = BattleJobs.EnemyName(galaxy, enemy), ["system"] = _session.Here.Name,
            ["captain"] = _session.Career.Captain, ["station"] = station, ["base"] = "Resolute",
        };
        var mission = new Mission(BattleJobs.Template(def), def.Name, "", "", "", def.Reward, null, 0, 10, null, null, null, variables);
        var offer = new MissionOffer("preview-" + def.Id, mission, friend.Id, _session.Career.SystemId, _session.Career.SystemId, station, galaxy.Day, null, def.Id, enemy);
        var job = new AcceptedMission(offer, galaxy.Day, galaxy.Day + 10);
        _session.Career.Missions.Clear();
        _session.Career.Missions.Add(job);
        _session.Career.ActiveJobId = job.Offer.Id;
        // Out in clear space, away from the station and the planet.
        _player.Respawn(new System.Numerics.Vector3(0, 0, 9000), System.Numerics.Quaternion.Identity);
        StartBattle(job);
        _hud.Message($"BATTLE PREVIEW: {def.Name.ToUpperInvariant()}");
        _hud.ShowPerformance = OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--battle="));
        _demo = OS.GetCmdlineUserArgs().Contains("--demo");
    }

    /// <summary>After a preview battle ends, a short pause, then the result screen.</summary>
    private void StepPreview(double delta)
    {
        if (_previewBattle == null || _battle?.Won is not { } won) return;
        if (_previewEndTimer < 0) _previewEndTimer = 6;
        _previewEndTimer -= (float)delta;
        if (_previewEndTimer > 0) return;
        string id = _previewBattle;
        _previewBattle = null;
        _previewEndTimer = -1;
        SetScreen(Screen.Menu);
        _menu.ShowResult(won ? "VICTORY" : "DEFEAT",
            won ? $"{_db.Battles[id].Name}: won, with {_kills} kills to your name." : $"{_db.Battles[id].Name}: the battle was lost.",
            () => PreviewRequested(id), () => Restart(null));
    }
}
