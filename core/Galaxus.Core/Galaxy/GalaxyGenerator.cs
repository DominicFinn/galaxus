using System.Numerics;
using Galaxus.Core.Content;

namespace Galaxus.Core.Galaxy;

public sealed class GalaxySettings
{
    public int SystemCount { get; init; } = 1400;
    /// <summary>Disc radius in light years.</summary>
    public float Radius { get; init; } = 1000f;
    public int MinorFactions { get; init; } = 160;
    /// <summary>Region boundaries as distances from the core.</summary>
    public float CoreEdge { get; init; } = 180f;
    public float InnerEdge { get; init; } = 450f;
    public float OuterEdge { get; init; } = 800f;
}

/// <summary>
/// Builds a galaxy from a seed: a four-armed spiral of systems, a gate network linking them,
/// the great powers in their home arms and a few hundred smaller factions around them.
/// The same seed always gives the same galaxy.
/// </summary>
public static class GalaxyGenerator
{
    private const float MinSpacing = 14f;
    private const float GateRange = 130f;

    public static Galaxy Generate(ContentDatabase db, ulong seed, GalaxySettings? settings = null)
    {
        settings ??= new GalaxySettings();
        var rng = new Rng(seed);
        var galaxy = new Galaxy { Seed = seed };

        PlaceSystems(galaxy, db, settings, rng.Fork("systems"));
        BuildGates(galaxy);
        SeedPowers(galaxy, db, rng.Fork("powers"));
        SeedMinorFactions(galaxy, db, settings, rng.Fork("factions"));
        FinishSystems(galaxy, db, rng.Fork("finish"));
        PlaceLandmarks(galaxy, db, seed);
        FoundCampaignFactions(galaxy, db, seed);
        return galaxy;
    }

    /// <summary>
    /// Puts mods' landmarks on the map: each picks a system in its region from the seed and its own
    /// id, so it lands in the same place every time, takes the landmark's name, and gets a station
    /// and an owner if the landmark asks for them.
    /// </summary>
    private static void PlaceLandmarks(Galaxy galaxy, ContentDatabase db, ulong seed)
    {
        var taken = new HashSet<int>();
        foreach (var landmark in db.Landmarks.Values.OrderBy(l => l.Id, StringComparer.Ordinal))
        {
            var rng = new Rng(seed ^ Rng.Hash("landmark:" + landmark.Id));
            var inRegion = galaxy.Systems.Where(s => s.IsGateSystem && s.Region == landmark.Region && !taken.Contains(s.Id)
                                                     && (landmark.Arm == null || s.Arm == landmark.Arm)).ToList();
            if (inRegion.Count == 0) inRegion = galaxy.Systems.Where(s => s.IsGateSystem && !taken.Contains(s.Id)).ToList();
            if (inRegion.Count == 0) continue;

            // Whoever it should belong to: a great power, or a faction of a type (founded here if there's none).
            Faction? owner = null;
            FactionTypeDef? ownerType = null;
            if (landmark.OwnerFactionType != null)
            {
                owner = galaxy.Factions.FirstOrDefault(f => f.Alive && f.PowerId == landmark.OwnerFactionType);
                if (owner == null && db.FactionTypes.TryGetValue(landmark.OwnerFactionType, out ownerType))
                    owner = galaxy.Factions.Where(f => f.Alive && f.TypeId == ownerType.Id).OrderByDescending(f => galaxy.OwnedBy(f.Id).Count()).ThenBy(f => f.Id).FirstOrDefault();
            }
            var preferred = owner != null ? inRegion.Where(s => s.Owner == owner.Id).ToList() : [];
            if (preferred.Count == 0) preferred = inRegion.Where(s => s.Settled).ToList();
            if (preferred.Count == 0) preferred = inRegion;
            var system = preferred[rng.Next(preferred.Count)];
            taken.Add(system.Id);

            system.Name = landmark.SystemName;
            system.LandmarkNote = landmark.Description;
            if (!system.Settled)
            {
                system.State = SystemState.Colony;
                if (system.Economy == Economy.None) system.Economy = Economy.Frontier;
            }
            string stationName = landmark.StationName ?? $"{landmark.SystemName} Station";
            if (system.Stations.Count == 0) system.Stations.Add(stationName);
            else if (landmark.StationName != null) system.Stations[0] = stationName;
            if (landmark.Economy != null && Enum.TryParse<Economy>(landmark.Economy, out var economy)) system.Economy = economy;

            if (owner == null && ownerType != null) owner = Found(galaxy, db, ownerType, system, rng.Fork("found"));
            if (owner != null) system.Owner = owner.Id;
            galaxy.Landmarks[landmark.Id] = system.Id;
        }
    }

