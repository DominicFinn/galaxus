using Galaxus.Core.Content;

namespace Galaxus.Core.Galaxy;

/// <summary>A market's offer for one good: what it costs, what you'd get, how much there is and which way it's going.</summary>
/// <param name="Buy">Credits a tonne to buy here now.</param>
/// <param name="Sell">Credits a tonne you'd get for the first tonne you sell here.</param>
/// <param name="Stock">Tonnes on sale.</param>
/// <param name="Wanted">Tonnes the market will take before the price you get starts to sag.</param>
/// <param name="Trend">Change in price over the last week, e.g. 0.12 for 12% up.</param>
/// <param name="Deal">True when this is part of a jackpot: a big surplus here, or a nearby system paying well over the odds.</param>
public sealed record MarketQuote(CommodityDef Commodity, int Buy, int Sell, int Stock, int Wanted, float Trend, bool Deal);

/// <summary>A good running short in a system for a few days: little stock, high prices, and in the news.</summary>
public sealed record Shortage(int SystemId, string CommodityId, int StartDay, int EndDay)
{
    public bool ActiveOn(int day) => day >= StartDay && day < EndDay;
}

/// <summary>A chance surplus in one system and a nearby system paying well for it, for a few days.</summary>
public sealed record TradeDeal(int Source, int Destination, string CommodityId, int StartDay, int EndDay)
{
    public bool ActiveOn(int day) => day >= StartDay && day < EndDay;
}

/// <summary>
/// Prices and stock. Every good has a price range; where a market sits in it comes from its
/// economy, size, security, nearby wars, recent takeovers, events, the week's drift and how much
/// stock is left. Stock is limited, and buying runs it down while selling fills what the market
/// wants, so prices move as you trade. Everything is worked out from the galaxy seed and the day,
/// so only the player's own trades need saving (<see cref="Career.Trades"/>), and they fade over
/// a few days as markets recover.
/// </summary>
public static class Market
{
    /// <summary>Where a category sits by economy: below 1 means it's made here (cheap, plenty), above 1 that it's wanted.</summary>
    private static readonly Dictionary<Economy, Dictionary<string, float>> EconomyBias = new()
    {
        [Economy.Agricultural] = new() { ["food"] = 0.6f, ["water"] = 0.8f, ["machinery"] = 1.3f, ["technology"] = 1.3f, ["luxury"] = 1.2f },
        [Economy.Mining] = new() { ["ore"] = 0.55f, ["food"] = 1.35f, ["water"] = 1.3f, ["machinery"] = 1.2f, ["medical"] = 1.3f, ["fuel"] = 1.1f },
        [Economy.Refinery] = new() { ["metals"] = 0.65f, ["fuel"] = 0.7f, ["ore"] = 1.3f, ["food"] = 1.2f },
        [Economy.Industrial] = new() { ["machinery"] = 0.7f, ["industrial"] = 0.7f, ["consumer"] = 0.8f, ["weapons"] = 0.85f, ["metals"] = 1.3f, ["food"] = 1.2f },
        [Economy.HighTech] = new() { ["technology"] = 0.65f, ["medical"] = 0.75f, ["data"] = 0.8f, ["luxury"] = 1.3f, ["food"] = 1.25f, ["metals"] = 1.2f },
        [Economy.Frontier] = new() { ["food"] = 1.4f, ["medical"] = 1.5f, ["machinery"] = 1.4f, ["weapons"] = 1.3f, ["ore"] = 0.8f, ["elder-artefacts"] = 0.8f, ["rim-tech"] = 0.8f },
    };

    private static readonly HashSet<string> Basics = ["food", "water", "medical", "fuel"];
    private static readonly HashSet<string> Finery = ["luxury", "technology", "consumer", "data"];
    private static readonly HashSet<string> WarGoods = ["weapons", "medical", "fuel", "food"];

    /// <summary>How many days the player's effect on a market takes to halve.</summary>
    public static float RecoveryHalfLife(bool easy) => easy ? 2f : 3f;

