using System;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Invites;

/// <summary>
/// The live half of <see cref="InviteRules.ApplyGrant"/>: takes an <c>invite_grant_until</c>
/// reading from the heartbeat or a redeem reply and, when it opens the invite week further,
/// saves and tells every premium gate to repaint. Mirrors <see cref="EntitlementTierSync"/>,
/// including its signed-in check, and adds the one thing a grace window never needed: a timer at
/// the week's end, because every trial user crosses it and the gates must repaint when they do.
/// </summary>
public static class InviteGrantSync
{
    /// <summary>Raised on the UI thread after a grant opened the week, with its end (UTC).</summary>
    public static event Action<DateTime>? GrantApplied;

    private static DispatcherTimer? _expiry;

    /// <summary>
    /// Offer a server reading. Safe from any thread; never throws. The account is captured NOW,
    /// so a reading that lands after a logout or an account switch is dropped instead of
    /// handing the old account's week to whoever is signed in next.
    /// </summary>
    public static void Offer(DateTime? grantUntilUtc, string source)
    {
        try
        {
            if (grantUntilUtc == null) return;
            var account = App.Settings?.Current?.UnifiedId;
            if (string.IsNullOrEmpty(account)) return;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(DispatcherPriority.Normal,
                new Action(() => ApplyOnUi(grantUntilUtc.Value, source, account)));
        }
        catch (Exception ex)
        {
            Diag.Swallowed(ex, "invite grant offer");
        }
    }

    /// <summary>The same reading, synchronously, for a caller already on the UI thread that
    /// needs to know whether the week actually opened (the redeem box).</summary>
    public static bool ApplyNow(DateTime? grantUntilUtc, string source)
        => grantUntilUtc != null && ApplyOnUi(grantUntilUtc.Value, source, App.Settings?.Current?.UnifiedId);

    private static bool ApplyOnUi(DateTime grantUntilUtc, string source, string? account)
    {
        try
        {
            var settings = App.Settings?.Current;
            if (settings == null || string.IsNullOrEmpty(account) || string.IsNullOrEmpty(settings.AuthToken)
                || !string.Equals(settings.UnifiedId, account, StringComparison.Ordinal))
                return false;
            if (!InviteRules.ApplyGrant(settings, grantUntilUtc, DateTime.UtcNow))
            {
                // Already recorded (the sign-in profile read writes it directly): make sure the
                // end-of-week timer is running for it all the same.
                if (_expiry == null) ArmExpiry();
                return false;
            }
            var until = settings.InviteGrantUntil ?? grantUntilUtc;
            App.Logger?.Information("[Invites] {Source}: invite week opens premium until {Until:u}", source, until);
            App.Settings?.Save();
            ArmExpiry();
            try { GrantApplied?.Invoke(until); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Invites] GrantApplied handler failed"); }
            App.Patreon?.NotifyEntitlementRaised(PatreonTier.Level1);
            return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "[Invites] grant apply failed");
            return false;
        }
    }

    /// <summary>
    /// One-shot timer at the end of a running week: HasPremiumAccess turns false silently there,
    /// and nothing else would tell the gates to repaint until a restart. Call at startup and after
    /// every grant; a no-op when no week is running. UI thread only.
    /// </summary>
    public static void ArmExpiry()
    {
        try
        {
            _expiry?.Stop();
            _expiry = null;
            var until = App.Settings?.Current?.InviteGrantUntil;
            if (until is not DateTime end) return;
            var left = end - DateTime.UtcNow;
            if (left <= TimeSpan.Zero || left > InviteRules.MaxGrantAhead) return;
            _expiry = new DispatcherTimer { Interval = left + TimeSpan.FromSeconds(2) };
            _expiry.Tick += (_, _) =>
            {
                _expiry?.Stop();
                _expiry = null;
                App.Logger?.Information("[Invites] invite week ended");
                App.Patreon?.NotifyEntitlementRaised(App.Patreon.CurrentTier);
            };
            _expiry.Start();
        }
        catch (Exception ex)
        {
            Diag.Swallowed(ex, "invite expiry timer");
        }
    }
}