    /// <summary>
    /// A campaign that flies for a faction type needs a faction of that type to exist; if the
    /// generator didn't roll one, one is founded on the edge of settled space (or at the campaign's
    /// base landmark). Seeded by the campaign id, so a rebuilt galaxy gets the same one.
    /// </summary>
    private static void FoundCampaignFactions(Galaxy galaxy, ContentDatabase db, ulong seed)
    {
        foreach (var campaign in db.Campaigns.Values.Where(c => c.FactionType != null).OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            if (!db.FactionTypes.TryGetValue(campaign.FactionType!, out var type)) continue;
            if (galaxy.Factions.Any(f => f.Alive && f.TypeId == type.Id)) continue;
            var rng = new Rng(seed ^ Rng.Hash("campaign-faction:" + campaign.Id));
            StarSystem? home = campaign.BaseLandmark != null && galaxy.Landmarks.TryGetValue(campaign.BaseLandmark, out int id) ? galaxy.Systems[id] : null;
            if (home == null)
            {
                var edge = galaxy.Systems.Where(s => s.IsGateSystem && s.Owner < 0 && galaxy.Neighbours(s).Any(n => n.Owner >= 0)).OrderBy(s => s.Id).ToList();
                home = PickWeighted(edge, rng);
            }
            if (home == null) continue;
            var faction = Found(galaxy, db, type, home, rng);
            if (home.Stations.Count == 0)
            {
                home.State = SystemState.Colony;
                if (home.Economy == Economy.None) home.Economy = Economy.Frontier;
                AddStations(home, db, rng.Fork("stations"));
                if (home.Stations.Count == 0) home.Stations.Add($"{home.Name} Station");
            }
            home.Owner = faction.Id;
        }
    }

    /// <summary>Founds a faction of a type at a system.</summary>
    private static Faction Found(Galaxy galaxy, ContentDatabase db, FactionTypeDef type, StarSystem home, Rng rng)
    {
        string race = type.Races.Count > 0 ? type.Races.OrderBy(r => r, StringComparer.Ordinal).First() : "human";
        if (!db.Races.ContainsKey(race)) race = "human";
        var faction = new Faction
        {
            Id = galaxy.Factions.Count,
            Name = FactionName(db, type, race, home, rng),
            TypeId = type.Id,
            RaceId = race,
            Tier = type.Tiers.Contains("regional") ? "regional" : type.Tiers.FirstOrDefault() ?? "minor",
            Criminal = type.Criminal,
            Colour = ColourFor(rng),
            HomeSystem = home.Id,
            Traits = RollTraits(type, rng.Fork("traits")),
        };
        galaxy.Factions.Add(faction);
        if (type.Criminal) faction.Haunts.Add(home.Id);
        else home.Owner = faction.Id;
        return faction;
    }

