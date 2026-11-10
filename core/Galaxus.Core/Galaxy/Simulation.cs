using Galaxus.Core.Content;

namespace Galaxus.Core.Galaxy;

/// <summary>
/// Runs the galaxy a day at a time whether or not the player is watching. Factions push into
/// empty systems, borders cause disputes that can escalate to war, events break out and the
/// news feed reports it all. Everything is seeded from the galaxy seed and the day.
/// </summary>
public sealed class Simulation(ContentDatabase db, Galaxy galaxy)
{
    /// <summary>Days a system spends in a state before it can grow to the next one.</summary>
    private const int ClaimToOutpostDays = 20, OutpostToColonyDays = 90;
    private const int ConflictStepDays = 6;

    private static readonly HashSet<string> ConflictEvents = [.. Conflict.StageEvents];

    public Galaxy Galaxy { get; } = galaxy;

    public void Run(int days)
    {
        for (int i = 0; i < days; i++)
        {
            Tick();
            // Replaying a save: the player's actions land on the days they happened.
            foreach (var action in _replay.Where(a => a.Day == Galaxy.Day).ToList())
            {
                PlayerImpact.Apply(Galaxy, action);
                Galaxy.PlayerActions.Add(action);
                _replay.Remove(action);
            }
        }
    }

    private readonly List<PlayerAction> _replay = [];

    /// <summary>Queues saved actions to be applied as the simulation reaches their days.</summary>
    public void Replay(IEnumerable<PlayerAction> actions) => _replay.AddRange(actions);

    /// <summary>Something the player just did: applied now and remembered for the save.</summary>
    public void Record(PlayerAction action)
    {
        PlayerImpact.Apply(Galaxy, action);
        Galaxy.PlayerActions.Add(action);
    }

    public void Tick()
    {
        Galaxy.Day++;
        var rng = new Rng(Galaxy.Seed ^ Rng.Hash($"day-{Galaxy.Day}"));

        foreach (var faction in Galaxy.Factions.Where(f => f.Alive && !f.Criminal))
            Expand(faction, rng.Fork($"expand-{faction.Id}"));
        GrowSettlements();
        StartConflicts(rng.Fork("conflicts"));
        if (Galaxy.Day % ConflictStepDays == 0) StepConflicts(rng.Fork("escalate"));
        StartRandomEvents(rng.Fork("events"));
        UpdateEvents(rng.Fork("news"));
    }

    /// <summary>A faction pushes into the next system along its border: survey it, then claim it.</summary>
    private void Expand(Faction faction, Rng rng)
    {
        if (rng.NextFloat() > faction.Trait("expansion") * 0.007f) return;

        var border = Galaxy.OwnedBy(faction.Id).SelectMany(Galaxy.Neighbours).Where(s => s.Owner < 0).Distinct()
            .OrderBy(s => s.Id).ToList();
        if (border.Count == 0) return;
        var target = rng.Pick(border);

        if (target.State == SystemState.Unsurveyed)
        {
            target.State = SystemState.Surveyed;
            target.SurveyedBy = faction.Id;
            target.StateSince = Galaxy.Day;
            if (rng.NextFloat() < 0.15f) Galaxy.AddNews(target.Id, $"{faction.Name} completes a survey of {target.Name}.");
        }
        else if (target.State == SystemState.Surveyed)
        {
            target.State = SystemState.Claimed;
            target.Owner = faction.Id;
            target.StateSince = Galaxy.Day;
            Galaxy.AddNews(target.Id, $"{faction.Name} files a claim on {target.Name}.");
        }
    }

    private void GrowSettlements()
    {
        foreach (var s in Galaxy.Systems.Where(s => s.Owner >= 0))
        {
            int age = Galaxy.Day - s.StateSince;
            var srng = new Rng(s.Seed ^ (ulong)Galaxy.Day);
            if (s.State == SystemState.Claimed && age > ClaimToOutpostDays && srng.NextFloat() < 0.05f)
            {
                s.State = SystemState.Outpost;
                s.StateSince = Galaxy.Day;
                s.Economy = srng.NextFloat() < 0.6f ? Economy.Mining : Economy.Frontier;
                GalaxyGenerator.AddStations(s, db, srng);
                Galaxy.AddNews(s.Id, $"A new outpost opens at {s.Name} under {Galaxy.Factions[s.Owner].Name}.");
            }
            else if (s.State == SystemState.Outpost && age > OutpostToColonyDays && srng.NextFloat() < 0.01f)
            {
                s.State = SystemState.Colony;
                s.StateSince = Galaxy.Day;
                GalaxyGenerator.AddStations(s, db, srng);
                Galaxy.AddNews(s.Id, $"{s.Name} is recognised as a colony of {Galaxy.Factions[s.Owner].Name}.");
            }
        }
    }

