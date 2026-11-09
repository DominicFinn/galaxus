using System.Numerics;

namespace Galaxus.Core.Flight;

// Coordinates match Godot: right-handed, +Y up, forward is -Z, right is +X.

public sealed class FlightBody
{
    public Vector3 Position;
    public Vector3 Velocity;
    public Quaternion Orientation = Quaternion.Identity;
    /// <summary>Angular velocity in ship space, rad/s: X is pitch, Y is yaw, Z is roll.</summary>
    public Vector3 AngularVelocity;

    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Orientation);
    public Vector3 Right => Vector3.Transform(Vector3.UnitX, Orientation);
    public Vector3 Up => Vector3.Transform(Vector3.UnitY, Orientation);
    public float Speed => Velocity.Length();
    public float ForwardSpeed => Vector3.Dot(Velocity, Forward);

    /// <summary>Converts a world-space point into ship space.</summary>
    public Vector3 ToLocal(Vector3 worldPoint) =>
        Vector3.Transform(worldPoint - Position, Quaternion.Conjugate(Orientation));
}

public struct FlightStats
{
    /// <summary>Top speed with flight assist on, m/s.</summary>
    public float MaxSpeed;
    /// <summary>Main drive acceleration, m/s².</summary>
    public float Acceleration;
    /// <summary>Turn rates, rad/s.</summary>
    public float PitchRate, YawRate, RollRate;
    /// <summary>How quickly the ship reaches its turn rate, rad/s².</summary>
    public float AngularAcceleration;
    /// <summary>Pilot tolerance in g. Caps turn rate at speed and the sideways grip of flight assist.</summary>
    public float GTolerance;
}

public struct FlightInput
{
    /// <summary>-1 to 1. Positive pitch is nose up, positive yaw is nose right, positive roll is right wing down.</summary>
    public float Pitch, Yaw, Roll;
    /// <summary>0 to 1, the fraction of top speed to hold. Used with flight assist on.</summary>
    public float Throttle;
    /// <summary>-1 to 1, main drive thrust. Used with flight assist off.</summary>
    public float Thrust;
    /// <summary>-1 to 1, thrusters: X is right, Y is up.</summary>
    public Vector2 Strafe;
    public bool Assist;
}

public static class FlightModel
{
    public const float G = 9.81f;
    /// <summary>With assist off, speed is still capped at this multiple of top speed so the ship can't run away.</summary>
    public const float NewtonianSpeedLimit = 3f;
    /// <summary>Strafe thrusters are weaker than the main drive.</summary>
    public const float StrafeFactor = 0.4f;

    /// <summary>
    /// The highest pitch/yaw rate the pilot can take at a given speed. Turning at rate w while moving at
    /// speed v pulls v*w of sideways acceleration, so tight turns need lower speed, as in X-Wing.
    /// </summary>
    public static float TurnRateLimit(float speed, float gTolerance) => gTolerance * G / MathF.Max(speed, 1f);

    public static void Step(FlightBody body, in FlightInput input, in FlightStats stats, float dt)
    {
        StepRotation(body, input, stats, dt);
        if (input.Assist) StepAssisted(body, input, stats, dt);
        else StepNewtonian(body, input, stats, dt);
        body.Position += body.Velocity * dt;
    }

    private static void StepRotation(FlightBody body, in FlightInput input, in FlightStats stats, float dt)
    {
        // Positive rotation about +X raises the nose; about +Y and +Z it turns left, so yaw and roll are negated.
        var target = new Vector3(
            Math.Clamp(input.Pitch, -1, 1) * stats.PitchRate,
            -Math.Clamp(input.Yaw, -1, 1) * stats.YawRate,
            -Math.Clamp(input.Roll, -1, 1) * stats.RollRate);

        var turn = new Vector2(target.X, target.Y);
        float limit = TurnRateLimit(body.Speed, stats.GTolerance);
        if (turn.Length() > limit)
        {
            turn = Vector2.Normalize(turn) * limit;
            target.X = turn.X;
            target.Y = turn.Y;
        }

        float maxChange = stats.AngularAcceleration * dt;
        body.AngularVelocity = new Vector3(
            Approach(body.AngularVelocity.X, target.X, maxChange),
            Approach(body.AngularVelocity.Y, target.Y, maxChange),
            Approach(body.AngularVelocity.Z, target.Z, maxChange));

        float angle = body.AngularVelocity.Length() * dt;
        if (angle > 1e-7f)
        {
            var delta = Quaternion.CreateFromAxisAngle(Vector3.Normalize(body.AngularVelocity), angle);
            body.Orientation = Quaternion.Normalize(body.Orientation * delta);
        }
    }

    /// <summary>
    /// Flight assist holds the throttle speed along the nose and cancels sideways drift, limited by
    /// drive acceleration forwards and by pilot g-tolerance sideways. This gives arcade-style handling.
    /// </summary>
    private static void StepAssisted(FlightBody body, in FlightInput input, in FlightStats stats, float dt)
    {
        var forward = body.Forward;
        var desired = forward * Math.Clamp(input.Throttle, 0, 1) * stats.MaxSpeed
                      + (body.Right * input.Strafe.X + body.Up * input.Strafe.Y) * stats.MaxSpeed * StrafeFactor;

        var change = desired - body.Velocity;
        float along = Vector3.Dot(change, forward);
        var sideways = change - forward * along;

        along = Math.Clamp(along, -stats.Acceleration * dt, stats.Acceleration * dt);
        float maxSideways = stats.GTolerance * G * dt;
        if (sideways.Length() > maxSideways) sideways = Vector3.Normalize(sideways) * maxSideways;

        body.Velocity += forward * along + sideways;
    }

    /// <summary>With assist off the drives just push. Momentum carries on until you counter-thrust.</summary>
    private static void StepNewtonian(FlightBody body, in FlightInput input, in FlightStats stats, float dt)
    {
        var accel = body.Forward * Math.Clamp(input.Thrust, -1, 1) * stats.Acceleration
                    + (body.Right * input.Strafe.X + body.Up * input.Strafe.Y) * stats.Acceleration * StrafeFactor;
        body.Velocity += accel * dt;

        float cap = stats.MaxSpeed * NewtonianSpeedLimit;
        if (body.Velocity.Length() > cap) body.Velocity = Vector3.Normalize(body.Velocity) * cap;
    }

    private static float Approach(float value, float target, float maxChange) =>
        value < target ? MathF.Min(value + maxChange, target) : MathF.Max(value - maxChange, target);
}
