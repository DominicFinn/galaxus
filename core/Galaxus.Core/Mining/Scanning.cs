using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Ships;

namespace Galaxus.Core.Mining;

/// <summary>Something you found by scanning, kept in the logbook.</summary>
public sealed record LogEntry(int Day, int SystemId, string Title, string Text, string? FindId);

/// <summary>A lead to a cache: scan that body in that system to find it.</summary>
public sealed record Trail(string CacheId, int SystemId, int Body, int Day);

/// <summary>Survey data you hold and haven't sold: what it was and what it's worth.</summary>
public sealed record SurveyRecord(int SystemId, string Subject, int Value, int Day);

/// <summary>What a finished scan says.</summary>
/// <param name="Find">A find, if this scan turned one up.</param>
/// <param name="SurveyValue">Credits the survey data is worth, if this is the first scan of it.</param>
public sealed record ScanResult(string Title, List<string> Lines, LogEntry? Find, int SurveyValue);

/// <summary>
/// The scanner. Point it at a rock, a ship, a planet, a moon or the star and hold it there: rocks
/// tell you their ore, ships their cargo, and bodies give survey data you can sell at any station.
/// Anything can carry a find (a wreck, a beacon, a seam, a stash), and some finds are leads that
/// point at a cache somewhere else. Everything is worked out from the galaxy seed, so finds are
/// always in the same places; what you've scanned and found is kept in the career.
/// </summary>
public static class Scanning
{
    /// <summary>The star, as a scan subject.</summary>
    public const int Star = -1;

    /// <summary>How long a scan takes. Scanner gear with a "detail" rating speeds it up.</summary>
    public static float Seconds(string subject, ShipSpec spec)
    {
        float basic = subject switch { "rock" => 1.5f, "ship" => 4, "star" => 6, _ => 8 };
        return basic / (1 + spec.ScanDetail * 0.5f);
    }

    // Bodies -----------------------------------------------------------------------------------

    public static ScanResult Body(Session session, SolarSystem solar, int index)
    {
        var system = session.Here;
        var career = session.Career;
        string key = $"{system.Id}:body:{index}";
        bool first = career.Scanned.Add(key);
        var lines = new List<string>();
        string title;
        int value;
        if (index == Star)
        {
            var star = solar.Star;
            title = $"{system.Name}, the star";
            lines.Add($"{star.Description}, {star.Temperature:N0} K, {star.Mass:0.00} solar masses, {star.Luminosity:0.###} times the Sun's light.");
            value = (int)(600 * StarRarity(star.Class) * Remoteness(session, system));
        }
        else
        {
            var b = solar.Bodies[index];
            title = b.Name;
            double radiusKm = solar.RadiusKm(index);
            float gravity = b.Radius > 0 ? b.Mass / (b.Radius * b.Radius) : 0;
            lines.Add($"{KindName(b.Kind)}{(b.IsMoon ? $", a moon of {solar.Bodies[b.Parent].Name}" : "")}. Radius {radiusKm:N0} km, gravity {gravity:0.00} g, {b.Temperature - 273:N0} °C.");
            lines.Add(Resources(b.Kind));
            if (b.Habitable) lines.Add("Liquid water and a breathable chance: the kind of world people settle.");
            value = (int)(BodyValue(b.Kind) * (b.IsMoon ? 0.7f : 1) * Remoteness(session, system));
        }

        var find = FindOnBody(session, solar, index, first);
        if (!first) value = 0;
        if (value > 0)
        {
            career.SurveyData.Add(new SurveyRecord(system.Id, title, value, session.Galaxy.Day));
            lines.Add($"Survey data worth about {value:N0} cr at any station.");
        }
        else lines.Add("You've surveyed this already.");
        return new ScanResult(title, lines, find, value);
    }

    /// <summary>Survey data pays more for rarer stars and for systems off the gates, and less where people already live.</summary>
    private static float Remoteness(Session session, StarSystem system) =>
        (system.IsGateSystem ? 1 : 3) * (system.Settled ? 0.4f : 1);

    private static float StarRarity(StarClass c) => c switch
    {
        StarClass.B => 3, StarClass.A => 2, StarClass.WhiteDwarf => 2.5f, StarClass.F => 1.4f, _ => 1,
    };

    private static int BodyValue(BodyKind kind) => kind switch
    {
        BodyKind.Terran => 1500, BodyKind.Ocean => 1200, BodyKind.Toxic => 600, BodyKind.Ice => 500, BodyKind.Desert => 500,
        BodyKind.Molten => 450, BodyKind.IceGiant => 450, BodyKind.GasGiant => 400, _ => 300,
    };

    private static string KindName(BodyKind kind) => kind switch
    {
        BodyKind.GasGiant => "Gas giant", BodyKind.IceGiant => "Ice giant", BodyKind.Molten => "Molten world", BodyKind.Barren => "Barren rock",
        BodyKind.Terran => "Earth-like world", BodyKind.Ocean => "Ocean world", BodyKind.Ice => "Ice world", BodyKind.Toxic => "Toxic world",
        BodyKind.Desert => "Desert world", _ => "Asteroid belt",
    };

