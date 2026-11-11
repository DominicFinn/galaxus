using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Mining;
using Galaxus.Core.People;

namespace Galaxus.Core.Lore;

/// <summary>A lore entry you were told: when, where and by whom.</summary>
public sealed record LoreRecord(string Id, int Day, int SystemId, string From);

/// <summary>
/// Lore and treasure hunts. People in bars sometimes pass on a piece of history or a legend, more
/// readily the more they like you, and in their own race's way; those go in the logbook. Now and
/// then they pass on a piece of a clue chain instead, and scans turn some up too. Three pieces
/// narrow a treasure from a region to a system to a body; with all three, the body goes in your
/// leads and scanning it finds the cache. Where each treasure lies comes from the galaxy seed.
/// </summary>
public static class Tales
{
    /// <summary>Where a clue chain's treasure is, the same for every save of this galaxy.</summary>
    public static (StarSystem System, int Body)? Target(Session session, ClueDef clue)
    {
        var galaxy = session.Galaxy;
        var candidates = galaxy.Systems.Where(s => s.IsGateSystem && clue.Regions.Contains(s.Region)).OrderBy(s => s.Id).ToList();
        if (candidates.Count == 0) return null;
        var rng = new Rng(galaxy.Seed ^ Rng.Hash("clue:" + clue.Id));
        for (int tries = 0; tries < 20; tries++)
        {
            var system = rng.Pick(candidates);
            var solar = SolarSystems.For(system, session.Db.Grammars);
            var bodies = Enumerable.Range(0, solar.Bodies.Count).Where(i => solar.Bodies[i].Kind is not (BodyKind.Belt or BodyKind.GasGiant or BodyKind.IceGiant)).ToList();
            if (bodies.Count > 0) return (system, rng.Pick(bodies));
        }
        return null;
    }

    /// <summary>A piece of a clue chain in words, with the place filled in.</summary>
    public static string Fragment(Session session, ClueDef clue, int index)
    {
        if (Target(session, clue) is not { } target) return "";
        var solar = SolarSystems.For(target.System, session.Db.Grammars);
        var body = solar.Bodies[target.Body];
        string kind = body.Kind switch
        {
            BodyKind.Molten => "molten world", BodyKind.Barren => "barren rock", BodyKind.Desert => "desert world", BodyKind.Terran => "green world",
            BodyKind.Ocean => "ocean world", BodyKind.Ice => "ice world", BodyKind.Toxic => "toxic world", _ => "world",
        };
        if (body.IsMoon) kind = $"moon of {solar.Bodies[body.Parent].Name}";
        return clue.Fragments[index]
            .Replace("{arm}", target.System.Arm).Replace("{region}", target.System.Region)
            .Replace("{system}", target.System.Name).Replace("{star}", solar.Star.Description.ToLowerInvariant())
            .Replace("{body}", body.Name).Replace("{kind}", kind);
    }

    /// <summary>Gives the next piece of a clue chain. With the last piece, the body goes in your leads. Returns the piece.</summary>
    public static string? Advance(Session session, ClueDef clue)
    {
        var career = session.Career;
        int known = career.Clues.GetValueOrDefault(clue.Id);
        if (known >= clue.Fragments.Count || Target(session, clue) is not { } target) return null;
        career.Clues[clue.Id] = known + 1;
        if (known + 1 == clue.Fragments.Count && career.Trails.All(t => t.CacheId != clue.Payoff))
            career.Trails.Add(new Trail(clue.Payoff, target.System.Id, target.Body, session.Galaxy.Day));
        return Fragment(session, clue, known);
    }

    /// <summary>The chain to hear about next: one you've started, else a new one.</summary>
    private static ClueDef? NextChain(Session session, Rng rng)
    {
        var career = session.Career;
        var clues = session.Db.Clues.Values.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var started = clues.FirstOrDefault(c => career.Clues.GetValueOrDefault(c.Id) is > 0 and < 3);
        if (started != null) return started;
        var fresh = clues.Where(c => !career.Clues.ContainsKey(c.Id)).ToList();
        return fresh.Count > 0 ? rng.Pick(fresh) : null;
    }

    /// <summary>
    /// Something extra a person might tell you while gossiping: a lore entry or a piece of a
    /// clue. Returns the line to add to what they said, or null.
    /// </summary>
    public static string? FromGossip(Session session, Person person, int disposition, int turn)
    {
        if (disposition < 0) return null;
        var rng = new Rng(person.Seed ^ Rng.Hash($"tale:{session.Galaxy.Day}:{turn}"));
        float chance = 0.25f + Math.Min(disposition, 60) / 200f;
        if (rng.NextFloat() >= chance) return null;
        var career = session.Career;
        var here = session.Here;

        if (rng.NextFloat() < 0.35f && NextChain(session, rng) is { } clue && Advance(session, clue) is { } piece)
            return $"Then, quieter: \"{piece}\" (A clue: {clue.Title}. See your logbook.)";

        var known = career.Lore.Select(l => l.Id).ToHashSet();
        var tales = session.Db.Lore.Values
            .Where(l => !known.Contains(l.Id) && (l.Tellers.Count == 0 || l.Tellers.Contains(person.RaceId)) && (l.Regions.Count == 0 || l.Regions.Contains(here.Region)))
            .OrderBy(l => l.Id, StringComparer.Ordinal).ToList();
        if (tales.Count == 0) return null;
        var tale = rng.Pick(tales);
        career.Lore.Add(new LoreRecord(tale.Id, session.Galaxy.Day, here.Id, person.Name));
        return $"And they tell you about {tale.Title.ToLowerInvariant()}. (Added to your logbook.)";
    }

    /// <summary>A scan of a new world sometimes turns up a piece of a clue: an old log, a marker. Returns the piece, or null.</summary>
    public static string? FromScan(Session session, string subject)
    {
        var rng = new Rng(Rng.Hash($"scan-clue:{session.Here.Id}:{subject}"));
        if (rng.NextFloat() >= 0.06f) return null;
        return NextChain(session, rng) is { } clue && Advance(session, clue) is { } piece ? $"{clue.Title}: {piece}" : null;
    }
}
