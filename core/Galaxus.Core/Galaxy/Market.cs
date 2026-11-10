using Galaxus.Core.Content;

namespace Galaxus.Core.Galaxy;

/// <summary>
/// Prices goods in a system from its economy, any running events and a little weekly noise.
/// There's no stock to track yet: prices tell you where things are wanted.
/// </summary>
public static class Market
{
    /// <summary>Price multipliers by economy and commodity category. Below 1 means it's made here and cheap.</summary>
    private static readonly Dictionary<Economy, Dictionary<string, float>> EconomyBias = new()
    {
        [Economy.Agricultural] = new() { ["food"] = 0.6f, ["water"] = 0.8f, ["machinery"] = 1.3f, ["technology"] = 1.3f, ["luxury"] = 1.2f },
        [Economy.Mining] = new() { ["ore"] = 0.55f, ["food"] = 1.35f, ["water"] = 1.3f, ["machinery"] = 1.2f, ["medical"] = 1.3f, ["fuel"] = 1.1f },
        [Economy.Refinery] = new() { ["metals"] = 0.65f, ["fuel"] = 0.7f, ["ore"] = 1.3f, ["food"] = 1.2f },
        [Economy.Industrial] = new() { ["machinery"] = 0.7f, ["industrial"] = 0.7f, ["consumer"] = 0.8f, ["weapons"] = 0.85f, ["metals"] = 1.3f, ["food"] = 1.2f },
        [Economy.HighTech] = new() { ["technology"] = 0.65f, ["medical"] = 0.75f, ["data"] = 0.8f, ["luxury"] = 1.3f, ["food"] = 1.25f, ["metals"] = 1.2f },
        [Economy.Frontier] = new() { ["food"] = 1.4f, ["medical"] = 1.5f, ["machinery"] = 1.4f, ["weapons"] = 1.3f, ["ore"] = 0.8f, ["elder-artefacts"] = 0.8f, ["rim-tech"] = 0.8f },
    };

    public static int Price(ContentDatabase db, Galaxy galaxy, StarSystem system, CommodityDef commodity)
    {
        float price = commodity.BasePrice;
        if (EconomyBias.TryGetValue(system.Economy, out var bias) && bias.TryGetValue(commodity.Category, out var m)) price *= m;

        foreach (var ev in galaxy.EventsIn(system.Id))
            if (db.Events.TryGetValue(ev.EventId, out var def) && def.CommodityDemand.TryGetValue(commodity.Category, out var demand))
                price *= demand;

        // Rim goods are cheaper the further out you are, and worth much more near the core.
        if (commodity.Category is "rim-tech" or "elder-artefacts")
            price *= system.Region switch { "core" => 1.8f, "inner" => 1.3f, "outer" => 1f, _ => 0.6f };

        var noise = new Rng(system.Seed ^ Rng.Hash(commodity.Id) ^ (ulong)(galaxy.Day / 7));
        price *= noise.Range(0.9f, 1.1f);
        return Math.Max(1, (int)MathF.Round(price));
    }
}
