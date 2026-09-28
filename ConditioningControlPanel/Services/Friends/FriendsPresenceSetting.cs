using System;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>
/// One door for "do my friends see what I'm doing", shared by the drawer header switch and the
/// Settings row. With a friends service up the switch goes through it (it saves the setting and
/// polls at once, so friends see the change within a poll); without one it writes the setting
/// straight away and the next service start reads it. Either way the one-time ask is spent,
/// because the player has now chosen.
/// </summary>
public static class FriendsPresenceSetting
{
    /// <summary>The current answer: the service's when it exists, else the saved setting.</summary>
    public static bool Read(IFriendsService? svc, Func<bool> saved)
    {
        try { if (svc != null) return svc.PresenceShared; } catch { }
        try { return saved(); } catch { return false; }
    }

    /// <summary>Sets the answer. <paramref name="save"/> is the fallback write used only when
    /// there is no service; <paramref name="markAsked"/> retires the drawer's ask pill.</summary>
    public static void Write(bool shared, IFriendsService? svc, Action<bool> save, Action markAsked)
    {
        bool done = false;
        try
        {
            if (svc != null)
            {
                svc.PresenceShared = shared;
                done = true;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] presence write failed: {E}", ex.Message); }
        if (!done)
        {
            try { save(shared); } catch (Exception ex) { App.Logger?.Debug("[Friends] presence save failed: {E}", ex.Message); }
        }
        try { markAsked(); } catch { }
    }
}
