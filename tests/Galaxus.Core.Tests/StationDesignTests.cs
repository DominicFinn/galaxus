using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;

namespace Galaxus.Core.Tests;

/// <summary>Station designs: each builds, stays within budget, and different owners get different shapes.</summary>
public class StationDesignTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    [Fact]
    public void Every_design_builds_at_every_size_within_budget()
    {
        foreach (var design in StationDesigns.All)
            foreach (var size in Enum.GetValues<StationSize>())
            {
                var (model, radius) = StationDesigns.Build(design, size, 7);
                Assert.NotEmpty(model);
                Assert.True(radius > 50, $"{design} {size} radius {radius}");
                var triangles = ShipGeometry.Build(new HullDef { Id = design, Model = model, Radius = radius });
                Assert.InRange(triangles.Count, 50, 6000);
                // A lit docking bay on the front (-Z) side.
                Assert.Contains(model, s => s.Color == "glow" && s.Position[2] < -20);
            }
    }

    [Fact]
    public void Owners_get_their_own_kind_of_station()
    {
        var galaxy = GalaxyGenerator.Generate(Db, 77);
        var designs = galaxy.Systems.Where(s => s.Stations.Count > 0)
            .Select(s => StationDesigns.Pick(Db, s, galaxy.OwnerOf(s), SystemProfiles.For(galaxy, s, Db).StationSize)).ToList();
        Assert.True(designs.Distinct().Count() >= 6, $"Only {designs.Distinct().Count()} designs in use: {string.Join(", ", designs.Distinct())}");
        foreach (var f in galaxy.Factions.Where(f => f.PowerId == "tolland-compact"))
            foreach (var s in galaxy.Systems.Where(s => s.Owner == f.Id && s.Stations.Count > 0))
                Assert.Equal("claw", StationDesigns.Pick(Db, s, f, SystemProfiles.For(galaxy, s, Db).StationSize));
    }
}
