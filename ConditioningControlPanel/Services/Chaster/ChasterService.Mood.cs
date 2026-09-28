using System;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>Circe's mood (see <see cref="CircesMood"/>): the heat modifier as something the page and
/// the rail chip can draw, plus the cooling a credit buys.</summary>
public sealed partial class ChasterService
{
    /// <summary>A booking moved Circe's mood (before, after). Raised after <see cref="Booked"/>, on
    /// whatever thread booked, and only while the heat row is on. Midnight raises nothing: the
    /// next read of <see cref="Mood"/> is simply calm.</summary>
    public event Action<CircesMood, CircesMood>? MoodChanged;

    /// <summary>Today's mood, or null when there is none to show: tab off, not linked, or the heat
    /// row off. With heat off nothing gets dearer, so a mood would be a lie.</summary>
    public CircesMood? Mood
    {
        get
        {
            var options = _options() ?? ChasterOptions.Off;
            if (!options.TabEnabled || !IsLinked || !options.Prices.Contains(TabDayEnd.HeatId)) return null;
            lock (_gate) return MoodNow();
        }
    }

    // Caller holds _gate.
    private CircesMood MoodNow() => CircesMood.Of(_tab, CircesTab.DayKey(_localNow()));

    // Caller holds _gate. A credit landed: tracked whether or not heat is on, like the counts.
    private void NoteCool() => CircesMood.Cool(_tab, CircesTab.DayKey(_localNow()));

    // Caller does not hold _gate.
    private void RaiseMood(CircesMood before, CircesMood after)
    {
        if (before == after) return;
        if (!(_options() ?? ChasterOptions.Off).Prices.Contains(TabDayEnd.HeatId)) return;
        MoodChanged?.Invoke(before, after);
    }
}