    /// <summary>The cheapest a good ever sells for, and the dearest, before events push it further.</summary>
    public static (int Low, int High) Range(CommodityDef c) =>
        (c.PriceLow > 0 ? c.PriceLow : (int)MathF.Round(c.BasePrice * 0.6f), c.PriceHigh > 0 ? c.PriceHigh : (int)MathF.Round(c.BasePrice * 1.55f));

    // The public face ------------------------------------------------------------------------

    /// <summary>Every good on this market, for the market screen.</summary>
    public static List<MarketQuote> Quotes(Session session, StarSystem system)
    {
        var ctx = Context.For(session.Db, session.Galaxy, system, session.Galaxy.Day, session);
        return Legal(session.Db, system, session.Galaxy).Select(c => Quote(session, system, c, ctx)).ToList();
    }

    public static MarketQuote Quote(Session session, StarSystem system, CommodityDef c) =>
        Quote(session, system, c, Context.For(session.Db, session.Galaxy, system, session.Galaxy.Day, session));

    private static MarketQuote Quote(Session session, StarSystem system, CommodityDef c, Context ctx)
    {
        var galaxy = session.Galaxy;
        var b = ctx.Base(c, galaxy.Day);
        var (bought, sold) = session.Career.TradeEffect(system.Id, c.Id, galaxy.Day, RecoveryHalfLife(session.Easy));
        float trend = Trend(ctx, c, galaxy.Day);
        return new MarketQuote(c, b.BuyPrice(bought), b.SellPrice(sold), Math.Max(0, (int)(b.Stock - bought)), Math.Max(0, (int)(b.Wanted - sold)), trend, b.Deal);
    }

    /// <summary>A market's middle price, without the player's trades: for rumours and tests.</summary>
    /// <summary>
    /// An economy in a line, from the same leanings the prices use: "Agricultural: exports food;
    /// imports machinery, technology and luxury." Empty for no economy.
    /// </summary>
    public static string TradeProfile(Economy economy)
    {
        if (!EconomyBias.TryGetValue(economy, out var bias)) return "";
        static string List(IEnumerable<string> items)
        {
            var l = items.Select(i => i.Replace('-', ' ')).ToList();
            return l.Count <= 1 ? string.Concat(l) : string.Join(", ", l.Take(l.Count - 1)) + " and " + l[^1];
        }
        var exports = bias.Where(b => b.Value < 0.9f).OrderBy(b => b.Value).Select(b => b.Key);
        var imports = bias.Where(b => b.Value > 1.15f).OrderByDescending(b => b.Value).Select(b => b.Key);
        var parts = new List<string>();
        if (exports.Any()) parts.Add("exports " + List(exports));
        if (imports.Any()) parts.Add("imports " + List(imports));
        return parts.Count == 0 ? "" : $"{economy}: {string.Join("; ", parts)}.";
    }

    public static int Price(ContentDatabase db, Galaxy galaxy, StarSystem system, CommodityDef c) =>
        (int)MathF.Round(Context.For(db, galaxy, system, galaxy.Day, null).Base(c, galaxy.Day).Mid);

    /// <summary>
    /// What buying tonnes here would cost, tonne by tonne as the stock runs down. Returns how many
    /// you can have (limited by stock, hold space and money) and the total.
    /// </summary>
    public static (int Tonnes, int Cost) BuyCost(Session session, StarSystem system, CommodityDef c, int tonnes, int credits)
    {
        var day = session.Galaxy.Day;
        var b = Context.For(session.Db, session.Galaxy, system, day, session).Base(c, day);
        var (bought, _) = session.Career.TradeEffect(system.Id, c.Id, day, RecoveryHalfLife(session.Easy));
        int got = 0, cost = 0;
        while (got < tonnes && bought + got + 1 <= b.Stock)
        {
            int price = b.BuyPrice(bought + got);
            if (cost + price > credits) break;
            cost += price;
            got++;
        }
        return (got, cost);
    }

    /// <summary>What selling tonnes here would bring, tonne by tonne as the market fills up.</summary>
    public static int SaleValue(Session session, StarSystem system, CommodityDef c, int tonnes)
    {
        var day = session.Galaxy.Day;
        var b = Context.For(session.Db, session.Galaxy, system, day, session).Base(c, day);
        var (_, sold) = session.Career.TradeEffect(system.Id, c.Id, day, RecoveryHalfLife(session.Easy));
        int value = 0;
        for (int i = 0; i < tonnes; i++) value += b.SellPrice(sold + i);
        return value;
    }

