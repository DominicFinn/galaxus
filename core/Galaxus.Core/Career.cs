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
    public HashSet<string> TakenOffers { get; } = [];
    /// <summary>Reputation per faction. Every 100 points is one rank, up to 4 (honoured).</summary>
    public Dictionary<int, int> Reputation { get; } = [];

    public int RankWith(int faction) => Math.Clamp(Reputation.GetValueOrDefault(faction) / 100, 0, 4);

    public static readonly string[] RankNames = ["Unknown", "Contractor", "Trusted", "Commissioned", "Honoured"];

    /// <summary>Cargo space: the hull's fixed hold plus fitted cargo racks.</summary>
    public int CargoCapacity(ContentDatabase db)
    {
        var hull = db.Hulls[HullId];
        var parts = (Loadout ?? hull.DefaultLoadout).Select(id => db.Parts[id]);
        return hull.CargoTonnes + (int)parts.Where(p => p.Type == "cargo").Sum(p => p.Stats.GetValueOrDefault("tonnes"));
    }

    public int CargoUsed => Missions.Sum(m => m.Offer.Mission.Tonnes);

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
        Missions.Add(new AcceptedMission(offer, galaxy.Day, galaxy.Day + offer.Mission.DeadlineDays));
    }

    /// <summary>Pays out jobs whose destination is here, and fails any that ran out of time.</summary>
    public List<MissionResult> Settle(Galaxy.Galaxy galaxy)
    {
        var results = new List<MissionResult>();
        foreach (var job in Missions.ToList())
        {
            var mission = job.Offer.Mission;
            if (job.Offer.DestinationSystemId == SystemId && galaxy.Day <= job.DeadlineDay)
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
        return results;
    }
}
