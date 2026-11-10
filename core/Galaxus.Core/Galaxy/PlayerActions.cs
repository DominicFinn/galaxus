namespace Galaxus.Core.Galaxy;

/// <summary>
/// Something the player did that changes the galaxy: a battle won or lost in a war, pirates
/// driven out, machines beaten back. Recorded with the day it happened, so a loaded save can
/// replay it at the same point in the simulation and arrive at the same galaxy.
/// </summary>
public sealed record PlayerAction(int Day, string Kind, int Client, int Enemy, int System, bool Won, float Weight, string Captain);

public static class PlayerImpact
{
    /// <summary>How far a war has to swing before the front falls, and before the loser sues for peace.</summary>
    public const float FrontFalls = 2.5f, SuesForPeace = 5f;

    /// <summary>Applies an action to the galaxy and writes the news it makes.</summary>
    public static void Apply(Galaxy galaxy, PlayerAction action)
    {
        var where = galaxy.Systems[action.System];
        var client = action.Client >= 0 && action.Client < galaxy.Factions.Count ? galaxy.Factions[action.Client] : null;
        var enemy = action.Enemy >= 0 && action.Enemy < galaxy.Factions.Count ? galaxy.Factions[action.Enemy] : null;
        if (client == null) return;

        switch (action.Kind)
        {
            case "war" when enemy != null:
            {
                var conflict = galaxy.Conflicts.FirstOrDefault(c => c.Involves(client.Id) && c.Involves(enemy.Id));
                if (conflict == null) break;
                // Momentum is positive when the war is going A's way.
                float push = (action.Won ? action.Weight : -action.Weight * 0.5f) * (conflict.A == client.Id ? 1 : -1);
                conflict.Momentum += push;
                Spread(galaxy, action.System, client, enemy, action.Won
                    ? $"{client.Name} win a hard fight at {where.Name}, helped by Captain {action.Captain}. {enemy.Name} fall back."
                    : $"{enemy.Name} beat off {client.Name} at {where.Name}. Captain {action.Captain} was among those who failed to hold.");
                Resolve(galaxy, conflict);
                break;
            }
            case "pirates":
                if (!action.Won) break;
                if (enemy is { Criminal: true } gang && gang.Haunts.Remove(action.System))
                    Spread(galaxy, action.System, client, null, $"{gang.Name} are driven out of {where.Name}. Captain {action.Captain} led the hunt for {client.Name}.");
                else
                    galaxy.AddNews(action.System, $"Pirates are broken up near {where.Name}. {client.Name} credit Captain {action.Captain}.");
                break;
            case "synth":
                galaxy.AddNews(action.System, action.Won
                    ? $"Unbound raiders are beaten back at {where.Name}. {client.Name} name Captain {action.Captain} in their dispatches."
                    : $"The Unbound press on at {where.Name}. {client.Name} call for more ships.");
                break;
            case "defence":
                if (enemy != null && galaxy.Conflicts.FirstOrDefault(c => c.Involves(client.Id) && c.Involves(enemy.Id)) is { } war)
                {
                    war.Momentum += (action.Won ? action.Weight : -action.Weight) * (war.A == client.Id ? 1 : -1);
                    Resolve(galaxy, war);
                }
                galaxy.AddNews(action.System, action.Won
                    ? $"{where.Name} holds. {client.Name} thank Captain {action.Captain} and the station's defenders."
                    : $"{where.Name} is battered after an assault. {client.Name} count the cost.");
                break;
        }
    }

    /// <summary>
    /// Big swings change the war: the front falls to whoever has the momentum, and a crushing
    /// run of defeats makes the loser sue for peace.
    /// </summary>
    public static void Resolve(Galaxy galaxy, Conflict conflict)
    {
        var a = galaxy.Factions[conflict.A];
        var b = galaxy.Factions[conflict.B];
        var winner = conflict.Momentum > 0 ? a : b;
        var loser = winner == a ? b : a;
        float swing = MathF.Abs(conflict.Momentum);
        var front = galaxy.Systems[conflict.FrontSystem];

        if (swing >= SuesForPeace)
        {
            if (front.Owner == loser.Id) front.Owner = winner.Id;
            galaxy.Conflicts.Remove(conflict);
            galaxy.Events.RemoveAll(e => e.SystemId == conflict.FrontSystem && Conflict.StageEvents.Contains(e.EventId)
                                         && ((e.FactionId == a.Id && e.RivalId == b.Id) || (e.FactionId == b.Id && e.RivalId == a.Id)));
            Spread(galaxy, front.Id, winner, loser, $"{loser.Name} sue for peace. The war over {front.Name} ends in victory for {winner.Name}.");
            return;
        }
        if (swing >= FrontFalls && front.Owner == loser.Id && conflict.Stage >= 1)
        {
            front.Owner = winner.Id;
            front.StateSince = galaxy.Day;
            conflict.Momentum *= 0.5f;
            Spread(galaxy, front.Id, winner, loser, $"{front.Name} falls to {winner.Name}. {loser.Name} pull back from the front.");
            // The fighting moves to the loser's next system along the border, if there is one.
            var next = galaxy.Neighbours(front).Where(s => s.Owner == loser.Id).OrderBy(s => s.Id).FirstOrDefault();
            if (next != null) conflict.FrontSystem = next.Id;
        }
    }

    /// <summary>News of a turn in a war is heard at the front and in both sides' capitals, so it travels.</summary>
    private static void Spread(Galaxy galaxy, int front, Faction a, Faction? b, string text)
    {
        galaxy.AddNews(front, text);
        if (a.HomeSystem != front) galaxy.AddNews(a.HomeSystem, text);
        if (b != null && b.HomeSystem != front && b.HomeSystem != a.HomeSystem) galaxy.AddNews(b.HomeSystem, text);
    }
}
