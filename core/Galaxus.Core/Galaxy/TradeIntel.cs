namespace Galaxus.Core.Galaxy;

/// <summary>A market as you last saw it.</summary>
public sealed record SeenMarket(int Day, Dictionary<string, SeenPrice> Prices);

/// <summary>One good on a market as you last saw it.</summary>
public sealed record SeenPrice(int Buy, int Sell, int Stock, int Wanted, float Trend, bool Deal);

/// <summary>A run you could make from here: buy here, carry it, sell there.</summary>
/// <param name="Tonnes">How much you could carry, limited by stock and your hold.</param>
/// <param name="Profit">Expected profit for the whole run, from the prices you know.</param>
/// <param name="AgeDays">How old the price at the other end is.</param>
public sealed record TradeRun(int From, int To, string CommodityId, int Buy, int Sell, int Tonnes, int Profit, int Days, int AgeDays);

/// <summary>One good compared between two markets: buy at one end, sell at the other.</summary>
/// <param name="Margin">Credits a tonne made carrying it this way.</param>
public sealed record PriceGap(string CommodityId, int Buy, int Sell, int Margin, float Trend);

/// <summary>Where you are against another market you know.</summary>
/// <param name="AgeDays">How old your prices for the other market are; null if you've never seen it.</param>
/// <param name="Higher">Goods that fetch more there than they cost here: worth taking.</param>
/// <param name="Lower">Goods that cost less there than they fetch here: worth bringing back.</param>
/// <param name="Best">The best single run from here to there, for your hold.</param>
public sealed record MarketComparison(int? AgeDays, List<PriceGap> Higher, List<PriceGap> Lower, TradeRun? Best);

/// <summary>
/// What the player knows about markets: live prices where you are, the prices you saw at markets
/// you've visited (they age), and word of jackpots within a few jumps. The galaxy map's trade
/// overlay and the market screen's tips are built from this; nothing here sees what you couldn't.
/// </summary>
public static class TradeIntel
{
    /// <summary>How many markets you remember; the oldest are forgotten first.</summary>
    public const int Remembered = 40;

    /// <summary>How far word of a jackpot travels, in jumps.</summary>
    public const int RumourJumps = 3;

    /// <summary>Notes the market here as you see it now.</summary>
    public static void Note(Session session) => Note(session, session.Here);

    /// <summary>Notes a market as it stands today, wherever it is (for prices heard about as well as seen).</summary>
    public static void Note(Session session, StarSystem here)
    {
        if (here.Stations.Count == 0) return;
        var prices = Market.Quotes(session, here).ToDictionary(q => q.Commodity.Id, q => new SeenPrice(q.Buy, q.Sell, q.Stock, q.Wanted, q.Trend, q.Deal));
        var seen = session.Career.SeenMarkets;
        seen[here.Id] = new SeenMarket(session.Galaxy.Day, prices);
        while (seen.Count > Remembered) seen.Remove(seen.MinBy(s => s.Value.Day).Key);
    }

    /// <summary>A good's price at every market you know of: live here, as last seen elsewhere.</summary>
    public static List<(StarSystem System, SeenPrice Price, int AgeDays)> Known(Session session, string commodityId)
    {
        var galaxy = session.Galaxy;
        var result = new List<(StarSystem, SeenPrice, int)>();
        if (session.Here.Stations.Count > 0 && Market.Legal(session.Db, session.Here, galaxy).Any(c => c.Id == commodityId))
        {
            var q = Market.Quote(session, session.Here, session.Db.Commodities[commodityId]);
            result.Add((session.Here, new SeenPrice(q.Buy, q.Sell, q.Stock, q.Wanted, q.Trend, q.Deal), 0));
        }
        foreach (var (id, market) in session.Career.SeenMarkets)
            if (id != session.Here.Id && market.Prices.TryGetValue(commodityId, out var price))
                result.Add((galaxy.Systems[id], price, galaxy.Day - market.Day));
        return result;
    }

