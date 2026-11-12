using Galaxus.Core.Galaxy;
using Galaxus.Core.Missions;

namespace Galaxus.Core.Law;

/// <summary>A crime on your record with one faction, and the fine that clears it.</summary>
public sealed record Offence(int FactionId, string Kind, int Fine, int Day, int SystemId);

/// <summary>How a faction's law sees you.</summary>
public enum Standing
{
    /// <summary>Nothing on your record.</summary>
    Clean,
    /// <summary>Fines to pay. Patrols will ask for them; you can still dock.</summary>
    Fined,
    /// <summary>A serious crime or a fine refused. Patrols stop you, stations turn you away until you pay, with a surcharge.</summary>
    Wanted,
}

/// <summary>What a customs scan found.</summary>
/// <param name="Seized">Goods taken off you, tonnes by commodity id.</param>
/// <param name="FailedJob">A smuggling job whose cargo was found, now lost.</param>
public sealed record CustomsResult(bool Found, Dictionary<string, int> Seized, int Fine, AcceptedMission? FailedJob, string Text);

/// <summary>
/// The law. Crimes are held against the faction whose space they happen in, and they follow you
/// anywhere that faction rules, not just where you did them. Each offence carries a fine; small
/// ones leave you fined, serious ones (destroying ships, running from a stop, refusing to pay) make
/// you wanted. Paying everything you owe a faction clears your record with it. Criminal factions
/// have no police and keep no records. Customs scans happen when you dock and when a patrol stops
/// you, more often where security is high; contraband in a hidden hold is much harder to find.
/// </summary>
public static class LawRules
{
    public sealed record OffenceKind(string Name, int Fine, bool Serious);

    public static readonly Dictionary<string, OffenceKind> Kinds = new()
    {
        ["assault"] = new("firing on a lawful ship", 600, false),
        ["destruction"] = new("destroying a lawful ship", 4000, true),
        ["police-killing"] = new("destroying a patrol ship", 8000, true),
        ["smuggling"] = new("carrying contraband", 1000, false),
        ["evasion"] = new("running from a stop", 1500, true),
        ["refusal"] = new("refusing to pay a fine", 1000, true),
        ["harbouring"] = new("harbouring a wanted person", 2500, true),
    };

    /// <summary>Wanted pilots pay this much over the fines to clear their record.</summary>
    public const float WantedSurcharge = 0.5f;

    /// <summary>The lawful faction that polices a system, or null where nobody does.</summary>
    public static Faction? Police(Galaxy.Galaxy galaxy, StarSystem system) =>
        galaxy.OwnerOf(system) is { Criminal: false, Alive: true } owner ? owner : null;

    /// <summary>Puts an offence on your record with the faction that polices this system. Returns it, or null if nobody does.</summary>
    public static Offence? Commit(Session session, string kind, int fine = 0)
    {
        if (Police(session.Galaxy, session.Here) is not { } police) return null;
        var def = Kinds[kind];
        var offence = new Offence(police.Id, kind, fine > 0 ? fine : def.Fine, session.Galaxy.Day, session.Here.Id);
        var career = session.Career;
        career.Offences.Add(offence);
        career.Reputation[police.Id] = career.Reputation.GetValueOrDefault(police.Id) - (def.Serious ? 40 : 10);
        return offence;
    }

    public static Standing StandingWith(Career career, int factionId)
    {
        var record = career.Offences.Where(o => o.FactionId == factionId).ToList();
        if (record.Count == 0) return Standing.Clean;
        return record.Any(o => Kinds.TryGetValue(o.Kind, out var k) && k.Serious) ? Standing.Wanted : Standing.Fined;
    }

    /// <summary>What it costs to clear your record with a faction.</summary>
    public static int Owed(Career career, int factionId)
    {
        int fines = career.Offences.Where(o => o.FactionId == factionId).Sum(o => o.Fine);
        return StandingWith(career, factionId) == Standing.Wanted ? (int)(fines * (1 + WantedSurcharge)) / 10 * 10 : fines;
    }

    /// <summary>Pays off your record with a faction, if you can afford it.</summary>
    public static bool Pay(Career career, int factionId)
    {
        int owed = Owed(career, factionId);
        if (owed <= 0 || owed > career.Credits) return false;
        career.Credits -= owed;
        career.Offences.RemoveAll(o => o.FactionId == factionId);
        return true;
    }

    /// <summary>How you stand with whoever polices this system.</summary>
    public static Standing StandingHere(Session session) =>
        Police(session.Galaxy, session.Here) is { } police ? StandingWith(session.Career, police.Id) : Standing.Clean;

    // Customs ----------------------------------------------------------------------------------

    /// <summary>Chance a customs scan happens when you dock here.</summary>
    public static float DockScanChance(Session session)
    {
        if (Police(session.Galaxy, session.Here) == null) return 0;
        var security = SystemProfiles.For(session.Galaxy, session.Here, session.Db).Security;
        return security switch { "High" => 0.35f, "Medium" => 0.2f, "War zone" => 0.15f, "Low" => 0.07f, _ => 0 };
    }

    /// <summary>Whether goods are illegal to carry here: contraband anywhere policed, restricted goods where security is high.</summary>
    public static bool Illegal(Session session, Content.CommodityDef goods)
    {
        if (Police(session.Galaxy, session.Here) == null) return false;
        if (goods.Legality == "contraband") return true;
        return goods.Legality == "restricted" && SystemProfiles.For(session.Galaxy, session.Here, session.Db).Security == "High";
    }

