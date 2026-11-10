using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;

namespace Galaxus.Core.Tests;

public class SolarSystemTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));
    private static readonly Galaxy.Galaxy Galaxy = GalaxyGenerator.Generate(Db, 2026);
    private static readonly List<SolarSystem> Systems = Galaxy.Systems.Select(s => SolarSystems.For(s)).ToList();

    [Fact]
    public void Same_system_every_time()
    {
        var a = SolarSystems.For(Galaxy.Systems[5]);
        var b = SolarSystems.For(Galaxy.Systems[5]);
        Assert.Equal(a.Star, b.Star);
        Assert.Equal(a.Bodies, b.Bodies);
    }

    [Fact]
    public void Red_dwarfs_are_common_and_blue_giants_rare()
    {
        int red = Systems.Count(s => s.Star.Class == StarClass.M), blue = Systems.Count(s => s.Star.Class == StarClass.B);
        Assert.True(red > Systems.Count * 0.35f);
        Assert.True(blue < Systems.Count * 0.05f);
    }

    [Fact]
    public void Giants_form_beyond_the_frost_line_and_hot_worlds_close_in()
    {
        foreach (var s in Systems)
        {
            Assert.All(s.Planets.Where(b => b.Kind is BodyKind.GasGiant or BodyKind.IceGiant), b => Assert.True(b.Orbit > s.Star.FrostLine));
            Assert.All(s.Planets.Where(b => b.Kind == BodyKind.Molten), b => Assert.True(b.Temperature > 700));
            Assert.All(s.Planets.Where(b => b.Habitable), b => Assert.InRange(b.Orbit, s.Star.HabitableInner, s.Star.HabitableOuter));
        }
    }

    [Fact]
    public void Orbits_follow_keplers_third_law_and_widen_outwards()
    {
        var earthLike = SolarSystems.Period(1f, 1f);
        Assert.InRange(earthLike, 364f, 366f);
        foreach (var s in Systems)
        {
            var planets = s.Planets.Where(b => b.Kind != BodyKind.Belt).ToList();
            for (int i = 1; i < planets.Count; i++)
            {
                Assert.True(planets[i].Orbit > planets[i - 1].Orbit);
                Assert.True(planets[i].PeriodDays > planets[i - 1].PeriodDays);
            }
        }
    }

    [Fact]
    public void Every_station_orbits_a_body_and_settled_worlds_are_often_habitable()
    {
        foreach (var (system, solar) in Galaxy.Systems.Zip(Systems))
        {
            Assert.Equal(system.Stations.Count, solar.Stations.Count);
            Assert.All(solar.Stations, st => Assert.InRange(st.Body, 0, solar.Bodies.Count - 1));
        }
        var colonies = Galaxy.Systems.Zip(Systems).Where(x => x.First.State == SystemState.Colony).ToList();
        Assert.True(colonies.Count(c => c.Second.Bodies.Any(b => b.Habitable)) > colonies.Count / 4);
    }
}

public class PlanetNameTests
{
    [Fact]
    public void Settled_worlds_get_names_and_the_rest_keep_numbers()
    {
        var db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));
        var galaxy = GalaxyGenerator.Generate(db, 2026);
        var named = galaxy.Systems.Where(s => s.Stations.Count > 0).Take(50).Select(s => (s, SolarSystems.For(s, db.Grammars))).ToList();
        Assert.Contains(named, x => x.Item2.Bodies.Any(b => !b.Name.StartsWith(x.s.Name)));
        Assert.All(named, x => Assert.All(x.Item2.Stations, st => Assert.False(string.IsNullOrWhiteSpace(x.Item2.Bodies[st.Body].Name))));
    }
}