    /// <summary>
    /// Goods a market deals in: legal goods everywhere, restricted goods except in the safest
    /// systems, contraband only where the law doesn't reach.
    /// </summary>
    public static IEnumerable<CommodityDef> Legal(ContentDatabase db, StarSystem system, Galaxy galaxy)
    {
        string security = SystemProfiles.For(galaxy, system, db).Security;
        bool lawless = security is "Lawless" or "Low";
        return db.Commodities.Values
            .Where(c => c.Legality == "legal" || (c.Legality == "restricted" && security != "High") || (c.Legality == "contraband" && lawless))
            .OrderBy(c => c.Category, StringComparer.Ordinal).ThenBy(c => c.Name, StringComparer.Ordinal);
    }

    /// <summary>Jackpots running anywhere on the day, between systems that have markets.</summary>
    public static IEnumerable<TradeDeal> Deals(ContentDatabase db, Galaxy galaxy, int day)
    {
        int period = day / DealPeriod;
        return DealsFor(db, galaxy, period).Concat(DealsFor(db, galaxy, period - 1))
            .Where(d => d.ActiveOn(day) && galaxy.Systems[d.Source].Stations.Count > 0 && galaxy.Systems[d.Destination].Stations.Count > 0);
    }

    /// <summary>Shortages running anywhere on the day, in systems that have markets.</summary>
    public static IEnumerable<Shortage> Shortages(ContentDatabase db, Galaxy galaxy, int day)
    {
        int period = day / ShortagePeriod;
        return ShortagesFor(db, galaxy, period).Concat(ShortagesFor(db, galaxy, period - 1))
            .Where(s => s.ActiveOn(day) && galaxy.Systems[s.SystemId].Stations.Count > 0);
    }

    // The model ------------------------------------------------------------------------------

    /// <summary>A market's starting point for one good on a day, before the player's own trades.</summary>
    private readonly record struct Base(int Low, int High, float Position, float EventFactor, float Spread, float Stock, float Wanted, bool Deal)
    {
        /// <summary>Taken off what you pay, from gear like the Free Machines' handshake beacon.</summary>
        public float Discount { get; init; }

        /// <summary>Price at a position in the range. Only a glut (selling far more than wanted) goes below the bottom.</summary>
        private float At(float position, float floor = 0)
        {
            float price = Low + (High - Low) * Math.Clamp(position, floor, 1f);
            price *= EventFactor;
            return MathF.Max(1, MathF.Min(price, High * 1.4f));
        }

        public float Mid => At(Position);

        /// <summary>Price to buy the next tonne once <paramref name="bought"/> tonnes have gone: dearer as stock runs low.</summary>
        public int BuyPrice(float bought)
        {
            float left = Stock > 0 ? Math.Clamp(1 - bought / Stock, 0, 1) : 0;
            return (int)MathF.Round(At(Position + 0.3f * (1 - left)) * (1 + Spread / 2) * (1 - Discount));
        }

        /// <summary>Price for the next tonne once <paramref name="sold"/> tonnes have been sold: lower as the market fills.</summary>
        public int SellPrice(float sold)
        {
            float filled = Wanted > 0 ? sold / Wanted : 2;
            float sag = filled <= 1 ? 0.15f * filled : 0.15f + 0.45f * MathF.Min(filled - 1, 2);
            return Math.Max(1, (int)MathF.Round(At(Position - sag, floor: filled > 1 ? -0.4f : 0) * (1 - Spread / 2)));
        }
    }

    /// <summary>The facts about a system that every good's price depends on, worked out once per market.</summary>
    private sealed class Context
    {
        private ContentDatabase _db = null!;
        private Galaxy _galaxy = null!;
        private StarSystem _system = null!;
        private SystemProfile _profile = null!;
        private Session? _session;
        private float _size;
        private int _warLevel; // 0 none, 1 within two jumps of a hot front, 2 the front itself
        private bool _takenOver;
        private LandmarkDef? _landmark;
        private List<TradeDeal> _deals = [];
        private HashSet<string> _shortages = [];
        private float _discount;

