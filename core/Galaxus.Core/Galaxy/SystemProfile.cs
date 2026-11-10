using Galaxus.Core.Content;

namespace Galaxus.Core.Galaxy;

/// <summary>How big a system's main station is: it grows with the population it serves.</summary>
public enum StationSize { Outpost, Station, Hub, Citadel }

/// <summary>The facts about a system you'd look up before going: who lives there, how many, and how safe it is.</summary>
public sealed record SystemProfile(long Population, StationSize StationSize, string Security, string Government, string? CapitalOf)
{
    public string PopulationText => Describe(Population);

    public static string Describe(long people) => people switch
    {
        0 => "uninhabited",
        >= 1_000_000_000 => $"{people / 1e9:0.#} billion",
        >= 1_000_000 => $"{people / 1e6:0.#} million",
        _ => $"{people:N0}",
    };

    /// <summary>The station model's scale against a standard station.</summary>
    public float StationScale => StationSize switch
    {
        StationSize.Outpost => 0.6f,
        StationSize.Hub => 2.2f,
        StationSize.Citadel => 4f,
        _ => 1f,
    };
}

public static class SystemProfiles
{
    /// <summary>
    /// Worked out from the system's seed, state and place, so it's the same every time. Core
    /// worlds run to billions, the rim to thousands; a great power's capital is the biggest place
    /// in its arm, with a citadel of a station to match.
    /// </summary>
    public static SystemProfile For(Galaxy galaxy, StarSystem system, ContentDatabase? db = null)
    {
        var owner = galaxy.OwnerOf(system);
        string? capitalOf = owner != null && owner.PowerId != null && owner.HomeSystem == system.Id ? owner.Name : null;
        var rng = new Rng(system.Seed ^ 0x9091A7);

        long population = 0;
        if (system.State == SystemState.Outpost)
            population = (long)Math.Pow(10, rng.Range(3f, 5.4f));
        else if (system.State == SystemState.Colony)
        {
            var (low, high) = system.Region switch
            {
                "core" => (8.5f, 10.4f),
                "inner" => (7.5f, 9.6f),
                "outer" => (6f, 8.6f),
                _ => (5f, 7.6f),
            };
            double log = rng.Range(low, high) + 0.25 * Math.Max(0, system.Stations.Count - 1);
            if (capitalOf != null) log = Math.Max(log, high) + 0.6;
            population = (long)Math.Pow(10, Math.Min(log, 10.6));
        }

        var size = capitalOf != null ? StationSize.Citadel
            : system.State == SystemState.Outpost || population < 1_000_000 ? StationSize.Outpost
            : population >= 1_500_000_000 ? StationSize.Hub
            : StationSize.Station;

        string security =
            owner == null ? "Lawless"
            : galaxy.Conflicts.Any(c => c.Stage >= 2 && c.FrontSystem == system.Id) ? "War zone"
            : galaxy.Factions.Any(f => f.Alive && f.Criminal && f.Haunts.Contains(system.Id)) ? "Low"
            : owner.Criminal ? "Lawless"
            : capitalOf != null || system.Region == "core" ? "High"
            : system.Region == "rim" ? "Low"
            : "Medium";

        string government = owner == null ? "None"
            : db != null && db.FactionTypes.TryGetValue(owner.TypeId, out var type) ? type.Name
            : owner.TypeId;
        return new SystemProfile(population, size, security, government, capitalOf);
    }
}
