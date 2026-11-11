using Galaxus.Core.Content;
using Galaxus.Core.Ships;

namespace Galaxus.Core.Galaxy;

/// <summary>A part on sale at a station's outfitter, at this outfitter's price.</summary>
public sealed record PartOffer(PartDef Part, int Price);

/// <summary>
/// What outfitters sell, and fitting it. Stock changes week by week, like the shipyards: parts come
/// from builders in this region and the next, plus whoever owns the system, and rim builders' gear
/// turns up only out on the rim and in the outer systems. Bigger stations carry higher marks. The
/// economy tilts the shelves: industry and high tech stock drives and shields, mining systems stock
/// mining gear and racks, frontier ports stock scoops and repair kits, and stations near a war stock
/// guns. An outfitter buys used parts for half the list price.
/// </summary>
public static class Outfitting
{
    /// <summary>What a used part fetches.</summary>
    public static int SellValue(PartDef part) => part.Price / 2;

    public static List<PartOffer> Offers(Session session, StarSystem system)
    {
        if (system.Stations.Count == 0) return [];
        var db = session.Db;
        var galaxy = session.Galaxy;
        int day = galaxy.Day, week = day / 7;
        var profile = SystemProfiles.For(galaxy, system, db);
        var owner = galaxy.OwnerOf(system);
        string? ownBuilder = owner != null ? Battles.BattleJobs.BuilderFor(db, owner) : null;
        bool war = galaxy.Conflicts.Any(c => c.Stage >= 2 && (c.FrontSystem == system.Id || galaxy.Jumps(system.Id, c.FrontSystem, 2) is > 0 and <= 2));
        int here = Array.IndexOf(ContentRules.Regions, system.Region);
        var (chance, topMark) = profile.StationSize switch
        {
            StationSize.Outpost => (0.18f, 2), StationSize.Station => (0.3f, 3), StationSize.Hub => (0.45f, 4), _ => (0.55f, 5),
        };

        var rolled = new List<(PartOffer Offer, float Roll, float Chance)>();
        foreach (var part in db.Parts.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (!db.Builders.TryGetValue(part.Builder, out var builder)) continue;
            bool own = part.Builder == ownBuilder;
            int distance = Math.Abs(Array.IndexOf(ContentRules.Regions, builder.Region) - here);
            if (distance > 1 && !own) continue;
            if (builder.Region == "rim" && system.Region is not ("rim" or "outer") && !own) continue;
            if (part.Mark > topMark) continue;
            // Rare one-off parts only turn up at big stations, and seldom.
            float affinity = part.Mark == 0 ? (topMark >= 4 ? 0.25f : 0) : 1;
            affinity *= own ? 1.6f : distance == 0 ? 1 : 0.6f;
            affinity *= part.Mark switch { 1 => 1.3f, 2 => 1.1f, 3 => 0.9f, 4 => 0.7f, 5 => 0.55f, _ => 1 };
            affinity *= Tilt(system.Economy, part);
            if (war) affinity *= part.Type is "weapon" or "shield" ? 1.4f : part.Type == "cargo" ? 0.7f : 1;
            float p = Math.Clamp(chance * affinity, 0, 0.95f);
            float roll = new Rng(system.Seed ^ Rng.Hash("outfit:" + part.Id) ^ (ulong)(uint)week).NextFloat();

            float position = 0.5f;
            if (own) position -= 0.2f;
            if (distance > 0) position += 0.15f;
            if (war && part.Type is "weapon" or "shield") position += 0.15f;
            if (session.Easy) position -= 0.05f;
            position += 0.15f * Market.Drift(system.Seed ^ Rng.Hash("outfit-price:" + part.Id), day);
            var (low, high) = Range(part);
            int price = (int)(MathF.Round((low + (high - low) * Math.Clamp(position, 0, 1)) / 10f) * 10);
            rolled.Add((new PartOffer(part, price), roll, p));
        }

        var offers = rolled.Where(r => r.Roll < r.Chance).Select(r => r.Offer).ToList();
        // A proper station always has a decent shelf: the parts that came closest to rolling in.
        int least = profile.StationSize == StationSize.Outpost ? 6 : 20;
        if (offers.Count < least)
            offers.AddRange(rolled.Where(r => r.Roll >= r.Chance && r.Chance > 0).OrderBy(r => r.Roll - r.Chance).Take(least - offers.Count).Select(r => r.Offer));
        return [.. offers.OrderBy(o => o.Part.Type, StringComparer.Ordinal).ThenBy(o => o.Part.Size).ThenBy(o => o.Price).ThenBy(o => o.Part.Id, StringComparer.Ordinal)];
    }

