using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Portraits;

namespace Galaxus.Core.People;

/// <summary>A job people do at stations, and what they wear for it. From game/data/people.</summary>
public sealed class RoleDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Headwear { get; set; } = ["none"];
    public List<string> Eyewear { get; set; } = ["none"];
    public List<string> Collar { get; set; } = ["plain"];
    public List<string> Extras { get; set; } = [];
    /// <summary>Economies where the role is common. Empty means anywhere.</summary>
    public List<string> Economies { get; set; } = [];
    public float Weight { get; set; } = 1;
    /// <summary>Only where a lawful faction owns the system.</summary>
    public bool Lawful { get; set; }
    /// <summary>Only where criminal factions hang about.</summary>
    public bool Criminal { get; set; }
    /// <summary>Can be one of a station's regulars.</summary>
    public bool Regular { get; set; }
}

/// <summary>
/// Someone you can meet. Every person comes from a stable id and the galaxy seed, so the same
/// person, face and all, turns up again whenever you come back.
/// </summary>
public sealed record Person(
    string Id,
    string Name,
    string Gender,
    string RaceId,
    string RoleId,
    int FactionId,
    int SystemId,
    ulong Seed,
    bool Regular);

/// <summary>
/// Who is where. Each station has regulars who are always there, plus visitors drawn each week
/// from a fixed pool for that system. Each faction has a handful of named contacts who post
/// its jobs. Nothing here is random per call: it's all derived from ids and the galaxy seed.
/// </summary>
public static class PeopleDirectory
{
    private const int VisitorPool = 16;

    public static List<Person> AtStation(ContentDatabase db, Galaxy.Galaxy galaxy, StarSystem system)
    {
        var people = new List<Person>();
        if (system.Stations.Count == 0) return people;

        int regulars = system.State == SystemState.Colony ? 4 : 3;
        for (int i = 0; i < regulars; i++)
        {
            // Every station has a bartender and someone running the docks.
            string? role = i switch { 0 => "bartender", 1 => "dockhand", _ => null };
            people.Add(Make(db, galaxy, $"sys{system.Id}-reg{i}", system, role, regular: true));
        }

        var rng = new Rng(galaxy.Seed ^ (ulong)system.Id ^ Rng.Hash($"visitors-{galaxy.Day / 7}"));
        var pool = Enumerable.Range(0, VisitorPool).ToList();
        int visitors = 3 + rng.Next(3);
        for (int i = 0; i < visitors && pool.Count > 0; i++)
        {
            int slot = pool[rng.Next(pool.Count)];
            pool.Remove(slot);
            people.Add(Make(db, galaxy, $"sys{system.Id}-vis{slot}", system, null, regular: false));
        }
        return people;
    }

    /// <summary>A faction's named contacts: the people who post its jobs.</summary>
    public static List<Person> Contacts(ContentDatabase db, Galaxy.Galaxy galaxy, Faction faction)
    {
        var home = galaxy.Systems[faction.HomeSystem];
        string[] roles = faction.Criminal ? ["broker", "smuggler", "enforcer"]
            : faction.TypeId is "mining-corporation" or "dwarf-hold" or "salvage-cooperative" ? ["trader", "miner", "engineer"]
            : faction.Tier == "great" ? ["envoy", "trader", "customs-officer"]
            : ["trader", "pilot", "medic"];
        return Enumerable.Range(0, 3)
            .Select(i => Make(db, galaxy, $"fac{faction.Id}-c{i}", home, roles[i], regular: false, faction.RaceId, faction.Id))
            .ToList();
    }

    /// <summary>Builds a person from their id. The same id always gives the same person.</summary>
    public static Person Make(ContentDatabase db, Galaxy.Galaxy galaxy, string id, StarSystem system, string? roleId, bool regular,
        string? raceId = null, int factionId = -1)
    {
        var rng = new Rng(galaxy.Seed ^ Rng.Hash(id));
        raceId ??= RaceFor(db, galaxy, system, rng.Fork("race"));
        if (!db.Races.ContainsKey(raceId)) raceId = "human";
        roleId ??= RoleFor(db, galaxy, system, rng.Fork("role"), regular);

        bool machine = db.Portraits.TryGetValue(raceId, out var style) && style.Machine;
        float g = rng.NextFloat();
        string gender = machine ? "neither" : raceId == "synth" && g < 0.15f ? "neither" : g < 0.575f ? "woman" : "man";

        return new Person(id, NameFor(db, raceId, gender, rng.Fork("name")), gender, raceId, roleId,
            factionId >= 0 ? factionId : system.Owner, system.Id, rng.NextULong(), regular);
    }

