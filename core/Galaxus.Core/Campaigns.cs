using Galaxus.Core.Battles;
using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Missions;

namespace Galaxus.Core;

/// <summary>
/// Campaigns: starting enlisted with a great power, taking orders at your base (a flagship parked
/// with its fleet), flying each mission, and returning for a debrief and the next one. The same
/// galaxy carries on around you, and when the campaign ends you're free to go where you like.
/// </summary>
public static class Campaigns
{
    /// <summary>
    /// The faction a campaign flies for: its great power, or the largest faction of its faction
    /// type (the galaxy founds one if there's none).
    /// </summary>
    public static Faction Client(Galaxy.Galaxy galaxy, CampaignDef def)
    {
        if (def.FactionType == null) return galaxy.Factions.First(f => f.PowerId == def.Power);
        return galaxy.Factions.Where(f => f.TypeId == def.FactionType)
            .OrderByDescending(f => f.Alive).ThenByDescending(f => galaxy.OwnedBy(f.Id).Count()).ThenBy(f => f.Id).First();
    }

    public static Session Start(ContentDatabase db, ulong seed, string campaignId, GalaxySettings? settings = null)
    {
        var def = db.Campaigns[campaignId];
        var session = Session.StartNew(db, seed, settings);
        var galaxy = session.Galaxy;
        var career = session.Career;
        var power = Client(galaxy, def);
        // The fleet waits at the base landmark if there is one, else the client's capital, or the
        // nearest system of theirs with a station.
        StarSystem home;
        if (def.BaseLandmark != null && galaxy.Landmarks.TryGetValue(def.BaseLandmark, out int landmark))
            home = galaxy.Systems[landmark];
        else if (galaxy.Systems[power.HomeSystem].Stations.Count > 0)
            home = galaxy.Systems[power.HomeSystem];
        else
            home = galaxy.Systems.Where(s => s.Owner == power.Id && s.Stations.Count > 0).OrderBy(s => galaxy.DistanceLy(s.Id, power.HomeSystem)).FirstOrDefault()
                   ?? galaxy.Systems.Where(s => s.Stations.Count > 0).OrderBy(s => galaxy.DistanceLy(s.Id, power.HomeSystem)).First();
        career.SystemId = home.Id;
        career.TradeRoute = Market.PickRoute(db, galaxy, home.Id);
        career.CampaignBase = home.Id;
        career.CampaignId = def.Id;
        career.CampaignStep = 0;
        career.HullId = def.Hull;
        career.Loadout = null;
        career.Credits = def.Credits;
        career.Reputation[power.Id] = def.Rank * 100 + 50;
        career.CampaignDebrief = Say(db, def, "intro", session, new Rng(seed));
        return session;
    }

    public static CampaignDef? Current(Session session) =>
        session.Career.CampaignId is { } id && session.Db.Campaigns.TryGetValue(id, out var def) ? def : null;

    /// <summary>The step you're on, or null when the campaign is done (or you're not in one).</summary>
    public static CampaignStepDef? Step(Session session) =>
        Current(session) is { } def && session.Career.CampaignStep < def.Steps.Count ? def.Steps[session.Career.CampaignStep] : null;

    public static bool Finished(Session session) => Current(session) is { } def && session.Career.CampaignStep >= def.Steps.Count;

    /// <summary>The job for the current step, if you've taken your orders and not yet finished it.</summary>
    public static AcceptedMission? OrdersInHand(Session session) =>
        session.Career.Missions.FirstOrDefault(m => m.Offer.Id.StartsWith($"campaign-{session.Career.CampaignId}-"));

