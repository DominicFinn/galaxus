using Galaxus.Core.Galaxy;
using Galaxus.Core.People;

namespace Galaxus.Core.Law;

/// <summary>Who's on the other end of a hail.</summary>
/// <param name="Kind">police (a patrol stopping you), inspection, patrol, trader, miner, pirate, fence or station.</param>
/// <param name="Key">Stable id for the ship or station, which also picks the face.</param>
public sealed record Caller(string Kind, string Key, string ShipName, string HullId, int FactionId, string RaceId)
{
    public bool Police => Kind is "police" or "inspection" or "patrol" or "station";
}

/// <summary>A choice in a hail. Action is what the game hands back to <see cref="Hails.Respond"/>.</summary>
public sealed record HailOption(string Label, string Action, bool Enabled = true);

/// <summary>What happens to the other ship when a hail ends.</summary>
public enum HailOutcome
{
    /// <summary>Talk goes on, or ends with nothing changed.</summary>
    None,
    /// <summary>They leave you be: a patrol satisfied, a pirate paid off.</summary>
    Leave,
    /// <summary>They attack: a refused patrol, a defied pirate.</summary>
    Hostile,
    /// <summary>The station lets you dock.</summary>
    Dock,
}

/// <summary>One turn of a hail: what they say, what you can say back, and what it led to.</summary>
public sealed record HailTurn(string Text, List<HailOption> Options, HailOutcome Outcome = HailOutcome.None)
{
    /// <summary>True when there's nothing left to say.</summary>
    public bool Over => Options.Count == 0;
}

/// <summary>
/// Ship-to-ship and station hails. Police stop you to collect fines or scan your hold; stations
/// turn away the wanted; pirates demand your cargo; traders and miners pass the time and the
/// news; fences, lurking in poorly policed systems, buy and sell contraband and point you at the
/// local gang's work. Each hail is a short exchange of lines and choices; the game shows it with
/// the caller's face and ship and acts on the outcome.
/// </summary>
public static class Hails
{
    /// <summary>The person speaking for a caller: the same face every time for the same ship.</summary>
    public static Person Speaker(Session session, Caller caller) =>
        PeopleDirectory.Make(session.Db, session.Galaxy, $"hail:{caller.Key}", session.Here, null, regular: false, caller.RaceId, caller.FactionId);

    private static string Pick(Session session, Caller caller, string what, params string[] lines) =>
        lines[(int)(Rng.Hash($"{caller.Key}:{what}:{session.Galaxy.Day}") % (ulong)lines.Length)];

    /// <summary>A faction's name as a word before "patrol": without a leading "The".</summary>
    public static string Short(Session session, int id) =>
        FactionName(session, id) is var name && name.StartsWith("The ") ? name[4..] : FactionName(session, id);

    private static string FactionName(Session session, int id) => id >= 0 && id < session.Galaxy.Factions.Count ? session.Galaxy.Factions[id].Name : "the authorities";

