using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Invites;

/// <summary>Where one of a subscriber's monthly codes stands.</summary>
public enum InviteSlotState
{
    /// <summary>Not handed out yet, or handed out and not redeemed.</summary>
    Open,

    /// <summary>Redeemed: the friend is inside their free week (or it ran out unpaid).</summary>
    Trying,

    /// <summary>The friend paid for a month. This is the one that counts for rewards.</summary>
    Converted
}

/// <summary>One monthly code and what became of it.</summary>
public sealed record InviteSlot(string Code, InviteSlotState State, string? InviteeName, int? Day);

/// <summary>
/// <c>/v2/invites/mine</c> as the app reads it: this month's codes, when they reset, and how many
/// friends have converted over the account's lifetime (the reward ladder's input).
/// </summary>
public sealed record InviteSnapshot(
    IReadOnlyList<InviteSlot> Slots,
    DateTime? ResetsAtUtc,
    int ConvertedTotal);

/// <summary>
/// A <c>mine</c> read. <see cref="Reachable"/> is true when the server answered in words at all
/// (a refusal such as <c>not_subscribed</c> included), which is how the app knows invites exist
/// server-side before it shows anyone a redeem box. <see cref="Snapshot"/> is set for subscribers.
/// </summary>
/// <see cref="ConvertedTotal"/> is the lifetime count from ANY worded reply, so an inviter whose
/// own subscription lapsed still collects ladder rewards for friends who convert later.
public sealed record InviteMine(bool Reachable, InviteSnapshot? Snapshot, int ConvertedTotal = 0)
{
    public static readonly InviteMine Unreachable = new(false, null);
}

/// <summary>A redeem reply. <see cref="Reason"/> is a server refusal word, or <c>offline</c>.</summary>
public sealed record RedeemOutcome(bool Ok, string? Reason, DateTime? GrantUntilUtc)
{
    public static readonly RedeemOutcome Offline = new(false, "offline", null);
}

/// <summary>
/// INVITE WEEK, the pure half. A paying subscriber hands out a few codes a month; a new account
/// that redeems one gets the vault (tier 1) for <see cref="GrantDays"/> days. The server owns the
/// codes, the one-week-per-account rule and conversion; the app only reads the results.
///
/// <para>The week is written into its own <see cref="AppSettings.InviteGrantUntil"/> at the
/// server's exact end date, which <c>HasPremiumAccess</c> and <c>HasAiAccess</c> OR in. It never
/// touches the Patreon stamps (the quest history, the celebration card and the boot heals read
/// those as "they paid") and never goes through <see cref="EntitlementTierRule.ExtendGrace"/>,
/// which would stretch a 7-day gift into the 14-day subscriber grace. See
/// docs/primers/INVITE_WEEK_PRIMER.md for the server contract.</para>
/// </summary>
public static class InviteRules
{
    public const int GrantDays = 7;

    /// <summary>A grant end further out than this is not believed and is ignored outright (a week
    /// plus three days of slack for a PC clock that runs slow). Not
    /// clamped: a clamp measured from "now" would slide forward on every heartbeat and turn one
    /// bad server value into premium for as long as it persists.</summary>
    public static readonly TimeSpan MaxGrantAhead = TimeSpan.FromDays(GrantDays + 3);

    public const int MinCodeLength = 6;
    public const int MaxCodeLength = 24;

    /// <summary>The share link prefix. A pasted link is reduced to its code.</summary>
    public const string LinkPrefix = "cclabs.app/i/";

