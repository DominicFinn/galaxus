using System.Numerics;
using Galaxus.Core.Battles;
using Galaxus.Core.Content;
using Galaxus.Core.Ships;
using Xunit;

namespace Galaxus.Core.Tests;

public class BattleTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    private static (Battle, World, Ship) Start(string id, int seed = 1)
    {
        var world = new World();
        var player = new Ship(1, "Player", ShipFactory.Build(Db, "kestrel")) { Side = 1 };
        world.Ships.Add(player);
        var sides = new BattleSides("The Meridian Concord", "meridian-yards", "human", "The Grav Khanates", "orc-yards", "orc");
        var def = Db.Battles[id];
        var vars = new Dictionary<string, string> { ["client"] = "The Meridian Concord", ["enemy"] = "The Grav Khanates", ["system"] = "Ashby", ["captain"] = "Vale", ["station"] = "Ashby Ring", ["base"] = "Sovereign Clause" };
        var battle = new Battle(Db, def, world, player, sides, new Rng((ulong)seed), vars, Battle.NameTags(def, new Rng(5)));
        return (battle, world, player);
    }

    /// <summary>Runs the battle with the player sitting still, until it ends or time runs out.</summary>
    private static List<BattleEvent> Run(Battle battle, World world, float seconds)
    {
        var events = new List<BattleEvent>();
        for (float t = 0; t < seconds && battle.Won == null; t += 1 / 30f)
        {
            battle.Step(1 / 30f);
            events.AddRange(battle.Events);
            world.Step(1 / 30f);
        }
        return events;
    }

    [Fact]
    public void Every_battle_loads_and_its_waves_arrive()
    {
        foreach (var id in Db.Battles.Keys)
        {
            var (battle, world, _) = Start(id);
            var events = Run(battle, world, 120);
            Assert.Contains(events, e => e is WaveArrived);
            Assert.Contains(events, e => e is RadioCall r && r.Text.Length > 0 && !r.Text.Contains('{'));
        }
    }

    [Fact]
    public void Ships_fight_and_the_battle_ends()
    {
        var (battle, world, _) = Start("hold-the-line");
        var events = Run(battle, world, 900);
        Assert.True(battle.Spawned.Count(s => !s.Alive) > 3, "ships should be destroyed in the fighting");
        Assert.NotNull(battle.Won);
        Assert.Contains(events, e => e is BattleOver);
    }

    [Fact]
    public void Homing_shots_turn_onto_their_target()
    {
        var world = new World();
        var a = new Ship(1, "A", ShipFactory.Build(Db, "kestrel")) { Side = 1 };
        var b = new Ship(2, "B", ShipFactory.Build(Db, "kestrel")) { Side = 2 };
        b.Body.Position = new Vector3(600, 0, -1500);
        world.Ships.AddRange([a, b]);
        world.SetHostile(1, 2);
        var shot = new Projectile(a, Vector3.Zero, new Vector3(0, 0, -500), 50, 10, "#ffffff", "missile") { Target = b };
        world.Projectiles.Add(shot);
        bool hit = false;
        for (int i = 0; i < 300 && !hit; i++)
        {
            world.Step(1 / 30f);
            hit = world.Events.Any(e => e is ShipHit h && h.Ship == b);
        }
        Assert.True(hit);
    }

    [Fact]
    public void A_missile_needs_a_lock_then_runs_its_target_down()
    {
        var world = new World();
        var hunter = new Ship(1, "Hunter", ShipFactory.Build(Db, "kestrel")) { Side = 1 };
        var prey = new Ship(2, "Prey", ShipFactory.Build(Db, "kestrel")) { Side = 2 };
        prey.Body.Position = new Vector3(150, 80, -1800);
        prey.Body.Velocity = new Vector3(60, 0, 0);
        world.Ships.AddRange([hunter, prey]);
        world.SetHostile(1, 2);
        hunter.Target = prey;
        Assert.True(hunter.MissilesLeft > 0);
        int missiles = hunter.MissilesLeft;

        // No lock yet: pressing does nothing.
        hunter.Controls.Missile = true;
        world.Step(1 / 30f);
        Assert.Equal(missiles, hunter.MissilesLeft);

        for (int i = 0; i < 60 && !hunter.Locked; i++) world.Step(1 / 30f);
        Assert.True(hunter.Locked);
        hunter.Controls.Missile = true;
        world.Step(1 / 30f);
        Assert.Equal(missiles - 1, hunter.MissilesLeft);

        bool hit = false;
        for (int i = 0; i < 400 && !hit; i++)
        {
            world.Step(1 / 30f);
            hit = world.Events.Any(e => e is ShipHit h && h.Ship == prey && h.Projectile.Kind == "missile");
        }
        Assert.True(hit);
    }

    [Fact]
    public void A_target_off_the_nose_never_locks()
    {
        var world = new World();
        var hunter = new Ship(1, "Hunter", ShipFactory.Build(Db, "kestrel")) { Side = 1 };
        var prey = new Ship(2, "Prey", ShipFactory.Build(Db, "kestrel")) { Side = 2 };
        prey.Body.Position = new Vector3(1500, 0, 0);
        world.Ships.AddRange([hunter, prey]);
        hunter.Target = prey;
        for (int i = 0; i < 120; i++) world.Step(1 / 30f);
        Assert.False(hunter.Locked);
    }

    [Fact]
    public void Long_ships_can_be_hit_near_the_ends()
    {
        var hull = Db.Hulls.Values.OrderByDescending(h => h.Model.Count == 0 ? 0 : h.Model.Max(m => MathF.Abs(m.Position[2]) + m.Length / 2)).First();
        var spec = ShipFactory.Build(Db, hull.Id);
        var ship = new Ship(3, "Long", spec);
        var nearEnd = new Vector3(0, 0, spec.HalfLength * 0.9f);
        Assert.True(World.DistanceToShip(ship, nearEnd) < 1, $"{hull.Id}: half length {spec.HalfLength}, radius {spec.Radius}");
    }

    [Fact]
    public void Shooting_a_neutral_makes_it_hostile()
    {
        var world = new World();
        var a = new Ship(1, "A", ShipFactory.Build(Db, "kestrel")) { Side = 1 };
        var b = new Ship(2, "B", ShipFactory.Build(Db, "kestrel")) { Side = 2 };
        b.Body.Position = new Vector3(0, 0, -300);
        world.Ships.AddRange([a, b]);
        Assert.False(world.Hostile(a, b));
        world.Projectiles.Add(new Projectile(a, new Vector3(0, 0, -20), new Vector3(0, 0, -900), 5, 2, "#ffffff"));
        for (int i = 0; i < 30; i++) world.Step(1 / 30f);
        Assert.True(world.Hostile(a, b));
    }
}
