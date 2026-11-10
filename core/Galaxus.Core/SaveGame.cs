using System.Text.Json;
using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Missions;

namespace Galaxus.Core;

/// <summary>
/// Saves and loads a session as JSON.
///
/// The galaxy is stored as its seed and day, and rebuilt on load by generating it again and
/// running the simulation forward. That's exact because nothing the player does changes the
/// simulation yet. Once it does (rank swaying wars, missions shifting borders), saves must store
/// the galaxy's state as well; bump Version when that happens.
/// </summary>
public static class SaveGame
{
    public const int Version = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public sealed class SaveData
    {
        public int Version { get; set; } = SaveGame.Version;
        public string SavedAt { get; set; } = "";
        public ulong Seed { get; set; }
        public int SystemCount { get; set; }
        public int MinorFactions { get; set; }
        public int Day { get; set; }
        public string Captain { get; set; } = "";
        public int Credits { get; set; }
        public int SystemId { get; set; }
        public string SystemName { get; set; } = "";
        public string HullId { get; set; } = "";
        public List<string>? Loadout { get; set; }
        public Dictionary<string, int> Cargo { get; set; } = [];
        public Dictionary<int, int> Reputation { get; set; } = [];
        public List<string> TakenOffers { get; set; } = [];
        public List<SavedMission> Missions { get; set; } = [];
    }

    public sealed class SavedMission
    {
        public string OfferId { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Briefing { get; set; } = "";
        public string SuccessText { get; set; } = "";
        public string FailureText { get; set; } = "";
        public int Reward { get; set; }
        public string? CargoId { get; set; }
        public int Tonnes { get; set; }
        public int DeadlineDays { get; set; }
        public string? ComplicationId { get; set; }
        public string? ComplicationReveal { get; set; }
        public string? ComplicationAftermath { get; set; }
        public int Giver { get; set; }
        public int Origin { get; set; }
        public int Destination { get; set; }
        public string DestinationStation { get; set; } = "";
        public int PostedDay { get; set; }
        public int AcceptedDay { get; set; }
        public int DeadlineDay { get; set; }
    }

    public static string Save(Session session)
    {
        var c = session.Career;
        var data = new SaveData
        {
            SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Seed = session.Galaxy.Seed,
            SystemCount = session.Settings.SystemCount,
            MinorFactions = session.Settings.MinorFactions,
            Day = session.Galaxy.Day,
            Captain = c.Captain,
            Credits = c.Credits,
            SystemId = c.SystemId,
            SystemName = session.Here.Name,
            HullId = c.HullId,
            Loadout = c.Loadout,
            Cargo = new(c.Cargo),
            Reputation = new(c.Reputation),
            TakenOffers = [.. c.TakenOffers],
            Missions = c.Missions.Select(m => new SavedMission
            {
                OfferId = m.Offer.Id,
                TemplateId = m.Offer.Mission.Template.Id,
                Title = m.Offer.Mission.Title,
                Briefing = m.Offer.Mission.Briefing,
                SuccessText = m.Offer.Mission.SuccessText,
                FailureText = m.Offer.Mission.FailureText,
                Reward = m.Offer.Mission.Reward,
                CargoId = m.Offer.Mission.Cargo?.Id,
                Tonnes = m.Offer.Mission.Tonnes,
                DeadlineDays = m.Offer.Mission.DeadlineDays,
                ComplicationId = m.Offer.Mission.Complication?.Id,
                ComplicationReveal = m.Offer.Mission.ComplicationReveal,
                ComplicationAftermath = m.Offer.Mission.ComplicationAftermath,
                Giver = m.Offer.GiverFactionId,
                Origin = m.Offer.OriginSystemId,
                Destination = m.Offer.DestinationSystemId,
                DestinationStation = m.Offer.DestinationStation,
                PostedDay = m.Offer.PostedDay,
                AcceptedDay = m.AcceptedDay,
                DeadlineDay = m.DeadlineDay,
            }).ToList(),
        };
        return JsonSerializer.Serialize(data, Json);
    }

    /// <summary>Reads just the header, for listing save slots without rebuilding the galaxy.</summary>
    public static SaveData? Peek(string json)
    {
        try { return JsonSerializer.Deserialize<SaveData>(json, Json); }
        catch (JsonException) { return null; }
    }

    public static Session Load(ContentDatabase db, string json)
    {
        var data = JsonSerializer.Deserialize<SaveData>(json, Json) ?? throw new InvalidDataException("Save file is empty.");
        if (data.Version != Version) throw new InvalidDataException($"Save version {data.Version} can't be loaded by this build (expects {Version}).");

        var settings = new GalaxySettings { SystemCount = data.SystemCount, MinorFactions = data.MinorFactions };
        var session = Session.Restore(db, data.Seed, settings, data.Day);
        var c = session.Career;
        c.Captain = data.Captain;
        c.Credits = data.Credits;
        c.SystemId = data.SystemId;
        c.HullId = db.Hulls.ContainsKey(data.HullId) ? data.HullId : "hc-sparrow";
        c.Loadout = data.Loadout?.All(db.Parts.ContainsKey) == true ? data.Loadout : null;
        foreach (var (k, v) in data.Cargo.Where(kv => db.Commodities.ContainsKey(kv.Key))) c.Cargo[k] = v;
        foreach (var (k, v) in data.Reputation) c.Reputation[k] = v;
        foreach (var id in data.TakenOffers) c.TakenOffers.Add(id);

        foreach (var m in data.Missions.Where(m => db.Missions.ContainsKey(m.TemplateId)))
        {
            var mission = new Mission(
                db.Missions[m.TemplateId], m.Title, m.Briefing, m.SuccessText, m.FailureText, m.Reward,
                m.CargoId != null ? db.Commodities.GetValueOrDefault(m.CargoId) : null, m.Tonnes, m.DeadlineDays,
                m.ComplicationId != null ? db.Complications.GetValueOrDefault(m.ComplicationId) : null,
                m.ComplicationReveal, m.ComplicationAftermath, new Dictionary<string, string>());
            var offer = new MissionOffer(m.OfferId, mission, m.Giver, m.Origin, m.Destination, m.DestinationStation, m.PostedDay);
            c.Missions.Add(new AcceptedMission(offer, m.AcceptedDay, m.DeadlineDay));
        }
        return session;
    }
}
