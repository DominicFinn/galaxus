using System.Numerics;

namespace Galaxus.Core.Ships;

/// <summary>Steering helpers for AI pilots.</summary>
public static class Autopilot
{
    /// <summary>Pitch and yaw inputs (-1 to 1) that turn the nose towards a world point.</summary>
    public static (float Pitch, float Yaw) SteerTowards(Ship ship, Vector3 point, float gain = 3f)
    {
        var local = ship.Body.ToLocal(point);
        if (local.LengthSquared() < 1e-6f) return (0, 0);
        local = Vector3.Normalize(local);

        // Directly behind: turn hard on yaw so we don't sit still trying to decide.
        if (local.Z > 0.95f) return (0, 1);

        float pitch = Math.Clamp(local.Y * gain, -1, 1);
        float yaw = Math.Clamp(local.X * gain, -1, 1);
        if (local.Z > 0) // behind us, so commit fully to the turn
        {
            float length = MathF.Sqrt(pitch * pitch + yaw * yaw);
            if (length > 1e-4f) { pitch /= length; yaw /= length; }
        }
        return (pitch, yaw);
    }
}

/// <summary>
/// A target drone: wanders between random points near an anchor, weaving a little, and never shoots.
/// </summary>
public sealed class DroneBrain(Rng rng, Vector3 anchor, float wanderRadius)
{
    private Vector3 _waypoint = anchor;
    private float _weaveTime;

    public Vector3 Anchor { get; set; } = anchor;

    public void Think(Ship ship, float dt)
    {
        _weaveTime += dt;
        if (Vector3.Distance(ship.Body.Position, _waypoint) < 60f || _weaveTime > 12f)
        {
            _waypoint = Anchor + new Vector3(rng.Range(-1, 1), rng.Range(-0.5f, 0.5f), rng.Range(-1, 1)) * wanderRadius;
            _weaveTime = 0;
        }

        var (pitch, yaw) = Autopilot.SteerTowards(ship, _waypoint);
        ship.Controls.Flight.Pitch = Math.Clamp(pitch + 0.35f * MathF.Sin(_weaveTime * 1.3f), -1, 1);
        ship.Controls.Flight.Yaw = Math.Clamp(yaw + 0.35f * MathF.Cos(_weaveTime * 0.9f), -1, 1);
        ship.Controls.Flight.Roll = 0.3f * MathF.Sin(_weaveTime * 0.5f);
        ship.Controls.Flight.Throttle = 0.55f;
        ship.Controls.Flight.Assist = true;
        ship.Controls.Fire = false;
    }
}
