using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Missions;

namespace Galaxus.Core.People;

public enum Topic { Rumour, Work, Self, Drink, Farewell }

/// <summary>What the captain says for each topic. The other side answers in their race's voice.</summary>
public static class Lines
{
    public static string Captain(Topic topic) => topic switch
    {
        Topic.Rumour => "Heard anything interesting?",
        Topic.Work => "Any work going?",
        Topic.Self => "What's your story?",
        Topic.Drink => "Let me buy you a drink.",
        _ => "Take care.",
    };
}

public sealed record Reply(string Text, int DispositionChange, string? Lead = null);

public sealed record HaggleResult(bool Accepted, bool Withdrawn, string Text, int NewReward);

/// <summary>
/// Conversations: the captain picks a topic, the person answers in their race's voice, and how
/// they feel about the captain (their disposition) shapes what they'll say. Rumours are real:
/// they come from events, conflicts and prices in nearby systems. People who trust you, criminals
/// especially, may say where a criminal faction can be found.
/// </summary>
public static class Conversation
{
    public const int DrinkCost = 25;
    private const int Cold = -20, Trusting = 25, QuestionsBeforePestered = 4;

    private static readonly string[] ConflictWords = ["squabbling", "raiding each other", "blockading each other", "at open war"];
    private static readonly HashSet<string> CriminalRoles = ["smuggler", "broker", "enforcer"];

    public static int Disposition(Career career, Person person) => career.Disposition.GetValueOrDefault(person.Id);

    /// <summary>What they say when you walk up: a welcome back if you've met, otherwise their race's greeting.</summary>
    public static string Greeting(Session session, Person person)
    {
        var career = session.Career;
        if (Disposition(career, person) <= Cold) return Say(session, person, "cold", null, 0);
        if (career.MetPeople.Contains(person.Id)) return Say(session, person, "greeting-known", null, 0);
        return PeopleDirectory.Greeting(session.Db, session.Galaxy, person, career.Captain, session.Db.Hulls[career.HullId].Name);
    }

    public static Reply Ask(Session session, Person person, Topic topic)
    {
        var career = session.Career;
        var galaxy = session.Galaxy;
        career.MetPeople.Add(person.Id);
        int disposition = Disposition(career, person);

        if (topic == Topic.Farewell) return Apply(career, person, new Reply(Say(session, person, "farewell", null, 0), 0));
        if (disposition <= Cold) return Apply(career, person, new Reply(Say(session, person, "cold", null, 0), 0));

        // Too many questions in one day wears thin. Drinks don't count.
        if (topic != Topic.Drink)
        {
            int asked = career.AskedToday(person.Id, galaxy.Day);
            career.Asked(person.Id, galaxy.Day);
            if (asked >= QuestionsBeforePestered) return Apply(career, person, new Reply(Say(session, person, "pestered", null, asked), -3));
        }

        int turn = career.AskedToday(person.Id, galaxy.Day);
        switch (topic)
        {
            case Topic.Rumour:
            {
                var (fact, lead) = PickFact(session, person, disposition, turn);
                return Apply(career, person, fact == null
                    ? new Reply(Say(session, person, "no-rumour", null, turn), 1)
                    : new Reply(Say(session, person, "rumour", fact, turn), 1, lead));
            }
            case Topic.Work:
            {
                string? fact = WorkFact(session);
                return Apply(career, person, new Reply(Say(session, person, fact == null ? "no-work" : "work", fact, turn), 1));
            }
            case Topic.Self:
                return Apply(career, person, new Reply(Say(session, person, "self", null, turn), 2));
            default: // Drink
                if (career.LastDrink.GetValueOrDefault(person.Id, -1) == galaxy.Day)
                    return Apply(career, person, new Reply(Say(session, person, "drink-again", null, turn), 0));
                if (career.Credits < DrinkCost) return new Reply("(You can't afford a round.)", 0);
                career.Credits -= DrinkCost;
                career.LastDrink[person.Id] = galaxy.Day;
                return Apply(career, person, new Reply(Say(session, person, "drink", null, turn), 8));
        }
    }

    private static Reply Apply(Career career, Person person, Reply reply)
    {
        career.Disposition[person.Id] = Math.Clamp(Disposition(career, person) + reply.DispositionChange, -100, 100);
        return reply;
    }

    private static string Say(Session session, Person person, string rule, string? fact, int turn)
    {
        var db = session.Db;
        var race = db.Races.GetValueOrDefault(person.RaceId);
        string grammar = race is { TalkGrammar: not "" } ? race.TalkGrammar : "talk-human";
        if (!db.Grammars.Contains(grammar)) return "...";
        var galaxy = session.Galaxy;
        var vars = new Dictionary<string, string>
        {
            ["captain"] = session.Career.Captain,
            ["system"] = galaxy.Systems[person.SystemId].Name,
            ["faction"] = person.FactionId >= 0 ? galaxy.Factions[person.FactionId].Name : "nobody in particular",
            ["fact"] = fact ?? "",
            ["role"] = db.Roles.TryGetValue(person.RoleId, out var r) ? r.Name.ToLowerInvariant() : person.RoleId,
            ["name"] = person.Name,
            ["place"] = galaxy.Systems[person.SystemId].Stations.FirstOrDefault() ?? galaxy.Systems[person.SystemId].Name,
        };
        var rng = new Rng(person.Seed ^ Rng.Hash($"{rule}-{galaxy.Day}-{turn}"));
        return db.Grammars.Expand(grammar, rule, rng, vars);
    }