    /// <summary>The cheapest and dearest an outfitter normally asks.</summary>
    public static (int Low, int High) Range(PartDef part) => ((int)(part.Price * 0.85f), (int)(part.Price * 1.2f));

    /// <summary>How much more (or less) a system's economy stocks a kind of part.</summary>
    private static float Tilt(Economy economy, PartDef part)
    {
        bool mining = part.Stats.Keys.Any(k => k is "miningRate" or "mining" or "oreScoopRate" or "refineRate" or "cuttingRate");
        bool fieldKit = part.Stats.Keys.Any(k => k is "scoopRate" or "fuelTonnes" or "repairRate" or "repairPerSecond" or "repairHullPerSecond" or "hullRepairPerSecond");
        return economy switch
        {
            Economy.Industrial or Economy.HighTech => part.Type is "engine" or "powerPlant" or "shield" ? 1.4f : part.Type == "sensor" && economy == Economy.HighTech ? 1.4f : 1,
            Economy.Mining or Economy.Refinery => mining ? 2.5f : part.Type == "cargo" ? 1.5f : part.Type == "weapon" ? 0.8f : 1,
            Economy.Frontier => fieldKit ? 2f : part.Type == "utility" ? 1.3f : part.Mark >= 4 ? 0.6f : 1,
            Economy.Agricultural => part.Type == "cargo" ? 1.4f : part.Type == "weapon" ? 0.7f : 1,
            _ => 1,
        };
    }

    /// <summary>The part in each of your ship's slots, null where a slot is empty.</summary>
    public static PartDef?[] Fitted(Session session) =>
        ShipFactory.Assign(session.Db, session.Db.Hulls[session.Career.HullId], session.Career.Loadout);

    /// <summary>The ship you'd have with one slot changed (null empties it).</summary>
    public static ShipSpec WithPart(Session session, int slot, PartDef? part)
    {
        var fitted = Fitted(session);
        fitted[slot] = part;
        return ShipFactory.Build(session.Db, session.Career.HullId, Ids(fitted));
    }

    private static List<string> Ids(PartDef?[] fitted) => [.. fitted.Select(p => p?.Id ?? "")];

    /// <summary>What fitting a part costs once the old one in that slot is sold.</summary>
    public static int Cost(Session session, int slot, PartOffer offer) =>
        offer.Price - (Fitted(session)[slot] is { } old ? SellValue(old) : 0);

    /// <summary>Why the part can't go in that slot, or null if it can.</summary>
    public static string? CannotFit(Session session, int slot, PartOffer offer)
    {
        var hull = session.Db.Hulls[session.Career.HullId];
        if (slot < 0 || slot >= hull.Slots.Count) return "Pick a slot first.";
        var s = hull.Slots[slot];
        if (offer.Part.Size > s.Size && (s.Type == offer.Part.Type || ShipFactory.IsMiningLaser(offer.Part))) return $"Too big: that slot takes size {s.Size} or smaller.";
        if (!ShipFactory.SlotTakes(s, offer.Part)) return $"That slot takes a {Label(s.Type)}.";
        if (Fitted(session)[slot]?.Id == offer.Part.Id) return "Already fitted.";
        int cost = Cost(session, slot, offer);
        if (cost > session.Career.Credits) return $"You need {cost:N0} cr after selling the old part.";
        if (WithPart(session, slot, offer.Part).CargoTonnes < session.Career.CargoUsed) return "Your hold would be too small for what you're carrying.";
        return null;
    }

    public static void Fit(Session session, int slot, PartOffer offer)
    {
        if (CannotFit(session, slot, offer) != null) return;
        session.Career.Credits -= Cost(session, slot, offer);
        var fitted = Fitted(session);
        fitted[slot] = offer.Part;
        session.Career.Loadout = Ids(fitted);
    }

