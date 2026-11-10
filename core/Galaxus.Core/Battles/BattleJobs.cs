using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Missions;

namespace Galaxus.Core.Battles;

/// <summary>
/// Turns set-piece battles into jobs. Wars between factions post battles at their front lines,
/// lawful factions post pirate hunts where criminals haunt, the outer galaxy has rogue machines,
/// and stations near trouble ask for defenders.
/// </summary>
public static class BattleJobs
{
    /// <summary>Faction id used for enemies that aren't a faction: pirates of no fixed allegiance, or rogue machines.</summary>
    public const int RogueMachines = -2, Pirates = -3;

    private static readonly Dictionary<string, string> RaceBuilders = new()
    {
        ["human"] = "human-corporate", ["dwarf"] = "dwarf-yards", ["light-elf"] = "light-elf-houses",
        ["orc"] = "orc-yards", ["free-machine"] = "free-machine-foundries", ["dark-elf"] = "ruin-courts",
    };

    private static readonly Dictionary<string, MissionTemplateDef> Templates = [];

    /// <summary>A job template standing in for a battle, so battles sit on the board like any job.</summary>
    public static MissionTemplateDef Template(BattleDef def)
    {
        lock (Templates)
        {
            if (!Templates.TryGetValue(def.Id, out var template))
                Templates[def.Id] = template = new MissionTemplateDef { Id = "battle-" + def.Id, Name = def.Name, Type = "battle", Scale = "war" };
            return template;
        }
    }

    public static List<MissionOffer> Offers(ContentDatabase db, Galaxy.Galaxy galaxy, StarSystem system, string captain, Func<int, int> rankWith, Rng rng)
    {
        var offers = new List<MissionOffer>();
        if (db.Battles.Count == 0 || system.Stations.Count == 0) return offers;
        int week = galaxy.Day / 7;
        var owner = galaxy.OwnerOf(system);
        var nearby = galaxy.WithinJumps(system.Id, 5).Select(s => s.Id).Append(system.Id).ToHashSet();

        // Wars: any conflict at raids or worse whose front is near, posted by whichever side is here.
        foreach (var conflict in galaxy.Conflicts.Where(c => c.Stage >= 1 && nearby.Contains(c.FrontSystem)))
        {
            int client = owner != null && conflict.Involves(owner.Id) ? owner.Id : rng.NextFloat() < 0.5f ? conflict.A : conflict.B;
            int enemy = client == conflict.A ? conflict.B : conflict.A;
            string stage = conflict.Stage switch { 1 => "raids", 2 => "blockade", _ => "war" };
            var front = galaxy.Systems[conflict.FrontSystem];
            var where = front.Stations.Count > 0 ? front : system;
            foreach (var def in Pick(db, rng, b => b.Kind == "war" && b.Stages.Contains(stage), conflict.Stage >= 3 ? 2 : 1))
                Add(def, client, enemy, where, $"{conflict.A}-{conflict.B}");
        }

        // Pirates, where criminals haunt nearby and someone respectable is here to pay.
        var gang = galaxy.Factions.FirstOrDefault(f => f.Alive && f.Criminal && f.Haunts.Any(nearby.Contains));
        if (owner is { Criminal: false } && (gang != null || rng.NextFloat() < 0.3f))
            foreach (var def in Pick(db, rng, b => b.Kind == "pirate", 1))
                Add(def, owner.Id, gang?.Id ?? Pirates, system, "pirates");

        // Rogue machines out towards the rim.
        if (owner != null && system.Region is "outer" or "rim" && rng.NextFloat() < 0.6f)
            foreach (var def in Pick(db, rng, b => b.Kind == "synth", 1))
                Add(def, owner.Id, RogueMachines, system, "synths");

        // Station defence, where there's trouble close by.
        if (owner != null && galaxy.Conflicts.Any(c => c.Stage >= 1 && c.Involves(owner.Id)) && rng.NextFloat() < 0.5f)
            foreach (var def in Pick(db, rng, b => b.Kind == "defence", 1))
            {
                var war = galaxy.Conflicts.First(c => c.Stage >= 1 && c.Involves(owner.Id));
                Add(def, owner.Id, war.A == owner.Id ? war.B : war.A, system, "defence");
            }
        return offers;

        void Add(BattleDef def, int client, int enemy, StarSystem where, string key)
        {
            // Big battles want some standing with the client first, but not much: they're the point.
            if (rankWith(client) + 2 < def.MinRank) return;
            string id = $"{system.Id}-{week}-battle-{def.Id}-{key}";
            if (offers.Any(o => o.Id == id)) return;
            var orng = new Rng(Rng.Hash(id));
            var clientFaction = galaxy.Factions[client];
            string enemyName = EnemyName(galaxy, enemy);
            var tagNames = Battle.NameTags(def, new Rng(Rng.Hash(id + "tags")));
            string station = orng.Pick(where.Stations);
            var variables = new Dictionary<string, string>
            {
                ["client"] = clientFaction.Name, ["enemy"] = enemyName, ["system"] = where.Name,
                ["captain"] = captain, ["station"] = station,
            };
            foreach (var (tag, name) in tagNames) variables[tag] = name;
            string Text(string rule) => Battle.Tidy(db.Grammars.Expand(def.TextGrammar, rule, orng.Fork(rule), variables));

            int travel = MissionBoard.TravelDays(galaxy, system.Id, where.Id);
            int reward = (int)(def.Reward * (1 + 0.1f * rankWith(client)) / 100) * 100;
            var mission = new Mission(Template(def), $"{def.Name}: {where.Name}", Text("briefing"), Text("success"), Text("failure"),
                reward, null, 0, travel + 6, null, null, null, variables);
            var contact = orng.Pick(People.PeopleDirectory.Contacts(db, galaxy, clientFaction));
            offers.Add(new MissionOffer(id, mission, client, system.Id, where.Id, station, galaxy.Day, contact.Id, def.Id, enemy));
        }
    }