    /// <summary>
    /// Something true the person might pass on. Most rumours are about events and conflicts nearby,
    /// or where goods are wanted. Trusting people may name a criminal faction's haunt (the lead).
    /// </summary>
    public static (string? Fact, string? Lead) PickFact(Session session, Person person, int disposition, int turn)
    {
        var galaxy = session.Galaxy;
        var db = session.Db;
        var here = galaxy.Systems[person.SystemId];
        var near = galaxy.WithinJumps(here.Id, 3).Append(here).ToList();
        var nearIds = near.Select(s => s.Id).ToHashSet();
        var facts = new List<(string Text, string? Lead)>();

        foreach (var ev in galaxy.Events.Where(e => nearIds.Contains(e.SystemId)).OrderBy(e => e.SystemId))
            if (db.Events.TryGetValue(ev.EventId, out var def))
                facts.Add(($"there's {Article(def.Name.ToLowerInvariant())} at {galaxy.Systems[ev.SystemId].Name}", null));

        foreach (var c in galaxy.Conflicts.Where(c => nearIds.Contains(c.FrontSystem)))
            facts.Add(($"{galaxy.Factions[c.A].Name} and {galaxy.Factions[c.B].Name} are {ConflictWords[c.Stage]} over {galaxy.Systems[c.FrontSystem].Name}", null));

        var wanted = near.Where(s => s.Stations.Count > 0)
            .SelectMany(s => db.Commodities.Values.Where(c => c.Legality == "legal").Select(c => (System: s, Goods: c, Ratio: Market.Price(db, galaxy, s, c) / (float)c.BasePrice)))
            .OrderByDescending(x => x.Ratio).ThenBy(x => x.Goods.Id).FirstOrDefault();
        if (wanted.Goods != null && wanted.Ratio > 1.4f)
            facts.Add(($"{wanted.System.Name} is paying well for {wanted.Goods.Name.ToLowerInvariant()}", null));

        bool criminal = CriminalRoles.Contains(person.RoleId);
        if (disposition >= Trusting || (criminal && disposition >= 10))
            foreach (var f in galaxy.Factions.Where(f => f.Criminal && f.Alive && f.Haunts.Any(nearIds.Contains)).OrderBy(f => f.Id))
            {
                var haunt = galaxy.Systems[f.Haunts.First(nearIds.Contains)];
                facts.Add(($"if you want {f.Name}, ask around {haunt.Name}", $"{f.Id}:{haunt.Id}"));
            }

        if (facts.Count == 0) return (null, null);
        // Leads are what trust buys, so offer them first once they're available.
        var leads = facts.Where(f => f.Lead != null && !session.Career.KnownLeads.Contains(f.Lead)).ToList();
        var pick = leads.Count > 0 ? leads[0] : facts[(int)((person.Seed + (ulong)turn + (ulong)galaxy.Day) % (ulong)facts.Count)];
        if (pick.Lead != null) session.Career.KnownLeads.Add(pick.Lead);
        return pick;
    }

    private static string? WorkFact(Session session)
    {
        var best = session.Board().Where(o => o.Playable).MaxBy(o => o.Mission.Reward);
        if (best == null) return null;
        var giver = session.Galaxy.Factions[best.GiverFactionId];
        return $"{giver.Name} is paying {best.Mission.Reward:N0} cr for a run to {session.Galaxy.Systems[best.DestinationSystemId].Name}";
    }

    private static string Article(string noun) => ("aeiou".Contains(noun[0]) ? "an " : "a ") + noun;

    /// <summary>
    /// Ask a client for more money. Their race's temperament and how they feel about you decide
    /// it. A refusal can offend them enough to pull the job. You only get one try per job.
    /// </summary>
    public static HaggleResult Haggle(Session session, MissionOffer offer)
    {
        var career = session.Career;
        var galaxy = session.Galaxy;
        var giver = galaxy.Factions[offer.GiverFactionId];
        var client = PeopleDirectory.Contacts(session.Db, galaxy, giver).FirstOrDefault(c => c.Id == offer.ClientId)
                     ?? PeopleDirectory.Contacts(session.Db, galaxy, giver)[0];
        var race = session.Db.Races.GetValueOrDefault(client.RaceId);
        float chance = (race?.HaggleChance ?? 0.4f) + Disposition(career, client) / 200f;
        float offence = race?.HaggleOffence ?? 0.15f;
        var rng = new Rng(client.Seed ^ Rng.Hash(offer.Id));

        if (career.Haggled.ContainsKey(offer.Id))
            return new HaggleResult(false, false, Say(session, client, "haggle-no", null, 9), offer.Mission.Reward);

        if (rng.NextFloat() < chance)
        {
            float multiplier = 1.1f + rng.NextFloat() * 0.15f;
            career.Haggled[offer.Id] = multiplier;
            int reward = (int)(MathF.Round(offer.Mission.Reward * multiplier / 50f) * 50);
            return new HaggleResult(true, false, Say(session, client, "haggle-yes", $"{reward:N0} cr it is", 1), reward);
        }

        career.Haggled[offer.Id] = 1f;
        if (rng.NextFloat() < offence)
        {
            career.WithdrawnOffers.Add(offer.Id);
            career.Disposition[client.Id] = Disposition(career, client) - 10;
            return new HaggleResult(false, true, Say(session, client, "haggle-offended", null, 2), 0);
        }
        career.Disposition[client.Id] = Disposition(career, client) - 2;
        return new HaggleResult(false, false, Say(session, client, "haggle-no", null, 3), offer.Mission.Reward);
    }
}