        public static Context For(ContentDatabase db, Galaxy galaxy, StarSystem system, int day, Session? session)
        {
            var profile = SystemProfiles.For(galaxy, system, db);
            var ctx = new Context
            {
                _db = db, _galaxy = galaxy, _system = system, _profile = profile, _session = session,
                _size = profile.StationSize switch { StationSize.Outpost => 0.35f, StationSize.Hub => 1.8f, StationSize.Citadel => 2.4f, _ => 1f },
                _takenOver = system.PreviousOwner >= 0 && day - system.OwnerSince < 21,
            };
            foreach (var c in galaxy.Conflicts.Where(c => c.Stage >= 2))
            {
                if (c.FrontSystem == system.Id) { ctx._warLevel = 2; break; }
                if (ctx._warLevel == 0 && galaxy.Jumps(system.Id, c.FrontSystem, 2) is > 0 and <= 2) ctx._warLevel = 1;
            }
            // Handshake gear gets you a better price at Free Machine ports.
            if (session != null && galaxy.OwnerOf(system)?.RaceId == "free-machine")
                ctx._discount = Ships.ShipFactory.Build(db, session.Career.HullId, session.Career.Loadout).MarketDiscount;
            ctx._landmark = LandmarkRules.At(db, galaxy, system.Id);
            ctx._deals = Deals(db, galaxy, day).Where(d => d.Source == system.Id || d.Destination == system.Id).ToList();
            ctx._shortages = [.. Shortages(db, galaxy, day).Where(s => s.SystemId == system.Id).Select(s => s.CommodityId)];
            return ctx;
        }

        public Base Base(CommodityDef c, int day)
        {
            var (low, high) = Range(c);
            float bias = EconomyBias.TryGetValue(_system.Economy, out var table) && table.TryGetValue(c.Category, out var m) ? m : 1f;
            // A landmark's own prices, such as a works selling its computers cheap: the whole price
            // range moves, and a cheap good is plentiful.
            float landmark = LandmarkRules.PriceFactor(_landmark, c);
            if (landmark != 1)
            {
                (low, high) = ((int)MathF.Round(low * landmark), (int)MathF.Round(high * landmark));
                bias *= landmark;
            }
            // Rim goods are cheap out on the rim and worth much more near the core.
            if (c.Category is "rim-tech" or "elder-artefacts")
                bias *= _system.Region switch { "core" => 1.8f, "inner" => 1.3f, "outer" => 1f, _ => 0.6f };

            float position = 0.42f + (bias - 1) * 0.85f;
            bool big = _profile.StationSize is StationSize.Hub or StationSize.Citadel;
            if (big && Finery.Contains(c.Category)) position += 0.08f;
            if (_profile.StationSize == StationSize.Outpost && Basics.Contains(c.Category)) position += 0.1f;
            if (_profile.Security is "Low" or "Lawless" && c.Category is "weapons" or "medical") position += 0.12f;
            if (_warLevel > 0 && WarGoods.Contains(c.Category)) position += _warLevel == 2 ? 0.25f : 0.1f;
            if (_takenOver && Basics.Contains(c.Category)) position += 0.08f;

            // The week's drift, smooth from day to day so prices have a trend.
            var route = EasyRole(c);
            float drift = route != 0 ? 0.02f : _takenOver ? 0.15f : 0.09f;
            position += drift * Drift(_system.Seed ^ Rng.Hash("price:" + c.Id), day);

            float eventFactor = 1;
            foreach (var ev in _galaxy.EventsIn(_system.Id))
                if (_db.Events.TryGetValue(ev.EventId, out var def) && def.CommodityDemand.TryGetValue(c.Category, out var demand))
                    eventFactor *= demand;

            // Stock and how much the market wants, by whether it makes the good or needs it.
            float stock = bias < 0.9f ? 120 : bias > 1.15f ? 4 : 30;
            float wanted = bias < 0.9f ? 10 : bias > 1.15f ? 90 : 35;
            float scale = _size * (c.BasePrice > 1000 ? 0.4f : 1f);
            stock *= scale * (1 + 0.25f * Drift(_system.Seed ^ Rng.Hash("stock:" + c.Id), day));
            wanted *= scale * (1 + 0.25f * Drift(_system.Seed ^ Rng.Hash("wanted:" + c.Id), day));
            if (c.Legality == "restricted") stock *= 0.5f;
            if (c.Legality == "contraband") { stock = 10 * _size; wanted = 15 * _size; }
            if (_takenOver) stock *= 0.5f;
            if (eventFactor > 1) { stock /= MathF.Pow(eventFactor, 1.5f); wanted *= eventFactor; }
            else if (eventFactor < 1) { stock *= MathF.Pow(1 / eventFactor, 1.2f); wanted *= eventFactor; }

            // A shortage: almost nothing on the shelves, and everyone wants it.
            if (_shortages.Contains(c.Id)) { stock *= 0.1f; wanted *= 1.6f; position += 0.3f; }

            // Jackpots: a big surplus going cheap, or a nearby system paying well over the odds.
            bool deal = false;
            foreach (var d in _deals.Where(d => d.CommodityId == c.Id))
            {
                deal = true;
                if (d.Source == _system.Id) { stock = stock * 4 + 150; position -= 0.35f; }
                else { wanted = wanted * 3 + 100; position += 0.45f; }
            }

            // Easy: a dependable route between the start and a neighbour.
            if (route < 0) { stock = MathF.Max(stock, 200); position = MathF.Min(position, 0.15f); }
            else if (route > 0) { wanted = MathF.Max(wanted, 250); position = MathF.Max(position, 0.82f); }

            float spread = _profile.StationSize switch { StationSize.Outpost => 0.12f, StationSize.Station => 0.08f, _ => 0.06f };
            if (_profile.Security is "Low" or "Lawless") spread += 0.03f;
            if (_session?.Easy == true) spread -= 0.03f;

            return new Base(low, high, position, eventFactor, spread, MathF.Max(0, stock), MathF.Max(5, wanted), deal) { Discount = _discount };
        }

