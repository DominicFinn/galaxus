using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;

namespace Galaxus.Core.Missions;

/// <summary>A job on a station's board, tied to real places in the galaxy.</summary>
public sealed record MissionOffer(
    string Id,
    Mission Mission,
    int GiverFactionId,
    int OriginSystemId,
    int DestinationSystemId,
    string DestinationStation,
    int PostedDay,
    string? ClientId = null,
    /// <summary>For a set-piece battle: which one, and who you'll be fighting (-1 pirates or rogue machines with no faction).</summary>
    string? BattleId = null,
    int EnemyFactionId = -1)
{
    /// <summary>
    /// Jobs completed by getting somewhere: these can be done now. Combat jobs need ships to
    /// fight, which come with the traffic simulation.
    /// </summary>
    public bool Playable => BattleId != null || MissionBoard.TravelTypes.Contains(Mission.Template.Type);
}

/// <summary>Builds the jobs on offer at a system's stations. The board refreshes weekly.</summary>
public static class MissionBoard
{
    public static readonly HashSet<string> TravelTypes =
        ["delivery", "courier", "passenger", "smuggling", "rescue", "blockade-running", "survey", "salvage", "investigation"];

    public static List<MissionOffer> For(ContentDatabase db, Galaxy.Galaxy galaxy, StarSystem system, string captain, Func<int, int> rankWith)
    {
        var offers = new List<MissionOffer>();
        if (system.Stations.Count == 0) return offers;

        int week = galaxy.Day / 7;
        var rng = new Rng(system.Seed ^ Rng.Hash($"board-{week}"));
        var givers = Givers(galaxy, system);
        if (givers.Count == 0) return offers;

        var nearby = galaxy.WithinJumps(system.Id, 4).Where(s => s.Stations.Count > 0).ToList();
        var activeEvent = galaxy.EventsIn(system.Id).Select(e => e.EventId).FirstOrDefault();
        int count = 5 + rng.Next(5);

        for (int i = 0; i < count * 3 && offers.Count < count; i++)
        {
            var orng = rng.Fork($"offer-{i}");
            var giver = orng.Pick(givers);
            var destination = nearby.Count > 0 ? orng.Pick(nearby) : system;
            string station = orng.Pick(destination.Stations);
            int travel = TravelDays(galaxy, system.Id, destination.Id);
            // The client is one of the faction's regular contacts, so you deal with the same people.
            var client = orng.Pick(People.PeopleDirectory.Contacts(db, galaxy, giver));

            var context = new MissionContext(
                new FactionInfo(giver.Name, giver.TypeId, giver.RaceId, giver.Criminal),
                system.Name,
                $"{station}, {destination.Name}",
                captain,
                rankWith(giver.Id),
                orng.NextFloat() < 0.6f ? activeEvent : null,
                travel,
                client.Name);
            var mission = MissionGenerator.Generate(db, context, orng.Fork("mission"));
            if (mission == null) continue;
            // Combat jobs are played out as a battle in the destination system.
            string? battleId = null;
            int enemy = -1;
            if (!TravelTypes.Contains(mission.Template.Type))
            {
                var fits = db.Battles.Values.Where(b => b.Kind == "job" && b.JobType == mission.Template.Type
                                                        && (b.Scales.Count == 0 || b.Scales.Contains(mission.Template.Scale)))
                    .OrderBy(b => b.Id, StringComparer.Ordinal).ToList();
                if (fits.Count == 0) continue;
                battleId = orng.Pick(fits).Id;
                enemy = Battles.BattleJobs.JobEnemy(galaxy, giver, destination, mission.Template.Type, orng);
            }
            offers.Add(new MissionOffer($"{system.Id}-{week}-{i}", mission, giver.Id, system.Id, destination.Id, station, galaxy.Day, client.Id, battleId, enemy));
        }
        offers.AddRange(Battles.BattleJobs.Offers(db, galaxy, system, captain, rankWith, rng.Fork("battles")));
        return offers;
    }

    /// <summary>The owner, factions with territory or haunts within two jumps, and anyone running an event here.</summary>
    private static List<Faction> Givers(Galaxy.Galaxy galaxy, StarSystem system)
    {
        var ids = new HashSet<int>();
        if (system.Owner >= 0) ids.Add(system.Owner);
        foreach (var s in galaxy.WithinJumps(system.Id, 2).Where(s => s.Owner >= 0)) ids.Add(s.Owner);
        foreach (var f in galaxy.Factions.Where(f => f.Criminal && f.Alive && f.Haunts.Contains(system.Id))) ids.Add(f.Id);
        return ids.Select(id => galaxy.Factions[id]).Where(f => f.Alive).OrderBy(f => f.Id).ToList();
    }

    /// <summary>Shortest travel time in days by gate, or 0 for the same system.</summary>
    public static int TravelDays(Galaxy.Galaxy galaxy, int from, int to)
    {
        if (from == to) return 0;
        var best = new Dictionary<int, int> { [from] = 0 };
        var queue = new PriorityQueue<int, int>();
        queue.Enqueue(from, 0);
        while (queue.TryDequeue(out int id, out int days))
        {
            if (id == to) return days;
            if (days > best[id]) continue;
            foreach (var n in galaxy.Systems[id].Gates)
            {
                int d = days + galaxy.TravelDays(id, n);
                if (d < best.GetValueOrDefault(n, int.MaxValue)) { best[n] = d; queue.Enqueue(n, d); }
            }
        }
        return int.MaxValue;
    }
}