    /// <summary>Takes the orders for the current step: a battle job like any other, but paid and tracked by the campaign.</summary>
    public static AcceptedMission? TakeOrders(Session session)
    {
        if (Current(session) is not { } def || Step(session) is not { } step || OrdersInHand(session) != null) return null;
        var db = session.Db;
        var galaxy = session.Galaxy;
        var career = session.Career;
        var battle = db.Battles[step.Battle];
        var power = Client(galaxy, def);
        var rival = galaxy.Factions.FirstOrDefault(f => f.PowerId == def.Rival);
        int enemy = step.Enemy switch
        {
            "pirates" => BattleJobs.Pirates,
            "unbound" => BattleJobs.RogueMachines,
            _ => rival?.Id ?? BattleJobs.Pirates,
        };
        var where = Where(session, step, power, rival);
        string station = where.Stations.FirstOrDefault() ?? where.Name;
        string id = $"campaign-{def.Id}-{step.Id}";
        var tags = Battle.NameTags(battle, new Rng(Rng.Hash(id + "tags")));
        var variables = new Dictionary<string, string>
        {
            ["client"] = power.Name, ["enemy"] = BattleJobs.EnemyName(galaxy, enemy), ["system"] = where.Name,
            ["captain"] = career.Captain, ["station"] = station, ["base"] = def.BaseName,
        };
        foreach (var (tag, name) in tags) variables[tag] = name;
        string briefing = Say(db, def, step.Briefing, session, new Rng(Rng.Hash(id)), variables);
        var mission = new Mission(BattleJobs.Template(battle), step.Title, briefing,
            Battle.Tidy(db.Grammars.Expand(battle.TextGrammar, "success", new Rng(1), variables)),
            Battle.Tidy(db.Grammars.Expand(battle.TextGrammar, "failure", new Rng(2), variables)),
            step.Reward, null, 0, MissionBoard.TravelDays(galaxy, career.SystemId, where.Id) + 8, null, null, null, variables);
        var offer = new MissionOffer(id, mission, power.Id, career.SystemId, where.Id, station, galaxy.Day, null, battle.Id, enemy);
        var job = new AcceptedMission(offer, galaxy.Day, galaxy.Day + mission.DeadlineDays);
        career.Missions.Add(job);
        career.ActiveJobId = id;
        career.CampaignDebrief = null;
        return job;
    }

    /// <summary>After a campaign battle: on a win, move on and leave a debrief for the base. A loss can be flown again.</summary>
    public static void Record(Session session, AcceptedMission job, bool won)
    {
        if (Current(session) is not { } def || Step(session) is not { } step || job.Offer.Id != $"campaign-{def.Id}-{step.Id}") return;
        var variables = new Dictionary<string, string>(job.Offer.Mission.Variables);
        if (won)
        {
            session.Career.CampaignDebrief = Say(session.Db, def, step.Debrief, session, new Rng(Rng.Hash(job.Offer.Id + "debrief")), variables);
            session.Career.CampaignStep++;
            if (Finished(session))
                session.Career.CampaignDebrief += "\n\n" + Say(session.Db, def, "outro", session, new Rng(7), variables);
        }
        else
            session.Career.CampaignDebrief = "The mission failed. Report back when you're ready to try again; the orders still stand.";
    }

    /// <summary>Where a step takes place: the base system, a neighbour, or towards the rival's territory.</summary>
    private static StarSystem Where(Session session, CampaignStepDef step, Faction power, Faction? rival)
    {
        var galaxy = session.Galaxy;
        var home = galaxy.Systems[session.Career.CampaignBase >= 0 ? session.Career.CampaignBase : session.Career.SystemId];
        var candidates = galaxy.WithinJumps(home.Id, 4).Where(s => s.Stations.Count > 0 && s.Id != home.Id).ToList();
        var rng = new Rng(Rng.Hash($"{session.Career.CampaignId}-{step.Id}-where"));
        if (step.Where.StartsWith("landmark:") && galaxy.Landmarks.TryGetValue(step.Where["landmark:".Length..], out int marked))
            return galaxy.Systems[marked];
        return step.Where switch
        {
            "near" when candidates.Count > 0 => candidates.OrderBy(s => galaxy.DistanceLy(home.Id, s.Id)).ElementAt(rng.Next(Math.Min(3, candidates.Count))),
            "front" when candidates.Count > 0 && rival != null => candidates.OrderBy(s => galaxy.DistanceLy(s.Id, rival.HomeSystem)).First(),
            "front" when candidates.Count > 0 => candidates.OrderByDescending(s => galaxy.DistanceLy(home.Id, s.Id)).First(),
            _ => home,
        };
    }

    private static string Say(ContentDatabase db, CampaignDef def, string rule, Session session, Rng rng, Dictionary<string, string>? variables = null)
    {
        variables ??= new Dictionary<string, string>
        {
            ["client"] = Client(session.Galaxy, def).Name,
            ["captain"] = session.Career.Captain, ["base"] = def.BaseName,
            ["enemy"] = session.Galaxy.Factions.FirstOrDefault(f => f.PowerId == def.Rival)?.Name ?? "the enemy",
            ["system"] = session.Here.Name, ["station"] = session.Here.Stations.FirstOrDefault() ?? session.Here.Name,
        };
        return Battle.Tidy(db.Grammars.Expand(def.TextGrammar, rule, rng, variables));
    }
}
