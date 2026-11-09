using Galaxus.Core.Ships;

namespace Galaxus.Core.Tests;

public class PowerDistributorTests
{
    [Fact]
    public void Starts_balanced_at_two_pips_each()
    {
        var power = new PowerDistributor(6);
        Assert.All(Enum.GetValues<ShipSystem>(), s => Assert.Equal(2f, power.Pips(s)));
        Assert.All(Enum.GetValues<ShipSystem>(), s => Assert.Equal(1f, power.Multiplier(s), 3));
    }

    [Fact]
    public void Raising_takes_half_a_pip_from_each_other_system()
    {
        var power = new PowerDistributor(6);
        Assert.True(power.Raise(ShipSystem.Engines));
        Assert.Equal(3f, power.Pips(ShipSystem.Engines));
        Assert.Equal(1.5f, power.Pips(ShipSystem.Shields));
        Assert.Equal(1.5f, power.Pips(ShipSystem.Weapons));
    }

    [Fact]
    public void Caps_at_four_pips_and_keeps_the_total()
    {
        var power = new PowerDistributor(6);
        for (int i = 0; i < 10; i++) power.Raise(ShipSystem.Weapons);
        Assert.Equal(4f, power.Pips(ShipSystem.Weapons));
        Assert.Equal(6f, Enum.GetValues<ShipSystem>().Sum(power.Pips));
        Assert.False(power.Raise(ShipSystem.Weapons));
    }

    [Fact]
    public void Takes_a_whole_pip_from_one_system_when_the_other_is_empty()
    {
        var power = new PowerDistributor(6);
        for (int i = 0; i < 4; i++) power.Raise(ShipSystem.Weapons); // weapons 4, others 1 each
        power.Raise(ShipSystem.Engines); // engines 2, shields 0.5, weapons 3.5
        power.Raise(ShipSystem.Engines); // engines 3, shields 0, weapons 3
        power.Raise(ShipSystem.Engines); // shields empty, so a whole pip from weapons
        Assert.Equal(4f, power.Pips(ShipSystem.Engines));
        Assert.Equal(0f, power.Pips(ShipSystem.Shields));
        Assert.Equal(2f, power.Pips(ShipSystem.Weapons));
    }

    [Fact]
    public void Balance_resets()
    {
        var power = new PowerDistributor(6);
        power.Raise(ShipSystem.Shields);
        power.Balance();
        Assert.Equal(2f, power.Pips(ShipSystem.Shields));
    }
}
