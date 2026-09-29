using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>
/// What this app would tell its friends it is doing, as a STACK rather than a last write.
/// Every surface enters its activity when it opens and leaves it when it closes; the top of
/// the stack is what friends see, and an empty stack is the panel.
///
/// <para>Why a stack: the old single slot let the last writer win, so closing the Arcademy
/// (or Remote, or the race) while a session ran published "in the panel" for the rest of the
/// session. Now closing the Arcademy pops it and the session underneath shows again.</para>
///
/// <para>Rules: entering an activity already on the stack moves it to the top (one entry per
/// activity, most recent wins). Leaving removes it wherever it sits, so a surface that closes
/// out of order never strands another one. <see cref="PresenceActivity.Panel"/> and
/// <see cref="PresenceActivity.Offline"/> are never pushed: they are what an empty stack means.
/// Pure, no WPF, not thread-safe (the app calls it on the dispatcher).</para>
/// </summary>
public sealed class PresenceActivityStack
{
    private readonly List<PresenceActivity> _items = new();

    /// <summary>The activity friends see: the most recent surface still open, else the panel.</summary>
    public PresenceActivity Top => _items.Count == 0 ? PresenceActivity.Panel : _items[^1];

    /// <summary>How many surfaces are open. For tests and the log.</summary>
    public int Count => _items.Count;

    /// <summary>A surface opened. Returns true when <see cref="Top"/> changed.</summary>
    public bool Enter(PresenceActivity activity)
    {
        if (activity is PresenceActivity.Panel or PresenceActivity.Offline) return false;
        var before = Top;
        _items.Remove(activity);
        _items.Add(activity);
        return Top != before;
    }

    /// <summary>A surface closed. Leaving something that was never entered is a no-op.
    /// Returns true when <see cref="Top"/> changed.</summary>
    public bool Leave(PresenceActivity activity)
    {
        var before = Top;
        _items.Remove(activity);
        return Top != before;
    }

    /// <summary>The old single-slot call: the stack becomes just this activity (Panel empties it).
    /// Kept for callers that have not moved to Enter/Leave. Returns true when Top changed.</summary>
    public bool Replace(PresenceActivity activity)
    {
        var before = Top;
        _items.Clear();
        if (activity is not (PresenceActivity.Panel or PresenceActivity.Offline)) _items.Add(activity);
        return Top != before;
    }
}
