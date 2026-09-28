using System;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>One limit as the settings file holds it: what is in force, and a raise waiting to
/// land. <see cref="PendingMinutes"/> is 0 when no raise waits.</summary>
public readonly record struct LimitSetting(int Minutes, int PendingMinutes, DateTime? PendingAtUtc)
{
    public bool HasPending => PendingMinutes > 0 && PendingAtUtc != null;
}

/// <summary>
/// The raise delay on the two limits (security pass 2, 2026-09-24). A limit can come DOWN at
/// once: that only ever protects the player. A limit going UP waits a day, so a raise is a
/// decision made with a clear head, not in the middle of a session, and never by a controller
/// who has the mouse for ten minutes. Lowering again cancels a raise still waiting.
///
/// <para>Pure: the page hands in the setting and the clock, and saves what comes back.</para>
/// </summary>
public static class LimitChange
{
    public static readonly TimeSpan RaiseDelay = TimeSpan.FromHours(24);

    /// <summary>What is in force right now: the pending raise once its day is up, else the current value.</summary>
    public static int Effective(LimitSetting setting, DateTime utcNow) =>
        setting.HasPending && utcNow >= setting.PendingAtUtc!.Value ? setting.PendingMinutes : setting.Minutes;

    /// <summary>Fold a raise whose day is up into the current value. Anything else is returned as it was.</summary>
    public static LimitSetting Settle(LimitSetting setting, DateTime utcNow) =>
        setting.HasPending && utcNow >= setting.PendingAtUtc!.Value
            ? new LimitSetting(setting.PendingMinutes, 0, null)
            : setting;

    /// <summary>
    /// The player moved the slider to <paramref name="wantedMinutes"/>. At or under what is in
    /// force: applied now, and any waiting raise is dropped. Over it: stored as a raise that lands
    /// <see cref="RaiseDelay"/> from now. Moving a waiting raise DOWN (still over the current
    /// value) keeps its landing time, since a smaller raise never needs a longer wait; moving it
    /// UP starts the day again.
    /// </summary>
    public static LimitSetting Request(LimitSetting setting, int wantedMinutes, DateTime utcNow)
    {
        var now = Settle(setting, utcNow);
        if (wantedMinutes <= now.Minutes) return new LimitSetting(wantedMinutes, 0, null);
        if (now.HasPending && wantedMinutes == now.PendingMinutes) return now;
        if (now.HasPending && wantedMinutes < now.PendingMinutes)
            return new LimitSetting(now.Minutes, wantedMinutes, now.PendingAtUtc);
        return new LimitSetting(now.Minutes, wantedMinutes, utcNow + RaiseDelay);
    }
}
