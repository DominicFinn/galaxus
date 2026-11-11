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
    public const int Version = 3;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public sealed class SaveData
    {
        public int Version { get; set; } = SaveGame.Version;
        public string SavedAt { get; set; } = "";
        public ulong Seed { get; set; }
        public int SystemCount { get; set; }
        public int MinorFactions { get; set; }
        public int Day { get; set; }
        /// <summary>Deep-space stars registered in this galaxy, in order, so their ids come back the same.</summary>
        public List<ulong> DeepKeys { get; set; } = [];
        /// <summary>What the player has done to the galaxy, replayed on load on the same days.</summary>
        public List<PlayerAction> Actions { get; set; } = [];
        public string Captain { get; set; } = "";
        public int Credits { get; set; }
        public int SystemId { get; set; }
        public string SystemName { get; set; } = "";
        public string HullId { get; set; } = "";
        public List<string>? Loadout { get; set; }
        public Dictionary<string, int> Cargo { get; set; } = [];
        public Dictionary<int, int> Reputation { get; set; } = [];
        public List<string> TakenOffers { get; set; } = [];
        public List<string> MetPeople { get; set; } = [];
        public Dictionary<string, int> Disposition { get; set; } = [];
        public Dictionary<string, int> LastDrink { get; set; } = [];
        public List<string> KnownLeads { get; set; } = [];
        public Dictionary<string, float> Haggled { get; set; } = [];
        public List<string> WithdrawnOffers { get; set; } = [];
        public List<SavedMission> Missions { get; set; } = [];
        public string? ActiveJob { get; set; }
        public string? CampaignId { get; set; }
        public int CampaignStep { get; set; }
        public int CampaignBase { get; set; } = -1;
        public string? CampaignDebrief { get; set; }
        /// <summary>Fuel in the tank, in light years; below zero means full.</summary>
        public float FuelLy { get; set; } = -1;
        /// <summary>Recent trades by "systemId:commodityId", so markets you've emptied stay short for a while.</summary>
        public Dictionary<string, TradeMark> Trades { get; set; } = [];
        /// <summary>Markets as you last saw them.</summary>
        public Dictionary<int, SeenMarket> SeenMarkets { get; set; } = [];
        /// <summary>The Easy trade loop picked at the start.</summary>
        public TradeRoute? TradeRoute { get; set; }
        /// <summary>Tonnes cut from each asteroid.</summary>
        public Dictionary<string, int> Mined { get; set; } = [];
        /// <summary>Everything scanned.</summary>
        public List<string> Scanned { get; set; } = [];
        public List<Mining.LogEntry> Logbook { get; set; } = [];
        public List<Mining.Trail> Trails { get; set; } = [];
        public List<Mining.SurveyRecord> SurveyData { get; set; } = [];
        public List<Law.Offence> Offences { get; set; } = [];
        public List<Galaxus.Core.Lore.LoreRecord> Lore { get; set; } = [];
        public Dictionary<string, int> Clues { get; set; } = [];
        public int NewsReadDay { get; set; } = -1;
        public int LogbookSeen { get; set; }
        public List<People.CrewMember> Crew { get; set; } = [];
        public int WagesPaidDay { get; set; } = -1;
        /// <summary>The mods loaded when this was saved. Mods can change the galaxy, so a save moved to a game with different mods may not come out the same.</summary>
        public List<string> Mods { get; set; } = [];
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
        public string? ClientId { get; set; }
        public int AcceptedDay { get; set; }
        public int DeadlineDay { get; set; }
        public string? BattleId { get; set; }
        public int EnemyFactionId { get; set; } = -1;
        public string? Task { get; set; }
    }

    public static string Save(Session session)
    {
        var c = session.Career;
        var data = new SaveData
        {
            SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Seed = session.Galaxy.Seed,
            Mods = [.. session.Db.LoadedMods],
            SystemCount = session.Settings.SystemCount,
            MinorFactions = session.Settings.MinorFactions,
            Day = session.Galaxy.Day,
            DeepKeys = [.. session.Galaxy.DeepKeys],
            Actions = [.. session.Galaxy.PlayerActions],
            Captain = c.Captain,
            Credits = c.Credits,
            SystemId = c.SystemId,
            SystemName = session.Here.Name,
            HullId = c.HullId,
            Loadout = c.Loadout,
            Cargo = new(c.Cargo),
            Reputation = new(c.Reputation),
            TakenOffers = [.. c.TakenOffers],
            MetPeople = [.. c.MetPeople],
            Disposition = new(c.Disposition),
            LastDrink = new(c.LastDrink),
            KnownLeads = [.. c.KnownLeads],
            Haggled = new(c.Haggled),
            WithdrawnOffers = [.. c.WithdrawnOffers],
            ActiveJob = c.ActiveJobId,
            CampaignId = c.CampaignId,
            CampaignStep = c.CampaignStep,
            CampaignBase = c.CampaignBase,
            CampaignDebrief = c.CampaignDebrief,
            Trades = new(c.Trades),
            FuelLy = c.FuelLy,
            TradeRoute = c.TradeRoute,
            SeenMarkets = new(c.SeenMarkets),
            Mined = new(c.Mined),
            Scanned = [.. c.Scanned],
            Logbook = [.. c.Logbook],
            Trails = [.. c.Trails],
            SurveyData = [.. c.SurveyData],
            Offences = [.. c.Offences],
            Lore = [.. c.Lore],
            Clues = new(c.Clues),
            NewsReadDay = c.NewsReadDay,
            LogbookSeen = c.LogbookSeen,
            Crew = [.. c.Crew],
            WagesPaidDay = c.WagesPaidDay,
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
                ClientId = m.Offer.ClientId,
                BattleId = m.Offer.BattleId,
                EnemyFactionId = m.Offer.EnemyFactionId,
                Task = m.Offer.Task,
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

    /// <summary>What checking a save file from elsewhere found.</summary>
    /// <param name="Data">The save's header, when it is a save this build can load.</param>
    /// <param name="Problem">Why it can't be loaded, if it can't.</param>
    /// <param name="Warning">Something worth knowing before loading it, such as different mods.</param>
    public sealed record SaveCheck(SaveData? Data, string? Problem, string? Warning);

    /// <summary>
    /// Checks a save file brought in from another copy of the game (the web version, another
    /// computer) before it replaces anything.
    /// </summary>
    public static SaveCheck Check(ContentDatabase db, string json)
    {
        var data = string.IsNullOrWhiteSpace(json) ? null : Peek(json);
        if (data == null || data.Seed == 0 || data.SystemCount <= 0 || string.IsNullOrEmpty(data.HullId))
            return new SaveCheck(null, "That file isn't a Galaxus save.", null);
        if (data.Version > Version)
            return new SaveCheck(null, "That save comes from a newer version of Galaxus. Update this copy of the game to load it.", null);
        if (data.Version < 1)
            return new SaveCheck(null, "That save is from a version of Galaxus this build can't read.", null);

        var missing = data.Mods.Except(db.LoadedMods).ToList();
        var extra = db.LoadedMods.Except(data.Mods).ToList();
        string? warning = null;
        if (missing.Count > 0 || extra.Count > 0)
        {
            var parts = new List<string>();
            if (missing.Count > 0) parts.Add($"it was made with {Join(missing)}, which this game doesn't have");
            if (extra.Count > 0) parts.Add($"this game has {Join(extra)}, which it wasn't made with");
            warning = $"The mods don't match: {string.Join(", and ", parts)}. The galaxy may not come out quite the same.";
        }
        return new SaveCheck(data, null, warning);
    }

    private static string Join(List<string> mods) => mods.Count == 1 ? $"the mod {mods[0]}" : $"the mods {string.Join(", ", mods)}";

    public static Session Load(ContentDatabase db, string json)
    {
        var data = JsonSerializer.Deserialize<SaveData>(json, Json) ?? throw new InvalidDataException("Save file is empty.");
        // Version 1 saves predate deep space; they load the same way with no deep stars.
        if (data.Version is < 1 or > Version) throw new InvalidDataException($"Save version {data.Version} can't be loaded by this build (expects {Version}).");

        var settings = new GalaxySettings { SystemCount = data.SystemCount, MinorFactions = data.MinorFactions };
        var session = Session.Restore(db, data.Seed, settings, data.Day, data.DeepKeys, data.Actions);
        var c = session.Career;
        c.Captain = data.Captain;
        c.Credits = data.Credits;
        c.SystemId = data.SystemId;
        c.HullId = db.Hulls.ContainsKey(data.HullId) ? data.HullId : "hc-sparrow";
        c.Loadout = data.Loadout?.All(id => id == "" || db.Parts.ContainsKey(id)) == true ? data.Loadout : null;
        foreach (var (k, v) in data.Cargo.Where(kv => db.Commodities.ContainsKey(kv.Key))) c.Cargo[k] = v;
        foreach (var (k, v) in data.Reputation) c.Reputation[k] = v;
        foreach (var id in data.TakenOffers) c.TakenOffers.Add(id);
        foreach (var id in data.MetPeople) c.MetPeople.Add(id);
        foreach (var (k, v) in data.Disposition) c.Disposition[k] = v;
        foreach (var (k, v) in data.LastDrink) c.LastDrink[k] = v;
        foreach (var id in data.KnownLeads) c.KnownLeads.Add(id);
        foreach (var (k, v) in data.Haggled) c.Haggled[k] = v;
        foreach (var id in data.WithdrawnOffers) c.WithdrawnOffers.Add(id);

        foreach (var m in data.Missions.Where(m => db.Missions.ContainsKey(m.TemplateId) || (m.BattleId != null && db.Battles.ContainsKey(m.BattleId))))
        {
            var mission = new Mission(
                m.BattleId != null ? Battles.BattleJobs.Template(db.Battles[m.BattleId]) : db.Missions[m.TemplateId], m.Title, m.Briefing, m.SuccessText, m.FailureText, m.Reward,
                m.CargoId != null ? db.Commodities.GetValueOrDefault(m.CargoId) : null, m.Tonnes, m.DeadlineDays,
                m.ComplicationId != null ? db.Complications.GetValueOrDefault(m.ComplicationId) : null,
                m.ComplicationReveal, m.ComplicationAftermath, new Dictionary<string, string>());
            var offer = new MissionOffer(m.OfferId, mission, m.Giver, m.Origin, m.Destination, m.DestinationStation, m.PostedDay, m.ClientId, m.BattleId, m.EnemyFactionId, m.Task);
            c.Missions.Add(new AcceptedMission(offer, m.AcceptedDay, m.DeadlineDay));
        }
        c.ActiveJobId = data.ActiveJob;
        c.CampaignId = data.CampaignId != null && db.Campaigns.ContainsKey(data.CampaignId) ? data.CampaignId : null;
        c.CampaignStep = data.CampaignStep;
        c.CampaignBase = data.CampaignBase;
        c.CampaignDebrief = data.CampaignDebrief;
        foreach (var (key, mark) in data.Trades) c.Trades[key] = mark;
        c.FuelLy = data.FuelLy;
        foreach (var (k, v) in data.Mined) c.Mined[k] = v;
        foreach (var k in data.Scanned) c.Scanned.Add(k);
        c.Logbook.AddRange(data.Logbook);
        c.Trails.AddRange(data.Trails.Where(t => db.Finds.ContainsKey(t.CacheId) && t.SystemId >= 0 && t.SystemId < session.Galaxy.Systems.Count));
        c.SurveyData.AddRange(data.SurveyData);
        c.Lore.AddRange(data.Lore.Where(l => db.Lore.ContainsKey(l.Id)));
        foreach (var (k, v) in data.Clues.Where(kv => db.Clues.ContainsKey(kv.Key))) c.Clues[k] = Math.Clamp(v, 0, 3);
        c.NewsReadDay = data.NewsReadDay;
        c.LogbookSeen = data.LogbookSeen;
        c.Crew.AddRange(data.Crew.Where(m => m.HullId == null || db.Hulls.ContainsKey(m.HullId)));
        c.WagesPaidDay = data.WagesPaidDay;
        c.Offences.AddRange(data.Offences.Where(o => o.FactionId >= 0 && o.FactionId < session.Galaxy.Factions.Count && Law.LawRules.Kinds.ContainsKey(o.Kind)));
        foreach (var (id, market) in data.SeenMarkets) if (id >= 0 && id < session.Galaxy.Systems.Count) c.SeenMarkets[id] = market;
        // Older saves had no route: pick one from where you are now.
        c.TradeRoute = data.TradeRoute ?? Market.PickRoute(db, session.Galaxy, c.SystemId);
        return session;
    }
}
