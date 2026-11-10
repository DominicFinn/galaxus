using System.Numerics;
using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;

namespace Galaxus.Core.Tests;

public class DeepSpaceTests
{
    private static readonly ContentDatabase Db = ContentDatabase.Load(new DirectoryContentSource(ContentTests.DataFolder));

    [Fact]
    public void The_galaxy_holds_billions_of_stars()
    {
        double stars = DeepSpace.EstimateStarCount();
        Assert.InRange(stars, 1e9, 1e10);
    }

    [Fact]
    public void A_column_always_has_the_same_stars()
    {
        Assert.Equal(DeepSpace.Column(7, 120, -40), DeepSpace.Column(7, 120, -40));
        Assert.NotEqual(DeepSpace.Column(7, 120, -40), DeepSpace.Column(8, 120, -40));
    }

    [Fact]
    public void Stars_crowd_the_core_and_arms_and_thin_at_the_rim()
    {
        Assert.True(DeepSpace.Density(0, 2000) > DeepSpace.Density(0, 30000));
        Assert.Equal(0, DeepSpace.Density(45000, 0));
    }

    [Fact]
    public void Keys_round_trip()
    {
        foreach (var (cx, cy, i) in new[] { (0, 0, 0), (-3000, 2500, 7), (3999, -3999, 200) })
            Assert.Equal((cx, cy, i), DeepSpace.Unpack(DeepSpace.Key(cx, cy, i)));
    }

    [Fact]
    public void An_explorer_can_reach_nearby_stars_and_jumps_beyond_range_are_refused()
    {
        var session = Session.StartNew(Db, 2026);
        session.Career.HullId = "hc-albatross";
        var inRange = session.InJumpRange();
        Assert.NotEmpty(inRange);
        Assert.All(inRange, s => Assert.True(s.DistanceLy <= session.JumpRangeLy));

        var target = inRange.First(s => s.DeepKey != 0);
        int id = session.Galaxy.Register(target.DeepKey);
        Assert.Equal(id, session.Galaxy.Register(target.DeepKey));
        int day = session.Galaxy.Day;
        var report = session.Jump(id)!;
        Assert.Equal(id, session.Career.SystemId);
        Assert.Equal(day + Session.JumpDays(target.DistanceLy), session.Galaxy.Day);
        Assert.False(session.Here.IsGateSystem);
        Assert.NotEmpty(SolarSystems.For(session.Here).Bodies);

        // Somewhere far beyond range, off the gate network: refused.
        var far = DeepSpace.Near(session.Galaxy.Seed, DeepSpace.PositionLy(session.Here) + new Vector3(400, 0, 0), 30).First();
        int farId = session.Galaxy.Register(far.Star.Key);
        Assert.Null(session.Jump(farId));
    }

    [Fact]
    public void Visited_deep_stars_come_back_with_the_same_ids_after_loading()
    {
        var session = Session.StartNew(Db, 2026);
        session.Career.HullId = "hc-albatross";
        var target = session.InJumpRange().First(s => s.DeepKey != 0);
        int id = session.Galaxy.Register(target.DeepKey);
        session.Jump(id);
        var loaded = SaveGame.Load(Db, SaveGame.Save(session));
        Assert.Equal(id, loaded.Career.SystemId);
        Assert.Equal(session.Here.Name, loaded.Here.Name);
        Assert.Equal(session.Here.Seed, loaded.Here.Seed);
    }
}
