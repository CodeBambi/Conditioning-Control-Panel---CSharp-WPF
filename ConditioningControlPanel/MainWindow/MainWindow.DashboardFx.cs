using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// PR-2 of the FX overhaul: the *Dashboard* pass. Everything in here decorates the mosaic tab
    /// (Views/Tabs/SettingsTabView.xaml) and nothing in here touches the shell - the chrome is
    /// PR-1's and lives in MainWindow.ChromeFx.cs.
    ///
    /// Six regions, and exactly ONE of them owns a continuous canvas:
    ///   1. behind the mosaic  - a single <see cref="AmbientFxCanvas"/> (FogDrift + sparse
    ///      DustField). The only looping ambient layer on the tab.
    ///   2. the 12 FeatureCards - hover lift + rim-light, breathing ring/glow when active. Those
    ///      clocks live in Features/FeatureCard.xaml.cs; this file only re-evaluates them.
    ///   3. the centre logo    - Ken Burns drift + an occasional sheen pass.
    ///   4. the premium rail   - hover lift + art nudge; status dots pulse only while live.
    ///   5. the browser card   - frame/header only. WebView2 is a native HWND: airspace means
    ///      nothing WPF draws can ever cover it, so no FX goes over the web surface.
    ///   6. the Program "Today" card - its hand-rolled fog/sheen/sparkles stay in
    ///      MainWindow.ProgramBanner.cs (see the note on StartDashboardProgramBannerFx below).
    ///
    /// House rules (FX plan section 2) this file obeys:
    ///   * two clocks - interaction 80-400ms, ambient 3.5-40s. Nothing in between.
    ///   * every ambient clock asks <see cref="ChromeAmbientAllowed"/> (window focus + the
    ///     reduced-motion setting + the performance tier) BEFORE it starts and parks at a static
    ///     resting state otherwise;
    ///   * colour only ever from the Fx* dynamic resources / <see cref="FxTheme"/>;
    ///   * <c>element.BeginAnimation</c> only - Storyboard.SetTargetName silently no-ops across
    ///     the SettingsTabView namescope;
    ///   * every callback wrapped. Decoration may never be why the dashboard throws.
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------

        /// <summary>Fog/dust alpha multiplier. Low: this is air in the room, not weather.</summary>
        private const double MosaicFxIntensity = 0.62;
        private const int MosaicFogPuffs = 3;

        private const double LogoDriftScaleTo = 1.04;
        private const double LogoDriftSeconds = 40;
        /// <summary>A 40s scale ramp moves ~0.001 of a unit per frame; 10fps is invisible from 24
        /// and costs less than half as much.</summary>
        private const int LogoDriftFrameRate = 10;
        private const double LogoSheenSeconds = 1.1;
        private const int LogoSheenMinGapSeconds = 25;
        private const int LogoSheenMaxGapSeconds = 40;

        private const double RailHoverArtNudge = -0.03;   // brush-relative: ~1.3px on a 42px chip
        private const int RailHoverMs = 150;

        private const double DotPulseMinOpacity = 0.45;
        private const double DotPulseSeconds = 3.4;

        /// <summary>Seconds for one pass of the browser frame's travelling light, and the full
        /// cycle including its rest. The long flat tail IS the pause - one clock, nothing to
        /// schedule.</summary>
        private const double BrowserFrameSweepSeconds = 2.4;
        private const double BrowserFrameCycleSeconds = 14.0;
        private const double BrowserStatusPulseSeconds = 1.6;

        // ---- state -----------------------------------------------------------------

        private bool _dashboardFxInitialized;
        private DispatcherTimer? _logoSheenTimer;
        private readonly Random _dashboardFxRng = new();
        private readonly List<Ellipse> _pulsingRailDots = new();
        private TextBlock? _browserStatusWatched;

        /// <summary>The 12 mosaic tiles. Null-tolerant - the view is a partial rewire.</summary>
        private IEnumerable<FeatureCard> DashboardFeatureCards
        {
            get
            {
                var tab = SettingsTab;
                if (tab == null) return Enumerable.Empty<FeatureCard>();
                var all = new[]
                {
                    tab.CardFlash, tab.CardVisuals, tab.CardVideo, tab.CardSubliminal,
                    tab.CardSpiral, tab.CardLockCard, tab.CardPinkFilter, tab.CardMindWipe,
                    tab.CardBubblePop, tab.CardBouncingText, tab.CardSystem, tab.CardBubbleCount,
                };
                return all.Where(c => c != null)!;
            }
        }

        /// <summary>The premium rail's 8 hoverable items (6 chips + the 2 launcher cards).</summary>
        private IEnumerable<FrameworkElement> PremiumRailItems
        {
            get
            {
                var tab = SettingsTab;
                if (tab == null) return Enumerable.Empty<FrameworkElement>();
                var all = new FrameworkElement?[]
                {
                    tab.ChipTakeover, tab.ChipAwareness, tab.ChipHaptics, tab.ChipGradedIntake,
                    tab.ChipVoice, tab.CardLockdown, tab.CardBlink, tab.ChipRemote,
                };
                return all.Where(c => c != null)!;
            }
        }

        // ============================== lifecycle ==============================

        /// <summary>
        /// Wires the dashboard FX up once, after the window is loaded (so every templated chip and
        /// tile is real before we touch it). Safe to call twice.
        /// </summary>
        internal void InitializeDashboardFx()
        {
            if (_dashboardFxInitialized) return;
            _dashboardFxInitialized = true;
            try
            {
                var tab = SettingsTab;
                if (tab == null) return;

                // 1. The one ambient canvas, registered against the dashboard's tab key so
                //    ShowTab parks it on the way out and resumes it on the way in for free.
                if (tab.MosaicFx != null)
                {
                    tab.MosaicFx.StartLayers(new AmbientFxConfig
                    {
                        Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField,
                        Intensity = MosaicFxIntensity,
                        FogPuffs = MosaicFogPuffs,
                    });
                    RegisterTabFx("settings", tab.MosaicFx);
                }

                // 3. Centre logo: the drift follows the wordmark FACE, so the intake flip
                //    collapsing it stops the clock instead of animating something invisible.
                if (tab.LogoFaceLogo != null)
                    tab.LogoFaceLogo.IsVisibleChanged += LogoFace_IsVisibleChanged;
                _logoSheenTimer = new DispatcherTimer();
                _logoSheenTimer.Tick += (_, __) => SweepLogoSheen();

                // 4. Premium rail hover. The chip's ART is its Background brush, so the nudge is a
                //    RelativeTransform on a private CLONE of it - the shared resource brush is
                //    left alone (and may well be frozen).
                foreach (var item in PremiumRailItems)
                {
                    PrepareRailArtNudge(item);
                    item.MouseEnter += RailItem_MouseEnter;
                    item.MouseLeave += RailItem_MouseLeave;
                }

                // 5. Browser card: watch the status text rather than editing the ~10 places that
                //    write it. One hook, and it cannot get out of step with them.
                HookBrowserStatusWatcher(tab.TxtBrowserStatus);

                ApplyDashboardFxLoops();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitializeDashboardFx failed"); }
        }

        /// <summary>
        /// Re-evaluates every dashboard loop. Called from <see cref="ApplyChromeFxLoops"/>, which
        /// is itself the single funnel for window activation, the mod switch (RefreshChromeFx) and
        /// the reduced-motion picker - so the dashboard follows all three without its own hooks.
        /// </summary>
        private void ApplyDashboardFxLoops()
        {
            if (!_dashboardFxInitialized) return;
            try
            {
                var tab = SettingsTab;
                if (tab == null) return;

                // The canvas parks itself on deactivate/minimise/tab-hide. This call is for the
                // other direction: coming back UP from Reduced/Off, nothing else would poke it.
                if (tab.MosaicFx != null &&
                    string.Equals(_activeTabKey, "settings", StringComparison.OrdinalIgnoreCase))
                    tab.MosaicFx.Resume();

                foreach (var card in DashboardFeatureCards) card.RefreshFx();

                ApplyLogoDrift();
                ApplyLogoSheenTimer();
                ApplyBrowserFrameSweep();
                ApplyBrowserStatusPulse();
                foreach (var dot in _pulsingRailDots.ToList()) ApplyRailDotPulse(dot, true, force: true);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyDashboardFxLoops: {E}", ex.Message); }
        }

        // ============================== 3. centre logo ==============================

        private void LogoFace_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                ApplyLogoDrift();
                ApplyLogoSheenTimer();
            }
            catch (Exception ex) { App.Logger?.Debug("LogoFace_IsVisibleChanged: {E}", ex.Message); }
        }

        /// <summary>
        /// Ken Burns idle drift on the wordmark: 1.00 to 1.04 and back over 40s. RenderTransform
        /// only, so it never takes layout with it, and the tile's 6px margin is the headroom the
        /// 4% growth paints into. The transform is LogoFaceLogo's own - see the long note in the
        /// view about why it cannot be ImgLogo's or LogoFlipHost's.
        /// </summary>
        private void ApplyLogoDrift()
        {
            try
            {
                var tab = SettingsTab;
                var scale = tab?.LogoDriftScale;
                if (scale == null || scale.IsFrozen) return;

                bool wanted = ChromeAmbientAllowed && tab!.LogoFaceLogo?.IsVisible == true;
                if (!wanted)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = scale.ScaleY = 1.0;
                    return;
                }

                var drift = new DoubleAnimation(1.0, LogoDriftScaleTo, TimeSpan.FromSeconds(LogoDriftSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(drift, LogoDriftFrameRate);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, drift);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, drift);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyLogoDrift: {E}", ex.Message); }
        }

        private void ApplyLogoSheenTimer()
        {
            try
            {
                if (_logoSheenTimer == null) return;
                var tab = SettingsTab;
                bool wanted = ChromeAmbientAllowed && tab?.LogoFaceLogo?.IsVisible == true;
                if (!wanted)
                {
                    _logoSheenTimer.Stop();
                    ParkSheen(tab?.LogoSheen, tab?.LogoSheenSlide);
                    return;
                }
                if (_logoSheenTimer.IsEnabled) return;
                _logoSheenTimer.Interval = NextLogoSheenGap();
                _logoSheenTimer.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyLogoSheenTimer: {E}", ex.Message); }
        }

        /// <summary>25-40s apart, re-rolled each pass: a fixed gap reads as a metronome.</summary>
        private TimeSpan NextLogoSheenGap() =>
            TimeSpan.FromSeconds(_dashboardFxRng.Next(LogoSheenMinGapSeconds, LogoSheenMaxGapSeconds + 1));

        private void SweepLogoSheen()
        {
            try
            {
                if (_logoSheenTimer != null) _logoSheenTimer.Interval = NextLogoSheenGap();
                var tab = SettingsTab;
                if (tab?.LogoFaceLogo?.IsVisible != true) return;
                if (!ChromeAmbientAllowed) return;
                SweepSheen(tab.LogoFaceLogo, tab.LogoSheen, tab.LogoSheenSlide, LogoSheenSeconds, 0.26);
            }
            catch (Exception ex) { App.Logger?.Debug("SweepLogoSheen: {E}", ex.Message); }
        }

        // ============================== 4. premium rail ==============================

        /// <summary>
        /// Gives a rail item its own copy of its art brush with a translate transform on it. The
        /// brushes are shared XAML resources and may be frozen, so we clone once at init rather
        /// than reach into the resource (which would also nudge every other user of it).
        /// </summary>
        private static void PrepareRailArtNudge(FrameworkElement item)
        {
            try
            {
                var brush = item switch
                {
                    Control c => c.Background,
                    Border b => b.Background,
                    _ => null,
                };
                if (brush == null || brush.RelativeTransform is TranslateTransform) return;
                if (brush.RelativeTransform != null && !brush.RelativeTransform.Value.IsIdentity) return;

                var copy = brush.Clone();
                copy.RelativeTransform = new TranslateTransform();
                switch (item)
                {
                    case Control c: c.Background = copy; break;
                    case Border b: b.Background = copy; break;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("PrepareRailArtNudge: {E}", ex.Message); }
        }

        private void RailItem_MouseEnter(object sender, MouseEventArgs e) => ApplyRailHover(sender, true);

        private void RailItem_MouseLeave(object sender, MouseEventArgs e) => ApplyRailHover(sender, false);

        /// <summary>Hover on a rail item: the 1.02 lift plus a ~1px push on the art behind it.
        /// The chips sit in a ScrollViewer that clips, so the lift is deliberately the shared
        /// 1.02 and not the tiles' old 1.03 - the rail only has 4px of margin to grow into.</summary>
        private void ApplyRailHover(object sender, bool on)
        {
            try
            {
                if (sender is not FrameworkElement item) return;
                MotionFx.HoverLift(item, on);

                var brush = item switch
                {
                    Control c => c.Background,
                    Border b => b.Background,
                    _ => null,
                };
                if (brush?.RelativeTransform is not TranslateTransform nudge || nudge.IsFrozen) return;

                double to = on ? RailHoverArtNudge : 0;
                if (!MotionFx.AllowTransitions)
                {
                    nudge.BeginAnimation(TranslateTransform.YProperty, null);
                    nudge.Y = to;
                    return;
                }
                var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(RailHoverMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                nudge.BeginAnimation(TranslateTransform.YProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyRailHover: {E}", ex.Message); }
        }

        /// <summary>
        /// Status-dot pulse. Event-driven on purpose: <see cref="RefreshPremiumRail"/> calls this
        /// through SetDot whenever a feature's state actually changes, so an all-off rail holds no
        /// clocks at all. A dot that is off never breathes - a pulse on a dead indicator is a lie
        /// told with animation.
        /// </summary>
        private void ApplyRailDotPulse(Ellipse? dot, bool on, bool force = false)
        {
            if (dot == null) return;
            try
            {
                bool known = _pulsingRailDots.Contains(dot);
                // RefreshPremiumRail repaints all five dots on every state change; re-arming a
                // Forever breath each time would visibly reset its phase.
                if (on && known && !force) return;
                if (on && !known) _pulsingRailDots.Add(dot);
                else if (!on) _pulsingRailDots.Remove(dot);

                if (!on || !ChromeAmbientAllowed)
                {
                    dot.BeginAnimation(UIElement.OpacityProperty, null);
                    dot.Opacity = 1;
                    return;
                }
                var pulse = new DoubleAnimation(DotPulseMinOpacity, 1.0, TimeSpan.FromSeconds(DotPulseSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(pulse, AmbientFrameRate);
                dot.BeginAnimation(UIElement.OpacityProperty, pulse);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyRailDotPulse: {E}", ex.Message); }
        }

        // ============================== 5. browser card ==============================

        /// <summary>
        /// A slow light travelling around the card frame: three gradient stops on one 14s clock,
        /// 2.4s of travel and the rest at rest. At rest the stops sit entirely before the brush's
        /// 0 mark, which pads to a flat GlassBorder - the border that shipped.
        /// </summary>
        private void ApplyBrowserFrameSweep()
        {
            try
            {
                var tab = SettingsTab;
                if (tab?.BrowserFrameStopLead == null || tab.BrowserFrameStopCore == null
                    || tab.BrowserFrameStopTail == null) return;

                var stops = new[] { tab.BrowserFrameStopLead, tab.BrowserFrameStopCore, tab.BrowserFrameStopTail };
                double[] rest = { -0.30, -0.15, 0.0 };

                if (!ChromeAmbientAllowed)
                {
                    for (int i = 0; i < stops.Length; i++)
                    {
                        if (stops[i].IsFrozen) continue;
                        stops[i].BeginAnimation(GradientStop.OffsetProperty, null);
                        stops[i].Offset = rest[i];
                    }
                    return;
                }

                for (int i = 0; i < stops.Length; i++)
                {
                    if (stops[i].IsFrozen) continue;
                    double from = rest[i];
                    double to = from + 1.35;
                    var sweep = new DoubleAnimationUsingKeyFrames
                    {
                        Duration = TimeSpan.FromSeconds(BrowserFrameCycleSeconds),
                        RepeatBehavior = RepeatBehavior.Forever,
                    };
                    sweep.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    sweep.KeyFrames.Add(new LinearDoubleKeyFrame(to,
                        KeyTime.FromTimeSpan(TimeSpan.FromSeconds(BrowserFrameSweepSeconds))));
                    sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(from,
                        KeyTime.FromTimeSpan(TimeSpan.FromSeconds(BrowserFrameSweepSeconds + 0.05))));
                    sweep.KeyFrames.Add(new LinearDoubleKeyFrame(from,
                        KeyTime.FromTimeSpan(TimeSpan.FromSeconds(BrowserFrameCycleSeconds))));
                    Timeline.SetDesiredFrameRate(sweep, AmbientFrameRate);
                    stops[i].BeginAnimation(GradientStop.OffsetProperty, sweep);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyBrowserFrameSweep: {E}", ex.Message); }
        }

        /// <summary>
        /// Watches the browser status text so the "loading" pulse follows it without editing the
        /// ~10 sites in MainWindow.Browser.cs that write it. The descriptor keeps a strong
        /// reference to the handler - harmless here, the TextBlock is a dashboard element that
        /// lives exactly as long as the window does.
        /// </summary>
        private void HookBrowserStatusWatcher(TextBlock? status)
        {
            if (status == null || ReferenceEquals(status, _browserStatusWatched)) return;
            try
            {
                var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
                descriptor?.AddValueChanged(status, BrowserStatus_TextChanged);
                _browserStatusWatched = status;
            }
            catch (Exception ex) { App.Logger?.Debug("HookBrowserStatusWatcher: {E}", ex.Message); }
        }

        private void BrowserStatus_TextChanged(object? sender, EventArgs e) => ApplyBrowserStatusPulse();

        /// <summary>
        /// A gentle pulse on the status badge while the browser is still coming up, and nothing at
        /// all once it is connected (or failed - a pulsing error reads as "retrying", which it is
        /// not). Interaction-length rather than a breath: this is a progress cue, not ambience.
        /// </summary>
        private void ApplyBrowserStatusPulse()
        {
            try
            {
                var status = SettingsTab?.TxtBrowserStatus;
                if (status == null) return;

                bool loading = string.Equals(status.Text?.Trim(),
                                             Loc.Get("label_loading")?.Trim(),
                                             StringComparison.OrdinalIgnoreCase);
                if (!loading || !ChromeAmbientAllowed || !MotionFx.AllowTransitions)
                {
                    status.BeginAnimation(UIElement.OpacityProperty, null);
                    status.Opacity = 1;
                    return;
                }
                var pulse = new DoubleAnimation(0.35, 1.0, TimeSpan.FromSeconds(BrowserStatusPulseSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(pulse, AmbientFrameRate);
                status.BeginAnimation(UIElement.OpacityProperty, pulse);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyBrowserStatusPulse: {E}", ex.Message); }
        }
    }
}
