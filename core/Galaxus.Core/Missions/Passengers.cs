using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Law;
using Galaxus.Core.People;

namespace Galaxus.Core.Missions;

/// <summary>
/// Someone travelling with you on a passenger job.
/// </summary>
/// <param name="Look">respectable, shady or dodgy: how they come across, which sets the pay.</param>
/// <param name="Trouble">What's after them (wanted, bounty, debtor, witness, runaway, valuables), or null for nothing.</param>
/// <param name="Count">People in the party, each taking a berth.</param>
public sealed record Passenger(string Name, string RaceId, string Gender, ulong Seed, string Look, string? Trouble, int Count = 1)
{
    /// <summary>The passenger as a person, for their face.</summary>
    public Person AsPerson(int systemId) => new($"passenger:{Seed}", Name, Gender, RaceId, "traveller", -1, systemId, Seed, false);
}

/// <summary>Someone who came after you for a passenger, or for a grudge.</summary>
/// <param name="Key">Stable id for the encounter, which picks faces and dice.</param>
/// <param name="JobId">The passenger job, or null for a grudge.</param>
/// <param name="Group">What this lot are called: "Vask's hunters".</param>
/// <param name="Fare">The job's pay, which sets what they offer and ask.</param>
public sealed record Interception(string Key, string? JobId, PursuerDef Pursuer, int FactionId, int Ships, bool Grudge, string Group, int Fare, Passenger? Passenger);

/// <summary>Pursuers you beat who are coming back with friends.</summary>
public sealed record Grudge(string PursuerId, string Group, int FactionId, int FromDay, int UntilDay);

/// <summary>Work a passenger's people offered after the job, on the board where you left them.</summary>
public sealed record FollowUp(string Id, string TemplateId, int GiverFactionId, int SystemId, int Day, string Passenger, string? Trouble);

/// <summary>
/// Passengers as people. Each has a look: respectable passengers pay a little and almost never
/// bring trouble, shady ones pay well and sometimes do, and dodgy ones pay very well and usually
/// do. Trouble means someone wants them: the police, bounty hunters, creditors, a house's agents.
/// They may intercept you when you arrive in a system, more likely where it suits them (the police
/// where security is high, outlaws where it's low), and hail with choices: hand the passenger over,
/// pay, talk, run or fight. Beaten pursuers with a grudge come back with friends, customs scans can
/// find a wanted passenger, and a passenger you got through can lead to more work.
/// </summary>
public static class Passengers
{
    public static string LookName(string look) => look switch { "respectable" => "respectable", "shady" => "shady", _ => "up to no good" };

    /// <summary>Pay against an ordinary job of the same kind.</summary>
    public static float PayFactor(string look) => look switch { "respectable" => 0.8f, "shady" => 1.5f, _ => 2.4f };

    /// <summary>Chance a passenger with this look is in some trouble.</summary>
    public static float TroubleChance(string look) => look switch { "respectable" => 0.08f, "shady" => 0.55f, _ => 0.95f };

    /// <summary>Days a follow-up job stays on the board.</summary>
    public const int FollowUpDays = 14;

    // Making passengers ----------------------------------------------------------------------

    /// <summary>Who's travelling on a passenger job: their name, look and any trouble they're in.</summary>
    public static Passenger Make(ContentDatabase db, MissionTemplateDef template, FactionInfo giver, ComplicationDef? complication, Rng rng)
    {
        var looks = template.Looks.Count > 0 ? template.Looks
            : template.Legal ? ["respectable", "respectable", "respectable", "shady"] : ["shady", "dodgy"];
        string look = rng.Pick(looks);

        string? trouble = complication?.Trouble;
        if (trouble == null && rng.NextFloat() < TroubleChance(look))
        {
            var troubles = (template.Troubles.Count > 0 ? template.Troubles
                    : template.Legal ? ["witness", "runaway", "debtor", "valuables", "wanted"] : ["wanted", "bounty", "debtor", "witness", "valuables"])
                .Where(t => db.Pursuers.Values.Any(p => p.Troubles.Contains(t))).ToList();
            if (troubles.Count > 0) trouble = rng.Pick(troubles);
        }

        // Mostly the client's own people, otherwise anyone.
        var races = db.Races.Values.Where(r => r.NameGrammar != "").OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        string race = db.Races.ContainsKey(giver.RaceId) && rng.NextFloat() < 0.55f ? giver.RaceId : rng.Pick(races).Id;
        bool machine = db.Portraits.TryGetValue(race, out var style) && style.Machine;
        string gender = machine ? "neither" : rng.NextFloat() < 0.5f ? "woman" : "man";
        string name = PeopleDirectory.NameFor(db, race, gender, rng.Fork("name"));
        return new Passenger(name, race, gender, rng.NextULong(), look, trouble, Math.Max(1, template.Berths));
    }

