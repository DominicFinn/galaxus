using Godot;

namespace Galaxus;

/// <summary>
/// Time acceleration, as in Frontier: for long cruises and descents. It drops back to normal by
/// itself whenever something needs your attention.
/// </summary>
public partial class Main
{
    private static readonly int[] WarpLevels = [1, 2, 5, 10, 20];
    private int _timeWarp = 1;
    /// <summary>Seconds of game time since you last fired or were hit, counting down from 8.</summary>
    private float _fightCooldown;

    private void ChangeWarp(int direction)
    {
        int index = System.Array.IndexOf(WarpLevels, _timeWarp);
        int next = WarpLevels[Mathf.Clamp(index + direction, 0, WarpLevels.Length - 1)];
        if (next > _timeWarp && WarpBlocker() is { } reason)
        {
            _hud.Message($"CAN'T SPEED UP TIME: {reason}");
            return;
        }
        if (next == _timeWarp) return;
        _timeWarp = next;
        _hud.Message(_timeWarp == 1 ? "NORMAL TIME" : $"TIME x{_timeWarp}");
        AudioManager.Instance?.Ui(direction > 0 ? "pip-2" : "pip-0");
    }

    private void SetWarp(int level, string reason)
    {
        if (_timeWarp == level) return;
        _timeWarp = level;
        if (reason != "") _hud.Message($"NORMAL TIME: {reason}");
    }

    /// <summary>Why time can't run fast right now, or null if it can.</summary>
    /// <summary>
    /// A fight that should stop the clock: a battle on, or something hostile within 8 km that isn't
    /// one of the practice drones. Shooting at drones doesn't count.
    /// </summary>
    private bool InRealFight() =>
        _battle is { Won: null }
        || _world.Ships.Any(s => s.Alive && s != _player && !_drones.ContainsKey(s) && _world.Hostile(_player, s)
                                 && System.Numerics.Vector3.Distance(s.Body.Position, _player.Body.Position) < 8000);

    private string? WarpBlocker()
    {
        if (_screen != Screen.Flight || !_player.Alive) return "NOT FLYING";
        if (_fightCooldown > 0 && InRealFight()) return "IN COMBAT";
        if (CanDock || PortToDock != null) return "DOCKING RANGE";
        if (!_cruising && _surface != null && HeightAboveGroundKm() is { } h && h < 1.5) return "LOW ALTITUDE";
        return null;
    }
}