    /// <summary>Illegal goods you're carrying here, in the hold and in jobs, by commodity id.</summary>
    public static Dictionary<string, int> IllegalCargo(Session session)
    {
        var db = session.Db;
        var found = new Dictionary<string, int>();
        foreach (var (id, tonnes) in session.Career.Cargo)
            if (tonnes > 0 && db.Commodities.TryGetValue(id, out var c) && Illegal(session, c)) found[id] = tonnes;
        foreach (var job in session.Career.Missions)
            if (job.Offer.Mission.Cargo is { } cargo && job.Offer.Mission.Tonnes > 0 && Illegal(session, cargo))
                found[cargo.Id] = found.GetValueOrDefault(cargo.Id) + job.Offer.Mission.Tonnes;
        return found;
    }

    /// <summary>
    /// A customs scan of your hold. Illegal goods in the open are almost always found; what fits in
    /// a hidden hold is found only now and then. Found goods are seized (a job's cargo too, which
    /// loses the job) and the fine is their value, at least the standard smuggling fine.
    /// </summary>
    public static CustomsResult Inspect(Session session, Rng rng)
    {
        var goods = InspectGoods(session, rng);
        // People are harder to hide than crates: a wanted passenger is found half the time, and a
        // hidden hold big enough to lie down in helps.
        var wanted = Missions.Passengers.WantedAboard(session);
        if (wanted.Count == 0) return goods;
        var career = session.Career;
        var spec = Ships.ShipFactory.Build(session.Db, career.HullId, career.Loadout);
        if (rng.Fork("people").NextFloat() >= (spec.HiddenTonnes >= 2 ? 0.2f : 0.55f)) return goods;

        foreach (var job in wanted)
        {
            career.Missions.Remove(job);
            career.Reputation[job.Offer.GiverFactionId] = career.Reputation.GetValueOrDefault(job.Offer.GiverFactionId) - 30;
            if (career.ActiveJobId == job.Offer.Id) career.ActiveJobId = null;
        }
        var police = Police(session.Galaxy, session.Here)!;
        var offence = Commit(session, "harbouring");
        string names = string.Join(" and ", wanted.Select(j => j.Offer.Mission.Passenger!.Name));
        string text = $"They found {names} aboard, wanted by {police.Name}, and took {(wanted.Count == 1 ? "them" : "them all")} off the ship. " +
                      $"Harbouring a wanted person: {offence?.Fine ?? 0:N0} cr, owed to {police.Name}.";
        return new CustomsResult(true, goods.Seized, goods.Fine + (offence?.Fine ?? 0), goods.FailedJob ?? wanted[0],
            goods.Found ? goods.Text + " " + text : text);
    }

    private static CustomsResult InspectGoods(Session session, Rng rng)
    {
        var db = session.Db;
        var career = session.Career;
        var illegal = IllegalCargo(session);
        if (illegal.Count == 0) return new CustomsResult(false, [], 0, null, "The scan finds nothing to interest them.");
        var spec = Ships.ShipFactory.Build(db, career.HullId, career.Loadout);
        int total = illegal.Values.Sum();
        int open = Math.Max(0, total - spec.HiddenTonnes);
        float chance = open > 0 ? 0.9f : 0.12f;
        if (rng.NextFloat() >= chance)
            return new CustomsResult(false, [], 0, null, open > 0 ? "The scan misses what you're carrying. You got lucky." : "The scan passes over the hidden hold.");

        int value = illegal.Sum(g => db.Commodities[g.Key].BasePrice * g.Value);
        foreach (var (id, _) in illegal) career.Cargo.Remove(id);
        var job = career.Missions.FirstOrDefault(m => m.Offer.Mission.Cargo is { } c && illegal.ContainsKey(c.Id) && m.Offer.Mission.Tonnes > 0);
        if (job != null)
        {
            career.Missions.Remove(job);
            career.Reputation[job.Offer.GiverFactionId] = career.Reputation.GetValueOrDefault(job.Offer.GiverFactionId) - 30;
            if (career.ActiveJobId == job.Offer.Id) career.ActiveJobId = null;
        }
        int fine = Math.Max(Kinds["smuggling"].Fine, value / 10 * 10);
        Commit(session, "smuggling", fine);
        string goods = string.Join(", ", illegal.Select(g => $"{g.Value} t of {db.Commodities[g.Key].Name.ToLowerInvariant()}"));
        string text = $"They found {goods} and seized it. Fine: {fine:N0} cr, owed to {Police(session.Galaxy, session.Here)!.Name}." +
                      (job != null ? $" The job \"{job.Offer.Mission.Title}\" is lost with it." : "");
        return new CustomsResult(true, illegal, fine, job, text);
    }

    // The underworld ---------------------------------------------------------------------------

    /// <summary>
    /// Whether you know how to find a criminal faction's work: someone told you where they hang
    /// out, or they already know you.
    /// </summary>
    public static bool KnowsUnderworld(Career career, int factionId) =>
        career.KnownLeads.Any(l => l.StartsWith($"{factionId}:")) || career.RankWith(factionId) >= 1;
}
