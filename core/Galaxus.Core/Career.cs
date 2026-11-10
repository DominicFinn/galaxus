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

    /// <summary>Selling gets a little less than the asking price.</summary>
    public const float SellFactor = 0.94f;

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

    /// <summary>Buys up to the requested tonnes at the local price. Returns tonnes bought.</summary>
    public int Buy(ContentDatabase db, Galaxy.Galaxy galaxy, string commodityId, int tonnes)
    {
        int price = Market.Price(db, galaxy, galaxy.Systems[SystemId], db.Commodities[commodityId]);
        int affordable = Math.Min(tonnes, Math.Min(FreeCargo(db), Credits / Math.Max(1, price)));
        if (affordable <= 0) return 0;
        Credits -= affordable * price;
        Cargo[commodityId] = Cargo.GetValueOrDefault(commodityId) + affordable;
        return affordable;
    }

    /// <summary>Sells up to the requested tonnes at the local price less the market's cut. Returns tonnes sold.</summary>
    public int Sell(ContentDatabase db, Galaxy.Galaxy galaxy, string commodityId, int tonnes)
    {
        int held = Cargo.GetValueOrDefault(commodityId);
        int sold = Math.Min(held, tonnes);
        if (sold <= 0) return 0;
        Credits += (int)(sold * Market.Price(db, galaxy, galaxy.Systems[SystemId], db.Commodities[commodityId]) * SellFactor);
        if (held == sold) Cargo.Remove(commodityId); else Cargo[commodityId] = held - sold;
        return sold;
    }

    /// <summary>Trade-in value of the current ship: 60% of its list price.</summary>
    public int TradeInValue(ContentDatabase db) => db.Hulls[HullId].Price * 6 / 10;

    /// <summary>Why the hull can't be bought, or null if it can.</summary>
    public string? CannotBuyShip(ContentDatabase db, HullDef hull)
    {
        if (hull.Id == HullId) return "You already fly one.";
        if (hull.Price - TradeInValue(db) > Credits) return $"You need {hull.Price - TradeInValue(db):N0} cr after trade-in.";
        int capacity = hull.CargoTonnes + (int)hull.DefaultLoadout.Select(id => db.Parts[id]).Where(p => p.Type == "cargo").Sum(p => p.Stats.GetValueOrDefault("tonnes"));
        if (capacity < CargoUsed) return $"Its hold is too small for your {CargoUsed} t of cargo.";
        return null;
    }

    public void BuyShip(ContentDatabase db, HullDef hull)
    {
        Credits -= hull.Price - TradeInValue(db);
        HullId = hull.Id;
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
            if (job.Offer.DestinationSystemId == SystemId && rightStation && galaxy.Day <= job.DeadlineDay)
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
