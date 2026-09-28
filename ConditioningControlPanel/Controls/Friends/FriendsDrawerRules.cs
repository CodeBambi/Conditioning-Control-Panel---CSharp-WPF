using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// Everything the drawer decides that is not drawing: which rows go in which section and in
/// what order, which loc key words a result, how a friend code is typed. Pure and WPF-free so
/// the suite can hold it without a window.
/// </summary>
internal static class FriendsDrawerRules
{
    /// <summary>The four actions on an expanded card, in the owner's order. "more" is the
    /// dashed row that opens the same menu as a right-click.</summary>
    public static readonly IReadOnlyList<string> CardActions = new[] { "invite", "poke", "watch", "more" };

    /// <summary>The menu under "more" and under a right-click, in order. A null is a divider.</summary>
    public static IReadOnlyList<string?> MenuItems(bool squelched) => new[]
    {
        squelched ? "unsquelch" : "squelch", null, "remove", "block", "report",
    };

    /// <summary>The fixed prefix every friend code wears. The box only takes the five after it.</summary>
    public const string CodePrefix = "CCP-";

    public const int CodeLength = 5;

    /// <summary>The friend-code alphabet (CONTRACT.md): no 0, 1, I or O, so nothing reads twice.</summary>
    public const string CodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>Uppercases what was typed or pasted, drops a pasted "CCP-" and anything outside
    /// the alphabet, and keeps at most five characters.</summary>
    public static string NormaliseCode(string? typed)
    {
        if (string.IsNullOrEmpty(typed)) return "";
        var s = typed.Trim().ToUpperInvariant();
        if (s.StartsWith(CodePrefix, StringComparison.Ordinal)) s = s.Substring(CodePrefix.Length);
        var sb = new StringBuilder(CodeLength);
        foreach (var c in s)
        {
            if (CodeAlphabet.IndexOf(c) < 0) continue;
            sb.Append(c);
            if (sb.Length == CodeLength) break;
        }
        return sb.ToString();
    }

    /// <summary>The whole code the wire takes, or null while the box is not full yet.</summary>
    public static string? FullCode(string? typed)
    {
        var n = NormaliseCode(typed);
        return n.Length == CodeLength ? CodePrefix + n : null;
    }

    /// <summary>Online first by name, then offline by who was here most recently.</summary>
    public static (IReadOnlyList<Friend> Online, IReadOnlyList<Friend> Offline) Split(FriendsSnapshot snap)
    {
        var online = snap.Friends.Where(f => f.Online)
            .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var offline = snap.Friends.Where(f => !f.Online)
            .OrderByDescending(f => f.Presence.LastSeen)
            .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return (online, offline);
    }

    /// <summary>Open tables (2026-09-23): a friend with a listed Goon table floats to the top of
    /// the online group, whatever their presence says (the listing already told us they are
    /// here). Order inside each group is kept.</summary>
    public static (IReadOnlyList<Friend> Online, IReadOnlyList<Friend> Offline) HostingFirst(
        IReadOnlyList<Friend> online, IReadOnlyList<Friend> offline, Func<Friend, bool> hosting)
    {
        var top = online.Where(hosting).Concat(offline.Where(hosting)).ToList();
        if (top.Count == 0) return (online, offline);
        var on = top.Concat(online.Where(f => !hosting(f))).ToList();
        var off = offline.Where(f => !hosting(f)).ToList();
        return (on, off);
    }

    /// <summary>The loc key for what an online friend is doing.</summary>
    public static string ActivityKey(PresenceActivity a) => "friends_activity_" + a.ToString().ToLowerInvariant();

    /// <summary>The loc key (and its one argument, or null) for how long ago someone was here.</summary>
    public static (string Key, int? Arg) SeenKey(DateTimeOffset lastSeen, DateTimeOffset now)
    {
        if (lastSeen == DateTimeOffset.MinValue) return ("friends_seen_long", null);
        var ago = now - lastSeen;
        if (ago < TimeSpan.FromMinutes(2)) return ("friends_seen_now", null);
        if (ago < TimeSpan.FromHours(1)) return ("friends_seen_minutes", (int)ago.TotalMinutes);
        if (ago < TimeSpan.FromHours(24)) return ("friends_seen_hours", (int)ago.TotalHours);
        if (ago < TimeSpan.FromHours(48)) return ("friends_seen_yesterday", null);
        if (ago < TimeSpan.FromDays(30)) return ("friends_seen_days", (int)ago.TotalDays);
        return ("friends_seen_long", null);
    }

    /// <summary>A squelched friend never learns it: the server already answers "sent" for one,
    /// and the client words its own copy the same way.</summary>
    public static string SendResultKey(SendResult r) => r switch
    {
        SendResult.Sent => "friends_result_sent",
        SendResult.Squelched => "friends_result_sent",
        SendResult.TooFast => "friends_result_too_fast",
        SendResult.NotFriends => "friends_result_not_friends",
        SendResult.Offline => "friends_result_offline",
        SendResult.Refused => "friends_result_refused",
        _ => "friends_result_try_later",
    };

    public static bool IsGood(SendResult r) => r is SendResult.Sent or SendResult.Squelched;

    public static string AddResultKey(AddResult r) => r switch
    {
        AddResult.Sent => "friends_add_sent",
        AddResult.Accepted => "friends_add_accepted",
        AddResult.Already => "friends_add_already",
        AddResult.NotFound => "friends_add_not_found",
        AddResult.Blocked => "friends_add_blocked",
        AddResult.Self => "friends_add_self",
        AddResult.Full => "friends_add_full",
        _ => "friends_add_try_later",
    };

    public static bool IsGood(AddResult r) => r is AddResult.Sent or AddResult.Accepted;

    /// <summary>Digits only, at most eight: the HT box's whole grammar.</summary>
    public static string NormaliseHtId(string? typed)
    {
        if (string.IsNullOrEmpty(typed)) return "";
        var sb = new StringBuilder(8);
        foreach (var c in typed)
        {
            if (!char.IsAsciiDigit(c)) continue;
            sb.Append(c);
            if (sb.Length == 8) break;
        }
        return sb.ToString();
    }

    /// <summary>Initials for a name, the same shape the roster draws: one or two letters.</summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2) return (char.ToUpperInvariant(parts[0][0]).ToString() + char.ToUpperInvariant(parts[1][0])).ToString();
        return char.ToUpperInvariant(name.Trim()[0]).ToString();
    }

    /// <summary>Which friends just came online between two snapshots: the avatar hop's list.</summary>
    public static IReadOnlyList<string> CameOnline(FriendsSnapshot before, FriendsSnapshot after)
    {
        var was = new HashSet<string>(before.Friends.Where(f => f.Online).Select(f => f.Id));
        return after.Friends.Where(f => f.Online && !was.Contains(f.Id)).Select(f => f.Id).ToList();
    }
}