    /// <summary>Where two factions' territories touch, aggressive neighbours start disputes.</summary>
    private void StartConflicts(Rng rng)
    {
        if (rng.NextFloat() > 0.15f) return;
        var borders = Galaxy.Systems.Where(s => s.Owner >= 0)
            .SelectMany(s => Galaxy.Neighbours(s).Where(n => n.Owner >= 0 && n.Owner != s.Owner).Select(n => (s, n)))
            .ToList();
        if (borders.Count == 0) return;

        var (a, b) = rng.Pick(borders);
        var fa = Galaxy.Factions[a.Owner];
        var fb = Galaxy.Factions[b.Owner];
        if (Galaxy.Conflicts.Any(c => c.Involves(fa.Id) && c.Involves(fb.Id))) return;
        if (rng.NextFloat() > (fa.Trait("aggression") + fb.Trait("aggression")) / 2) return;

        var conflict = new Conflict { A = fa.Id, B = fb.Id, FrontSystem = b.Id, StartDay = Galaxy.Day };
        Galaxy.Conflicts.Add(conflict);
        SetConflictEvent(conflict, rng);
    }

    /// <summary>Each step, a conflict may climb or calm. Most fizzle out; a few become wars.</summary>
    private void StepConflicts(Rng rng)
    {
        foreach (var conflict in Galaxy.Conflicts.ToList())
        {
            var a = Galaxy.Factions[conflict.A];
            var b = Galaxy.Factions[conflict.B];
            float up = (a.Trait("aggression") + b.Trait("aggression")) * 0.12f;
            float down = (a.Trait("honour") + b.Trait("honour")) * 0.06f + 0.03f;
            float roll = rng.NextFloat();

            // Momentum fades as the war grinds on.
            conflict.Momentum *= 0.92f;
            if (roll < up && conflict.Stage < 3) conflict.Stage++;
            else if (roll > 1 - down) conflict.Stage--;
            else continue;

            if (conflict.Stage < 0)
            {
                Galaxy.Conflicts.Remove(conflict);
                EndEventsFor(conflict);
                Galaxy.AddNews(conflict.FrontSystem, $"{a.Name} and {b.Name} settle their differences over {Galaxy.Systems[conflict.FrontSystem].Name}.");
                continue;
            }

            // At blockade and war, contested systems can change hands.
            if (conflict.Stage >= 2 && rng.NextFloat() < 0.3f) FlipFront(conflict, a, b, rng);
            SetConflictEvent(conflict, rng);
        }
    }

    private void FlipFront(Conflict conflict, Faction a, Faction b, Rng rng)
    {
        var front = Galaxy.Systems[conflict.FrontSystem];
        if (front.Owner < 0) return;
        // The side with the momentum is likelier to take the front.
        float oddsA = Math.Clamp(0.5f + conflict.Momentum * 0.15f, 0.1f, 0.9f);
        var winner = rng.NextFloat() < oddsA ? a : b;
        if (front.Owner == winner.Id) return;
        var loser = Galaxy.Factions[front.Owner];
        front.TakenBy(winner.Id, Galaxy.Day);
        Galaxy.AddNews(front.Id, $"{front.Name} falls to {winner.Name}. {loser.Name} withdraws.");
        if (!Galaxy.OwnedBy(loser.Id).Any())
        {
            loser.Alive = false;
            Galaxy.Conflicts.RemoveAll(c => c.Involves(loser.Id));
            Galaxy.AddNews(front.Id, $"{loser.Name} is no more.");
        }
    }

