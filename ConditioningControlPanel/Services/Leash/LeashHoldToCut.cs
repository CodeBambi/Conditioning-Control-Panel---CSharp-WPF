using System;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>
/// Hold the panic key for five seconds to cut the leash (owner, 2026-09-26). The global hook only
/// reports key DOWNS, and a held key auto-repeats about thirty times a second, so without this
/// every repeat would be a fresh panic press and two of those in a row quit the app. While the
/// account is leashed a held panic key is ONE press: the first down runs the panic as always,
/// the repeats only feed this clock, and at <see cref="Hold"/> it asks once whether to cut.
/// A repeat is a down that arrives within <see cref="RepeatGap"/> of the last one with no key-up
/// in between, so a lost key-up can never swallow the next real press (a real press always comes
/// later than any keyboard's repeat delay). Not leashed = no state, every down is a press. Pure.
/// </summary>
public sealed class LeashHoldToCut
{
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(5);

    /// <summary>Longer than the slowest Windows repeat delay (1 s).</summary>
    public static readonly TimeSpan RepeatGap = TimeSpan.FromMilliseconds(1200);

    private DateTime? _since;
    private DateTime _last;
    private bool _asked;

    /// <summary>True while a hold is being timed.</summary>
    public bool Held => _since != null;

    /// <summary>How long the current hold has run, or null when none is timed.</summary>
    public TimeSpan? HeldFor(DateTime now) => _since is { } s ? now - s : null;

    /// <summary>A key-down of the panic key. <c>Repeat</c> = swallow it (not a new press);
    /// <c>Due</c> = the hold just reached five seconds, ask now (true once per hold).</summary>
    public (bool Repeat, bool Due) Down(DateTime now, bool leashed)
    {
        if (!leashed) { Up(); return (false, false); }
        var repeat = _since != null && now - _last <= RepeatGap && now >= _last;
        if (!repeat) { _since = now; _asked = false; }
        _last = now;
        var due = !_asked && now - _since!.Value >= Hold;
        if (due) _asked = true;
        return (repeat, due);
    }

    /// <summary>The panic key came up.</summary>
    public void Up()
    {
        _since = null;
        _asked = false;
    }

    /// <summary>What the chrome calls the panic key ("Escape" reads as "Esc").</summary>
    public static string KeyLabel(string? key) =>
        string.IsNullOrWhiteSpace(key) ? "Esc"
        : string.Equals(key, "Escape", StringComparison.OrdinalIgnoreCase) ? "Esc"
        : key;
}