    /// <summary>The first thing said: when they hail you, or when you hail them.</summary>
    public static HailTurn Open(Session session, Caller caller)
    {
        var career = session.Career;
        string you = career.Captain;
        switch (caller.Kind)
        {
            case "police":
            {
                int owed = LawRules.Owed(career, caller.FactionId);
                var standing = LawRules.StandingWith(career, caller.FactionId);
                string record = Record(session, caller.FactionId);
                string text = standing == Standing.Wanted
                    ? $"Captain {you}, this is a {Short(session, caller.FactionId)} patrol. You're wanted for {record}. Clearing it costs {owed:N0} cr, now, or we take the ship."
                    : $"Captain {you}, {Short(session, caller.FactionId)} patrol. You have fines outstanding for {record}: {owed:N0} cr. We'd like that settled.";
                return new HailTurn(text, PoliceOptions(session, caller, owed));
            }
            case "inspection":
                return new HailTurn(Pick(session, caller, "inspect",
                    $"Captain {you}, {Short(session, caller.FactionId)} patrol. Cut your drives and stand by for a cargo scan.",
                    $"Routine inspection, captain. Hold your course and open your hold to our scanner.",
                    $"This is a {Short(session, caller.FactionId)} customs patrol. Stand by to be scanned."),
                    [new("Comply", "comply"), new("Run for it", "run")]);
            case "station":
            {
                int owed = LawRules.Owed(career, caller.FactionId);
                return new HailTurn($"Traffic control to Captain {you}: docking refused. {FactionName(session, caller.FactionId)} has you wanted for {Record(session, caller.FactionId)}. Clear {owed:N0} cr and we'll give you a pad.",
                    [new($"Pay {owed:N0} cr and dock", "pay-dock", owed <= career.Credits), new("Turn away", "close")]);
            }
            case "patrol":
            {
                int owed = LawRules.Owed(career, caller.FactionId);
                var options = new List<HailOption>();
                if (owed > 0) options.Add(new($"Pay my fines: {owed:N0} cr", "pay", owed <= career.Credits));
                options.Add(new("Any trouble about?", "trouble"));
                options.Add(new("Nothing. Carry on.", "close"));
                return new HailTurn(Pick(session, caller, "patrol", "Patrol here. Go ahead, captain.", "We hear you, captain. Keep it brief.", $"{Short(session, caller.FactionId)} patrol. What do you need?"), options);
            }
            case "pirate":
                return new HailTurn(Pick(session, caller, "pirate",
                    "Cut your drives and dump your cargo, and nobody needs patching up.",
                    $"Nice ship, captain. Shame about the cargo. Drop it and fly on.",
                    "Here's how this goes: your hold opens, you leave, everyone's happy."), PirateOptions(session));
            case "fence":
                return new HailTurn(Pick(session, caller, "fence",
                    "Quiet channel, captain. I buy what others won't, and sell it too.",
                    "You found me. Either you're lost or you're in the market.",
                    "Keep your voice down. What are you after?"), FenceOptions(session, caller));
            default:
                return new HailTurn(Pick(session, caller, "trader",
                    $"{caller.ShipName} here. Go ahead.",
                    "Hello, captain. Long run, this. Talk away.",
                    $"This is the {caller.ShipName}. What can we do for you?"), TraderOptions());
        }
    }