    /// <summary>The conflict's current stage shows in the galaxy as an event at the front.</summary>
    private void SetConflictEvent(Conflict conflict, Rng rng)
    {
        EndEventsFor(conflict, silently: true);
        string eventId = Conflict.StageEvents[conflict.Stage];
        if (!db.Events.ContainsKey(eventId)) return;
        StartEvent(eventId, conflict.FrontSystem, conflict.A, conflict.B, durationDays: 365, rng);
    }

    private void EndEventsFor(Conflict conflict, bool silently = false)
    {
        foreach (var ev in Galaxy.Events.Where(e => ConflictEvents.Contains(e.EventId) && e.SystemId == conflict.FrontSystem
                                                     && ((e.FactionId == conflict.A && e.RivalId == conflict.B) || (e.FactionId == conflict.B && e.RivalId == conflict.A))).ToList())
        {
            Galaxy.Events.Remove(ev);
            if (!silently) WriteNews(ev, "end", new Rng(Galaxy.Seed ^ (ulong)Galaxy.Day ^ (ulong)ev.SystemId));
        }
    }

    /// <summary>Famines, plagues, booms, strange signals: random events in settled systems.</summary>
    private void StartRandomEvents(Rng rng)
    {
        var pool = db.Events.Values.Where(e => !ConflictEvents.Contains(e.Id)).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        if (pool.Count == 0) return;
        foreach (var s in Galaxy.Systems.Where(s => s.Settled))
        {
            var srng = rng.Fork($"s{s.Id}");
            if (srng.NextFloat() > 0.0015f) continue;
            if (Galaxy.EventsIn(s.Id).Any()) continue;

            // Strange things happen further out.
            var ev = srng.Pick(pool);
            bool strange = ev.Id is "rim-incident" or "ships-going-quiet" or "elder-machine-waking" or "unexplained-signal" or "rim-refugee-wave";
            if (strange && s.Region is "core" or "inner" && srng.NextFloat() < 0.8f) continue;

            int duration = ev.DurationDays.Length == 2 ? ev.DurationDays[0] + srng.Next(Math.Max(1, ev.DurationDays[1] - ev.DurationDays[0] + 1)) : 20;
            int rival = Galaxy.Neighbours(s).Select(n => n.Owner).FirstOrDefault(o => o >= 0 && o != s.Owner, -1);
            StartEvent(ev.Id, s.Id, s.Owner, rival, duration, srng);
        }
    }

    private void StartEvent(string eventId, int systemId, int faction, int rival, int durationDays, Rng rng)
    {
        var ev = new ActiveEvent
        {
            EventId = eventId,
            SystemId = systemId,
            FactionId = faction,
            RivalId = rival,
            StartDay = Galaxy.Day,
            EndDay = Galaxy.Day + durationDays,
            LastNewsDay = Galaxy.Day,
        };
        Galaxy.Events.Add(ev);
        WriteNews(ev, "start", rng);
    }

    private void UpdateEvents(Rng rng)
    {
        foreach (var ev in Galaxy.Events.ToList())
        {
            if (Galaxy.Day >= ev.EndDay)
            {
                Galaxy.Events.Remove(ev);
                WriteNews(ev, "end", rng.Fork($"end-{ev.SystemId}"));
            }
            else if (Galaxy.Day - ev.LastNewsDay >= 9 && rng.NextFloat() < 0.3f)
            {
                ev.LastNewsDay = Galaxy.Day;
                WriteNews(ev, "ongoing", rng.Fork($"ongoing-{ev.SystemId}"));
            }
        }
    }

    private void WriteNews(ActiveEvent ev, string rule, Rng rng)
    {
        if (!db.Events.TryGetValue(ev.EventId, out var def) || def.NewsGrammar == "") return;
        var vars = new Dictionary<string, string>
        {
            ["system"] = Galaxy.Systems[ev.SystemId].Name,
            ["faction"] = ev.FactionId >= 0 ? Galaxy.Factions[ev.FactionId].Name : "The local authorities",
            ["rival"] = ev.RivalId >= 0 ? Galaxy.Factions[ev.RivalId].Name : "unknown parties",
        };
        Galaxy.AddNews(ev.SystemId, db.Grammars.Expand(def.NewsGrammar, rule, rng, vars));
    }
}
