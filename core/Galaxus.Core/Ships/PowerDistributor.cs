namespace Galaxus.Core.Ships;

public enum ShipSystem { Engines = 0, Shields = 1, Weapons = 2 }

/// <summary>
/// Shares the power plant's output between engines, shields and weapons, counted in pips.
/// Internally everything is in half pips. Sending a pip to one system takes half a pip from
/// each of the other two, or a whole pip from one if the other is empty.
/// </summary>
public sealed class PowerDistributor
{
    public const int MaxHalfPipsPerSystem = 8; // 4 pips

    private readonly int[] _halfPips = new int[3];

    public int TotalHalfPips { get; }

    public PowerDistributor(int totalPips = 6)
    {
        TotalHalfPips = Math.Clamp(totalPips * 2, 0, MaxHalfPipsPerSystem * 3);
        Balance();
    }

    public float Pips(ShipSystem system) => _halfPips[(int)system] / 2f;

    public int HalfPips(ShipSystem system) => _halfPips[(int)system];

    /// <summary>
    /// How hard a system runs. Two pips (the balanced default) is 1.0, zero pips is 0.4 and
    /// four pips is 1.6.
    /// </summary>
    public float Multiplier(ShipSystem system) => 0.4f + 0.3f * Pips(system);

    public void Balance()
    {
        int each = TotalHalfPips / 3;
        int remainder = TotalHalfPips - each * 3;
        for (int i = 0; i < 3; i++) _halfPips[i] = each;
        // Spread any remainder in engines, shields, weapons order.
        for (int i = 0; remainder > 0; i++, remainder--) _halfPips[i]++;
    }

    /// <summary>Moves one pip into the system. Returns false if nothing changed.</summary>
    public bool Raise(ShipSystem system)
    {
        int target = (int)system;
        int room = MaxHalfPipsPerSystem - _halfPips[target];
        if (room <= 0) return false;

        int a = (target + 1) % 3, b = (target + 2) % 3;
        int wanted = Math.Min(2, room);
        int moved = 0;

        // Take evenly first, then from whichever still has power.
        if (wanted == 2 && _halfPips[a] > 0 && _halfPips[b] > 0)
        {
            _halfPips[a]--;
            _halfPips[b]--;
            moved = 2;
        }
        else
        {
            while (moved < wanted && (_halfPips[a] > 0 || _halfPips[b] > 0))
            {
                int from = _halfPips[a] >= _halfPips[b] ? a : b;
                _halfPips[from]--;
                moved++;
            }
        }

        _halfPips[target] += moved;
        return moved > 0;
    }
}
