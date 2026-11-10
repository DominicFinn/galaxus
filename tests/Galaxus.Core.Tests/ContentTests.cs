using System.Numerics;
using Galaxus.Core.Content;
using Galaxus.Core.Ships;

namespace Galaxus.Core.Tests;

/// <summary>Loads the real game data, so broken content fails the test run.</summary>
public class ContentTests
{
    public static string DataFolder
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "game", "data"))) dir = dir.Parent;
            return dir == null ? throw new DirectoryNotFoundException("Couldn't find game/data.") : Path.Combine(dir.FullName, "game", "data");
        }
    }

    private static ContentDatabase Load() => ContentDatabase.Load(new DirectoryContentSource(DataFolder));

    [Fact]
    public void All_shipped_content_is_valid()
    {
        try
        {
            Load();
        }
        catch (ContentException e)
        {
            Assert.Fail(e.Message);
        }
    }

    [Fact]
    public void Every_name_and_model_grammar_expands()
    {
        var db = Load();
        foreach (var race in db.Races.Values.Where(r => r.NameGrammar != ""))
            for (ulong seed = 0; seed < 50; seed++)
                Assert.False(string.IsNullOrWhiteSpace(db.Grammars.Expand(race.NameGrammar, "name", new Rng(seed))));
        foreach (var builder in db.Builders.Values.Where(b => b.ModelNameGrammar != ""))
            for (ulong seed = 0; seed < 50; seed++)
                Assert.False(string.IsNullOrWhiteSpace(db.Grammars.Expand(builder.ModelNameGrammar, "model", new Rng(seed))));
    }

    [Fact]
    public void Every_hull_builds_into_a_flyable_ship()
    {
        var db = Load();
        foreach (var hull in db.Hulls.Values)
        {
            var spec = ShipFactory.Build(db, hull.Id);
            Assert.True(spec.MaxSpeed > 0, $"{hull.Id} has no top speed");
            Assert.True(spec.Acceleration > 0, $"{hull.Id} has no acceleration");
            Assert.True(spec.Pips > 0, $"{hull.Id} has no power");
        }
    }

    [Fact]
    public void Kestrel_numbers()
    {
        var spec = ShipFactory.Build(Load(), "kestrel");
        Assert.Equal(110f, spec.MaxSpeed, 0.01f);
        Assert.Equal(40f, spec.Acceleration, 0.01f);
        Assert.Equal(2, spec.Weapons.Count);
        Assert.Equal(new Vector3(-3.9f, -0.1f, -0.6f), spec.Weapons[0].Hardpoint);
    }

    [Fact]
    public void Kestrel_destroys_a_drone_it_is_pointing_at()
    {
        var db = Load();
        var world = new World();
        var player = new Ship(1, "Player", ShipFactory.Build(db, "kestrel")) { Side = 0 };
        var drone = new Ship(2, "Drone", ShipFactory.Build(db, "target-drone")) { Side = 1 };
        drone.Body.Position = new Vector3(0, 0, -400);
        world.Ships.Add(player);
        world.Ships.Add(drone);

        player.Controls.Fire = true;
        bool destroyed = false;
        for (int i = 0; i < 60 * 10 && !destroyed; i++)
        {
            world.Step(1 / 60f);
            destroyed = world.Events.OfType<ShipDestroyed>().Any(e => e.Ship == drone);
        }
        Assert.True(destroyed);
    }

    [Fact]
    public void Aim_assist_hits_a_crossing_drone_when_nearly_on_target()
    {
        var db = Load();
        var world = new World();
        var player = new Ship(1, "Player", ShipFactory.Build(db, "kestrel")) { Side = 0, AimAssist = true };
        var drone = new Ship(2, "Drone", ShipFactory.Build(db, "target-drone")) { Side = 1 };
        drone.Body.Position = new Vector3(0, 0, -500);
        drone.Body.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2); // flying across
        drone.Controls.Flight.Throttle = 0.5f;
        // Nose three degrees off the drone: a near miss without assist.
        player.Body.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 3f * MathF.PI / 180f);
        player.Target = drone;
        player.Controls.Fire = true;
        world.Ships.Add(player);
        world.Ships.Add(drone);

        bool hit = false;
        for (int i = 0; i < 60 * 3 && !hit; i++)
        {
            world.Step(1 / 60f);
            hit = world.Events.OfType<ShipHit>().Any(e => e.Ship == drone);
        }
        Assert.True(hit);
    }

    [Fact]
    public void Lead_point_hits_a_crossing_target()
    {
        var lead = World.LeadPoint(Vector3.Zero, Vector3.Zero, new Vector3(0, 0, -900), new Vector3(100, 0, 0), 900);
        Assert.NotNull(lead);
        // Shot and target arrive at the lead point at the same moment.
        float shotTime = lead.Value.Length() / 900;
        float targetTime = (lead.Value.X - 0) / 100;
        Assert.Equal(shotTime, targetTime, 0.01f);
    }
}
