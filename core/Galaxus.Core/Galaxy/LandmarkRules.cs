using Galaxus.Core.Content;

namespace Galaxus.Core.Galaxy;

/// <summary>What a landmark changes about its system: prices, parts on sale and the work on offer.</summary>
public static class LandmarkRules
{
    /// <summary>The landmark in a system, or null.</summary>
    public static LandmarkDef? At(ContentDatabase db, Galaxy galaxy, int systemId)
    {
        foreach (var (id, system) in galaxy.Landmarks)
            if (system == systemId && db.Landmarks.TryGetValue(id, out var landmark)) return landmark;
        return null;
    }

    /// <summary>A landmark's price multiple for a good: by its id first, then its category, else 1.</summary>
    public static float PriceFactor(LandmarkDef? landmark, CommodityDef c) =>
        landmark == null ? 1
        : landmark.Prices.TryGetValue(c.Id, out float byId) ? byId
        : landmark.Prices.TryGetValue(c.Category, out float byCategory) ? byCategory : 1;

    /// <summary>Parts sold only at a landmark, which never turn up on ordinary shelves.</summary>
    public static bool LandmarkOnly(ContentDatabase db, string partId) =>
        db.Landmarks.Values.Any(l => l.Parts.Any(p => p.Part == partId));
}