    /// <summary>
    /// A line for the briefing that tells you what sort of passenger this is. Dodgy passengers more
    /// or less say what's after them; shady ones only sometimes give it away.
    /// </summary>
    public static string Hint(ContentDatabase db, Passenger passenger, IReadOnlyDictionary<string, string> variables, IReadOnlyDictionary<string, string> aliases, Rng rng)
    {
        if (!db.Grammars.Contains(ContentRules.PassengerHintGrammar)) return "";
        var grammar = db.Grammars.Get(ContentRules.PassengerHintGrammar);
        string specific = $"{passenger.Look}-{passenger.Trouble}";
        string rule = passenger.Trouble != null && grammar.Rules.ContainsKey(specific) && (passenger.Look == "dodgy" || rng.NextFloat() < 0.5f) ? specific : passenger.Look;
        return grammar.Rules.ContainsKey(rule) ? db.Grammars.Expand(grammar.Id, rule, rng.Fork("hint"), variables, aliases) : "";
    }

    // Berths ---------------------------------------------------------------------------------

    public static int Aboard(Career career) => career.Missions.Sum(m => m.Offer.Mission.Passenger?.Count ?? 0);

    /// <summary>Berths free for passengers: the hull's, less crew aboard and passengers already carried.</summary>
    public static int FreeBerths(ContentDatabase db, Career career) =>
        (db.Hulls.TryGetValue(career.HullId, out var hull) ? hull.CrewBerths : 0) - career.Crew.Count(c => c.Job != "pilot") - Aboard(career);

    // Interceptions --------------------------------------------------------------------------

    /// <summary>How much a system's security suits a side: the police like it high, outlaws low.</summary>
    public static float SecurityFactor(string side, string security) => side switch
    {
        "police" => security switch { "High" => 1.3f, "Medium" => 1f, "Low" or "War zone" => 0.6f, _ => 0 },
        "outlaw" => security switch { "High" => 0.4f, "Medium" => 0.8f, "Low" => 1.2f, "Lawless" => 1.5f, _ => 1.3f },
        _ => security == "High" ? 0.8f : 1f,
    };

    private static string Security(Session session, StarSystem system) => SystemProfiles.For(session.Galaxy, system, session.Db).Security;

    /// <summary>Pursuers who could come for this trouble here: the police only where there are police.</summary>
    public static List<PursuerDef> PursuersFor(Session session, string trouble, StarSystem system) =>
        [.. session.Db.Pursuers.Values.Where(p => p.Troubles.Contains(trouble))
            .Where(p => p.Side != "police" || LawRules.Police(session.Galaxy, system) != null)
            .OrderBy(p => p.Id, StringComparer.Ordinal)];

    /// <summary>Who comes for this job's passenger in this system, the same each time, or null.</summary>
    public static PursuerDef? PursuerFor(Session session, AcceptedMission job, StarSystem system)
    {
        if (job.Offer.Mission.Passenger is not { Trouble: { } trouble }) return null;
        var options = PursuersFor(session, trouble, system);
        if (options.Count == 0) return null;
        var rng = new Rng(Rng.Hash($"pursuer:{job.Offer.Id}:{system.Id}"));
        float total = options.Sum(p => p.Weight), roll = rng.NextFloat() * total;
        foreach (var p in options) if ((roll -= p.Weight) <= 0) return p;
        return options[^1];
    }

    /// <summary>
    /// Chance this job's passenger is intercepted when you arrive in a system: none if they're in no
    /// trouble or it's already caught up with them, more for shadier passengers, more where the
    /// system suits whoever's after them, and more as you reach the destination.
    /// </summary>
    public static float Chance(Session session, AcceptedMission job, StarSystem system)
    {
        if (job.Offer.Mission.Passenger is not { Trouble: not null } passenger) return 0;
        if (session.Career.PassengerOutcomes.ContainsKey(job.Offer.Id)) return 0;
        if (PursuerFor(session, job, system) is not { } pursuer) return 0;
        float look = passenger.Look switch { "respectable" => 0.25f, "shady" => 0.35f, _ => 0.5f };
        float chance = look * SecurityFactor(pursuer.Side, Security(session, system));
        if (system.Id == job.Offer.DestinationSystemId) chance *= 1.3f;
        return Math.Clamp(chance, 0, 0.9f);
    }

