using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;

namespace Galaxus.Core.Tests;

/// <summary>Living markets: limited stock, prices that move as you trade, jackpots and the Easy loop.</summary>
public class MarketTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    private static Session NewGame(ulong seed, bool easy = false)
    {
        var session = Session.StartNew(Db, seed);
        session.Easy = easy;
        session.Career.Credits = 1_000_000;
        return session;
    }

    [Fact]
    public void Stock_is_finite_and_recovers_over_days()
    {
        var session = NewGame(11);
        var quote = Market.Quotes(session, session.Here).Where(q => q.Stock is > 0 and < 200).MaxBy(q => q.Stock)!;
        var id = quote.Commodity.Id;
        session.Career.Cargo.Clear();
        // Buy it all out, a hold at a time.
        int total = 0;
        for (int i = 0; i < 50 && Market.Quote(session, session.Here, quote.Commodity).Stock > 0; i++)
        {
            total += session.Buy(id, 1000);
            session.Career.Cargo.Clear();
        }
        Assert.InRange(total, quote.Stock - 1, quote.Stock + 1);
        Assert.Equal(0, Market.Quote(session, session.Here, quote.Commodity).Stock);
        Assert.Equal(0, session.Buy(id, 1));

        session.Galaxy.Day += 6;
        Assert.True(Market.Quote(session, session.Here, quote.Commodity).Stock > quote.Stock / 2);
    }

    [Fact]
    public void Buying_raises_the_price_and_selling_a_lot_lowers_it()
    {
        var session = NewGame(12);
        var quote = Market.Quotes(session, session.Here).Where(q => q.Stock >= 20).MaxBy(q => q.Stock)!;
        var c = quote.Commodity;
        session.Buy(c.Id, quote.Stock - 2);
        Assert.True(Market.Quote(session, session.Here, c).Buy > quote.Buy);

        // Dump a big load into a market that doesn't want much: each tonne fetches less.
        var market = Market.Quotes(session, session.Here).First(q => q.Wanted is > 0 and < 60);
        session.Career.Cargo[market.Commodity.Id] = market.Wanted * 3;
        int first = Market.Quote(session, session.Here, market.Commodity).Sell;
        session.Sell(market.Commodity.Id, market.Wanted * 3);
        Assert.True(Market.Quote(session, session.Here, market.Commodity).Sell < first * 0.8f);
    }

    [Fact]
    public void Prices_stay_in_range_without_events_and_drift_from_week_to_week()
    {
        var session = NewGame(13);
        var galaxy = session.Galaxy;
        galaxy.Events.Clear();
        var markets = galaxy.Systems.Where(s => s.Stations.Count > 0).Take(30).ToList();
        foreach (var system in markets)
        foreach (var c in Market.Legal(Db, system, galaxy))
        {
            var (low, high) = Market.Range(c);
            int price = Market.Price(Db, galaxy, system, c);
            Assert.InRange(price, (int)(low * 0.95f), (int)(high * 1.05f));
        }
        int Week(int day) { galaxy.Day = day; return Market.Price(Db, galaxy, markets[0], Db.Commodities.Values.First(c => c.Legality == "legal")); }
        var prices = Enumerable.Range(0, 8).Select(w => Week(100 + w * 7)).ToList();
        Assert.True(prices.Distinct().Count() > 3, "Prices should drift week to week.");
    }

    [Fact]
    public void Jackpots_turn_up_now_and_then_and_expire()
    {
        var galaxy = Session.StartNew(Db, 14).Galaxy;
        var seen = new HashSet<TradeDeal>();
        for (int day = 30; day < 130; day++)
        {
            var deals = Market.Deals(Db, galaxy, day).ToList();
            Assert.All(deals, d => Assert.True(d.ActiveOn(day)));
            Assert.All(deals, d => Assert.InRange(d.EndDay - d.StartDay, 4, 8));
            foreach (var d in deals) seen.Add(d);
        }
        Assert.True(seen.Count >= 10, $"Only {seen.Count} jackpots in 100 days.");
        // The source has plenty going cheap; the destination pays well over the odds.
        var deal = seen.First(d => d.ActiveOn(60) || true);
        galaxy.Day = deal.StartDay;
        var c = Db.Commodities[deal.CommodityId];
        var (low, high) = Market.Range(c);
        Assert.True(Market.Price(Db, galaxy, galaxy.Systems[deal.Source], c) < Market.Price(Db, galaxy, galaxy.Systems[deal.Destination], c));
    }

    [Theory]
    [InlineData(2026UL)]
    [InlineData(31UL)]
    [InlineData(99UL)]
    public void On_easy_the_starting_loop_makes_steady_money(ulong seed)
    {
        var session = NewGame(seed, easy: true);
        session.Career.Credits = 5_000;
        var route = session.Career.TradeRoute!;
        Assert.NotNull(route);
        int start = session.Career.Credits;
        int previous = start;
        for (int trip = 0; trip < 4; trip++)
        {
            foreach (var (from, to, goods) in new[] { (route.A, route.B, route.GoodsAtoB), (route.B, route.A, route.GoodsBtoA) })
            {
                Assert.Equal(from, session.Career.SystemId);
                session.Buy(goods, 1000);
                Assert.NotNull(session.Jump(to));
                session.Sell(goods, 1000);
            }
            Assert.True(session.Career.Credits > previous, $"Trip {trip} lost money: {previous} to {session.Career.Credits}.");
            previous = session.Career.Credits;
        }
        Assert.True(session.Career.Credits > start * 1.5, $"Four round trips only took {start} to {session.Career.Credits}.");
    }

    [Fact]
    public void Markets_are_the_same_after_a_save_and_load()
    {
        var session = NewGame(15);
        var quote = Market.Quotes(session, session.Here).First(q => q.Stock > 10);
        session.Buy(quote.Commodity.Id, 10);
        var loaded = SaveGame.Load(Db, SaveGame.Save(session));
        Assert.Equal(Market.Quotes(session, session.Here), Market.Quotes(loaded, loaded.Here));
        Assert.Equal(session.Career.TradeRoute, loaded.Career.TradeRoute);
    }
}
