using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Missions;

namespace Galaxus.Core;

public sealed record AcceptedMission(MissionOffer Offer, int AcceptedDay, int DeadlineDay);

public sealed record MissionResult(AcceptedMission Mission, bool Success, string Text, int Payment);

/// <summary>The player's captain: money, ship, location, jobs in hand and standing with each faction.</summary>
public sealed class Career
{
    public string Captain { get; set; } = "Ashby";
    public int Credits { get; set; } = 8000;
    public int SystemId { get; set; }
    public string HullId { get; set; } = "hc-sparrow";
    public List<string>? Loadout { get; set; }
    public List<AcceptedMission> Missions { get; } = [];
    /// <summary>The campaign you're flying, if you started in one, and how far through it you are.</summary>
    public string? CampaignId { get; set; }
    public int CampaignStep { get; set; }
    /// <summary>The system your campaign base is parked in.</summary>
    public int CampaignBase { get; set; } = -1;
    /// <summary>The debrief from the step just finished, shown when you next dock at the base.</summary>
    public string? CampaignDebrief { get; set; }
    /// <summary>The job you're working on, which the maps and HUD guide you to. Null picks the most urgent.</summary>
    public string? ActiveJobId { get; set; }

    /// <summary>The job you're working on: the one you chose, or else the one due soonest.</summary>
    public AcceptedMission? ActiveJob =>
        Missions.FirstOrDefault(m => m.Offer.Id == ActiveJobId) ?? Missions.OrderBy(m => m.DeadlineDay).FirstOrDefault();
    public HashSet<string> TakenOffers { get; } = [];
    /// <summary>Reputation per faction. Every 100 points is one rank, up to 4 (honoured).</summary>
    public Dictionary<int, int> Reputation { get; } = [];
    /// <summary>Trade goods in the hold, tonnes by commodity id.</summary>
    public Dictionary<string, int> Cargo { get; } = [];
    /// <summary>Ids of people you've met, so they can greet you as someone they know.</summary>
    public HashSet<string> MetPeople { get; } = [];
    /// <summary>How each person feels about you, -100 to 100, by person id.</summary>
    public Dictionary<string, int> Disposition { get; } = [];
    /// <summary>Day you last bought each person a drink.</summary>
    public Dictionary<string, int> LastDrink { get; } = [];
    /// <summary>Questions asked of each person today, as "day:count".</summary>
    public Dictionary<string, string> Questions { get; } = [];
    /// <summary>Criminal haunts you've been told about, as "factionId:systemId".</summary>
    public HashSet<string> KnownLeads { get; } = [];
    /// <summary>Jobs you've haggled over and the pay multiplier agreed (1 means refused).</summary>
    public Dictionary<string, float> Haggled { get; } = [];
    /// <summary>Jobs pulled after a haggle offended the client.</summary>
    public HashSet<string> WithdrawnOffers { get; } = [];

    public int AskedToday(string personId, int day) =>
        Questions.TryGetValue(personId, out var v) && v.Split(':') is [var d, var n] && int.Parse(d) == day ? int.Parse(n) : 0;

    public void Asked(string personId, int day) => Questions[personId] = $"{day}:{AskedToday(personId, day) + 1}";

    public int RankWith(int faction) => Math.Clamp(Reputation.GetValueOrDefault(faction) / 100, 0, 4);

    public static readonly string[] RankNames = ["Unknown", "Contractor", "Trusted", "Commissioned", "Honoured"];

    /// <summary>Cargo space: the hull's fixed hold plus fitted cargo racks.</summary>
    public int CargoCapacity(ContentDatabase db)
    {
        var hull = db.Hulls[HullId];
        var parts = (Loadout ?? hull.DefaultLoadout).Select(id => db.Parts[id]);
        return hull.CargoTonnes + (int)parts.Where(p => p.Type == "cargo").Sum(p => p.Stats.GetValueOrDefault("tonnes"));
    }

    public int CargoUsed => Missions.Sum(m => m.Offer.Mission.Tonnes) + Cargo.Values.Sum();

    public int FreeCargo(ContentDatabase db) => CargoCapacity(db) - CargoUsed;

    /// <summary>
    /// What you've bought from and sold to each market recently, by "systemId:commodityId". Markets
    /// recover, so the effect fades: see <see cref="TradeEffect"/>.
    /// </summary>
    public Dictionary<string, TradeMark> Trades { get; } = [];

    /// <summary>The start system's partner and goods for the dependable Easy trade loop.</summary>
    public Galaxy.TradeRoute? TradeRoute { get; set; }

    /// <summary>Tonnes you've taken from and put into a market, faded to the day.</summary>
    public (float Bought, float Sold) TradeEffect(int systemId, string commodityId, int day, float halfLife)
    {
        if (!Trades.TryGetValue($"{systemId}:{commodityId}", out var mark)) return (0, 0);
        float fade = MathF.Pow(0.5f, Math.Max(0, day - mark.Day) / halfLife);
        return (mark.Bought * fade, mark.Sold * fade);
    }