    /// <summary>Your answer, and what comes of it.</summary>
    public static HailTurn Respond(Session session, Caller caller, string action)
    {
        var career = session.Career;
        var db = session.Db;
        switch (action)
        {
            case "pay" or "pay-dock":
            {
                int owed = LawRules.Owed(career, caller.FactionId);
                if (!LawRules.Pay(career, caller.FactionId)) return new HailTurn("You don't have it. Come back when you do.", [new("Close", "close")]);
                return new HailTurn($"Received, {owed:N0} cr. Your record with {FactionName(session, caller.FactionId)} is clean." + (action == "pay-dock" ? " Pad is yours." : " Fly safe."),
                    [], action == "pay-dock" ? HailOutcome.Dock : HailOutcome.Leave);
            }
            case "later":
                return new HailTurn($"Then pay at the next {FactionName(session, caller.FactionId)} station. Don't make us ask twice.", [], HailOutcome.Leave);
            case "refuse":
                LawRules.Commit(session, "refusal");
                return new HailTurn("Wrong answer, captain. Weapons free.", [], HailOutcome.Hostile);
            case "comply":
            {
                var result = LawRules.Inspect(session, new Rng(Rng.Hash($"inspect:{caller.Key}:{session.Galaxy.Day}:{career.Credits}")));
                return new HailTurn(result.Found ? $"Well, well. {result.Text}" : $"Scan complete. {result.Text} On your way.", [], HailOutcome.Leave);
            }
            case "run":
                LawRules.Commit(session, "evasion");
                return new HailTurn("Running only makes it worse. All ships, stop that vessel.", [], HailOutcome.Hostile);
            case "trouble":
            {
                var pirates = SystemContacts.For(session, session.Here, SolarSystems.For(session.Here, db.Grammars)).Count(c => c.Role == "pirate");
                var gang = session.Galaxy.Factions.Where(f => f.Criminal && f.Alive && f.Haunts.Contains(session.Here.Id)).OrderBy(f => f.Id).FirstOrDefault();
                string text = pirates == 0 ? "Quiet today. Long may it last." : $"We're tracking {pirates} raider{(pirates == 1 ? "" : "s")} in the system. Watch yourself away from the station.";
                if (gang != null) text += $" And {gang.Name} have people here. We know it, they know we know it.";
                return new HailTurn(text, [new("Thanks. Out.", "close")]);
            }
            case "news":
            {
                var person = Speaker(session, caller);
                var (fact, _) = Conversation.PickFact(session, person, 0, (int)(Rng.Hash(caller.Key) % 7));
                return new HailTurn(fact != null ? $"Word is {fact}." : "Nothing worth repeating. Quiet out here.", TraderOptions(asked: true));
            }
            case "cargo":
            {
                var goods = db.Commodities.Values.Where(c => c.Legality == "legal").OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
                var g = goods[(int)(Rng.Hash($"{caller.Key}:cargo") % (ulong)goods.Count)];
                return new HailTurn($"{g.Name}, mostly. Margins are thin, but it's honest.", TraderOptions(asked: true));
            }
            case "dump":
            {
                int tonnes = career.Cargo.Values.Sum();
                career.Cargo.Clear();
                return new HailTurn(tonnes > 0 ? $"Pleasure doing business. {tonnes} t, thank you kindly." : "An empty hold? Fine. Not worth the ammunition.", [], HailOutcome.Leave);
            }
            case "bribe":
            {
                int price = Bribe(career);
                if (price > career.Credits) return new HailTurn("You haven't got it. Pity.", [], HailOutcome.Hostile);
                career.Credits -= price;
                return new HailTurn($"{price:N0} cr. Sensible. Off you go.", [], HailOutcome.Leave);
            }
            case "fight":
                return new HailTurn("Your funeral.", [], HailOutcome.Hostile);
            case "sell":
            {
                int paid = 0;
                foreach (var (id, tonnes) in career.Cargo.Where(c => db.Commodities[c.Key].Legality == "contraband").ToList())
                {
                    paid += (int)(db.Commodities[id].BasePrice * 1.1f) * tonnes;
                    career.Cargo.Remove(id);
                }
                career.Credits += paid;
                return new HailTurn(paid > 0 ? $"{paid:N0} cr, and no questions asked. Anything else?" : "You've nothing I want.", FenceOptions(session, caller));
            }
            case "work":
            {
                var near = session.Galaxy.WithinJumps(session.Here.Id, 4).Append(session.Here).Select(s => s.Id).ToHashSet();
                var gang = session.Galaxy.Factions.Where(f => f.Criminal && f.Alive && (caller.FactionId == f.Id || f.Haunts.Any(near.Contains)))
                    .OrderBy(f => f.Id == caller.FactionId ? 0 : 1).ThenBy(f => f.Id).FirstOrDefault();
                if (gang == null) return new HailTurn("Nobody hiring round here. Try further out.", FenceOptions(session, caller));
                int haunt = gang.Haunts.Where(near.Contains).Cast<int?>().FirstOrDefault() ?? (gang.Haunts.Count > 0 ? gang.Haunts[0] : session.Here.Id);
                var where = session.Galaxy.Systems[haunt];
                career.KnownLeads.Add($"{gang.Id}:{haunt}");
                string station = where.Stations.FirstOrDefault() ?? where.Name;
                return new HailTurn($"{gang.Name} pay well and ask little. They use the board at {station}, {where.Name}, under other names. Now you'll know which ones.", FenceOptions(session, caller));
            }
        }
        if (action.StartsWith("buy:"))
        {
            var goods = db.Commodities[action[4..]];
            int price = FencePrice(goods);
            int tonnes = Math.Min(4, Math.Min(career.FreeCargo(db), career.Credits / Math.Max(1, price)));
            if (tonnes <= 0) return new HailTurn("No room or no money. Come back with both.", FenceOptions(session, caller));
            career.Credits -= tonnes * price;
            career.Cargo[goods.Id] = career.Cargo.GetValueOrDefault(goods.Id) + tonnes;
            return new HailTurn($"{tonnes} t of {goods.Name.ToLowerInvariant()}, transferring now. Mind the customs scanners.", FenceOptions(session, caller));
        }
        return new HailTurn("", [], HailOutcome.None);
    }

