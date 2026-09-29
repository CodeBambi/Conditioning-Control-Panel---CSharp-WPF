using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>
/// The feed's pure half: which events it keeps, how each one reads, how old it looks, how many
/// lines show folded, and what the badges and the tray say. No WPF, no disk, no loc lookups here:
/// every string comes back as a loc key plus its arguments, so the suite reads them bare.
/// </summary>
public static class FriendsFeedRules
{
    /// <summary>Folded, the feed shows at least this many lines...</summary>
    public const int FoldMin = 3;

    /// <summary>...and every new line up to this many, so opening the drawer usually clears the badge.</summary>
    public const int FoldMax = 8;

    /// <summary>Each "more" press shows this many further lines.</summary>
    public const int MoreStep = 10;

    /// <summary>A Windows tray tooltip holds 127 characters; a longer one throws.</summary>
    public const int TrayTextMax = 127;

    /// <summary>False for an event the feed never tells: no key or no friend, a kind this build
    /// does not know, or a Remote invite (dropped by the landing since the owner call of
    /// 2026-09-28, so a line about it would point at nothing).</summary>
    public static bool Keep(FriendEvent? e)
    {
        if (e == null || string.IsNullOrEmpty(e.Key) || string.IsNullOrEmpty(e.FriendId)) return false;
        if (!Enum.IsDefined(e.Kind)) return false;
        if (e.Kind == FriendEventKind.InviteReceived && e.Destination == InviteDestination.Remote) return false;
        return true;
    }

    /// <summary>A server time as UTC: a local time is converted, an unspecified one is taken as UTC.</summary>
    public static DateTime Utc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Utc => t,
        DateTimeKind.Local => t.ToUniversalTime(),
        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc),
    };

    /// <summary>How many lines show before "more": every new line, never under
    /// <see cref="FoldMin"/> nor over <see cref="FoldMax"/>, never more than there are.</summary>
    public static int FoldCount(int total, int fresh)
        => Math.Max(0, Math.Min(total, Math.Clamp(fresh, FoldMin, FoldMax)));

    /// <summary>The unread badge: nothing at 0, the number up to 9, then 9+.</summary>
    public static string BadgeText(int unread) => unread <= 0 ? "" : unread > 9 ? "9+" : unread.ToString();

    /// <summary>How old a line looks: now, then minutes, hours and days, short.
    /// Loc keys friends_notice_now / _minutes / _hours and friends_feed_days.</summary>
    public static (string Key, int? N) Ago(DateTime atUtc, DateTime nowUtc)
    {
        var s = (Utc(nowUtc) - Utc(atUtc)).TotalSeconds;
        if (s < 60) return ("friends_notice_now", null);
        if (s < 3600) return ("friends_notice_minutes", (int)(s / 60));
        if (s < 86400) return ("friends_notice_hours", (int)(s / 3600));
        return ("friends_feed_days", (int)(s / 86400));
    }

    /// <summary>The tray tooltip: the app's own line, then "N new" under it when there is anything
    /// unread. Cut to what the tray accepts, the app's line first.</summary>
    public static string TrayText(string baseText, int unread, string note)
    {
        var text = unread > 0 && !string.IsNullOrWhiteSpace(note) ? baseText + "\n" + note : baseText;
        return text.Length <= TrayTextMax ? text : text.Substring(0, TrayTextMax);
    }

    /// <summary>
    /// How one line reads: a loc key whose <c>{0}</c> is the friend's name (drawn bold) and whose
    /// <c>{1}</c>, when present, is <see cref="Line.Detail"/> (already a loc key for a poke or a
    /// destination, flagged by <see cref="Line.DetailIsKey"/>, or the watch's own title).
    /// </summary>
    public static Line Describe(FriendEvent e)
    {
        switch (e.Kind)
        {
            case FriendEventKind.PokeReceived:
                return PokeSet.IsValid(e.Detail)
                    ? new Line("friends_feed_poke", "friends_poke_" + e.Detail, true)
                    : new Line("friends_feed_poke_plain");
            case FriendEventKind.InviteReceived:
                return InviteDestination.IsValid(e.Destination)
                    ? new Line("friends_feed_invite", "friends_invite_" + e.Destination, true)
                    : new Line("friends_feed_invite_plain");
            case FriendEventKind.WatchReceived:
                return string.IsNullOrWhiteSpace(e.Detail)
                    ? new Line("friends_feed_watch_plain")
                    : new Line("friends_feed_watch", e.Detail!.Trim(), false);
            case FriendEventKind.RequestReceived:
                return new Line("friends_feed_request");
            case FriendEventKind.RequestAccepted:
                return new Line("friends_feed_accepted");
            case FriendEventKind.ItemSeen:
                return new Line(e.Detail switch
                {
                    ReceiptKind.Poke => "friends_feed_seen_poke",
                    ReceiptKind.Invite => "friends_feed_seen_invite",
                    ReceiptKind.Watch => "friends_feed_seen_watch",
                    ReceiptKind.Request => "friends_feed_seen_request",
                    _ => "friends_feed_seen",
                });
            case FriendEventKind.InviteAnswered:
                return new Line(e.Detail == ReceiptState.Joined ? "friends_feed_joined" : "friends_feed_declined");
            case FriendEventKind.InviteUnanswered:
                return new Line("friends_feed_no_answer");
            default:
                return new Line("friends_feed_seen");
        }
    }

    /// <summary>Every loc key the feed can draw, for the nine-languages check.</summary>
    public static IReadOnlyList<string> AllKeys { get; } = new[]
    {
        "friends_feed_title", "friends_feed_new", "friends_feed_more", "friends_feed_more_new", "friends_feed_less",
        "friends_feed_someone", "friends_feed_days", "friends_feed_tray",
        "friends_feed_poke", "friends_feed_poke_plain", "friends_feed_invite", "friends_feed_invite_plain",
        "friends_feed_watch", "friends_feed_watch_plain", "friends_feed_request", "friends_feed_accepted",
        "friends_feed_seen_poke", "friends_feed_seen_invite", "friends_feed_seen_watch", "friends_feed_seen_request",
        "friends_feed_seen", "friends_feed_joined", "friends_feed_declined", "friends_feed_no_answer",
    };

    /// <summary>A line's copy: the template key, and the second argument if it has one.</summary>
    public sealed record Line(string Key, string? Detail = null, bool DetailIsKey = false);

    /// <summary>The name a line wears: the one the event carried, else the friend's name in the
    /// list, else null (the drawer then says "someone").</summary>
    public static string? NameFor(FriendEvent e, IEnumerable<Friend>? friends)
    {
        if (!string.IsNullOrWhiteSpace(e.FriendName)) return e.FriendName!.Trim();
        var f = friends?.FirstOrDefault(x => x.Id == e.FriendId);
        return string.IsNullOrWhiteSpace(f?.Name) ? null : f!.Name.Trim();
    }
}
