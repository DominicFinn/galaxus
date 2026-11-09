using System.Numerics;
using Galaxus.Core.Flight;

namespace Galaxus.Core.Tests;

public class FlightModelTests
{
    private static FlightStats Stats => new()
    {
        MaxSpeed = 100,
        Acceleration = 40,
        PitchRate = 1.2f,
        YawRate = 1f,
        RollRate = 2.5f,
        AngularAcceleration = 12,
        GTolerance = 9,
    };

    private static void Run(FlightBody body, FlightInput input, float seconds, FlightStats? stats = null)
    {
        var s = stats ?? Stats;
        for (float t = 0; t < seconds; t += 1 / 60f) FlightModel.Step(body, input, s, 1 / 60f);
    }

    [Fact]
    public void Positive_pitch_raises_the_nose()
    {
        var body = new FlightBody();
        Run(body, new FlightInput { Pitch = 1, Assist = true }, 0.3f);
        Assert.True(body.Forward.Y > 0.1f);
    }

    [Fact]
    public void Positive_yaw_turns_right()
    {
        var body = new FlightBody();
        Run(body, new FlightInput { Yaw = 1, Assist = true }, 0.3f);
        Assert.True(body.Forward.X > 0.1f);
    }

    [Fact]
    public void Positive_roll_drops_the_right_wing()
    {
        var body = new FlightBody();
        Run(body, new FlightInput { Roll = 1, Assist = true }, 0.3f);
        Assert.True(body.Right.Y < -0.1f);
    }

    [Fact]
    public void Assist_settles_at_throttle_speed_along_the_nose()
    {
        var body = new FlightBody();
        Run(body, new FlightInput { Throttle = 0.5f, Assist = true }, 5f);
        Assert.Equal(50f, body.ForwardSpeed, 1f);
        Assert.Equal(50f, body.Speed, 1f);
    }

    [Fact]
    public void Assist_cancels_drift_after_a_turn()
    {
        var body = new FlightBody();
        Run(body, new FlightInput { Throttle = 0.6f, Assist = true }, 4f);
        Run(body, new FlightInput { Throttle = 0.6f, Yaw = 1, Assist = true }, 1.5f);
        Run(body, new FlightInput { Throttle = 0.6f, Assist = true }, 3f);
        // Velocity should line up with the nose again.
        Assert.True(Vector3.Dot(Vector3.Normalize(body.Velocity), body.Forward) > 0.999f);
    }

    [Fact]
    public void Without_assist_momentum_carries_on()
    {
        var body = new FlightBody();
        Run(body, new FlightInput { Thrust = 1, Assist = false }, 1f);
        float speed = body.Speed;
        Run(body, new FlightInput { Yaw = 1, Assist = false }, 1f);
        // Turned, no thrust: same speed, same direction of travel, nose elsewhere.
        Assert.Equal(speed, body.Speed, 0.01f);
        Assert.True(MathF.Abs(body.Velocity.X) < 0.01f);
        Assert.True(body.Forward.X > 0.3f);
    }

    [Fact]
    public void Newtonian_speed_is_still_capped()
    {
        var body = new FlightBody();
        Run(body, new FlightInput { Thrust = 1, Assist = false }, 30f);
        Assert.Equal(Stats.MaxSpeed * FlightModel.NewtonianSpeedLimit, body.Speed, 0.5f);
    }

    [Fact]
    public void G_tolerance_slows_turning_at_high_speed()
    {
        var slow = new FlightBody { Velocity = new Vector3(0, 0, -20) };
        var fast = new FlightBody { Velocity = new Vector3(0, 0, -300) };
        var input = new FlightInput { Pitch = 1, Thrust = 0, Assist = false };
        Run(slow, input, 0.5f);
        Run(fast, input, 0.5f);
        Assert.True(slow.AngularVelocity.X > fast.AngularVelocity.X * 2);
        Assert.Equal(FlightModel.TurnRateLimit(300, 9), fast.AngularVelocity.X, 0.01f);
    }

    [Fact]
    public void Tougher_pilots_turn_harder_at_speed()
    {
        var human = new FlightBody { Velocity = new Vector3(0, 0, -200) };
        var orc = new FlightBody { Velocity = new Vector3(0, 0, -200) };
        var input = new FlightInput { Pitch = 1, Assist = false };
        Run(human, input, 0.5f, Stats with { GTolerance = 9 });
        Run(orc, input, 0.5f, Stats with { GTolerance = 13 });
        Assert.True(orc.AngularVelocity.X > human.AngularVelocity.X);
    }
}