    /// <summary>
    /// A typed or pasted code in the server's form (upper case, letters, digits and dashes), or
    /// null when it cannot be one. Accepts the share link, spaces and lower case.
    /// </summary>
    public static string? NormalizeCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        var at = s.IndexOf(LinkPrefix, StringComparison.OrdinalIgnoreCase);
        if (at >= 0) s = s[(at + LinkPrefix.Length)..];
        var q = s.IndexOfAny(new[] { '?', '#', '/' });
        if (q >= 0) s = s[..q];

        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c)) continue;
            var u = char.ToUpperInvariant(c);
            if ((u >= 'A' && u <= 'Z') || (u >= '0' && u <= '9') || u == '-') sb.Append(u);
            else return null;
        }
        var code = sb.ToString().Trim('-');
        return code.Length >= MinCodeLength && code.Length <= MaxCodeLength ? code : null;
    }

    /// <summary>An ISO date (string or an already-parsed date token) as UTC, or null. Never throws.</summary>
    public static DateTime? ParseUtc(JToken? token)
    {
        if (token == null) return null;
        try
        {
            if (token.Type == JTokenType.Date)
            {
                var d = token.Value<DateTime>();
                return d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime();
            }
            if (token.Type == JTokenType.String
                && DateTime.TryParse(token.Value<string>(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                return parsed;
        }
        catch (Exception ex) { Diag.Swallowed(ex, "malformed invite date"); }
        return null;
    }

    /// <summary>Top-level <c>invite_grant_until</c> off a heartbeat body, or null. Never throws.</summary>
    public static DateTime? ParseHeartbeatGrant(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return JObject.Parse(body) is JObject root ? ParseUtc(root["invite_grant_until"]) : null; }
        catch (Exception ex) { Diag.Swallowed(ex, "heartbeat body not JSON"); return null; }
    }

    /// <summary>
    /// Record the grant's end. True when the invite week grew (the caller saves and repaints).
    /// Ignores a grant that has ended or one that claims more than <see cref="MaxGrantAhead"/>,
    /// and never shortens a week already recorded. Touches nothing but
    /// <see cref="AppSettings.InviteGrantUntil"/>.
    /// </summary>
    public static bool ApplyGrant(AppSettings? settings, DateTime? grantUntilUtc, DateTime nowUtc)
    {
        if (settings == null || !IsBelievable(grantUntilUtc, nowUtc)) return false;
        var until = grantUntilUtc!.Value;
        if (settings.InviteGrantUntil is DateTime held && held >= until) return false;
        settings.InviteGrantUntil = until;
        return true;
    }

    /// <summary>True when <see cref="ApplyGrant"/> would accept this end date at all.</summary>
    public static bool IsBelievable(DateTime? grantUntilUtc, DateTime nowUtc)
        => grantUntilUtc is DateTime end && end > nowUtc && end <= nowUtc + MaxGrantAhead;

    /// <summary>The lifetime <c>converted_total</c> off any worded reply, refusals included; 0 when absent.</summary>
    public static int ParseConverted(JObject? reply)
        => reply?["converted_total"]?.Type == JTokenType.Integer ? Math.Max(0, reply.Value<int>("converted_total")) : 0;

    /// <summary>The <c>mine</c> reply, or null when it is not one. Unknown slot states read as open.</summary>
    public static InviteSnapshot? ParseSnapshot(JObject? reply)
    {
        if (reply == null || reply.Value<bool?>("ok") == false) return null;
        if (reply["codes"] is not JArray codes) return null;
        var slots = new List<InviteSlot>();
        foreach (var c in codes.OfType<JObject>())
        {
            var code = NormalizeCode(c.Value<string?>("code"));
            if (code == null) continue;
            var state = c.Value<string?>("state") switch
            {
                "converted" => InviteSlotState.Converted,
                "trying" => InviteSlotState.Trying,
                _ => InviteSlotState.Open
            };
            var name = state == InviteSlotState.Open ? null : c.Value<string?>("invitee_name");
            int? day = c["day"]?.Type == JTokenType.Integer ? c.Value<int>("day") : null;
            slots.Add(new InviteSlot(code, state, string.IsNullOrWhiteSpace(name) ? null : name, day));
        }
        return new InviteSnapshot(slots, ParseUtc(reply["resets_at"]), ParseConverted(reply));
    }

    /// <summary>The <c>redeem</c> reply. A null reply (network, timeout, signed out) is offline.</summary>
    public static RedeemOutcome ParseRedeem(JObject? reply)
    {
        if (reply == null) return RedeemOutcome.Offline;
        if (reply.Value<bool?>("ok") != true)
            return new RedeemOutcome(false, reply.Value<string?>("reason") ?? "unknown", null);
        return new RedeemOutcome(true, null, ParseUtc(reply["grant_until"]));
    }
}