    /// <summary>Chance a grudge catches up with you here.</summary>
    public static float GrudgeChance(Session session, Grudge grudge, StarSystem system) =>
        session.Db.Pursuers.TryGetValue(grudge.PursuerId, out var p) && grudge.UntilDay >= session.Galaxy.Day && session.Galaxy.Day > grudge.FromDay
            ? 0.3f * SecurityFactor(p.Side == "police" ? "agents" : p.Side, Security(session, system)) : 0;

    /// <summary>
    /// Whether anyone comes for you on arriving here, decided once per arrival: a grudge first,
    /// then each passenger in turn. Null for a quiet arrival.
    /// </summary>
    public static Interception? OnArrival(Session session)
    {
        var here = session.Here;
        var rng = new Rng(session.Galaxy.Seed ^ Rng.Hash($"arrive:{session.Galaxy.Day}:{here.Id}:{session.Career.Credits}"));
        foreach (var grudge in session.Career.Grudges.OrderBy(g => g.FromDay))
            if (rng.NextFloat() < GrudgeChance(session, grudge, here)) return Comeback(session, grudge);
        foreach (var job in session.Career.Missions.Where(m => m.Offer.Mission.Passenger != null).OrderBy(m => m.Offer.Id, StringComparer.Ordinal))
            if (rng.NextFloat() < Chance(session, job, here)) return Intercept(session, job);
        return null;
    }

    /// <summary>The pursuers for a job here, whether or not the dice would send them.</summary>
    public static Interception? Intercept(Session session, AcceptedMission job, PursuerDef? pursuer = null)
    {
        if (job.Offer.Mission.Passenger is not { } passenger) return null;
        pursuer ??= PursuerFor(session, job, session.Here);
        if (pursuer == null) return null;
        string key = $"pursuit:{job.Offer.Id}:{session.Here.Id}:{session.Galaxy.Day}";
        int faction = FactionFor(session, pursuer, job.Offer.GiverFactionId);
        return new Interception(key, job.Offer.Id, pursuer, faction, pursuer.Ships, false, GroupName(session, pursuer, key, passenger.Name, faction), job.Offer.Mission.Reward, passenger);
    }

    /// <summary>A grudge come back: the same lot, with two more ships.</summary>
    public static Interception Comeback(Session session, Grudge grudge)
    {
        var pursuer = session.Db.Pursuers[grudge.PursuerId];
        string key = $"grudge:{grudge.PursuerId}:{grudge.FromDay}:{session.Galaxy.Day}";
        return new Interception(key, null, pursuer, grudge.FactionId, Math.Min(6, pursuer.Ships + 2), true, grudge.Group, 4000, null);
    }

    /// <summary>
    /// Whose people they are: the police here, another faction near here for agents (not the
    /// client), or a gang that haunts the system for outlaws.
    /// </summary>
    private static int FactionFor(Session session, PursuerDef pursuer, int giver)
    {
        var galaxy = session.Galaxy;
        var here = session.Here;
        switch (pursuer.Side)
        {
            case "police":
                return LawRules.Police(galaxy, here)?.Id ?? -1;
            case "agents":
            {
                var near = galaxy.WithinJumps(here.Id, 3).Append(here).Where(s => s.Owner >= 0).Select(s => s.Owner).Distinct()
                    .Where(id => id != giver && galaxy.Factions[id] is { Alive: true, Criminal: false }).OrderBy(id => id).ToList();
                return near.Count == 0 ? -1 : near[(int)(Rng.Hash($"agents:{giver}:{here.Id}") % (ulong)near.Count)];
            }
            default:
                return galaxy.Factions.Where(f => f.Criminal && f.Alive && f.Haunts.Contains(here.Id)).OrderBy(f => f.Id).FirstOrDefault()?.Id ?? -1;
        }
    }

    private static string FactionName(Session session, int id) => id >= 0 && id < session.Galaxy.Factions.Count ? session.Galaxy.Factions[id].Name : "nobody you know";

    private static Dictionary<string, string> Variables(Session session, Interception x, string group) => new()
    {
        ["captain"] = session.Career.Captain,
        ["passenger"] = x.Passenger?.Name ?? "your passenger",
        ["group"] = group,
        ["fare"] = $"{x.Fare:N0} cr",
        ["price"] = $"{Price(session, x):N0} cr",
        ["faction"] = FactionName(session, x.FactionId),
        ["system"] = session.Here.Name,
    };

