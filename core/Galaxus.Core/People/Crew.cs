using Galaxus.Core.Content;

namespace Galaxus.Core.People;

/// <summary>Someone working for you: a wingman flying their own ship, or crew in a berth aboard yours.</summary>
public sealed class CrewMember
{
    public string PersonId { get; set; } = "";
    public string Name { get; set; } = "";
    public string RaceId { get; set; } = "human";
    /// <summary>pilot (a wingman), engineer, gunner or navigator.</summary>
    public string Job { get; set; } = "pilot";
    /// <summary>Skill when hired, 0 to 1. Experience adds to it.</summary>
    public float BaseSkill { get; set; }
    /// <summary>Ships destroyed while flying with you.</summary>
    public int Kills { get; set; }
    /// <summary>Wage in credits a day.</summary>
    public int Wage { get; set; }
    public int HiredDay { get; set; }
    /// <summary>A wingman's ship.</summary>
    public string? HullId { get; set; }
    /// <summary>Shot down and hurt: sits out until you next dock.</summary>
    public bool Injured { get; set; }

    public float Skill => MathF.Min(0.95f, BaseSkill + Kills * 0.025f);
}

/// <summary>The terms someone would join you on.</summary>
public sealed record CrewOffer(string Job, float Skill, int Fee, int Wage, string? HullId);

/// <summary>
/// Hiring and keeping crew. Pilots and mercenaries fly as wingmen in a fighter of their own, up to
/// two of them; engineers, gunners and navigators take a berth aboard your ship and help a little
/// (shields, damage, jump range and fuel). Everyone takes a fee up front and a daily wage, paid
/// when you dock; anyone you can't pay leaves. Wingmen get better with every kill and can die
/// when their ship goes down.
/// </summary>
public static class CrewRules
{
    public const int MaxWingmen = 2;

    /// <summary>The job someone would do for you, from their trade, or null if they wouldn't.</summary>
    public static string? JobFor(Person person) => person.RoleId switch
    {
        "pilot" or "mercenary" => "pilot",
        "engineer" or "miner" or "dockhand" => "engineer",
        "enforcer" => "gunner",
        "scholar" or "gate-warden" or "smuggler" => "navigator",
        _ => null,
    };

    public static string JobName(string job) => job switch { "pilot" => "wingman", _ => job };

    /// <summary>What they'd want to join you, worked out from who they are.</summary>
    public static CrewOffer? Offer(Session session, Person person)
    {
        if (JobFor(person) is not { } job) return null;
        var rng = new Rng(person.Seed ^ 0xC2E3);
        float skill = 0.3f + rng.NextFloat() * 0.45f;
        if (person.RoleId == "mercenary") skill += 0.1f;
        int wage = job == "pilot" ? 60 + (int)(skill * 140) : 25 + (int)(skill * 60);
        wage = wage / 5 * 5;
        string? hull = job == "pilot" ? WingmanHull(session.Db, person, rng) : null;
        return new CrewOffer(job, MathF.Min(skill, 0.9f), wage * 10, wage, hull);
    }

    /// <summary>A pilot's own fighter: one their people build, or any fighter on sale.</summary>
    private static string? WingmanHull(ContentDatabase db, Person person, Rng rng)
    {
        var fighters = db.Hulls.Values.Where(h => h.Class == "fighter" && h.ForSale && db.Builders.TryGetValue(h.Builder, out var b) && b.Race == person.RaceId)
            .OrderBy(h => h.Id, StringComparer.Ordinal).ToList();
        if (fighters.Count == 0) fighters = [.. db.Hulls.Values.Where(h => h.Class == "fighter" && h.ForSale).OrderBy(h => h.Price).Take(6).OrderBy(h => h.Id, StringComparer.Ordinal)];
        return fighters.Count == 0 ? null : rng.Pick(fighters).Id;
    }

    /// <summary>Berths for crew aboard your ship (wingmen fly their own).</summary>
    public static int Berths(Session session) => session.Db.Hulls[session.Career.HullId].CrewBerths;