    /// <summary>Records a trade on top of what's left of earlier ones.</summary>
    public void RecordTrade(int systemId, string commodityId, int day, float halfLife, int bought, int sold)
    {
        var (b, s) = TradeEffect(systemId, commodityId, day, halfLife);
        Trades[$"{systemId}:{commodityId}"] = new TradeMark(b + bought, s + sold, day);
        // Forget markets you haven't touched for a month; they've long since recovered.
        foreach (var stale in Trades.Where(t => day - t.Value.Day > 30).Select(t => t.Key).ToList()) Trades.Remove(stale);
    }

    /// <summary>Why the ship can't be bought, or null if it can.</summary>
    public string? CannotBuyShip(ContentDatabase db, ShipOffer offer, int tradeIn)
    {
        var hull = offer.Hull;
        if (hull.Id == HullId) return "You already fly one.";
        if (offer.Price - tradeIn > Credits) return $"You need {offer.Price - tradeIn:N0} cr after trade-in.";
        int capacity = hull.CargoTonnes + (int)hull.DefaultLoadout.Select(id => db.Parts[id]).Where(p => p.Type == "cargo").Sum(p => p.Stats.GetValueOrDefault("tonnes"));
        if (capacity < CargoUsed) return $"Its hold is too small for your {CargoUsed} t of cargo.";
        return null;
    }

    public void BuyShip(ShipOffer offer, int tradeIn)
    {
        Credits -= offer.Price - tradeIn;
        HullId = offer.Hull.Id;
        Loadout = null;
    }

    /// <summary>Why the job can't be taken, or null if it can.</summary>
    public string? CannotAccept(ContentDatabase db, MissionOffer offer)
    {
        if (TakenOffers.Contains(offer.Id)) return "Already taken.";
        if (!offer.Playable) return "This kind of job comes in a later build.";
        if (offer.Mission.Tonnes > CargoCapacity(db) - CargoUsed) return $"Needs {offer.Mission.Tonnes} t of free cargo space.";
        if (Missions.Count >= 6) return "You already have six jobs.";
        return null;
    }

    public void Accept(Galaxy.Galaxy galaxy, MissionOffer offer)
    {
        TakenOffers.Add(offer.Id);
        if (offer.ClientId != null) MetPeople.Add(offer.ClientId);
        Missions.Add(new AcceptedMission(offer, galaxy.Day, galaxy.Day + offer.Mission.DeadlineDays));
        ActiveJobId ??= offer.Id;
    }

    /// <summary>A battle job ends with the battle: paid (with a bonus for optional objectives) or failed.</summary>
    public MissionResult FinishBattle(AcceptedMission job, bool won, int bonuses)
    {
        var mission = job.Offer.Mission;
        Missions.Remove(job);
        int pay = won ? mission.Reward + bonuses * mission.Reward / 5 : 0;
        Credits += pay;
        int faction = job.Offer.GiverFactionId;
        Reputation[faction] = Reputation.GetValueOrDefault(faction) + (won ? 40 + mission.Reward / 1000 : -20);
        if (ActiveJobId == job.Offer.Id) ActiveJobId = Missions.OrderBy(m => m.DeadlineDay).FirstOrDefault()?.Offer.Id;
        return new MissionResult(job, won, won ? mission.SuccessText : mission.FailureText, pay);
    }

    /// <summary>
    /// Pays out jobs for the station you've docked at, and fails any that ran out of time. With no
    /// station (in transit) only deadlines are checked. If a job's station has gone, any station in
    /// its system will do.
    /// </summary>
    /// <param name="sameStop">Whether docking here counts as reaching a given station; by default only the same name does.</param>
    public List<MissionResult> Settle(Galaxy.Galaxy galaxy, string? dockedAt = null, Func<string, bool>? sameStop = null)
    {
        var results = new List<MissionResult>();
        foreach (var job in Missions.ToList())
        {
            var mission = job.Offer.Mission;
            bool rightStation = dockedAt != null && ((sameStop?.Invoke(job.Offer.DestinationStation) ?? dockedAt == job.Offer.DestinationStation)
                                || !galaxy.Systems[SystemId].Stations.Contains(job.Offer.DestinationStation));
            if (job.Offer.BattleId == null && job.Offer.DestinationSystemId == SystemId && rightStation && galaxy.Day <= job.DeadlineDay)
            {
                Credits += mission.Reward;
                Reputation[job.Offer.GiverFactionId] = Reputation.GetValueOrDefault(job.Offer.GiverFactionId) + 20 + mission.Reward / 500;
                Missions.Remove(job);
                results.Add(new MissionResult(job, true, mission.SuccessText, mission.Reward));
            }
            else if (galaxy.Day > job.DeadlineDay)
            {
                Reputation[job.Offer.GiverFactionId] = Reputation.GetValueOrDefault(job.Offer.GiverFactionId) - 30;
                Missions.Remove(job);
                results.Add(new MissionResult(job, false, mission.FailureText, 0));
            }
        }
        if (ActiveJobId != null && Missions.All(m => m.Offer.Id != ActiveJobId)) ActiveJobId = Missions.OrderBy(m => m.DeadlineDay).FirstOrDefault()?.Offer.Id;
        return results;
    }
}

/// <summary>Tonnes bought from and sold to one market, as they stood on a day.</summary>
public sealed record TradeMark(float Bought, float Sold, int Day);