    /// <summary>The crimes on your record with a faction, in words.</summary>
    public static string Record(Session session, int factionId)
    {
        var kinds = session.Career.Offences.Where(o => o.FactionId == factionId).GroupBy(o => o.Kind)
            .Select(g => (g.Count() > 1 ? $"{g.Count()} counts of " : "") + LawRules.Kinds[g.Key].Name).ToList();
        return kinds.Count == 0 ? "nothing" : kinds.Count == 1 ? kinds[0] : string.Join(", ", kinds.Take(kinds.Count - 1)) + " and " + kinds[^1];
    }

    private static List<HailOption> PoliceOptions(Session session, Caller caller, int owed)
    {
        var standing = LawRules.StandingWith(session.Career, caller.FactionId);
        var options = new List<HailOption> { new($"Pay {owed:N0} cr", "pay", owed <= session.Career.Credits) };
        if (standing == Standing.Fined) options.Add(new("I'll pay at the next station", "later"));
        options.Add(new("Refuse", "refuse"));
        return options;
    }

    private static List<HailOption> PirateOptions(Session session)
    {
        var career = session.Career;
        int bribe = Bribe(career);
        return
        [
            new(career.Cargo.Values.Sum() > 0 ? $"Dump the cargo ({career.Cargo.Values.Sum()} t)" : "My hold's empty", "dump"),
            new($"Pay them off: {bribe:N0} cr", "bribe", bribe <= career.Credits),
            new("Come and take it", "fight"),
        ];
    }

    private static int Bribe(Career career) => Math.Max(500, Math.Min(career.Credits / 10, 5000)) / 50 * 50;

    private static List<HailOption> TraderOptions(bool asked = false) =>
        asked
            ? [new("Any other news?", "news"), new("Thanks. Fly safe.", "close")]
            : [new("Any news?", "news"), new("What are you carrying?", "cargo"), new("Never mind.", "close")];

    private static int FencePrice(Content.CommodityDef goods) => (int)(goods.BasePrice * 0.85f);

    private static List<HailOption> FenceOptions(Session session, Caller caller)
    {
        var db = session.Db;
        var career = session.Career;
        var contraband = db.Commodities.Values.Where(c => c.Legality == "contraband").OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var options = new List<HailOption>();
        // Two lines on offer today.
        for (int i = 0; i < Math.Min(2, contraband.Count); i++)
        {
            var g = contraband[(int)((Rng.Hash($"{caller.Key}:stock:{i}") + (ulong)i) % (ulong)contraband.Count)];
            if (options.Any(o => o.Action == $"buy:{g.Id}")) continue;
            options.Add(new($"Buy {g.Name.ToLowerInvariant()}: {FencePrice(g):N0} cr a tonne", $"buy:{g.Id}", career.FreeCargo(db) > 0 && career.Credits >= FencePrice(g)));
        }
        int carrying = career.Cargo.Where(c => db.Commodities[c.Key].Legality == "contraband").Sum(c => c.Value);
        if (carrying > 0) options.Add(new($"Sell my contraband ({carrying} t)", "sell"));
        options.Add(new("Know anyone with work?", "work"));
        options.Add(new("Never mind.", "close"));
        return options;
    }
}
