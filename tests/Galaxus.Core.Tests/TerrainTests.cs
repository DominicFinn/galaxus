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

    [Fact]
    public void Ground_is_level_at_a_port()
    {
        var body = new Body("Test", BodyKind.Terran, 1, 365, 0, 1, 1, 288, true, 21, -1);
        var plain = new PlanetTerrain(body);
        var site = plain.FindPortSite(new Rng(4));
        var levelled = new PlanetTerrain(body, [site]);
        // Points a kilometre apart near the pad are at the same height.
        var up = Vector3.Normalize(site);
        var side = Vector3.Normalize(Vector3.Cross(up, Vector3.UnitY));
        float step = 1f / 6371f;
        float a = levelled.HeightKm(up), b = levelled.HeightKm(up + side * step);
        Assert.True(MathF.Abs(a - b) < 0.002f, $"{a} vs {b}");
        Assert.False(levelled.Sample(site).Wet);
    }

    [Fact]
    public void Close_up_ground_has_detail()
    {
        var terrain = new PlanetTerrain(BodyKind.Barren, 1700, 5);
        var dir = Vector3.Normalize(new Vector3(0.3f, 0.4f, 0.8f));
        var side = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY));
        // Over a few hundred metres the ground should rise and fall, not lie perfectly flat.
        var heights = Enumerable.Range(0, 20).Select(i => terrain.HeightKm(dir + side * (i * 0.3f / 1700f))).ToList();
        Assert.True(heights.Max() - heights.Min() > 0.005f);
    }
}
