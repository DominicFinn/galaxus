using Galaxus.Core.Content;

namespace Galaxus.Core.Missions;

/// <summary>A faction as the mission board sees it.</summary>
public sealed record FactionInfo(string Name, string TypeId, string RaceId, bool Criminal = false);

/// <summary>Where and why a job is being offered.</summary>
public sealed record MissionContext(
    FactionInfo Giver,
    string System,
    string Destination,
    string Captain,
    int Rank = 0,
    string? EventId = null,
    int MinDeadlineDays = 0,
    string? ClientName = null);

/// <summary>A generated job, ready for the mission board.</summary>
public sealed record Mission(
    MissionTemplateDef Template,
    string Title,
    string Briefing,
    string SuccessText,
    string FailureText,
    int Reward,
    CommodityDef? Cargo,
    int Tonnes,
    int DeadlineDays,
    ComplicationDef? Complication,
    string? ComplicationReveal,
    string? ComplicationAftermath,
    IReadOnlyDictionary<string, string> Variables);

/// <summary>
/// Builds jobs from templates. Missions come out of what's happening: a famine event makes
/// famine work likely, and the giver's race decides how the contract reads.
/// </summary>
public static class MissionGenerator
{
    /// <summary>Chance that a job has a complication, if its template allows any.</summary>
    public const float ComplicationChance = 0.35f;

    public static Mission? Generate(ContentDatabase db, MissionContext context, Rng rng)
    {
        var candidates = db.Missions.Values
            .Where(m => m.Givers.Count == 0 || m.Givers.Contains(context.Giver.TypeId))
            // Open templates go to the obvious side of the law: criminals don't post patrols, lawful factions don't post smuggling.
            .Where(m => m.Givers.Count > 0 || m.Legal != context.Giver.Criminal)
            .Where(m => m.Events.Count == 0 || (context.EventId != null && m.Events.Contains(context.EventId)))
            .Where(m => m.MinRank <= context.Rank)
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0) return null;

        // Work created by the current event is three times as likely as standing work.
        var template = PickWeighted(candidates, m => m.Events.Count > 0 ? 3f : 1f, rng);
        return Build(db, template, context, rng);
    }

    /// <summary>Builds a specific template. Tests use this to expand every template.</summary>
    public static Mission Build(ContentDatabase db, MissionTemplateDef template, MissionContext context, Rng rng,
        ComplicationDef? forcedComplication = null)
    {
        CommodityDef? cargo = null;
        int tonnes = 0;
        if (template.CargoCategories.Count > 0)
        {
            var options = db.Commodities.Values.Where(c => template.CargoCategories.Contains(c.Category))
                .OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            cargo = rng.Pick(options);
            tonnes = 2 + (int)(29 * MathF.Pow(rng.NextFloat(), 2)); // mostly small loads, some big ones
        }

        float scale = template.Scale switch { "skirmish" => 1.5f, "battle" => 2.5f, "war" => 4f, _ => 1f };
        int reward = RoundTo((template.BaseReward + (cargo?.BasePrice ?? 0) * tonnes / 10f) * scale * rng.Range(0.85f, 1.25f), 50);

        int deadlineDays = Math.Max(context.MinDeadlineDays + 2 + rng.Next(4), 3 + rng.Next(12));
        var clientRace = db.Races.GetValueOrDefault(context.Giver.RaceId);
        var variables = new Dictionary<string, string>
        {
            ["captain"] = context.Captain,
            ["client"] = context.ClientName ?? PersonName(db, clientRace, rng),
            ["faction"] = context.Giver.Name,
            ["system"] = context.System,
            ["destination"] = context.Destination,
            ["cargo"] = cargo?.Name.ToLowerInvariant() ?? "the package",
            ["tonnes"] = tonnes.ToString(),
            ["reward"] = $"{reward:N0} cr",
            ["target"] = PersonName(db, db.Races.Values.OrderBy(r => r.Id).ElementAt(rng.Next(db.Races.Count)), rng),
            ["deadline"] = $"{deadlineDays} days",
        };

        var aliases = new Dictionary<string, string> { [ContentRules.VoiceAlias] = VoiceGrammarFor(db, clientRace) };
        string Text(string grammar, string rule) => db.Grammars.Expand(grammar, rule, rng.Fork(rule), variables, aliases);

        ComplicationDef? complication = forcedComplication;
        if (complication == null && template.Complications.Count > 0 && rng.NextFloat() < ComplicationChance)
        {
            var options = template.Complications.Select(id => db.Complications[id]).ToList();
            complication = PickWeighted(options, c => c.Weight, rng);
        }

        return new Mission(
            template,
            Text(template.TextGrammar, "title"),
            Text(template.TextGrammar, "briefing"),
            Text(template.TextGrammar, "success"),
            Text(template.TextGrammar, "failure"),
            reward,
            cargo,
            tonnes,
            deadlineDays,
            complication,
            complication == null ? null : Text(complication.TextGrammar, "reveal"),
            complication == null ? null : Text(complication.TextGrammar, "aftermath"),
            variables);
    }

    /// <summary>The client race's mission voice, or the first race that has one.</summary>
    public static string VoiceGrammarFor(ContentDatabase db, RaceDef? race) =>
        race is { MissionVoiceGrammar: not "" } ? race.MissionVoiceGrammar
            : db.Races.Values.OrderBy(r => r.Id).Select(r => r.MissionVoiceGrammar).FirstOrDefault(g => g != "") ?? "";

    private static string PersonName(ContentDatabase db, RaceDef? race, Rng rng) =>
        race is { NameGrammar: not "" } ? db.Grammars.Expand(race.NameGrammar, "name", rng.Fork("name")) : "a stranger";

    private static T PickWeighted<T>(IReadOnlyList<T> items, Func<T, float> weight, Rng rng)
    {
        float total = items.Sum(weight);
        float roll = rng.NextFloat() * total;
        foreach (var item in items)
        {
            roll -= weight(item);
            if (roll <= 0) return item;
        }
        return items[^1];
    }

    private static int RoundTo(float value, int step) => Math.Max(step, (int)MathF.Round(value / step) * step);
}