    private static string Resources(BodyKind kind) => kind switch
    {
        BodyKind.Barren => "Surface rich in iron and nickel, with titanium in the old craters.",
        BodyKind.Molten => "Heavy metals near the surface: tungsten and rare earths in the cooling flows.",
        BodyKind.Desert => "Silica sand and bauxite, with copper in the dry beds.",
        BodyKind.Ice => "Water ice to great depth, with heavy water in the deep layers.",
        BodyKind.Toxic => "A corrosive atmosphere over sulphur and carbon compounds.",
        BodyKind.Ocean => "A world ocean with lithium salts in the shallows.",
        BodyKind.Terran => "Soil, water and life of its own.",
        BodyKind.GasGiant => "Hydrogen and helium, with fuel-grade gas in the upper cloud.",
        BodyKind.IceGiant => "Water, ammonia and methane ices under a thin hydrogen sky.",
        _ => "Rock and ice.",
    };

    // Rocks ------------------------------------------------------------------------------------

    public static ScanResult Rock(Session session, Rock rock)
    {
        var career = session.Career;
        bool first = career.Scanned.Add($"{session.Here.Id}:rock:{rock.Id}");
        var ore = session.Db.Commodities[rock.OreId];
        int left = RockLeft(career, rock);
        var lines = new List<string>
        {
            $"{ore.Name}: {left:N0} t of {rock.Tonnes:N0} t left, {rock.RadiusM * 2:0} m across.",
            rock.Richness > 1.2f ? "Rich: it cuts fast." : rock.Richness < 0.75f ? "Poor: slow cutting." : "Average yield.",
        };
        LogEntry? find = null;
        if (first)
        {
            var rng = new Rng(Rng.Hash("rock-find:" + rock.Id));
            if (rng.NextFloat() < 0.025f) find = Award(session, Pick(session, "rock", rng), $"a {ore.Name.ToLowerInvariant()} rock", rng);
        }
        return new ScanResult("Asteroid", lines, find, 0);
    }

    /// <summary>Tonnes still in a rock after what you've cut from it.</summary>
    public static int RockLeft(Career career, Rock rock) => Math.Max(0, rock.Tonnes - career.Mined.GetValueOrDefault(rock.Id));

    // Ships ------------------------------------------------------------------------------------

    /// <param name="key">Something that tells this ship apart today, such as its name and id.</param>
    public static ScanResult Ship(Session session, string name, HullDef hull, string key)
    {
        var db = session.Db;
        var career = session.Career;
        bool first = career.Scanned.Add($"ship:{session.Galaxy.Day}:{key}");
        var rng = new Rng(Rng.Hash($"ship-scan:{session.Galaxy.Day}:{key}"));
        var lines = new List<string> { $"{hull.Name}, a {hull.Class} by {(db.Builders.TryGetValue(hull.Builder, out var b) ? b.Name : hull.Builder)}." };
        int hold = Math.Max(0, ShipFactory.Build(db, hull.Id).CargoTonnes);
        if (hold == 0 || rng.NextFloat() < 0.15f) lines.Add("Hold empty.");
        else
        {
            var legal = db.Commodities.Values.Where(c => c.Legality != "contraband").OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            var goods = Enumerable.Range(0, 1 + rng.Next(3)).Select(_ => rng.Pick(legal)).Distinct().ToList();
            if (rng.NextFloat() < 0.08f) goods.Add(rng.Pick(db.Commodities.Values.Where(c => c.Legality == "contraband").OrderBy(c => c.Id, StringComparer.Ordinal).ToList()));
            int room = hold;
            var parts = new List<string>();
            foreach (var g in goods)
            {
                int t = Math.Max(1, (int)(room * rng.Range(0.2f, 0.6f)));
                room -= t;
                parts.Add($"{t} t of {g.Name.ToLowerInvariant()}" + (g.Legality == "contraband" ? " (contraband)" : ""));
            }
            lines.Add("Carrying " + string.Join(", ", parts) + ".");
        }
        LogEntry? find = null;
        if (first && rng.NextFloat() < 0.05f) find = Award(session, Pick(session, "ship", rng), name, rng);
        return new ScanResult(name, lines, find, 0);
    }

    // Finds ------------------------------------------------------------------------------------

    /// <summary>A find on a body: a cache you have a lead to, or else the body's own find, once.</summary>
    private static LogEntry? FindOnBody(Session session, SolarSystem solar, int index, bool first)
    {
        var career = session.Career;
        var system = session.Here;
        string name = index == Star ? $"{system.Name}'s star" : solar.Bodies[index].Name;
        var trail = career.Trails.FirstOrDefault(t => t.SystemId == system.Id && t.Body == index);
        if (trail != null && session.Db.Finds.TryGetValue(trail.CacheId, out var cache))
        {
            career.Trails.Remove(trail);
            return Award(session, cache, name, new Rng(Rng.Hash($"cache:{system.Id}:{index}")));
        }
        if (!first || index == Star) return null;
        var rng = new Rng(system.Seed ^ Rng.Hash($"find:{index}"));
        if (rng.NextFloat() >= 0.08f) return null;
        return Award(session, Pick(session, "body", rng), name, rng);
    }