        /// <summary>-1 if this market is the cheap end of the Easy route for the good, 1 the dear end, 0 otherwise.</summary>
        private int EasyRole(CommodityDef c)
        {
            if (_session is not { Easy: true } || _session.Career.TradeRoute is not { } r) return 0;
            if ((_system.Id == r.A && c.Id == r.GoodsAtoB) || (_system.Id == r.B && c.Id == r.GoodsBtoA)) return -1;
            if ((_system.Id == r.B && c.Id == r.GoodsAtoB) || (_system.Id == r.A && c.Id == r.GoodsBtoA)) return 1;
            return 0;
        }
    }

    /// <summary>Price change over the last week, from the model alone (wars and events as they are now).</summary>
    private static float Trend(Context ctx, CommodityDef c, int day)
    {
        float now = ctx.Base(c, day).Mid, before = ctx.Base(c, day - 7).Mid;
        return before > 0 ? now / before - 1 : 0;
    }

    /// <summary>A smooth value from -1 to 1 that drifts week by week: each week has a target, and days blend between them.</summary>
    internal static float Drift(ulong seed, int day)
    {
        int week = (int)Math.Floor(day / 7.0);
        float frac = (day - week * 7) / 7f;
        float a = new Rng(seed ^ (ulong)(uint)week).Range(-1, 1);
        float b = new Rng(seed ^ (ulong)(uint)(week + 1)).Range(-1, 1);
        float t = frac * frac * (3 - 2 * frac);
        return a + (b - a) * t;
    }

    // Jackpots -------------------------------------------------------------------------------

    private const int DealPeriod = 8;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Galaxy, Dictionary<int, List<TradeDeal>>> DealCache = new();

