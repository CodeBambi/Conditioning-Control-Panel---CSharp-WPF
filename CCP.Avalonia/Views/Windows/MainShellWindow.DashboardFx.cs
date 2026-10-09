// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.DashboardFx.cs (920 lines).
//
// LIVE: InitializeDashboardFx (ctor) enrols the mosaic fog in the tab park/resume and owns WPF's
// re-evaluation funnel, ApplyDashboardFxLoops (:171): a motion-level or performance change
// (Env.MotionGateChanged) and window activation/minimise re-run every dashboard card's RefreshFx and
// the vault CTA breath (ApplyVaultCtaBreath, :213). WPF reaches the same funnel through
// ApplyChromeFxLoops; this head has no chrome-FX funnel yet, so the dashboard hooks its own.
//
// STILL MISSING (each a WPF member of MainWindow.DashboardFx.cs):
//   the "? box" doorbell + hover flip - ApplyMysteryFx/ApplyMysteryBreath/ApplyMysteryBadgeVisibility/
//     EnsureMysteryTileFx/RequestMysteryFace/RunMysteryFlip/Open/SettleMysteryPlate/StartMysteryPop;
//   the centre wordmark's drift and sheen - ApplyLogoDrift/ApplyLogoSheenTimer/SweepLogoSheen;
//   the premium rail hover lift/art nudge and live-dot pulse - PrepareRailArtNudge/ApplyRailHover/
//     ApplyRailDotPulse;
//   the browser card's frame sweep and status pulse - ApplyBrowserFrameSweep/ApplyBrowserStatusPulse.

using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Features;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private const double VaultCtaBreathTo = 1.07;      // WPF :200
        private const double VaultCtaBreathSeconds = 2.2;  // WPF :201

        private bool _dashboardFxInitialized;
        private CancellationTokenSource? _vaultCtaBreath;

        /// <summary>True while the vault CTA's breath clock runs (test seam).</summary>
        internal bool VaultCtaBreathing => _vaultCtaBreath != null;

        /// <summary>WPF InitializeDashboardFx (:122), called once from the constructor.</summary>
        private void InitializeDashboardFx()
        {
            if (_dashboardFxInitialized) return;
            _dashboardFxInitialized = true;
            try
            {
                var tab = Named<Tabs.SettingsTabView>("SettingsTab");
                // SettingsTabView composed the fog itself; this only enrols it in the tab park.
                RegisterTabFx("settings", tab?.FindControl<AmbientFxCanvas>("MosaicFx"));

                Activated += OnDashboardFxWindowStateish;
                Deactivated += OnDashboardFxWindowStateish;
                PropertyChanged += OnDashboardFxWindowProperty;
                if (tab != null)
                    tab.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) ApplyVaultCtaBreath(); };
                // A static event: subscribe while open, never root a closed shell (P41).
                Opened += (_, _) => { Env.MotionGateChanged -= ApplyDashboardFxLoops; Env.MotionGateChanged += ApplyDashboardFxLoops; ApplyDashboardFxLoops(); };
                Closed += (_, _) => { Env.MotionGateChanged -= ApplyDashboardFxLoops; StopVaultCtaBreath(); };
            }
            catch (Exception ex) { Log.Warning(ex, "InitializeDashboardFx failed"); }
        }

        private void OnDashboardFxWindowStateish(object? sender, EventArgs e) => ApplyDashboardFxLoops();

        private void OnDashboardFxWindowProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty) ApplyDashboardFxLoops();
        }

        /// <summary>
        /// WPF ApplyDashboardFxLoops (:171): re-evaluates every dashboard loop against the current
        /// motion/tier/focus gate - both ways, so Full re-arms what Reduced/Off parked.
        /// </summary>
        private void ApplyDashboardFxLoops()
        {
            if (!_dashboardFxInitialized) return;
            try
            {
                var tab = Named<Tabs.SettingsTabView>("SettingsTab");
                if (tab == null) return;
                foreach (var card in tab.GetLogicalDescendants().OfType<FeatureCard>()) card.RefreshFx();
                foreach (var combo in tab.GetLogicalDescendants().OfType<SplitFeatureCard>()) combo.RefreshFx();
                ApplyVaultCtaBreath();
            }
            catch (Exception ex) { Log.Debug("ApplyDashboardFxLoops: {E}", ex.Message); }
        }

        /// <summary>
        /// WPF ApplyVaultCtaBreath (:213): the vault stamp's slow 1.0-1.07 scale breath. Parks at
        /// 1.0 unless ambient motion is allowed, the window is focused and the stamp is on screen.
        /// </summary>
        private void ApplyVaultCtaBreath()
        {
            try
            {
                var cta = Named<Tabs.SettingsTabView>("SettingsTab")?.FindControl<TextBlock>("VaultCta");
                if (cta?.RenderTransform is not TransformGroup { Children: [ScaleTransform scale, ..] }) return;
                bool run = Env.AllowAmbientLoops && IsActive && WindowState != WindowState.Minimized
                           && cta.IsEffectivelyVisible;
                if (run == VaultCtaBreathing) return;
                StopVaultCtaBreath();
                scale.ScaleX = scale.ScaleY = 1.0;
                if (!run) return;
                _vaultCtaBreath = new CancellationTokenSource();
                _ = new Animation
                {
                    Duration = TimeSpan.FromSeconds(VaultCtaBreathSeconds),
                    IterationCount = IterationCount.Infinite,
                    PlaybackDirection = PlaybackDirection.Alternate,
                    Easing = new SineEaseInOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(ScaleTransform.ScaleXProperty, 1.0), new Setter(ScaleTransform.ScaleYProperty, 1.0) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(ScaleTransform.ScaleXProperty, VaultCtaBreathTo), new Setter(ScaleTransform.ScaleYProperty, VaultCtaBreathTo) } },
                    },
                }.RunAsync(scale, _vaultCtaBreath.Token);
            }
            catch (Exception ex) { Log.Debug("ApplyVaultCtaBreath: {E}", ex.Message); }
        }

        private void StopVaultCtaBreath()
        {
            _vaultCtaBreath?.Cancel();
            _vaultCtaBreath = null;
        }
    }
}
