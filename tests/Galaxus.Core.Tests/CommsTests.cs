using Galaxus.Core.Law;
using Xunit;

namespace Galaxus.Core.Tests;

public class CommsTests
{
    [Fact]
    public void A_caller_with_nobody_in_the_way_gets_straight_through()
    {
        var comms = new Comms();
        Assert.True(comms.TryCall("pirate", 0));
        Assert.True(comms.Busy);
    }

    [Fact]
    public void Callers_who_clash_back_off_for_longer_each_time_up_to_fifteen_minutes()
    {
        var comms = new Comms();
        Assert.True(comms.TryCall("first", 0));
        float now = 1;
        var waits = new List<float>();
        for (int i = 0; i < 6; i++)
        {
            Assert.False(comms.TryCall("patrol", now));
            // Find when the patrol may next try.
            float next = now;
            while (comms.BackingOff("patrol", next)) next += 1;
            waits.Add(next - now);
            now = next;
        }
        Assert.Equal(new float[] { 60, 120, 240, 480, 900, 900 }, waits);
    }

    [Fact]
    public void Nobody_calls_the_moment_a_call_ends()
    {
        var comms = new Comms();
        Assert.True(comms.TryCall("first", 0));
        comms.Ended(30);
        Assert.False(comms.TryCall("second", 40));
        Assert.True(comms.TryCall("third", 30 + Comms.Gap + 1));
    }

    [Fact]
    public void Docking_or_leaving_clears_every_back_off()
    {
        var comms = new Comms();
        Assert.True(comms.TryCall("first", 0));
        Assert.False(comms.TryCall("patrol", 1));
        comms.Reset(2, 0);
        Assert.False(comms.Busy);
        Assert.True(comms.TryCall("patrol", 2));
    }

    [Fact]
    public void Your_own_calls_always_go_through_and_hold_the_line()
    {
        var comms = new Comms();
        Assert.True(comms.TryCall("pirate", 0));
        comms.Outgoing();
        Assert.True(comms.Busy);
        Assert.False(comms.TryCall("patrol", 1));
    }

    [Fact]
    public void Nobody_calls_as_you_arrive_but_nobody_is_put_off_for_trying()
    {
        var comms = new Comms();
        comms.Reset(100, 60);
        Assert.False(comms.TryCall("pirate", 110));
        Assert.False(comms.BackingOff("pirate", 161));
        Assert.True(comms.TryCall("pirate", 161));
    }
}
