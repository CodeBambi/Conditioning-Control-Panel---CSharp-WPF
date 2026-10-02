using System;
using System.Windows;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Invites;

/// <summary>
/// The live half of <see cref="InviteRules.ApplyGrant"/>: takes an <c>invite_grant_until</c>
/// reading from the heartbeat or a redeem reply and, when it opens the premium window further,
/// saves and tells every premium gate to repaint. Mirrors <see cref="EntitlementTierSync"/>.
/// </summary>
public static class InviteGrantSync
{
    /// <summary>Raised on the UI thread after a grant opened the window, with its end (UTC).</summary>
    public static event Action<DateTime>? GrantApplied;

    /// <summary>Offer a server reading. Safe from any thread; never throws.</summary>
    public static void Offer(DateTime? grantUntilUtc, string source)
    {
        try
        {
            if (grantUntilUtc == null) return;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                new Action(() => ApplyOnUi(grantUntilUtc.Value, source)));
        }
        catch (Exception ex)
        {
            Diag.Swallowed(ex, "invite grant offer");
        }
    }

    private static void ApplyOnUi(DateTime grantUntilUtc, string source)
    {
        try
        {
            var settings = App.Settings?.Current;
            if (settings == null || !InviteRules.ApplyGrant(settings, grantUntilUtc, DateTime.UtcNow)) return;
            var until = settings.PatreonPremiumValidUntil ?? grantUntilUtc;
            App.Logger?.Information("[Invites] {Source}: invite week opens premium until {Until:u}", source, until);
            App.Settings?.Save();
            try { GrantApplied?.Invoke(until); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Invites] GrantApplied handler failed"); }
            App.Patreon?.NotifyEntitlementRaised(PatreonTier.Level1);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "[Invites] grant apply failed");
        }
    }
}
