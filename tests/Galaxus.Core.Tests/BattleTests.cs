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

/// <summary>Difficulty: measured win rates with a bot pilot, the second wind, and fair fights for fighters.</summary>
public class DifficultyTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    private static bool Practice(BattleDef b) =>
        b.Objectives.Any(o => o.Type == "destroy" && o.Tag != null && b.Waves.Any(w => w.Side == "friend" && w.Ships.Any(g => g.Tag == o.Tag)));

    [Fact]
    public void An_average_player_wins_most_battles_on_easy_and_fewer_as_it_gets_harder()
    {
        var battles = Db.Battles.Values.Where(b => !Practice(b)).Select(b => b.Id).ToList();
        double Rate(Difficulty level) => battles.AsParallel()
            .SelectMany(id => new[] { 1, 2, 3 }.Select(seed => Balance.Play(Db, id, level, 0.5f, seed)))
            .Average(r => r.Won ? 1.0 : 0.0);
        double easy = Rate(Difficulty.Easy), normal = Rate(Difficulty.Normal), hard = Rate(Difficulty.Hard);
        Assert.True(easy >= 0.85, $"Easy: {easy:P0} won.");
        Assert.True(easy > normal && normal > hard, $"Easy {easy:P0}, Normal {normal:P0}, Hard {hard:P0}.");
    }

    [Fact]
    public void The_second_wind_eases_enemies_off_when_you_are_badly_hurt()
    {
        var world = new World();
        var player = new Ship(1, "Player", ShipFactory.Build(Db, "kestrel")) { Side = 1, AimAssist = true };
        Battle.ApplyToPlayer(player, Difficulty.Easy);
        world.Ships.Add(player);
        var def = Db.Battles["hold-the-line"];
        var battle = new Battle(Db, def, world, player, Balance.SidesFor(def), new Rng(3), new Dictionary<string, string>()) { Difficulty = Difficulty.Easy };
        // Let the enemy arrive, then knock the player down to almost nothing.
        for (int i = 0; i < 30 * 40 && battle.EnemiesLeft() == 0; i++) { battle.Step(1 / 30f); world.Step(1 / 30f); }
        while (player.Hull + player.Shields > (player.Spec.MaxHull + player.Spec.MaxShields) * 0.15f) player.TakeDamage(5);
        for (int i = 0; i < 30 * 5; i++) battle.Step(1 / 30f);
        // Badly hurt counts for half; being outnumbered as well would bring it to full.
        Assert.True(battle.SecondWind >= 0.45f, $"Second wind {battle.SecondWind} when nearly down.");
        var enemy = battle.Crews.First(c => c.Ship.Side == battle.EnemySide && c.Ship.Alive).Ship;
        Assert.True(enemy.FireRateScale < Difficulty.Easy.EnemyFireRate);
        Assert.True(player.ShieldRechargeScale > Difficulty.Easy.ShieldRecharge);
    }

    [Fact]
    public void Torpedoes_pass_fighters_by_and_nobody_shoots_marker_drones()
    {
        var world = new World();
        var fighter = new Ship(1, "Fighter", ShipFactory.Build(Db, "kestrel")) { Side = 1 };
        var drone = new Ship(2, "Beacon", ShipFactory.Build(Db, Db.Hulls.Values.First(h => h.Class == "drone").Id)) { Side = 3 };
        var enemy = new Ship(3, "Raider", ShipFactory.Build(Db, "kestrel")) { Side = 2 };
        world.Ships.AddRange([fighter, drone, enemy]);
        world.SetHostile(2, 1);
        world.SetHostile(2, 3);
        drone.Body.Position = enemy.Body.Position + new System.Numerics.Vector3(0, 0, -200);
        fighter.Body.Position = enemy.Body.Position + new System.Numerics.Vector3(0, 0, -900);
        var pilot = new Pilot(PilotRole.Fighter, new Rng(1)) { Skill = 1 };
        pilot.Think(enemy, world, 1 / 30f);
        Assert.Equal(fighter, enemy.Target);

        var torpedo = new Projectile(enemy, fighter.Body.Position + new System.Numerics.Vector3(0, 0, 30), new System.Numerics.Vector3(0, 0, -400), 500, 2, "#ffffff", "torpedo");
        world.Projectiles.Add(torpedo);
        for (int i = 0; i < 10; i++) world.Step(1 / 30f);
        Assert.Equal(fighter.Spec.MaxHull, fighter.Hull);
    }
}

/// <summary>Losing a battle and going again from the last wave.</summary>
public class BattleRetryTests
{
    private static readonly Galaxus.Core.Content.ContentDatabase Db =
        Galaxus.Core.Content.ContentDatabase.Load(new Galaxus.Core.Content.DirectoryContentSource(ContentTests.DataFolder));

    private static (Battle Battle, Galaxus.Core.Ships.Ship Player) Start(string id, Galaxus.Core.Ships.World world)
    {
        var def = Db.Battles[id];
        var player = new Galaxus.Core.Ships.Ship(1, "Player", Galaxus.Core.Ships.ShipFactory.Build(Db, "hc-sparrow")) { Side = 1 };
        world.Ships.Add(player);
        var vars = new Dictionary<string, string> { ["client"] = "A", ["enemy"] = "B", ["system"] = "C", ["captain"] = "D", ["station"] = "E", ["base"] = "F" };
        var battle = new Battle(Db, def, world, player, Balance.SidesFor(def), new Rng(5), vars, Battle.NameTags(def, new Rng(6))) { Difficulty = Difficulty.Easy };
        return (battle, player);
    }

    [Fact]
    public void A_lost_battle_resumes_from_the_wave_you_were_fighting()
    {
        var world = new Galaxus.Core.Ships.World();
        var (battle, player) = Start("blockade-run", world);
        // Fight a while, wipe out the picket, let the next waves come, then lose.
        for (int i = 0; i < 30 * 5; i++) { battle.Step(1 / 30f); world.Step(1 / 30f); }
        foreach (var ship in battle.Spawned.Where(s => s.Side == battle.EnemySide)) ship.TakeDamage(1e6f);
        for (int i = 0; i < 30 * 50; i++) { battle.Step(1 / 30f); world.Step(1 / 30f); }
        player.TakeDamage(1e6f);
        battle.Step(1 / 30f);
        Assert.False(battle.Won);
        Assert.Contains("Objectives", battle.Debrief());

        var retry = battle.Retry();
        Assert.NotNull(retry);
        Assert.Contains("picket", retry!.Cleared);
        Assert.NotEqual("picket", retry.WaveId);
        int picketObjective = battle.Objectives.FindIndex(o => o.Def.Id == "picketdown");
        Assert.Contains(picketObjective, retry.Done);

        // Go again: the picket doesn't come back and its objective stays done.
        var world2 = new Galaxus.Core.Ships.World();
        var (again, _) = Start("blockade-run", world2);
        again.ResumeFrom(retry);
        Assert.Equal(1, again.Attempt);
        Assert.Equal(ObjectiveState.Done, again.Objectives[picketObjective].State);
        for (int i = 0; i < 30 * 10; i++) { again.Step(1 / 30f); world2.Step(1 / 30f); }
        // The picket's frigate was beaten, so no enemy frigate comes back.
        Assert.DoesNotContain(again.Spawned, s => s.Side == again.EnemySide && s.Spec.Hull.Class == "frigate");
        Assert.True(again.Spawned.Any(s => s.Side == again.EnemySide), "The wave you were fighting comes back.");
    }
}