    /// <summary>Why they can't be hired, or null if they can.</summary>
    public static string? CannotHire(Session session, Person person)
    {
        var career = session.Career;
        if (career.Crew.Any(c => c.PersonId == person.Id)) return "Already working for you.";
        if (Offer(session, person) is not { } offer) return "Not looking for that kind of work.";
        if (offer.Job == "pilot")
        {
            if (offer.HullId == null) return "Has no ship to fly.";
            if (career.Crew.Count(c => c.Job == "pilot") >= MaxWingmen) return $"You already have {MaxWingmen} wingmen.";
        }
        else if (career.Crew.Count(c => c.Job != "pilot") >= Berths(session)) return $"No free berth: your ship has {Berths(session)}.";
        if (Conversation.Disposition(career, person) < 0) return "They don't want to work for you.";
        if (offer.Fee > career.Credits) return $"You need {offer.Fee:N0} cr up front.";
        return null;
    }

    public static CrewMember? Hire(Session session, Person person)
    {
        if (CannotHire(session, person) != null || Offer(session, person) is not { } offer) return null;
        var career = session.Career;
        career.Credits -= offer.Fee;
        var member = new CrewMember
        {
            PersonId = person.Id, Name = person.Name, RaceId = person.RaceId, Job = offer.Job, BaseSkill = offer.Skill,
            Wage = offer.Wage, HiredDay = session.Galaxy.Day, HullId = offer.HullId,
        };
        career.Crew.Add(member);
        if (career.WagesPaidDay < 0) career.WagesPaidDay = session.Galaxy.Day;
        return member;
    }

    public static void Dismiss(Career career, CrewMember member) => career.Crew.Remove(member);

    /// <summary>
    /// Pays wages owed since you last paid, most valued first. Anyone you can't pay leaves. The
    /// injured are back on their feet. Returns what happened, in a line, or null for nothing.
    /// </summary>
    public static string? PayWages(Session session)
    {
        var career = session.Career;
        int day = session.Galaxy.Day;
        foreach (var c in career.Crew) c.Injured = false;
        if (career.Crew.Count == 0) { career.WagesPaidDay = day; return null; }
        int days = Math.Max(0, day - career.WagesPaidDay);
        career.WagesPaidDay = day;
        if (days == 0) return null;
        int paid = 0;
        var left = new List<string>();
        foreach (var c in career.Crew.OrderByDescending(c => c.Skill).ToList())
        {
            int owed = c.Wage * days;
            if (owed <= career.Credits) { career.Credits -= owed; paid += owed; }
            else { career.Crew.Remove(c); left.Add(c.Name); }
        }
        string text = paid > 0 ? $"Paid your crew {paid:N0} cr for {days} day{(days == 1 ? "" : "s")}." : "";
        if (left.Count > 0) text += $" Couldn't pay {string.Join(" and ", left)}, who left.";
        return text.Trim();
    }

    /// <summary>What your crew aboard adds: the best in each job counts.</summary>
    public static CrewBonus Bonus(Career career)
    {
        float Best(string job) => career.Crew.Where(c => c.Job == job && !c.Injured).Select(c => c.Skill).DefaultIfEmpty(0).Max();
        return new CrewBonus(1 + Best("engineer") * 0.3f, 1 + Best("gunner") * 0.2f, 1 + Best("navigator") * 0.2f, 1 - Best("navigator") * 0.2f);
    }

    /// <summary>A wingman's ship went down. Some get out; the rest don't. Returns true if they died.</summary>
    public static bool ShotDown(Career career, CrewMember member, Rng rng)
    {
        if (rng.NextFloat() < 0.45f)
        {
            career.Crew.Remove(member);
            return true;
        }
        member.Injured = true;
        return false;
    }
}

/// <summary>Multipliers from crew aboard: shield recharge, damage dealt, jump range, and fuel used on direct jumps.</summary>
public sealed record CrewBonus(float Shields, float Damage, float JumpRange, float JumpFuel);