    private static IEnumerable<BattleDef> Pick(ContentDatabase db, Rng rng, Func<BattleDef, bool> fits, int count)
    {
        var options = db.Battles.Values.Where(fits).OrderBy(b => b.Id, StringComparer.Ordinal).ToList();
        for (int i = 0; i < count && options.Count > 0; i++)
        {
            var pick = options[rng.Next(options.Count)];
            options.Remove(pick);
            yield return pick;
        }
    }

    /// <summary>
    /// Who an ordinary combat job is against: raids hit whoever the giver is at war with; the rest
    /// are pirates, the local gang if one haunts the destination.
    /// </summary>
    public static int JobEnemy(Galaxy.Galaxy galaxy, Faction giver, StarSystem destination, string type, Rng rng)
    {
        if (type == "raid" && galaxy.Conflicts.FirstOrDefault(c => c.Involves(giver.Id)) is { } war)
            return war.A == giver.Id ? war.B : war.A;
        var gang = galaxy.Factions.FirstOrDefault(f => f.Alive && f.Criminal && f.Id != giver.Id && f.Haunts.Contains(destination.Id));
        return gang?.Id ?? Pirates;
    }

    public static string EnemyName(Galaxy.Galaxy galaxy, int enemy) => enemy switch
    {
        RogueMachines => "the Unbound",
        Pirates => "pirates",
        _ => galaxy.Factions[enemy].Name,
    };

    /// <summary>Who's fighting whom in a battle job, and whose ships each side flies.</summary>
    public static BattleSides Sides(ContentDatabase db, Galaxy.Galaxy galaxy, MissionOffer offer)
    {
        var client = galaxy.Factions[offer.GiverFactionId];
        var (enemyBuilder, enemyRace) = offer.EnemyFactionId switch
        {
            RogueMachines => ("free-machine-foundries", "free-machine"),
            Pirates => ("breakers-guild", "human"),
            _ => (BuilderFor(db, galaxy.Factions[offer.EnemyFactionId]), galaxy.Factions[offer.EnemyFactionId].RaceId),
        };
        // Criminals fly salvage, unless their faction type has a yard of its own.
        if (offer.EnemyFactionId >= 0 && galaxy.Factions[offer.EnemyFactionId] is { Criminal: true } gang
            && !(db.FactionTypes.TryGetValue(gang.TypeId, out var gangType) && gangType.Builder != null && db.Builders.ContainsKey(gangType.Builder)))
            enemyBuilder = "breakers-guild";
        return new BattleSides(client.Name, BuilderFor(db, client), client.RaceId,
            EnemyName(galaxy, offer.EnemyFactionId), enemyBuilder, enemyRace, offer.EnemyFactionId == RogueMachines);
    }

    /// <summary>
    /// A great power flies its own yard's ships; a faction whose type names a builder flies that
    /// builder's; everyone else buys from their race's main builder.
    /// </summary>
    public static string BuilderFor(ContentDatabase db, Faction faction)
    {
        if (faction.PowerId != null && db.Powers.TryGetValue(faction.PowerId, out var power) && db.Builders.ContainsKey(power.Builder))
            return power.Builder;
        if (db.FactionTypes.TryGetValue(faction.TypeId, out var type) && type.Builder != null && db.Builders.ContainsKey(type.Builder))
            return type.Builder;
        return RaceBuilders.GetValueOrDefault(faction.RaceId, "human-corporate");
    }
}
