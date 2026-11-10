using Galaxus.Core.Content;

namespace Galaxus.Core.Galaxy;

/// <summary>A ship on sale at a shipyard, at this yard's price.</summary>
public sealed record ShipOffer(HullDef Hull, int Price);

/// <summary>
/// What shipyards have on sale. Stock changes week by week: a big station nearly always has a broad
/// list, an outpost one or two ships and sometimes none. The owner's own builder is stocked more
/// often and sold cheaper; designs from further away are rarer and dearer; yards near a war carry
/// more fighters and gunships and charge more for them; and after a takeover the yard is thin for a
/// few weeks while the new owner's builder moves in. Rim designs, drones and capital ships are never
/// sold. Like markets, it's all worked out from the seed and the week.
/// </summary>
public static class Shipyards
{
    /// <summary>The cheapest and dearest a hull normally sells for.</summary>
    public static (int Low, int High) Range(HullDef h) =>
        (h.PriceLow > 0 ? h.PriceLow : (int)MathF.Round(h.Price * 0.8f), h.PriceHigh > 0 ? h.PriceHigh : (int)MathF.Round(h.Price * 1.25f));

    public static List<ShipOffer> Offers(Session session, StarSystem system)
    {
        if (system.Stations.Count == 0) return [];
        var db = session.Db;
        var galaxy = session.Galaxy;
        int day = galaxy.Day, week = day / 7;
        var profile = SystemProfiles.For(galaxy, system, db);
        var owner = galaxy.OwnerOf(system);
        string? ownBuilder = owner != null ? Battles.BattleJobs.BuilderFor(db, owner) : null;
        string? oldBuilder = system.PreviousOwner >= 0 ? Battles.BattleJobs.BuilderFor(db, galaxy.Factions[system.PreviousOwner]) : null;
        float sinceTakeover = system.PreviousOwner >= 0 ? Math.Clamp((day - system.OwnerSince) / 21f, 0, 1) : 1;
        bool war = galaxy.Conflicts.Any(c => c.Stage >= 2 && (c.FrontSystem == system.Id || galaxy.Jumps(system.Id, c.FrontSystem, 2) is > 0 and <= 2));
        int here = Array.IndexOf(ContentRules.Regions, system.Region);
        float chance = profile.StationSize switch { StationSize.Outpost => 0.2f, StationSize.Station => 0.45f, StationSize.Hub => 0.75f, _ => 0.85f };

        var rolled = new List<(ShipOffer Offer, float Roll, float Chance)>();
        foreach (var h in db.Hulls.Values.Where(h => h.Class != "drone" && h.ForSale && !h.IsCapital).OrderBy(h => h.Id, StringComparer.Ordinal))
        {
            if (!db.Builders.TryGetValue(h.Builder, out var builder) || builder.Region == "rim") continue;
            int distance = Math.Abs(Array.IndexOf(ContentRules.Regions, builder.Region) - here);
            bool own = h.Builder == ownBuilder;
            if (distance > 1 && !own) continue;

            // Owners stock their own builder's ships; after a takeover the old owner's fade out as the new one's arrive.
            float affinity = own ? 1 + 0.8f * sinceTakeover : distance == 0 ? 1 : 0.6f;
            if (h.Builder == oldBuilder && !own) affinity *= 1 - 0.5f * sinceTakeover;
            if (war) affinity *= h.Class is "fighter" or "gunship" ? 1.4f : h.Class is "freighter" or "courier" ? 0.7f : 1;
            float p = Math.Clamp(chance * affinity * (0.6f + 0.4f * sinceTakeover), 0, 0.97f);
            float roll = new Rng(system.Seed ^ Rng.Hash("yard:" + h.Id) ^ (ulong)(uint)week).NextFloat();

            float position = 0.5f;
            if (own) position -= 0.25f;
            if (distance > 0) position += 0.15f;
            if (war && h.Class is "fighter" or "gunship") position += 0.2f;
            if (profile.StationSize is StationSize.Hub or StationSize.Citadel) position -= 0.05f;
            if (sinceTakeover < 1) position += 0.1f;
            if (session.Easy) position -= 0.05f;
            position += 0.15f * Market.Drift(system.Seed ^ Rng.Hash("yard-price:" + h.Id), day);
            var (low, high) = Range(h);
            int price = (int)(MathF.Round((low + (high - low) * Math.Clamp(position, 0, 1)) / 50f) * 50);
            rolled.Add((new ShipOffer(h, price), roll, p));
        }

        var offers = rolled.Where(r => r.Roll < r.Chance).Select(r => r.Offer).ToList();
        // A proper station always has a couple of ships in: the ones that came closest to rolling in.
        int least = profile.StationSize == StationSize.Outpost ? 0 : 2;
        if (offers.Count < least)
            offers.AddRange(rolled.Where(r => r.Roll >= r.Chance).OrderBy(r => r.Roll - r.Chance).Take(least - offers.Count).Select(r => r.Offer));
        return [.. offers.OrderBy(o => o.Price).ThenBy(o => o.Hull.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// What a yard will give for your ship: 60% of what it sells them for here, or 55% of the list
    /// price if it doesn't stock them.
    /// </summary>
    public static int TradeIn(Session session, StarSystem system, string hullId)
    {
        var offer = Offers(session, system).FirstOrDefault(o => o.Hull.Id == hullId);
        return offer != null ? offer.Price * 6 / 10 : session.Db.Hulls[hullId].Price * 55 / 100;
    }
}
