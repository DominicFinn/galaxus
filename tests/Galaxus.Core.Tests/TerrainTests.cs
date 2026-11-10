using System.Numerics;
using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Xunit;

namespace Galaxus.Core.Tests;

public class TerrainTests
{
    private static List<TerrainSample> Survey(PlanetTerrain terrain)
    {
        var rng = new Rng(5);
        var samples = new List<TerrainSample>();
        for (int i = 0; i < 4000; i++)
            samples.Add(terrain.Sample(new Vector3(rng.Range(-1, 1), rng.Range(-1, 1), rng.Range(-1, 1)) + new Vector3(1e-4f)));
        return samples;
    }

    [Fact]
    public void The_same_seed_gives_the_same_ground()
    {
        var a = new PlanetTerrain(BodyKind.Terran, 6371, 42);
        var b = new PlanetTerrain(BodyKind.Terran, 6371, 42);
        var dir = new Vector3(0.3f, 0.5f, -0.8f);
        Assert.Equal(a.Sample(dir), b.Sample(dir));
        Assert.NotEqual(a.HeightKm(dir), new PlanetTerrain(BodyKind.Terran, 6371, 43).HeightKm(dir));
    }

    [Fact]
    public void Earth_like_worlds_have_seas_land_and_mountains()
    {
        var samples = Survey(new PlanetTerrain(BodyKind.Terran, 6371, 7));
        float wet = samples.Count(s => s.Wet) / (float)samples.Count;
        Assert.InRange(wet, 0.35f, 0.85f);
        Assert.Contains(samples, s => s.HeightKm > 3);
        Assert.Contains(samples, s => s.Ground is Ground.Forest or Ground.Grass);
        Assert.Contains(samples, s => s.Ground is Ground.Snow or Ground.Ice);
    }

    [Fact]
    public void Ocean_worlds_are_mostly_sea_and_deserts_have_none()
    {
        float oceanWet = Survey(new PlanetTerrain(BodyKind.Ocean, 6371, 3)).Count(s => s.Wet) / 4000f;
        Assert.True(oceanWet > 0.8f, $"ocean world only {oceanWet:P0} sea");
        Assert.DoesNotContain(Survey(new PlanetTerrain(BodyKind.Desert, 5000, 3)), s => s.Wet);
    }

    [Fact]
    public void Barren_moons_are_cratered()
    {
        Assert.Contains(Survey(new PlanetTerrain(BodyKind.Barren, 1700, 9)), s => s.Ground == Ground.CraterFloor);
    }

    [Fact]
    public void Ports_stand_on_dry_ground()
    {
        var terrain = new PlanetTerrain(BodyKind.Terran, 6371, 11);
        var site = terrain.FindPortSite(new Rng(1));
        Assert.False(terrain.Sample(site).Wet);
    }

    [Fact]
    public void Some_stations_are_surface_ports()
    {
        int surface = 0, total = 0;
        var db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));
        var galaxy = GalaxyGenerator.Generate(db, 3, new GalaxySettings());
        foreach (var system in galaxy.Systems.Where(s => s.Stations.Count > 1).Take(200))
        {
            var solar = SolarSystems.For(system);
            Assert.False(solar.Stations[0].Surface);
            surface += solar.Stations.Count(s => s.Surface);
            total += solar.Stations.Count;
        }
        Assert.True(surface > total / 6, $"{surface} of {total} stations on the surface");
    }
}
