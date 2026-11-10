using System.Numerics;
using Galaxus.Core.Ships;
using Xunit;

namespace Galaxus.Core.Tests;

public class CollisionTests
{
    [Fact]
    public void The_hub_and_ring_are_solid_but_the_space_between_is_not()
    {
        Assert.True(Collision.StationDistance(Vector3.Zero, out _) < 0);
        Assert.True(Collision.StationDistance(new Vector3(150, 0, 0), out _) < 0);
        Assert.True(Collision.StationDistance(new Vector3(0, 95, 0), out _) > 20);
        Assert.True(Collision.StationDistance(new Vector3(0, 0, -400), out _) > 300);
    }

    [Fact]
    public void Normals_point_away_from_the_surface()
    {
        Collision.StationDistance(new Vector3(0, 0, -70), out var front);
        Assert.True(front.Z < -0.9f);
        Collision.StationDistance(new Vector3(180, 0, 0), out var outer);
        Assert.True(outer.X > 0.9f);
    }

    [Fact]
    public void A_fast_move_cannot_skip_through_a_planet()
    {
        // 100,000 km in one step straight through a planet 6,000 km across.
        var t = Collision.FirstContact((-50_000, 0, 0), (50_000, 0, 0), (0, 0, 0), 6_000);
        Assert.NotNull(t);
        Assert.InRange(t!.Value, 0.43, 0.45);
        Assert.Null(Collision.FirstContact((-50_000, 9_000, 0), (50_000, 9_000, 0), (0, 0, 0), 6_000));
        // Leaving from inside is allowed.
        Assert.Null(Collision.FirstContact((1_000, 0, 0), (60_000, 0, 0), (0, 0, 0), 6_000));
    }

    [Fact]
    public void Bumps_are_free_and_rams_hurt()
    {
        Assert.Equal(0, Collision.ImpactDamage(8));
        Assert.True(Collision.ImpactDamage(200) > 250);
    }
}