    private static void PlaceSystems(Galaxy galaxy, ContentDatabase db, GalaxySettings settings, Rng rng)
    {
        var names = new HashSet<string>();
        var positions = new List<Vector2>();
        int attempts = 0;
        while (positions.Count < settings.SystemCount && attempts++ < settings.SystemCount * 40)
        {
            // Denser towards the core; most systems sit along the four spiral arms.
            float r = settings.Radius * MathF.Pow(rng.NextFloat(), 0.7f) + 20f;
            int arm = rng.Next(4);
            float angle = arm * MathF.PI / 2 + MathF.Log(r / 60f + 1) * 1.6f;
            angle += rng.NextFloat() < 0.8f ? Gaussian(rng) * 0.22f : rng.Range(-MathF.PI, MathF.PI);
            var p = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * r;
            if (positions.Any(q => Vector2.DistanceSquared(p, q) < MinSpacing * MinSpacing)) continue;
            positions.Add(p);
        }

        for (int i = 0; i < positions.Count; i++)
        {
            var p = positions[i];
            float r = p.Length();
            var systemRng = rng.Fork($"system-{i}");
            string name = db.Grammars.Contains("places-system") ? db.Grammars.Expand("places-system", "name", systemRng.Fork("name")) : $"System {i}";
            for (int tries = 0; !names.Add(name) && tries < 20; tries++)
                name = db.Grammars.Expand("places-system", "name", systemRng.Fork($"name-{tries}"));

            galaxy.Systems.Add(new StarSystem
            {
                Id = i,
                Name = name,
                Position = p,
                Region = r < settings.CoreEdge ? "core" : r < settings.InnerEdge ? "inner" : r < settings.OuterEdge ? "outer" : "rim",
                Arm = NearestArm(p),
                Seed = systemRng.NextULong(),
            });
        }
    }

    /// <summary>Which arm a point belongs to, by comparing its angle with each arm's spiral at that radius.</summary>
    public static string NearestArm(Vector2 p)
    {
        float r = p.Length();
        float angle = MathF.Atan2(p.Y, p.X);
        int best = 0;
        float bestGap = float.MaxValue;
        for (int arm = 0; arm < 4; arm++)
        {
            float armAngle = arm * MathF.PI / 2 + MathF.Log(r / 60f + 1) * 1.6f;
            float gap = MathF.Abs(MathF.IEEERemainder(angle - armAngle, MathF.Tau));
            if (gap < bestGap) { bestGap = gap; best = arm; }
        }
        return ContentRules.Arms[best];
    }

    /// <summary>Each system links to its nearest neighbours, then isolated clusters are joined up.</summary>
    private static void BuildGates(Galaxy galaxy)
    {
        var systems = galaxy.Systems;
        void Link(int a, int b)
        {
            if (a == b || systems[a].Gates.Contains(b)) return;
            systems[a].Gates.Add(b);
            systems[b].Gates.Add(a);
        }

        foreach (var s in systems)
        {
            var nearest = systems.Where(o => o.Id != s.Id)
                .Select(o => (o.Id, Distance: Vector2.Distance(s.Position, o.Position)))
                .Where(o => o.Distance < GateRange)
                .OrderBy(o => o.Distance).Take(3);
            foreach (var (id, _) in nearest) Link(s.Id, id);
        }

        // Join components with the shortest available link until everything is reachable.
        var parent = Enumerable.Range(0, systems.Count).ToArray();
        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        foreach (var s in systems) foreach (var g in s.Gates) parent[Find(s.Id)] = Find(g);

        while (true)
        {
            var groups = systems.GroupBy(s => Find(s.Id)).ToList();
            if (groups.Count <= 1) break;
            var smallest = groups.OrderBy(g => g.Count()).First();
            var inside = smallest.ToList();
            var (a, b, _) = inside
                .SelectMany(s => systems.Where(o => Find(o.Id) != smallest.Key)
                    .Select(o => (s.Id, o.Id, Vector2.DistanceSquared(s.Position, o.Position))))
                .MinBy(x => x.Item3);
            Link(a, b);
            parent[Find(a)] = Find(b);
        }
    }