    /// <summary>Why the part in that slot can't be sold, or null if it can.</summary>
    public static string? CannotRemove(Session session, int slot)
    {
        var fitted = Fitted(session);
        if (slot < 0 || slot >= fitted.Length || fitted[slot] is not { } part) return "Nothing fitted there.";
        if (part.Type is "engine" or "powerPlant" && fitted.Count(p => p?.Type == part.Type) == 1)
            return $"You can't fly without a {Label(part.Type)}. Fit a replacement instead.";
        if (WithPart(session, slot, null).CargoTonnes < session.Career.CargoUsed) return "Your hold would be too small for what you're carrying.";
        return null;
    }

    public static void Remove(Session session, int slot)
    {
        if (CannotRemove(session, slot) != null) return;
        var fitted = Fitted(session);
        session.Career.Credits += SellValue(fitted[slot]!);
        fitted[slot] = null;
        session.Career.Loadout = Ids(fitted);
    }

    /// <summary>A slot or part type in words.</summary>
    public static string Label(string type) => type switch
    {
        "powerPlant" => "power plant", "cargo" => "cargo rack", _ => type,
    };

    /// <summary>What a utility part does, in words, or null if it does nothing yet.</summary>
    public static List<string> Effects(PartDef part)
    {
        var s = part.Stats;
        var lines = new List<string>();
        float Get(params string[] keys) => keys.Sum(k => s.GetValueOrDefault(k));
        if (Get("repairRate", "repairPerSecond", "repairHullPerSecond", "hullRepairPerSecond") is > 0 and var repair)
            lines.Add($"Patches {repair:0.#} hull a second once your shields are back up");
        if (Get("scoopRate") is > 0 and var scoop) lines.Add($"Scoops about {scoop:0.#} ly of fuel a second skimming close to a star");
        if (Get("fuelTonnes") is > 0 and var tank) lines.Add($"Adds {tank * 5:0} ly to the fuel tank");
        if (Get("hullPoints") is > 0 and var plate) lines.Add($"Adds {plate:0} hull");
        if (Get("massTonnes") is > 0 and var mass) lines.Add($"Weighs {mass:0} t, which slows acceleration");
        if (Get("starRecharge") is > 0 and var sail) lines.Add($"Weapons recharge {sail * 100:0}% faster");
        if (Get("missileDecoyCharges") is > 0 and var decoys) lines.Add($"{decoys:0} decoys a sortie, each with a {s.GetValueOrDefault("lockBreakChance") * 100:0}% chance to shake off a missile");
        if (s.TryGetValue("detectionRangeFactor", out var seen)) lines.Add($"Enemies notice you at {seen * 100:0}% of the usual range");
        if (s.TryGetValue("collisionDamageFactor", out var ram)) lines.Add($"Collisions do {ram * 100:0}% of the usual damage");
        if (Get("cargoCapacityBonus") is > 0 and var bonus) lines.Add($"Hold {bonus * 100:0}% bigger");
        if (Get("hiddenCargoTonnes") is > 0 and var hidden) lines.Add($"A hidden hold of {hidden:0} t");
        if (Get("gateFuelSaving") is > 0 and var saving) lines.Add($"Direct jumps use {saving * 100:0}% less fuel");
        if (Get("sensorRange") is > 0 and var lens) lines.Add($"Sensor range {lens * 100:0}% further");
        if (s.ContainsKey("detail") && s.TryGetValue("range", out var scan)) lines.Add($"Scans out to {scan:N0} m, extending your sensors");
        if (Get("freeMachineMarketDiscount") is > 0 and var shake) lines.Add($"Goods cost {shake * 100:0}% less at Free Machine ports");
        if (Get("boostSpeedFactor") is > 0 and var burn) lines.Add($"An afterburner: {burn:0.#} times top speed for {s.GetValueOrDefault("boostSeconds"):0} s, ready again after {s.GetValueOrDefault("cooldownSeconds"):0} s");
        if (Get("miningRate", "mining", "cuttingRate", "oreScoopRate", "refineRate", "salvageYield", "grappleRange") > 0)
            lines.Add("Mining and salvage gear: it comes into its own when mining arrives");
        return lines;
    }
}