    public static string NameFor(ContentDatabase db, string raceId, string gender, Rng rng)
    {
        var race = db.Races.GetValueOrDefault(raceId);
        if (race is not { NameGrammar: not "" }) return "Someone";
        var grammar = db.Grammars.All.FirstOrDefault(g => g.Id == race.NameGrammar);
        string rule = gender switch { "woman" => "name-feminine", "man" => "name-masculine", _ => "name" };
        if (grammar == null || !grammar.Rules.ContainsKey(rule)) rule = "name";
        return db.Grammars.Expand(race.NameGrammar, rule, rng);
    }

    /// <summary>Locals are mostly the owner's race, with the region's usual mix and anyone the criminals bring.</summary>
    private static string RaceFor(ContentDatabase db, Galaxy.Galaxy galaxy, StarSystem system, Rng rng)
    {
        float roll = rng.NextFloat();
        var owner = galaxy.OwnerOf(system);
        if (owner != null && roll < 0.5f) return owner.RaceId;
        var criminal = galaxy.Factions.FirstOrDefault(f => f.Criminal && f.Alive && f.Haunts.Contains(system.Id));
        if (criminal != null && roll < 0.6f) return criminal.RaceId;
        roll = rng.NextFloat();
        return system.Region switch
        {
            "core" => roll < 0.6f ? "human" : roll < 0.82f ? "light-elf" : roll < 0.95f ? "synth" : "dwarf",
            "inner" => roll < 0.45f ? "human" : roll < 0.7f ? "dwarf" : roll < 0.82f ? "synth" : roll < 0.9f ? "orc" : "light-elf",
            "outer" => roll < 0.32f ? "human" : roll < 0.56f ? "orc" : roll < 0.72f ? "free-machine" : roll < 0.86f ? "dwarf" : roll < 0.95f ? "synth" : "dark-elf",
            _ => roll < 0.5f ? "dark-elf" : roll < 0.8f ? "human" : "free-machine",
        };
    }

    private static string RoleFor(ContentDatabase db, Galaxy.Galaxy galaxy, StarSystem system, Rng rng, bool regular)
    {
        bool lawful = galaxy.OwnerOf(system) is { Criminal: false };
        bool criminals = galaxy.Factions.Any(f => f.Criminal && f.Alive && f.Haunts.Contains(system.Id));
        string economy = system.Economy.ToString();
        var options = db.Roles.Values
            .Where(r => r.Weight > 0 && (!regular || r.Regular) && (!r.Lawful || lawful) && (!r.Criminal || criminals))
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
        if (options.Count == 0) return db.Roles.Keys.FirstOrDefault() ?? "dockhand";
        // Roles that fit the local economy are four times as likely.
        float Weight(RoleDef r) => r.Weight * (r.Economies.Count == 0 ? 1 : r.Economies.Contains(economy) ? 4 : 0.3f);
        float total = options.Sum(Weight), pick = rng.NextFloat() * total;
        foreach (var r in options) { pick -= Weight(r); if (pick <= 0) return r.Id; }
        return options[^1].Id;
    }

    /// <summary>The person's work clothes, picked from their role with their seed.</summary>
    public static Attire AttireFor(ContentDatabase db, Person person)
    {
        if (!db.Roles.TryGetValue(person.RoleId, out var role)) return new Attire();
        var rng = new Rng(person.Seed ^ Rng.Hash("attire"));
        string Pick(List<string> options) => options.Count == 0 ? "none" : rng.Pick(options);
        return new Attire { Headwear = Pick(role.Headwear), Eyewear = Pick(role.Eyewear), Collar = role.Collar.Count == 0 ? "plain" : rng.Pick(role.Collar), Extras = [.. role.Extras] };
    }

    public static List<PortraitShape> Portrait(ContentDatabase db, Galaxy.Galaxy galaxy, Person person)
    {
        var style = db.Portraits.GetValueOrDefault(person.RaceId) ?? db.Portraits.Values.First();
        string? colour = person.FactionId >= 0 ? galaxy.Factions[person.FactionId].Colour : null;
        return PortraitBuilder.Build(style, person.Seed, colour, AttireFor(db, person), person.Gender);
    }

    /// <summary>A line of greeting in the person's race's voice, the same each time you meet them that day.</summary>
    public static string Greeting(ContentDatabase db, Galaxy.Galaxy galaxy, Person person, string captain, string ship)
    {
        var race = db.Races.GetValueOrDefault(person.RaceId);
        if (race is not { VoiceGrammar: not "" }) return "";
        var vars = new Dictionary<string, string>
        {
            ["captain"] = captain,
            ["ship"] = ship,
            ["system"] = galaxy.Systems[person.SystemId].Name,
            ["crewmate"] = "your engineer",
        };
        return db.Grammars.Expand(race.VoiceGrammar, "greeting", new Rng(person.Seed ^ (ulong)galaxy.Day), vars);
    }
}