    private static void SeedPowers(Galaxy galaxy, ContentDatabase db, Rng rng)
    {
        foreach (var power in db.Powers.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var candidates = galaxy.Systems.Where(s => s.Owner < 0 && s.Arm == power.Arm && s.Region == power.Region).ToList();
            if (candidates.Count == 0) candidates = galaxy.Systems.Where(s => s.Owner < 0 && s.Region == power.Region).ToList();
            if (candidates.Count == 0) continue;

            // Home is the candidate with the most free neighbours, so the power has room around it.
            var home = candidates.OrderByDescending(s => galaxy.WithinJumps(s.Id, 2).Count(n => n.Owner < 0))
                .ThenBy(s => s.Id).First();
            var type = db.FactionTypes[power.FactionType];
            var faction = new Faction
            {
                Id = galaxy.Factions.Count,
                Name = power.Name,
                TypeId = power.FactionType,
                RaceId = power.Race,
                Tier = "great",
                PowerId = power.Id,
                Colour = power.Colour,
                HomeSystem = home.Id,
                Traits = RollTraits(type, rng.Fork(power.Id)),
            };
            galaxy.Factions.Add(faction);
            Claim(galaxy, faction, home, power.Systems, rng.Fork(power.Id + "-claim"));
        }
    }

    /// <summary>Takes free systems outward from home by gate distance. The nearer ones are colonies.</summary>
    private static void Claim(Galaxy galaxy, Faction faction, StarSystem home, int count, Rng rng)
    {
        home.Owner = faction.Id;
        home.State = SystemState.Colony;
        int claimed = 1;
        foreach (var s in galaxy.WithinJumps(home.Id, 12))
        {
            if (claimed >= count) break;
            if (s.Owner >= 0) continue;
            s.Owner = faction.Id;
            s.State = claimed < count * 0.45f ? SystemState.Colony : rng.NextFloat() < 0.75f ? SystemState.Outpost : SystemState.Claimed;
            claimed++;
        }
    }

    private static void SeedMinorFactions(Galaxy galaxy, ContentDatabase db, GalaxySettings settings, Rng rng)
    {
        var types = db.FactionTypes.Values.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        var lawful = types.Where(t => !t.Criminal).ToList();
        var criminal = types.Where(t => t.Criminal).ToList();

        for (int i = 0; i < settings.MinorFactions; i++)
        {
            var frng = rng.Fork($"faction-{i}");
            var type = frng.NextFloat() < 0.75f ? frng.Pick(lawful) : frng.Pick(criminal);
            string tier = type.Tiers.Contains("regional") && frng.NextFloat() < 0.2f ? "regional" : type.Tiers.Contains("minor") ? "minor" : type.Tiers[0];

            // Lawful factions take free systems next to settled space; criminals hide in settled systems.
            StarSystem? home;
            if (type.Criminal)
                home = PickWeighted(galaxy.Systems.Where(s => s.Settled).ToList(), frng);
            else
            {
                var edge = galaxy.Systems.Where(s => s.Owner < 0 && galaxy.Neighbours(s).Any(n => n.Owner >= 0)).ToList();
                if (edge.Count == 0) edge = galaxy.Systems.Where(s => s.Owner < 0).ToList();
                home = PickWeighted(edge, frng);
            }
            if (home == null) continue;

            string race = type.Races.Count > 0 ? frng.Pick(type.Races) : RaceFor(home, frng);
            if (!db.Races.ContainsKey(race)) race = "human";
            var faction = new Faction
            {
                Id = galaxy.Factions.Count,
                Name = FactionName(db, type, race, home, frng),
                TypeId = type.Id,
                RaceId = race,
                Tier = tier,
                Criminal = type.Criminal,
                Colour = ColourFor(frng),
                HomeSystem = home.Id,
                Traits = RollTraits(type, frng.Fork("traits")),
            };
            galaxy.Factions.Add(faction);

            if (type.Criminal)
            {
                faction.Haunts.Add(home.Id);
                foreach (var s in galaxy.WithinJumps(home.Id, 3).Where(s => s.Settled).Take(1 + frng.Next(4)))
                    faction.Haunts.Add(s.Id);
            }
            else
            {
                int count = tier == "regional" ? 3 + frng.Next(3) : 1 + frng.Next(2);
                Claim(galaxy, faction, home, count, frng.Fork("claim"));
            }
        }
    }

    /// <summary>Which races settle where. Generic factions take their race from their surroundings.</summary>
    private static string RaceFor(StarSystem s, Rng rng)
    {
        float roll = rng.NextFloat();
        return s.Region switch
        {
            "core" => roll < 0.7f ? "human" : roll < 0.9f ? "light-elf" : "synth",
            "inner" => roll < 0.55f ? "human" : roll < 0.85f ? "dwarf" : "synth",
            "outer" => roll < 0.4f ? "human" : roll < 0.7f ? "orc" : roll < 0.85f ? "free-machine" : "dwarf",
            _ => roll < 0.6f ? "dark-elf" : "human",
        };
    }

