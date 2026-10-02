using System;
using System.Collections.Generic;
using System.Globalization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Vault;

/// <summary>
/// The pure half of the vault gate card (Dialogs/VaultGateDialog): which feature a padlock
/// belongs to, what the tiers cost, the supporter-count line, and when the last-day card for an
/// invite week is owed. Prices live here and nowhere else in the client; they must match Patreon.
/// </summary>
public static class VaultOffer
{
    /// <summary>Monthly price in US cents, by tier (1 = vault, 2 = lab). Mirrors the Patreon tiers.</summary>
    public static int MonthlyCents(int tier) => tier >= 2 ? 1000 : 500;

    /// <summary>"$5", "$10", "$7.50".</summary>
    public static string MonthlyLabel(int tier)
    {
        var cents = MonthlyCents(tier);
        return cents % 100 == 0
            ? "$" + (cents / 100).ToString(CultureInfo.InvariantCulture)
            : "$" + (cents / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>"17¢": the monthly price over 30 days, rounded to the nearest cent.</summary>
    public static string PerDayLabel(int tier)
        => ((int)Math.Round(MonthlyCents(tier) / 30.0, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + "¢";

    /// <summary>
    /// The feature behind a tab's padlock, from the tab view's type name. Every gated tab forwards
    /// its unlock button to <c>MainWindow.BtnGateUnlock_Click</c>, so this is how the card knows
    /// what was clicked. Null (a generic vault card) for anything it does not recognise.
    /// </summary>
    public static string? FeatureKeyForView(string? viewTypeName) => viewTypeName switch
    {
        "LockdownTabView" => "lockdown",
        "BambiTakeoverTabView" => "bambitakeover",
        "HapticsTabView" => "haptics",
        "AwarenessTabView" => "awareness",
        "RemoteControlTabView" => "remotecontrol",
        "SheListeningTabView" => "shelistening",
        "BlinkTrainerTabView" => "blinktrainer",
        _ => null,
    };

    /// <summary>The registry entry for a feature key, or null.</summary>
    public static ExclusiveFeature? Feature(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (var f in ExclusiveFeature.All)
            if (string.Equals(f.Key, key, StringComparison.Ordinal)) return f;
        return null;
    }

    /// <summary>
    /// The supporter count as the card says it: rounded DOWN to the hundred and shown as "1,200",
    /// so the "+" in the copy is always true. Null below 100 or when unknown: the line is left
    /// out rather than shown with a small or invented number.
    /// </summary>
    public static string? SupporterFloor(int? count)
    {
        if (count is not int n || n < 100) return null;
        return (n / 100 * 100).ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary><c>GET /v2/public/supporters</c> → <c>{ "count": 1234 }</c>. Null for anything else.</summary>
    public static int? ParseSupporterCount(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            var token = JObject.Parse(body)["count"];
            return token?.Type == JTokenType.Integer && token.Value<long>() is long v && v >= 0 && v < int.MaxValue ? (int)v : null;
        }
        catch (Exception ex) { Diag.Swallowed(ex, "supporters body not JSON"); return null; }
    }

    /// <summary>The last-day card shows inside this window before an invite week ends.</summary>
    public static readonly TimeSpan EndingWindow = TimeSpan.FromHours(36);

    /// <summary>
    /// The seen-flag key of the last-day card this invite week still owes, or null. Owed only to
    /// premium that comes from the week alone, only inside <see cref="EndingWindow"/>, and once
    /// per week (the key carries the week's end, so a later week is owed its own card).
    /// </summary>
    public static string? InviteEndingOwed(DateTime? grantUntilUtc, DateTime nowUtc, bool inviteWeekOnly, ICollection<string>? seen)
    {
        if (!inviteWeekOnly || grantUntilUtc is not DateTime end) return null;
        var left = end - nowUtc;
        if (left <= TimeSpan.Zero || left > EndingWindow) return null;
        var key = "invite-ending:" + end.ToUniversalTime().ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        return seen != null && seen.Contains(key) ? null : key;
    }
}
