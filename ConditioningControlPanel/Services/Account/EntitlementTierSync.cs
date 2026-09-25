using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The live half of <see cref="EntitlementTierRule"/>: takes an <c>effective_tier</c> reading
    /// from the heartbeat, the read-before-write profile read or a focus refresh, and on a rise
    /// stamps the grace windows, saves, and tells the app. Buy on the site, click back into the
    /// app, unlocked about a second later.
    /// </summary>
    public static class EntitlementTierSync
    {
        /// <summary>Minimum gap between two focus refreshes.</summary>
        public static readonly TimeSpan FocusCheckGap = TimeSpan.FromSeconds(20);

        /// <summary>Raised on the UI thread, BEFORE TierChanged, with the new tier (1 or 2).</summary>
        public static event Action<int>? TierRaised;

        private static DateTime _lastFocusCheckUtc = DateTime.MinValue;
        private static int _focusInFlight;
        private static int? _lastLowerLogged;

        /// <summary>Only for V2 accounts: a unified id and a token to prove it.</summary>
        private static bool SignedInWithV2(AppSettings? s)
            => s != null && !string.IsNullOrEmpty(s.UnifiedId) && !string.IsNullOrEmpty(s.AuthToken);

        /// <summary>Offer a server reading. Safe from any thread; never throws.</summary>
        public static void Offer(int? serverTier, string source)
        {
            try
            {
                if (serverTier is null) return;
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                    new Action(() => ApplyOnUi(serverTier, source)));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Entitlement offer ({Source}) skipped: {Error}", source, ex.Message);
            }
        }

        private static void ApplyOnUi(int? serverTier, string source)
        {
            try
            {
                var settings = App.Settings?.Current;
                if (!SignedInWithV2(settings)) return;

                switch (EntitlementTierRule.Decide(settings!.PatreonTier, serverTier))
                {
                    case EntitlementVerdict.Rise:
                        var tier = serverTier!.Value;
                        App.Logger?.Information("[Entitlement] {Source}: server tier {Server} above stored {Local}, unlocking now",
                            source, tier, settings.PatreonTier);
                        settings.PatreonTier = tier;
                        EntitlementTierRule.ExtendGrace(settings, tier, DateTime.UtcNow);
                        App.Settings?.Save();
                        _lastLowerLogged = null;
                        try { TierRaised?.Invoke(tier); }
                        catch (Exception ex) { App.Logger?.Warning(ex, "[Entitlement] TierRaised handler failed"); }
                        App.Patreon?.NotifyEntitlementRaised(tier >= 2 ? PatreonTier.Level2 : PatreonTier.Level1);
                        break;

                    case EntitlementVerdict.Lower:
                        // Recorded, never applied (see EntitlementVerdict.Lower). Logged once per value.
                        if (_lastLowerLogged != serverTier)
                        {
                            _lastLowerLogged = serverTier;
                            App.Logger?.Information("[Entitlement] {Source}: server tier {Server} below stored {Local}; keeping the grace window",
                                source, serverTier, settings.PatreonTier);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Entitlement] apply failed ({Source})", source);
            }
        }

        /// <summary>
        /// The main window came to the front. At most once per <see cref="FocusCheckGap"/>, read
        /// the profile off the UI thread and offer its tier. Fire and forget.
        /// </summary>
        public static void OnAppFocused()
        {
            try
            {
                var settings = App.Settings?.Current;
                if (!SignedInWithV2(settings) || settings!.OfflineMode) return;
                var now = DateTime.UtcNow;
                if (now - _lastFocusCheckUtc < FocusCheckGap) return;
                if (Interlocked.Exchange(ref _focusInFlight, 1) == 1) return;
                _lastFocusCheckUtc = now;
                var unifiedId = settings.UnifiedId!;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var node = await new V2AuthService().GetUserProfileNodeAsync(unifiedId);
                        Offer(EntitlementTierRule.ParseTier(node?["effective_tier"]), "focus");
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("[Entitlement] focus refresh failed: {Error}", ex.Message);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _focusInFlight, 0);
                    }
                });
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _focusInFlight, 0);
                App.Logger?.Debug("[Entitlement] focus hook failed: {Error}", ex.Message);
            }
        }
    }
}