    private static string GroupName(Session session, PursuerDef pursuer, string key, string passenger, int faction)
    {
        var vars = new Dictionary<string, string>
        {
            ["captain"] = session.Career.Captain, ["passenger"] = passenger, ["group"] = pursuer.Name, ["fare"] = "", ["price"] = "",
            ["faction"] = FactionName(session, faction), ["system"] = session.Here.Name,
        };
        return session.Db.Grammars.Expand(pursuer.TextGrammar, "group", new Rng(Rng.Hash($"{key}:group")), vars);
    }

    private static string Line(Session session, Interception x, string rule) =>
        session.Db.Grammars.Expand(x.Pursuer.TextGrammar, rule, new Rng(Rng.Hash($"{x.Key}:{rule}")), Variables(session, x, x.Group));

    /// <summary>What handing the passenger over pays.</summary>
    public static int HandOverPay(Interception x) => (int)(x.Fare * x.Pursuer.HandOverReward) / 50 * 50;

    /// <summary>What paying them off costs.</summary>
    public static int Price(Session session, Interception x) => x.Grudge
        ? Math.Max(1500, Math.Min(session.Career.Credits / 6, 12000)) / 50 * 50
        : Math.Max(300, (int)(x.Fare * x.Pursuer.PayFactor)) / 50 * 50;

    /// <summary>Whether your ship can outrun theirs: noticeably faster than their fastest.</summary>
    public static bool Outruns(Session session, Interception x)
    {
        var db = session.Db;
        float mine = Ships.ShipFactory.Build(db, session.Career.HullId, session.Career.Loadout).MaxSpeed;
        float theirs = Hulls(session, x).Max(h => Ships.ShipFactory.Build(db, h).MaxSpeed);
        return mine > theirs * 1.05f;
    }

    /// <summary>The hulls they fly, one per ship: their faction's builder if it makes the class, else anything on sale.</summary>
    public static List<string> Hulls(Session session, Interception x)
    {
        var db = session.Db;
        string? builder = x.FactionId >= 0 ? Battles.BattleJobs.BuilderFor(db, session.Galaxy.Factions[x.FactionId]) : null;
        var options = db.Hulls.Values.Where(h => h.ForSale && x.Pursuer.Hulls.Contains(h.Class) && h.Builder == builder).OrderBy(h => h.Id, StringComparer.Ordinal).ToList();
        if (options.Count == 0) options = [.. db.Hulls.Values.Where(h => h.ForSale && x.Pursuer.Hulls.Contains(h.Class)).OrderBy(h => h.Id, StringComparer.Ordinal)];
        var rng = new Rng(Rng.Hash($"{x.Key}:hulls"));
        return [.. Enumerable.Range(0, x.Ships).Select(_ => rng.Pick(options).Id)];
    }

    /// <summary>Their hail.</summary>
    public static HailTurn Open(Session session, Interception x) =>
        new(Line(session, x, x.Grudge ? "grudge" : "open"), Options(session, x, talked: false));

    private static List<HailOption> Options(Session session, Interception x, bool talked)
    {
        var options = new List<HailOption>();
        var choices = x.Pursuer.Choices;
        string name = x.Passenger?.Name ?? "them";
        if (!x.Grudge && choices.Contains("hand-over"))
        {
            int pay = HandOverPay(x);
            options.Add(new(pay > 0 ? $"Hand {name} over for {pay:N0} cr" : $"Hand {name} over", "px:hand-over"));
        }
        if (choices.Contains("pay") || x.Grudge)
        {
            int price = Price(session, x);
            options.Add(new($"Pay them {price:N0} cr", "px:pay", price <= session.Career.Credits));
        }
        if (!x.Grudge && !talked && choices.Contains("talk")) options.Add(new("Talk them round", "px:talk"));
        options.Add(new("Run for it", "px:run"));
        options.Add(new(x.Pursuer.Side == "police" ? "Refuse" : "Come and get them", "px:fight"));
        return options;
    }