    private static List<TradeDeal> DealsFor(ContentDatabase db, Galaxy galaxy, int period)
    {
        if (period < 0) return [];
        var cache = DealCache.GetValue(galaxy, _ => []);
        lock (cache)
        {
            if (cache.TryGetValue(period, out var cached)) return cached;
            var rng = new Rng(galaxy.Seed ^ Rng.Hash("jackpots") ^ (ulong)period);
            // Picked from the gate systems, which never change, so the same deals come up whenever this
            // is worked out; they only run where both ends have a market at the time.
            var markets = galaxy.Systems.Where(s => s.IsGateSystem).ToList();
            var goods = db.Commodities.Values.Where(c => c.Legality == "legal" && c.BasePrice <= 1500).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            var deals = new List<TradeDeal>();
            int count = Math.Max(2, markets.Count / 60);
            for (int i = 0; i < count && markets.Count > 1 && goods.Count > 0; i++)
            {
                var source = rng.Pick(markets);
                var goodsId = rng.Pick(goods).Id;
                var near = galaxy.WithinJumps(source.Id, 3).ToList();
                if (near.Count == 0) continue;
                var destination = rng.Pick(near);
                int start = period * DealPeriod + rng.Next(DealPeriod);
                deals.Add(new TradeDeal(source.Id, destination.Id, goodsId, start, start + 4 + rng.Next(4)));
            }
            cache[period] = deals;
            if (cache.Count > 8) cache.Remove(cache.Keys.Min());
            return deals;
        }
    }

    // Shortages --------------------------------------------------------------------------------

    private const int ShortagePeriod = 10;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Galaxy, Dictionary<int, List<Shortage>>> ShortageCache = new();
    private static readonly HashSet<string> Staples = ["food", "water", "medical", "fuel"];

    private static List<Shortage> ShortagesFor(ContentDatabase db, Galaxy galaxy, int period)
    {
        if (period < 0) return [];
        var cache = ShortageCache.GetValue(galaxy, _ => []);
        lock (cache)
        {
            if (cache.TryGetValue(period, out var cached)) return cached;
            var rng = new Rng(galaxy.Seed ^ Rng.Hash("shortages") ^ (ulong)period);
            var systems = galaxy.Systems.Where(s => s.IsGateSystem).ToList();
            // Mostly staples (food, water, medicine, fuel), now and then anything.
            var staples = db.Commodities.Values.Where(c => c.Legality == "legal" && Staples.Contains(c.Category)).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            var any = db.Commodities.Values.Where(c => c.Legality == "legal").OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            var list = new List<Shortage>();
            for (int i = 0; i < Math.Max(2, systems.Count / 150) && staples.Count > 0; i++)
            {
                var system = rng.Pick(systems);
                var goods = rng.NextFloat() < 0.75f ? rng.Pick(staples) : rng.Pick(any);
                int start = period * ShortagePeriod + rng.Next(ShortagePeriod);
                list.Add(new Shortage(system.Id, goods.Id, start, start + 5 + rng.Next(5)));
            }
            cache[period] = list;
            if (cache.Count > 8) cache.Remove(cache.Keys.Min());
            return list;
        }
    }

    // The Easy route -------------------------------------------------------------------------

    /// <summary>
    /// For a new game: a neighbour of the start within two jumps, and a good to carry each way
    /// that one makes and the other needs. On Easy, this pair is kept dependably profitable.
    /// </summary>
    public static TradeRoute? PickRoute(ContentDatabase db, Galaxy galaxy, int start)
    {
        var home = galaxy.Systems[start];
        var goods = db.Commodities.Values.Where(c => c.Legality == "legal" && c.BasePrice <= 1500).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        float Bias(StarSystem s, CommodityDef c) =>
            EconomyBias.TryGetValue(s.Economy, out var t) && t.TryGetValue(c.Category, out var m) ? m : 1f;
        TradeRoute? best = null;
        float bestScore = float.MinValue;
        foreach (var other in galaxy.WithinJumps(start, 2).Where(s => s.Stations.Count > 0 && s.Settled))
        {
            var there = goods.MaxBy(c => Bias(other, c) - Bias(home, c))!;
            var back = goods.MaxBy(c => Bias(home, c) - Bias(other, c))!;
            float score = Bias(other, there) - Bias(home, there) + Bias(home, back) - Bias(other, back) - galaxy.Jumps(start, other.Id, 2) * 0.1f;
            if (score > bestScore) { bestScore = score; best = new TradeRoute(start, other.Id, there.Id, back.Id); }
        }
        return best;
    }
}

/// <summary>Two systems and the goods to carry each way between them.</summary>
public sealed record TradeRoute(int A, int B, string GoodsAtoB, string GoodsBtoA);