    /// <summary>
    /// The best runs from here among markets you know: buy here, sell where you last saw a good
    /// price, ranked by profit per day of travel. Pass a good to look at that one only; without one
    /// you get the best run to each destination.
    /// </summary>
    public static List<TradeRun> Routes(Session session, string? commodityId = null, int count = 5)
    {
        var here = session.Here;
        if (here.Stations.Count == 0) return [];
        var galaxy = session.Galaxy;
        int hold = session.Career.CargoCapacity(session.Db);
        var runs = new List<TradeRun>();
        var days = new Dictionary<int, int>();
        foreach (var q in Market.Quotes(session, here))
        {
            if (commodityId != null && q.Commodity.Id != commodityId) continue;
            int tonnes = Math.Min(hold, q.Stock);
            if (tonnes <= 0) continue;
            foreach (var (id, market) in session.Career.SeenMarkets)
            {
                if (id == here.Id || !market.Prices.TryGetValue(q.Commodity.Id, out var there)) continue;
                int margin = there.Sell - q.Buy;
                if (margin <= 0) continue;
                if (!days.TryGetValue(id, out int d))
                {
                    var route = galaxy.Route(here.Id, id);
                    d = days[id] = route.Count > 1 ? galaxy.RouteDays(route) : -1;
                }
                if (d <= 0) continue;
                runs.Add(new TradeRun(here.Id, id, q.Commodity.Id, q.Buy, there.Sell, tonnes, margin * tonnes, d, galaxy.Day - market.Day));
            }
        }
        // With no good picked, the best run to each place, so the list covers different destinations.
        var ranked = runs.OrderByDescending(r => r.Profit / (float)r.Days).ThenBy(r => r.To).ThenBy(r => r.CommodityId, StringComparer.Ordinal);
        return commodityId != null ? [.. ranked.Take(count)] : [.. ranked.GroupBy(r => r.To).Select(g => g.First()).Take(count)];
    }

    /// <summary>Jackpots you've heard about: running, with either end within a few jumps of you.</summary>
    public static List<TradeDeal> Rumours(Session session)
    {
        var galaxy = session.Galaxy;
        var near = galaxy.WithinJumps(session.Here.Id, RumourJumps).Select(s => s.Id).Append(session.Here.Id).ToHashSet();
        return [.. Market.Deals(session.Db, galaxy, galaxy.Day).Where(d => near.Contains(d.Source) || near.Contains(d.Destination))];
    }

    /// <summary>
    /// Compares the market here (live) with another you know (as last seen): what's higher there,
    /// what's lower, and the best run. Unknown markets come back with no prices.
    /// </summary>
    public static MarketComparison Compare(Session session, StarSystem there, int count = 6)
    {
        var here = session.Here;
        if (here.Stations.Count == 0 || there.Id == here.Id || !session.Career.SeenMarkets.TryGetValue(there.Id, out var seen))
            return new MarketComparison(null, [], [], null);
        var live = Market.Quotes(session, here).ToDictionary(q => q.Commodity.Id);
        var higher = new List<PriceGap>();
        var lower = new List<PriceGap>();
        foreach (var (id, far) in seen.Prices)
        {
            if (!live.TryGetValue(id, out var near)) continue;
            if (near.Stock > 0 && far.Sell > near.Buy) higher.Add(new PriceGap(id, near.Buy, far.Sell, far.Sell - near.Buy, far.Trend));
            if (far.Stock > 0 && near.Sell > far.Buy) lower.Add(new PriceGap(id, far.Buy, near.Sell, near.Sell - far.Buy, far.Trend));
        }
        var best = Routes(session, null, 50).FirstOrDefault(r => r.To == there.Id);
        return new MarketComparison(session.Galaxy.Day - seen.Day,
            [.. higher.OrderByDescending(g => g.Margin).Take(count)], [.. lower.OrderByDescending(g => g.Margin).Take(count)], best);
    }

    /// <summary>The best place you know to sell something you're carrying, if anywhere beats here.</summary>
    public static (StarSystem System, int Sell, int AgeDays)? BestSale(Session session, string commodityId)
    {
        var known = Known(session, commodityId);
        if (known.Count == 0) return null;
        var best = known.MaxBy(k => k.Price.Sell);
        return best.System.Id == session.Here.Id ? null : (best.System, best.Price.Sell, best.AgeDays);
    }
}