    private static string FactionName(ContentDatabase db, FactionTypeDef type, string race, StarSystem home, Rng rng)
    {
        var raceDef = db.Races.GetValueOrDefault(race);
        string founder = raceDef is { NameGrammar: not "" } ? db.Grammars.Expand(raceDef.NameGrammar, "name", rng.Fork("founder")) : "Someone";
        var vars = new Dictionary<string, string> { ["place"] = home.Name, ["founder"] = founder, ["race"] = raceDef?.Name ?? race };
        return db.Grammars.Expand(type.NameGrammar, "name", rng.Fork("name"), vars);
    }

    private static void FinishSystems(Galaxy galaxy, ContentDatabase db, Rng rng)
    {
        foreach (var s in galaxy.Systems)
        {
            var srng = new Rng(s.Seed).Fork("finish");
            if (s.Owner < 0 && srng.NextFloat() < 0.06f && galaxy.Neighbours(s).FirstOrDefault(n => n.Owner >= 0) is { } next)
            {
                s.State = SystemState.Surveyed;
                s.SurveyedBy = next.Owner;
            }

            s.Economy = s.State switch
            {
                SystemState.Colony => s.Region switch
                {
                    "core" => srng.Pick(new[] { Economy.HighTech, Economy.Industrial, Economy.Agricultural, Economy.HighTech }),
                    "inner" => srng.Pick(new[] { Economy.Industrial, Economy.Refinery, Economy.Mining, Economy.Agricultural }),
                    "outer" => srng.Pick(new[] { Economy.Mining, Economy.Frontier, Economy.Agricultural, Economy.Refinery }),
                    _ => srng.Pick(new[] { Economy.Frontier, Economy.Mining }),
                },
                SystemState.Outpost => srng.NextFloat() < 0.6f ? Economy.Mining : Economy.Frontier,
                _ => Economy.None,
            };
            AddStations(s, db, srng);
        }
    }

    public static void AddStations(StarSystem s, ContentDatabase db, Rng rng)
    {
        int wanted = s.State switch { SystemState.Colony => 1 + rng.Next(3), SystemState.Outpost => 1, _ => 0 };
        for (int i = s.Stations.Count; i < wanted; i++)
        {
            string name = db.Grammars.Contains("places-station") ? db.Grammars.Expand("places-station", "name", rng.Fork($"station-{i}")) : $"{s.Name} Station";
            if (!s.Stations.Contains(name)) s.Stations.Add(name);
        }
    }

    private static Dictionary<string, float> RollTraits(FactionTypeDef type, Rng rng) =>
        ContentRules.FactionTraits.Order().ToDictionary(t => t, t =>
            type.Traits.TryGetValue(t, out var range) && range.Length == 2 ? rng.Range(range[0], range[1]) : rng.Range(0.2f, 0.8f));

    private static StarSystem? PickWeighted(List<StarSystem> options, Rng rng) => options.Count == 0 ? null : options[rng.Next(options.Count)];

    private static string ColourFor(Rng rng)
    {
        // Muted colours from a random hue, so minor factions don't drown out the great powers.
        float h = rng.NextFloat(), s = 0.35f + rng.NextFloat() * 0.3f, v = 0.55f + rng.NextFloat() * 0.3f;
        int i = (int)(h * 6);
        float f = h * 6 - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        var (r, g, b) = (i % 6) switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
        return $"#{(int)(r * 255):x2}{(int)(g * 255):x2}{(int)(b * 255):x2}";
    }

    private static float Gaussian(Rng rng)
    {
        float u1 = MathF.Max(rng.NextFloat(), 1e-6f), u2 = rng.NextFloat();
        return MathF.Sqrt(-2 * MathF.Log(u1)) * MathF.Cos(MathF.Tau * u2);
    }
}
