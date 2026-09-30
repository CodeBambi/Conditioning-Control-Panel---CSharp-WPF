using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Friends;

// The corner notifications for pokes, knocks and requests, as pure rules: which notices are up,
// which one a repeat folds into, how long each has left, where the stack sits. No WPF, no App
// statics. FriendNotices (Windows/Friends) draws what this says and runs the buttons.

/// <summary>What a corner notice is about. Repeats fold only within the same kind.</summary>
public enum NoticeKind { Poke, Invite, Watch, Request }

/// <summary>One corner notice. Mutable on purpose: a repeat bumps <see cref="Count"/> and
/// refills the timer instead of adding a second notice.</summary>
public sealed class FriendNotice
{
    public FriendNotice(NoticeKind kind, string friendId, string name, DateTimeOffset at, double lifetimeMs, object? payload = null)
    {
        Kind = kind;
        FriendId = friendId ?? "";
        Name = name ?? "";
        At = at;
        LifetimeMs = Math.Max(1, lifetimeMs);
        LeftMs = LifetimeMs;
        Payload = payload;
    }

    public NoticeKind Kind { get; }
    public string FriendId { get; }
    public string Name { get; }
    /// <summary>When the latest repeat arrived. The relative time reads from this.</summary>
    public DateTimeOffset At { get; internal set; }
    public double LifetimeMs { get; }
    public double LeftMs { get; internal set; }
    public int Count { get; internal set; } = 1;
    /// <summary>The latest thing that arrived (an InboxItem or a FriendRequest). A repeat replaces it,
    /// so the inline action always answers the newest one.</summary>
    public object? Payload { get; internal set; }

    /// <summary>Fraction of the lifetime bar still lit, 1 at the start, 0 when it runs out.</summary>
    public double Fraction => Math.Clamp(LeftMs / LifetimeMs, 0, 1);
}

public static class FriendNoticeRules
{
    /// <summary>At most this many notices are drawn; the rest collapse into "+N more".</summary>
    public const int MaxVisible = 3;

    /// <summary>A repeat from the same friend and kind inside this window folds into one notice.</summary>
    public const double CoalesceSeconds = 30;

    /// <summary>The whole list never grows past this; the oldest goes first.</summary>
    public const int Cap = 12;

    /// <summary>Gap from the work area edges, and between two notices, in DIPs.</summary>
    public const double EdgeMargin = 14;

    public static double LifetimeMs(NoticeKind kind) => kind switch
    {
        // Something to answer stays up a little longer than a wave.
        NoticeKind.Invite or NoticeKind.Watch => 10_000,
        NoticeKind.Request => 8_000,
        _ => 6_000,
    };

    /// <summary>"now", "12s", "3m": the short relative time a notice wears.</summary>
    public static (string Key, int Arg) Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var s = (now - at).TotalSeconds;
        if (s < 5) return ("friends_notice_now", 0);
        if (s < 60) return ("friends_notice_seconds", (int)s);
        if (s < 3600) return ("friends_notice_minutes", (int)(s / 60));
        return ("friends_notice_hours", (int)(s / 3600));
    }

    /// <summary>The count suffix: empty for one, "x3" for three.</summary>
    public static string CountSuffix(int count) => count > 1 ? " x" + count : "";

    /// <summary>Top-left of a stack of <paramref name="width"/> x <paramref name="height"/> DIPs
    /// sitting in the bottom-right corner of the work area (so above the taskbar), kept inside it.</summary>
    public static (double Left, double Top) Place(double areaLeft, double areaTop, double areaWidth, double areaHeight,
        double width, double height)
    {
        var left = areaLeft + areaWidth - width - EdgeMargin;
        var top = areaTop + areaHeight - height - EdgeMargin;
        return (Math.Max(areaLeft, left), Math.Max(areaTop, top));
    }
}

/// <summary>The notices that are up, newest first. Adding folds repeats, ticking runs the timers
/// (all of them stand still while the pointer is over the stack), expired ones leave.</summary>
public sealed class FriendNoticeStack
{
    private readonly List<FriendNotice> _all = new();
    private readonly List<FriendNotice> _evicted = new();

    /// <summary>Every live notice, newest first.</summary>
    public IReadOnlyList<FriendNotice> All => _all;

    /// <summary>The ones drawn, newest first, at most <see cref="FriendNoticeRules.MaxVisible"/>.</summary>
    public IReadOnlyList<FriendNotice> Visible => _all.Count <= FriendNoticeRules.MaxVisible
        ? _all
        : _all.GetRange(0, FriendNoticeRules.MaxVisible);

    /// <summary>How many sit behind the "+N more" line.</summary>
    public int Hidden => Math.Max(0, _all.Count - FriendNoticeRules.MaxVisible);

    public bool Paused { get; set; }

    /// <summary>Adds a notice, or folds it into a live one from the same friend and kind that
    /// arrived within <see cref="FriendNoticeRules.CoalesceSeconds"/>. A fold bumps the count,
    /// refills the timer, takes the new payload and moves the notice to the top. Returns the
    /// notice that is now up and whether it was a fold.</summary>
    public (FriendNotice Notice, bool Folded) Add(FriendNotice notice)
    {
        if (notice == null) throw new ArgumentNullException(nameof(notice));
        for (int i = 0; i < _all.Count; i++)
        {
            var n = _all[i];
            if (n.Kind != notice.Kind || !string.Equals(n.FriendId, notice.FriendId, StringComparison.Ordinal)) continue;
            if ((notice.At - n.At).TotalSeconds > FriendNoticeRules.CoalesceSeconds) continue;
            n.Count++;
            n.At = notice.At;
            n.LeftMs = n.LifetimeMs;
            n.Payload = notice.Payload;
            _all.RemoveAt(i);
            _all.Insert(0, n);
            return (n, true);
        }
        _all.Insert(0, notice);
        while (_all.Count > FriendNoticeRules.Cap)
        {
            _evicted.Add(_all[_all.Count - 1]);
            _all.RemoveAt(_all.Count - 1);
        }
        return (notice, false);
    }

    /// <summary>The notices a full stack pushed off since the last call, oldest last. They left
    /// unseen, so the window treats them like one that ran out.</summary>
    public List<FriendNotice> TakeEvicted()
    {
        var list = new List<FriendNotice>(_evicted);
        _evicted.Clear();
        return list;
    }

    /// <summary>Runs the visible timers by <paramref name="elapsedMs"/> unless paused. Hidden
    /// notices wait their turn with a full bar. Returns the ones that ran out, already removed.</summary>
    public List<FriendNotice> Tick(double elapsedMs)
    {
        var gone = new List<FriendNotice>();
        if (Paused || elapsedMs <= 0) return gone;
        var visible = Math.Min(_all.Count, FriendNoticeRules.MaxVisible);
        for (int i = 0; i < visible; i++) _all[i].LeftMs -= elapsedMs;
        for (int i = _all.Count - 1; i >= 0; i--)
            if (_all[i].LeftMs <= 0) { gone.Add(_all[i]); _all.RemoveAt(i); }
        return gone;
    }

    /// <summary>Takes one notice down (its close X, its action, a click on it).</summary>
    public bool Remove(FriendNotice notice) => notice != null && _all.Remove(notice);

    public void Clear() => _all.Clear();
}
