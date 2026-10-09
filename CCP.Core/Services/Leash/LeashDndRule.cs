using System;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>
/// Do not disturb (CONTRACT "dnd"). While it runs, assignments, punishments and tugs answer
/// <c>dnd</c>; rewards still land and the leash stays on. Pure.
/// </summary>
public static class LeashDndRule
{
    /// <summary>The server clamps <c>dnd_until</c> to at most this far out.</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromHours(24);

    public static string ToWire(LeashDnd dnd) => dnd switch
    {
        LeashDnd.OneHour => "1h",
        LeashDnd.FourHours => "4h",
        LeashDnd.Today => "today",
        _ => "off",
    };

    /// <summary>When a pause picked now ends, or null for <see cref="LeashDnd.Off"/>.
    /// <paramref name="localNow"/> carries the local offset, so "today" means the next local
    /// midnight, never more than 24 h out.</summary>
    public static DateTimeOffset? Until(LeashDnd dnd, DateTimeOffset localNow)
    {
        DateTimeOffset until;
        switch (dnd)
        {
            case LeashDnd.OneHour: until = localNow.AddHours(1); break;
            case LeashDnd.FourHours: until = localNow.AddHours(4); break;
            case LeashDnd.Today:
                var midnight = new DateTimeOffset(localNow.Date.AddDays(1), localNow.Offset);
                until = midnight;
                break;
            default: return null;
        }
        var max = localNow + MaxLength;
        return until > max ? max : until;
    }

    public static bool IsOn(DateTimeOffset? dndUntil, DateTimeOffset now) => dndUntil is { } u && u > now;
}