    /// <summary>
    /// Your answer and what it leads to. Handing over loses the job but may pay; paying keeps the
    /// passenger; talking works more often for respectable passengers and with people who rate
    /// you; running works if you're faster; refusing is a fight, and refusing the police is a crime.
    /// </summary>
    public static HailTurn Respond(Session session, Interception x, string action)
    {
        var career = session.Career;
        var job = x.JobId == null ? null : career.Missions.FirstOrDefault(m => m.Offer.Id == x.JobId);
        void Outcome(string what) { if (x.JobId != null) career.PassengerOutcomes[x.JobId] = what; }
        switch (action)
        {
            case "px:hand-over":
            {
                int pay = HandOverPay(x);
                career.Credits += pay;
                if (job != null)
                {
                    career.Missions.Remove(job);
                    int giver = job.Offer.GiverFactionId;
                    career.Reputation[giver] = career.Reputation.GetValueOrDefault(giver) - 25;
                    if (career.ActiveJobId == job.Offer.Id) career.ActiveJobId = career.Missions.OrderBy(m => m.DeadlineDay).FirstOrDefault()?.Offer.Id;
                }
                if (x.FactionId >= 0) career.Reputation[x.FactionId] = career.Reputation.GetValueOrDefault(x.FactionId) + 15;
                Outcome("handed-over");
                return new HailTurn(Line(session, x, "hand-over"), [], HailOutcome.Leave);
            }
            case "px:pay":
            {
                int price = Price(session, x);
                if (price > career.Credits) return new HailTurn("You haven't got it. Then we'll take what we came for.", [], HailOutcome.Hostile);
                career.Credits -= price;
                Outcome("paid");
                if (x.Grudge) career.Grudges.RemoveAll(g => g.PursuerId == x.Pursuer.Id);
                return new HailTurn(Line(session, x, "pay"), [], HailOutcome.Leave);
            }
            case "px:talk":
            {
                if (TalkWorks(session, x))
                {
                    Outcome("talked");
                    return new HailTurn(Line(session, x, "talk-yes"), [], HailOutcome.Leave);
                }
                return new HailTurn(Line(session, x, "talk-no"), Options(session, x, talked: true));
            }
            case "px:run":
            {
                if (x.Pursuer.Side == "police") LawRules.Commit(session, "evasion");
                if (Outruns(session, x))
                {
                    Outcome("outran");
                    return new HailTurn(Line(session, x, "outran"), [], HailOutcome.Leave);
                }
                Outcome("fighting");
                return new HailTurn(Line(session, x, "caught"), [], HailOutcome.Hostile);
            }
            case "px:fight":
                if (x.Pursuer.Side == "police") LawRules.Commit(session, "harbouring");
                Outcome("fighting");
                return new HailTurn(Line(session, x, "fight"), [], HailOutcome.Hostile);
        }
        return new HailTurn("", [], HailOutcome.None);
    }

    /// <summary>Whether talking them round works: likelier for respectable passengers and with people who rate you.</summary>
    public static bool TalkWorks(Session session, Interception x)
    {
        float chance = 0.3f + (x.FactionId >= 0 ? 0.12f * session.Career.RankWith(x.FactionId) : 0)
                       + (x.Passenger?.Look switch { "respectable" => 0.25f, "shady" => 0.1f, _ => 0 });
        return new Rng(Rng.Hash($"{x.Key}:talk")).NextFloat() < chance;
    }

    /// <summary>
    /// The fight's over: you beat them, or they gave up the chase. Pursuers with a grudge come back
    /// within the next weeks with friends; beat them a second time and they give up for good.
    /// Returns a line for the HUD.
    /// </summary>
    public static string Fought(Session session, Interception x)
    {
        var career = session.Career;
        if (x.JobId != null) career.PassengerOutcomes[x.JobId] = "beat";
        if (x.Grudge)
        {
            career.Grudges.RemoveAll(g => g.PursuerId == x.Pursuer.Id);
            return $"{x.Group.ToUpperInvariant()} WON'T BE BACK";
        }
        if (x.Pursuer.Grudge && career.Grudges.All(g => g.PursuerId != x.Pursuer.Id))
        {
            int day = session.Galaxy.Day;
            career.Grudges.Add(new Grudge(x.Pursuer.Id, x.Group, x.FactionId, day, day + 40));
            return $"{x.Group.ToUpperInvariant()} DRIVEN OFF. THEY'LL REMEMBER YOU";
        }
        return $"{x.Group.ToUpperInvariant()} DRIVEN OFF";
    }

    // Customs ---------------------------------------------------------------------------------

    /// <summary>Jobs carrying someone the law here wants.</summary>
    public static List<AcceptedMission> WantedAboard(Session session) =>
        LawRules.Police(session.Galaxy, session.Here) == null ? []
            : [.. session.Career.Missions.Where(m => m.Offer.Mission.Passenger?.Trouble == "wanted")];

