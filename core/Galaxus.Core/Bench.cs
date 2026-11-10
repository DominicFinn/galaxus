using System.Diagnostics;
using Galaxus.Core.Battles;
using Galaxus.Core.Content;
using Galaxus.Core.Ships;

namespace Galaxus.Core;

/// <summary>
/// A fixed CPU workload for comparing runtimes (desktop, the web interpreter, web AOT): load the
/// content, start a new game, then set up the Fleet Engagement battle and step it with a fixed
/// time step. Everything is seeded, so every run does the same work. Each phase's time goes to
/// <paramref name="log"/> as a "BENCH" line, and the last says "BENCH DONE" unless the
/// caller has more phases of its own.
/// </summary>
public static class Bench
{
    public const ulong Seed = 20261110;
    public const string BattleId = "fleet-engagement";
    public const int Steps = 600;
    public const float Dt = 1 / 30f;

    public static (ContentDatabase Db, Session Session) Run(Func<ContentDatabase> loadContent, Action<string> log, bool done = true)
    {
        var total = Stopwatch.StartNew();
        var sw = Stopwatch.StartNew();
        var db = loadContent();
        log($"BENCH content {sw.Elapsed.TotalMilliseconds:F0} ms");

        sw.Restart();
        var galaxy = Galaxy.GalaxyGenerator.Generate(db, Seed);
        log($"BENCH galaxy {sw.Elapsed.TotalMilliseconds:F0} ms ({galaxy.Systems.Count} systems)");

        sw.Restart();
        var session = Session.StartNew(db, Seed);
        log($"BENCH new-game {sw.Elapsed.TotalMilliseconds:F0} ms (galaxy and 30 days)");

        sw.Restart();
        var world = new World();
        var player = new Ship(1, "Player", ShipFactory.Build(db, "kestrel")) { Side = 1 };
        world.Ships.Add(player);
        var sides = new BattleSides("The Meridian Concord", "meridian-yards", "human", "The Grav Khanates", "orc-yards", "orc");
        var def = db.Battles[BattleId];
        var vars = new Dictionary<string, string>
        {
            ["client"] = "The Meridian Concord", ["enemy"] = "The Grav Khanates", ["system"] = "Ashby",
            ["captain"] = "Vale", ["station"] = "Ashby Ring", ["base"] = "Sovereign Clause",
        };
        var battle = new Battle(db, def, world, player, sides, new Rng(Seed), vars, Battle.NameTags(def, new Rng(5)));
        battle.Step(Dt);
        world.Step(Dt);
        log($"BENCH battle-setup {sw.Elapsed.TotalMilliseconds:F0} ms ({world.Ships.Count} ships)");

        sw.Restart();
        double worst = 0;
        int maxShips = 0, maxShots = 0;
        var step = new Stopwatch();
        for (int i = 0; i < Steps; i++)
        {
            step.Restart();
            battle.Step(Dt);
            world.Step(Dt);
            worst = Math.Max(worst, step.Elapsed.TotalMilliseconds);
            maxShips = Math.Max(maxShips, world.Ships.Count);
            maxShots = Math.Max(maxShots, world.Projectiles.Count);
        }
        double ms = sw.Elapsed.TotalMilliseconds;
        int alive = world.Ships.Count(s => s.Alive);
        log($"BENCH battle-steps {ms:F0} ms for {Steps} steps, {ms / Steps:F2} ms a step, worst {worst:F1} ms " +
            $"(up to {maxShips} ships and {maxShots} shots, {alive} alive, checksum {Checksum(world):F1})");

        log($"BENCH total {total.Elapsed.TotalMilliseconds:F0} ms (day {session.Galaxy.Day})");
        if (done) log("BENCH DONE");
        return (db, session);
    }

    /// <summary>A number that depends on where every ship ended up, to show runs did the same work.</summary>
    private static double Checksum(World world)
    {
        double sum = 0;
        foreach (var s in world.Ships) sum += s.Body.Position.Length() + (s.Alive ? 1 : 0);
        return sum;
    }
}
