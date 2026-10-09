// PORTED from WPF 7.1.5 MainWindow/MainWindow.PremiumSpark.cs (polish 12) and
// MainWindow/MainWindow.SparkleWallet.cs (nav polish wave 6): the two header controls in
// Controls/Header read their own state; the shell only routes their clicks and feeds the wallet.

using System;
using System.ComponentModel;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Nav;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ============================== the Premium spark ==============================

        /// <summary>Opens Premium: the "premium" key once the nav table knows it, otherwise the
        /// old "exclusives" key, which redirects to wherever this build keeps the vault.</summary>
        private void HeaderPremiumSpark_Click(object? sender, RoutedEventArgs e)
        {
            try { ShowTab(PremiumSparkRules.TargetTab(NavSections.SectionForTab)); }
            catch (Exception ex) { Log.Debug("Premium spark click failed: {E}", ex.Message); }
        }

        /// <summary>Re-reads tier and motion. Cheap and idempotent; call it from any tier change
        /// (sign-in, sign-out, a Patreon or SubscribeStar refresh).</summary>
        internal void RefreshPremiumSpark()
        {
            try
            {
                Named<PremiumSpark>("HeaderPremiumSpark")?.Refresh();
                RefreshProfileBubbleTierBadge();
            }
            catch (Exception ex) { Log.Debug("Premium spark refresh failed: {E}", ex.Message); }
        }

        // ============================== the bubble's tier badge ==============================

        private static global::Avalonia.Media.Imaging.Bitmap? _bubbleBadgeT1, _bubbleBadgeT2;
        private static bool _bubbleBadgeT1Tried, _bubbleBadgeT2Tried;

        /// <summary>WPF RefreshProfileBubbleTierBadge: Basic (gold) / Prime (cyan) art on the
        /// bubble's rim from the canonical gates, the same rule as the spark; hidden when free.
        /// The hover blow-up and the 8 s wobble live in MainShellWindow.ProfileBubbleTierFx.cs.</summary>
        internal void RefreshProfileBubbleTierBadge()
        {
            if (Named<global::Avalonia.Controls.Image>("ProfileBubbleTierBadge") is not { } badge) return;
            var tier = PremiumSpark.TierSource();
            global::Avalonia.Media.Imaging.Bitmap? art = tier switch
            {
                SparkTier.Prime => Art(ref _bubbleBadgeT2, ref _bubbleBadgeT2Tried, "tier_badge_t2.png"),
                SparkTier.Basic => Art(ref _bubbleBadgeT1, ref _bubbleBadgeT1Tried, "tier_badge_t1.png"),
                _ => null,
            };
            badge.Source = art;
            badge.IsVisible = art != null;
            if (art != null) EnsureProfileTierFx();
        }

        private static global::Avalonia.Media.Imaging.Bitmap? Art(ref global::Avalonia.Media.Imaging.Bitmap? cache, ref bool tried, string file)
        {
            if (!tried) { tried = true; cache = LoadBadge(file); }
            return cache;
        }

        private static global::Avalonia.Media.Imaging.Bitmap? LoadBadge(string file)
        {
            try
            {
                var uri = new Uri("avares://CCP.Avalonia/Resources/features/" + file);
                if (!global::Avalonia.Platform.AssetLoader.Exists(uri)) return null;
                using var stream = global::Avalonia.Platform.AssetLoader.Open(uri);
                return global::Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 132);
            }
            catch (Exception ex) { Log.Debug("Bubble tier badge art missing: {F} ({E})", file, ex.Message); return null; }
        }

        // ============================== the Sparkle wallet ==============================

        private AppSettings? _sparkleWalletSettings;
        private bool _sparkleWalletActive;
        private int _sparkleWalletShown = -1;

        private SparkleWallet? HeaderWallet => Named<SparkleWallet>("HeaderSparkleWallet");

        private void InitializeSparkleWallet()
        {
            if (_sparkleWalletActive || HeaderWallet is not { } wallet) return;
            _sparkleWalletActive = true;
            if (CoreSettings.Service is { } service) service.CurrentReplaced += ReplaceSparkleWalletSettings;
            wallet.VisitRequested += VisitFromSparkleWallet;
            Closed += (_, _) => ShutdownSparkleWallet();
            ReplaceSparkleWalletSettings();
        }

        private void ReplaceSparkleWalletSettings()
        {
            if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(ReplaceSparkleWalletSettings);
                return;
            }
            if (!_sparkleWalletActive) return;
            if (_sparkleWalletSettings != null) _sparkleWalletSettings.PropertyChanged -= OnSparkleWalletSettingChanged;
            _sparkleWalletSettings = CoreSettings.Current;
            _sparkleWalletSettings.PropertyChanged += OnSparkleWalletSettingChanged;
            _sparkleWalletShown = -1;
            RefreshSparkleWalletBalance();
        }

        private void OnSparkleWalletSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.SkillPoints) || string.IsNullOrEmpty(e.PropertyName))
                global::Avalonia.Threading.Dispatcher.UIThread.Post(RefreshSparkleWalletBalance);
        }

        /// <summary>Paints the balance. ponytail: WPF shows the "+N" reward from
        /// SparklePointRewards.Awarded, which is not in Core; here a RISE in the ledger is the
        /// award (purchases only ever lower it, the wallet rule), so the same badge shows.</summary>
        private void RefreshSparkleWalletBalance()
        {
            if (!_sparkleWalletActive || HeaderWallet is not { } wallet) return;
            var balance = CoreSettings.Current.SkillPoints;
            wallet.SetBalance(balance);
            if (_sparkleWalletShown >= 0 && balance > _sparkleWalletShown
                && IsVisible && WindowState != global::Avalonia.Controls.WindowState.Minimized)
                wallet.ShowReward(balance - _sparkleWalletShown);
            _sparkleWalletShown = balance;
        }

        /// <summary>ponytail: WPF opens the Back Room (BtnStartBackRoom_Click); this head reaches
        /// it from Play's Games cards, so the wallet's invite lands there.</summary>
        private void VisitFromSparkleWallet(object? sender, EventArgs e)
        {
            try { ShowTab("play"); }
            catch (Exception ex) { Log.Debug("VisitFromSparkleWallet: {E}", ex.Message); }
        }

        private void ShutdownSparkleWallet()
        {
            _sparkleWalletActive = false;
            if (CoreSettings.Service is { } service) service.CurrentReplaced -= ReplaceSparkleWalletSettings;
            if (_sparkleWalletSettings != null) _sparkleWalletSettings.PropertyChanged -= OnSparkleWalletSettingChanged;
            _sparkleWalletSettings = null;
            if (HeaderWallet is { } wallet)
            {
                wallet.VisitRequested -= VisitFromSparkleWallet;
                wallet.ClosePopup();
            }
        }
    }
}
