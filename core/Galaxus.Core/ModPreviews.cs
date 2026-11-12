using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;

namespace Galaxus.Core;

/// <summary>
/// Trying a mod straight from the menu: a new game that starts at the mod's own place, its
/// landmark or the home of one of its factions, docked and ready to look around.
/// </summary>
public static class ModPreviews
{
    /// <summary>Where a mod lives in a galaxy: its first landmark, else a station held by one of its faction types. -1 if it has no place.</summary>
    public static int Place(ContentDatabase db, Galaxy.Galaxy galaxy, LoadedMod mod)
    {
        foreach (var id in mod.Ids("landmarks"))
            if (galaxy.Landmarks.TryGetValue(id, out int system)) return system;
        var types = mod.Ids("factions").ToHashSet();
        var held = galaxy.Factions.Where(f => f.Alive && types.Contains(f.TypeId))
            .SelectMany(f => galaxy.OwnedBy(f.Id))
            .Where(s => s.Stations.Count > 0 && s.IsGateSystem)
            .OrderBy(s => s.Id)
            .FirstOrDefault();
        return held?.Id ?? -1;
    }

    /// <summary>Whether a mod has a place to start a game at, without building a galaxy.</summary>
    public static bool HasPlace(ContentDatabase db, LoadedMod mod) =>
        mod.Ids("landmarks").Count > 0 || mod.Ids("factions").Any(id => db.FactionTypes.TryGetValue(id, out var t) && !t.Criminal);

    /// <summary>A new free game, starting docked at the mod's place. Null if the galaxy gave it nowhere.</summary>
    public static Session? StartAt(ContentDatabase db, ulong seed, LoadedMod mod, bool easy = false)
    {
        var session = Session.StartNew(db, seed, null, easy);
        int place = Place(db, session.Galaxy, mod);
        if (place < 0) return null;
        session.Career.SystemId = place;
        session.Career.TradeRoute = Market.PickRoute(db, session.Galaxy, place);
        TradeIntel.Note(session);
        if (session.Career.TradeRoute is { } route) TradeIntel.Note(session, session.Galaxy.Systems[route.B]);
        return session;
    }
}