    // Afterwards -------------------------------------------------------------------------------

    /// <summary>
    /// A passenger delivered. Someone you got through trouble, or a witness, often has more work
    /// for you, posted on the board here; now and then a passenger tips. Returns a line to report, or null.
    /// </summary>
    public static string? Delivered(Session session, MissionResult result)
    {
        var offer = result.Mission.Offer;
        if (!result.Success || offer.Mission.Passenger is not { } passenger) return null;
        var career = session.Career;
        string? outcome = career.PassengerOutcomes.GetValueOrDefault(offer.Id);
        career.PassengerOutcomes.Remove(offer.Id);
        var rng = new Rng(Rng.Hash($"delivered:{offer.Id}"));
        bool through = outcome is "paid" or "talked" or "outran" or "beat" or "fighting";
        float chance = through ? 0.75f : passenger.Trouble == "witness" ? 0.5f : passenger.Trouble != null ? 0.25f : 0.1f;
        string? line = null;
        if (rng.NextFloat() < chance && FollowUpTemplate(session, offer, passenger, rng) is { } template)
        {
            career.FollowUps.Add(new FollowUp($"fu-{offer.Id}", template.Id, offer.GiverFactionId, session.Here.Id, session.Galaxy.Day, passenger.Name, passenger.Trouble));
            line = through ? $"{passenger.Name} hasn't forgotten what you did out there. There's more work on the board here."
                           : $"{passenger.Name} put a word in for you. There's more work on the board here.";
        }
        if (through || passenger.Look == "respectable" && rng.NextFloat() < 0.3f)
        {
            int tip = Math.Max(100, offer.Mission.Reward * (through ? 25 : 10) / 100) / 50 * 50;
            career.Credits += tip;
            line = (line == null ? "" : line + " ") + $"{passenger.Name} tips you {tip:N0} cr.";
        }
        return line;
    }

    /// <summary>What the passenger's people offer next: the job's own follow-ups, or one that suits their trouble.</summary>
    private static MissionTemplateDef? FollowUpTemplate(Session session, MissionOffer offer, Passenger passenger, Rng rng)
    {
        var db = session.Db;
        var own = offer.Mission.Template.FollowUps.Where(db.Missions.ContainsKey).ToList();
        string fallback = passenger.Trouble switch
        {
            "witness" => "passenger-evidence",
            "wanted" or "bounty" or "debtor" => "passenger-old-friends",
            _ => "passenger-family",
        };
        if (own.Count > 0 && rng.NextFloat() < 0.5f) return db.Missions[rng.Pick(own)];
        return db.Missions.GetValueOrDefault(fallback) ?? (own.Count > 0 ? db.Missions[own[0]] : null);
    }

    /// <summary>Follow-up jobs on the board here, while they last.</summary>
    public static List<MissionOffer> FollowUpOffers(Session session)
    {
        var offers = new List<MissionOffer>();
        var galaxy = session.Galaxy;
        var here = session.Here;
        if (here.Stations.Count == 0) return offers;
        foreach (var f in session.Career.FollowUps.Where(f => f.SystemId == here.Id && galaxy.Day - f.Day <= FollowUpDays))
        {
            if (!session.Db.Missions.TryGetValue(f.TemplateId, out var template) || f.GiverFactionId >= galaxy.Factions.Count) continue;
            var giver = galaxy.Factions[f.GiverFactionId];
            var rng = new Rng(galaxy.Seed ^ Rng.Hash(f.Id));
            var nearby = galaxy.WithinJumps(here.Id, 3).Where(s => s.Stations.Count > 0 && s.Id != here.Id).OrderBy(s => s.Id).ToList();
            var destination = nearby.Count > 0 ? rng.Pick(nearby) : here;
            string station = rng.Pick(destination.Stations);
            var context = new MissionContext(new FactionInfo(giver.Name, giver.TypeId, giver.RaceId, giver.Criminal), here.Name,
                $"{station}, {destination.Name}", session.Career.Captain, session.Career.RankWith(giver.Id), null,
                MissionBoard.TravelDays(galaxy, here.Id, destination.Id), f.Passenger);
            // The passenger you carried is the client now.
            var mission = MissionGenerator.Build(session.Db, template, context, rng.Fork("mission"));
            offers.Add(new MissionOffer(f.Id, mission, giver.Id, here.Id, destination.Id, station, f.Day));
        }
        return offers;
    }
}
