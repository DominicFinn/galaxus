namespace Galaxus.Core.Law;

/// <summary>
/// Who gets through on your comms. One call at a time: a caller with nobody in the way gets
/// straight through, and one who clashes with another call backs off and tries again later,
/// waiting 1, 2, 4, 8 and then 15 minutes of flight time. Calls are spaced out: nobody calls the
/// moment one ends, or as you arrive or undock. Docking or leaving the system clears the back-offs.
/// </summary>
public sealed class Comms
{
    /// <summary>Back-off after each clash, in seconds.</summary>
    public static readonly float[] BackOff = [60, 120, 240, 480, 900];

    /// <summary>Time after a call ends that still counts as a clash, so calls don't come back to back.</summary>
    public const float Gap = 30;

    private readonly Dictionary<string, (float NextTry, int Clashes)> _waiting = [];
    private float _quietFrom, _settledAt;

    /// <summary>A call is open.</summary>
    public bool Busy { get; private set; }

    /// <summary>Whether a caller is still backing off, so shouldn't try yet.</summary>
    public bool BackingOff(string caller, float now) => _waiting.TryGetValue(caller, out var w) && now < w.NextTry;

    /// <summary>
    /// A caller tries to reach you. Returns true if they get through, which opens the line. If
    /// someone else is on it, they back off for longer each time they clash.
    /// </summary>
    public bool TryCall(string caller, float now)
    {
        if (BackingOff(caller, now)) return false;
        // Just arrived or undocked: nobody calls yet, and nobody is put off for trying.
        if (now < _settledAt) return false;
        if (Busy || now < _quietFrom)
        {
            int clashes = _waiting.TryGetValue(caller, out var w) ? w.Clashes : 0;
            _waiting[caller] = (now + BackOff[Math.Min(clashes, BackOff.Length - 1)], clashes + 1);
            return false;
        }
        _waiting.Remove(caller);
        Busy = true;
        return true;
    }

    /// <summary>A call you make yourself: always goes through, and nobody else gets in while it's open.</summary>
    public void Outgoing() => Busy = true;

    /// <summary>The call has ended.</summary>
    public void Ended(float now)
    {
        Busy = false;
        _quietFrom = now + Gap;
    }

    /// <summary>
    /// Docked or arrived in a system: every back-off is cleared, and nobody calls until you've had
    /// time to settle (seconds of flight time from now).
    /// </summary>
    public void Reset(float now, float settle)
    {
        _waiting.Clear();
        Busy = false;
        _quietFrom = 0;
        _settledAt = now + settle;
    }
}