    private static FindDef? Pick(Session session, string where, Rng rng)
    {
        var region = session.Here.Region;
        var options = session.Db.Finds.Values.Where(f => f.Where.Contains(where) && (f.Regions.Count == 0 || f.Regions.Contains(region)))
            .OrderBy(f => f.Id, StringComparer.Ordinal).ToList();
        if (options.Count == 0) return null;
        float roll = rng.NextFloat() * options.Sum(f => f.Weight);
        foreach (var f in options) { roll -= f.Weight; if (roll <= 0) return f; }
        return options[^1];
    }

    /// <summary>Pays out a find, lays any lead it gives, and writes it in the logbook.</summary>
    private static LogEntry? Award(Session session, FindDef? find, string where, Rng rng)
    {
        if (find == null) return null;
        var career = session.Career;
        var galaxy = session.Galaxy;
        string text = find.Text.Replace("{system}", session.Here.Name).Replace("{body}", where);
        if (find.LeadsTo != null && LeadTarget(session, rng) is { } lead)
        {
            career.Trails.Add(new Trail(find.LeadsTo, lead.System.Id, lead.Body, galaxy.Day));
            text = text.Replace("{target}", lead.System.Name).Replace("{targetBody}", lead.BodyName);
        }
        else text = text.Replace("{targetBody}, {target}", "somewhere the log no longer says").Replace("{target}", "somewhere").Replace("{targetBody}", "a body");

        var rewards = new List<string>();
        if (find.Credits > 0) { career.Credits += find.Credits; rewards.Add($"{find.Credits:N0} cr"); }
        foreach (var (goods, tonnes) in find.Goods.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            int t = Math.Min(tonnes, career.FreeCargo(session.Db));
            if (t <= 0) { rewards.Add($"no room for the {session.Db.Commodities[goods].Name.ToLowerInvariant()}"); continue; }
            career.Cargo[goods] = career.Cargo.GetValueOrDefault(goods) + t;
            rewards.Add($"{t} t of {session.Db.Commodities[goods].Name.ToLowerInvariant()}");
        }
        if (rewards.Count > 0) text += $" (Recovered: {string.Join(", ", rewards)}.)";
        var entry = new LogEntry(galaxy.Day, session.Here.Id, find.Title, text, find.Id);
        career.Logbook.Add(entry);
        return entry;
    }

    /// <summary>A body two to six gates away for a lead to point at.</summary>
    private static (StarSystem System, int Body, string BodyName)? LeadTarget(Session session, Rng rng)
    {
        var galaxy = session.Galaxy;
        var near = galaxy.WithinJumps(session.Here.Id, 6).Where(s => s.Id != session.Here.Id && galaxy.Jumps(session.Here.Id, s.Id, 6) >= 2)
            .OrderBy(s => s.Id).ToList();
        if (near.Count == 0) near = galaxy.WithinJumps(session.Here.Id, 3).Where(s => s.Id != session.Here.Id).OrderBy(s => s.Id).ToList();
        if (near.Count == 0) return null;
        var system = rng.Pick(near);
        var solar = SolarSystems.For(system, session.Db.Grammars);
        var bodies = Enumerable.Range(0, solar.Bodies.Count).Where(i => solar.Bodies[i].Kind is not (BodyKind.Belt or BodyKind.GasGiant or BodyKind.IceGiant)).ToList();
        if (bodies.Count == 0) return null;
        int body = rng.Pick(bodies);
        return (system, body, solar.Bodies[body].Name);
    }

    /// <summary>
    /// Cuts ore from a rock into the hold, up to what's left in it and what you have room for.
    /// Returns the tonnes cut.
    /// </summary>
    public static int Cut(Session session, Rock rock, int tonnes)
    {
        var career = session.Career;
        int t = Math.Min(tonnes, Math.Min(RockLeft(career, rock), career.FreeCargo(session.Db)));
        if (t <= 0) return 0;
        career.Mined[rock.Id] = career.Mined.GetValueOrDefault(rock.Id) + t;
        career.Cargo[rock.OreId] = career.Cargo.GetValueOrDefault(rock.OreId) + t;
        return t;
    }

    /// <summary>What your unsold survey data would fetch.</summary>
    public static int SurveyWorth(Career career) => career.SurveyData.Sum(s => s.Value);

    /// <summary>Sells all your survey data. Returns what it paid.</summary>
    public static int SellSurvey(Career career)
    {
        int worth = SurveyWorth(career);
        career.Credits += worth;
        career.SurveyData.Clear();
        return worth;
    }
}
