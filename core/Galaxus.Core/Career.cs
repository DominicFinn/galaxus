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
    /// <summary>Fuel in the tank, in light years of direct jumps. Below zero means full (a new ship or an older save).</summary>
    public float FuelLy { get; set; } = -1;
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
    /// <summary>Wingmen and crew working for you.</summary>
    public List<People.CrewMember> Crew { get; } = [];
    /// <summary>The day wages were last paid, or -1 before anyone was hired.</summary>
    public int WagesPaidDay { get; set; } = -1;
    /// <summary>Lore entries you've been told, in the order you heard them.</summary>
    public List<Galaxus.Core.Lore.LoreRecord> Lore { get; } = [];
    /// <summary>How many pieces of each clue chain you have, by chain id.</summary>
    public Dictionary<string, int> Clues { get; } = [];
    /// <summary>News up to this day has been read.</summary>
    public int NewsReadDay { get; set; } = -1;
    /// <summary>Logbook entries (finds, lore and clue pieces) you've seen, for the unread count.</summary>
    public int LogbookSeen { get; set; }
    /// <summary>Everything in the logbook that isn't news: finds, lore and clue pieces.</summary>
    public int LogbookCount => Logbook.Count + Lore.Count + Clues.Values.Sum();
    /// <summary>Crimes on your record, each with the faction it's held against and its fine.</summary>
    public List<Law.Offence> Offences { get; } = [];
    /// <summary>Tonnes cut from each asteroid, by rock id.</summary>
    public Dictionary<string, int> Mined { get; } = [];
    /// <summary>Everything you've scanned: bodies and rocks by system, ships by day.</summary>
    public HashSet<string> Scanned { get; } = [];
    /// <summary>What you've found by scanning, oldest first.</summary>
    public List<Mining.LogEntry> Logbook { get; } = [];
    /// <summary>Leads you're following to caches elsewhere.</summary>
    public List<Mining.Trail> Trails { get; } = [];
    /// <summary>Survey data you hold and haven't sold.</summary>
    public List<Mining.SurveyRecord> SurveyData { get; } = [];
    /// <summary>Criminal haunts you've been told about, as "factionId:systemId".</summary>
    public HashSet<string> KnownLeads { get; } = [];
    /// <summary>Parts you've been given and not yet fitted: any outfitter fits them free.</summary>
    public List<string> GiftParts { get; set; } = [];
    /// <summary>Jobs you've haggled over and the pay multiplier agreed (1 means refused).</summary>
    public Dictionary<string, float> Haggled { get; } = [];
    /// <summary>Jobs pulled after a haggle offended the client.</summary>
    public HashSet<string> WithdrawnOffers { get; } = [];
    /// <summary>What happened when someone came for a passenger, by job id: handed-over, paid, talked, outran, fighting or beat.</summary>
    public Dictionary<string, string> PassengerOutcomes { get; } = [];
    /// <summary>Pursuers you beat who mean to come back.</summary>
    public List<Galaxus.Core.Missions.Grudge> Grudges { get; } = [];
    /// <summary>Work offered after a passenger job, on the board where you left them.</summary>
    public List<Galaxus.Core.Missions.FollowUp> FollowUps { get; } = [];

    public int AskedToday(string personId, int day) =>
        Questions.TryGetValue(personId, out var v) && v.Split(':') is [var d, var n] && int.Parse(d) == day ? int.Parse(n) : 0;

    public void Asked(string personId, int day) => Questions[personId] = $"{day}:{AskedToday(personId, day) + 1}";

    public int RankWith(int faction) => Math.Clamp(Reputation.GetValueOrDefault(faction) / 100, 0, 4);

    public static readonly string[] RankNames = ["Unknown", "Contractor", "Trusted", "Commissioned", "Honoured"];

    /// <summary>Cargo space: the hull's fixed hold, fitted cargo racks and any hidden compartments.</summary>
    public int CargoCapacity(ContentDatabase db) => Ships.ShipFactory.Build(db, HullId, Loadout).CargoTonnes;

    /// <summary>True if the ship has a fuel scoop fitted.</summary>
    public bool ShipFuelScoop(ContentDatabase db) => Ships.ShipFactory.Build(db, HullId, Loadout).FuelScoop > 0;

    public int CargoUsed => Missions.Sum(m => m.Offer.Mission.Tonnes) + Cargo.Values.Sum();

    public int FreeCargo(ContentDatabase db) => CargoCapacity(db) - CargoUsed;

    /// <summary>
    /// What you've bought from and sold to each market recently, by "systemId:commodityId". Markets
    /// recover, so the effect fades: see <see cref="TradeEffect"/>.
    /// </summary>
    public Dictionary<string, TradeMark> Trades { get; } = [];

    /// <summary>Markets as you last saw them, by system id (see TradeIntel).</summary>
    public Dictionary<int, SeenMarket> SeenMarkets { get; } = [];

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
        int capacity = Ships.ShipFactory.Build(db, hull.Id).CargoTonnes;
        if (capacity < CargoUsed) return $"Its hold is too small for your {CargoUsed} t of cargo.";
        int aboard = Crew.Count(c => c.Job != "pilot") + Galaxus.Core.Missions.Passengers.Aboard(this);
        if (hull.CrewBerths < aboard) return $"It has {hull.CrewBerths} berths, and you have {aboard} people aboard.";
        return null;
    }

    public void BuyShip(ShipOffer offer, int tradeIn)
    {
        Credits -= offer.Price - tradeIn;
        HullId = offer.Hull.Id;
        Loadout = null;
        FuelLy = -1; // new ships come with a full tank
    }

    /// <summary>Whether a job's work at its destination is done (true if it has none).</summary>
    public bool TaskDone(MissionOffer offer) => offer.Task?.Split(':') switch
    {
        null => true,
        ["scan", var body] => Scanned.Contains($"{offer.DestinationSystemId}:body:{body}"),
        ["rocks", var n] => Scanned.Count(s => s.StartsWith($"{offer.DestinationSystemId}:rock:")) >= int.Parse(n),
        ["mine", var ore, var t] => Cargo.GetValueOrDefault(ore) >= int.Parse(t),
        _ => true,
    };

    /// <summary>Why the job can't be taken, or null if it can.</summary>
    public string? CannotAccept(ContentDatabase db, MissionOffer offer)
    {
        if (TakenOffers.Contains(offer.Id)) return "Already taken.";
        if (!offer.Playable) return "This kind of job comes in a later build.";
        if (offer.Mission.Tonnes > CargoCapacity(db) - CargoUsed) return $"Needs {offer.Mission.Tonnes} t of free cargo space.";
        if (offer.Task?.Split(':') is ["mine", _, var t] && int.Parse(t) > CargoCapacity(db)) return $"Needs a hold of at least {t} t for the ore.";
        if (offer.Mission.Passenger is { } passenger && passenger.Count > Galaxus.Core.Missions.Passengers.FreeBerths(db, this))
        {
            int free = Math.Max(0, Galaxus.Core.Missions.Passengers.FreeBerths(db, this));
            return $"Needs {passenger.Count} free berth{(passenger.Count == 1 ? "" : "s")}: you have {(free == 0 ? "none" : free.ToString())}.";
        }
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
            if (job.Offer.BattleId == null && job.Offer.DestinationSystemId == SystemId && rightStation && galaxy.Day <= job.DeadlineDay && TaskDone(job.Offer))
            {
                // Mined ore is handed over.
                if (job.Offer.Task?.Split(':') is ["mine", var ore, var t] && (Cargo[ore] -= int.Parse(t)) <= 0) Cargo.Remove(ore);
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
