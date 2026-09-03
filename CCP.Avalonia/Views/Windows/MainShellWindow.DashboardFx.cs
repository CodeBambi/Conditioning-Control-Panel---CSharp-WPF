// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.DashboardFx.cs (1112 lines).
//
// Sorted member by member against the seams. The blanket "App.*/WebView2/Win32" header this file
// shipped with was written before they existed and was wrong about most of it: what blocks the
// Dashboard's decoration is not services, it is CALLERS and two gates, named per region below.
//
// WHAT IS REAL HERE: the mosaic's one ambient canvas is enrolled in the tab-level park/resume.
// The other half was already done - SettingsTabView composes MosaicFx itself on first attach with
// the WPF tuning (FogDrift|DustField, intensity 0.62, 3 puffs), because when that view was ported
// there was no tab host to start it. What was missing is the PARK: AmbientFxCanvas.Evaluate gates
// on the canvas's OWN IsVisible, and hiding the Dashboard sets the TAB's, so the fog kept a clock
// running while the user sat somewhere else entirely. RegisterTabFx is what stops that.
//
// EnsureDashboardFx HAS NO CALLER YET, and the whole of its effect waits on one line -
//     case "settings": EnsureDashboardFx(); break;
// in EnsureTabFx (CCP.Avalonia/Views/Windows/MainShellWindow.AmbientFx.cs), which this layer does
// not own. Until that lands the fog behaves exactly as it does today.
//
// STILL NOTES, each with the exact symbol and what it waits on:
//   ApplyVaultCtaBreath  - the vault CTA's 1.0->1.07 scale breath. The element and its
//                          ScaleTransform are in SettingsTabView.axaml (VaultCta), so the motion
//                          itself is one Animation with PlaybackDirection.Alternate. It is blocked
//                          TWICE and neither half is on this head: ChromeAmbientAllowed
//                          (MainWindow.ChromeFx.cs - reduced motion + the performance tier +
//                          window focus), and a per-tab stop, which needs a SwitchTabFx call site
//                          of the ApplyDeeperGlyphDrift(tab) shape in MainShellWindow.AmbientFx.cs.
//                          A Forever clock on a hidden tab is exactly what WPF's gate prevents.
//   ApplyMysteryFx, ApplyMysteryBreath, ApplyMysteryBadgeVisibility, EnsureMysteryTileFx,
//   StartMysteryPop, StopMysteryPop
//                        - the "? box" doorbell (MysteryGlow / MysteryBadge in the same view).
//                          Same two gates as the CTA, plus its own latch: MysteryGiftUnopened
//                          reads AppSettings.DailyGiftLastRevealDate, which IS in Core, but the
//                          breath must stop on the first HOVER of the day and that hover is
//                          RequestMysteryFace's. Breathing without the latch is a doorbell that
//                          never stops ringing.
//   RequestMysteryFace, RunMysteryFlip, OpenMysteryPlate, SettleMysteryPlate, SetMysteryFace,
//   MysteryPointerOverPlate, ReconcileMysteryHover, CancelMysteryReveal
//                        - the hover flip onto today's feature. Needs whatever says WHICH feature
//                          today is (ConditioningControlPanel/Features/); a plate that flips onto
//                          a blank face is worse than a plate that does not flip.
//   ApplyLogoDrift, ApplyLogoSheenTimer, NextLogoSheenGap, SweepLogoSheen,
//   LogoFace_IsVisibleChanged
//                        - the centre wordmark's Ken Burns drift and its occasional sheen. Both
//                          drove NAMED transforms/brushes (LogoDriftScale, LogoSheenSlide) that
//                          AVLN2000 stripped from SettingsTabView.axaml, so each needs LogoFaceLogo
//                          to own a mutable transform first; and both are ambient loops behind
//                          ChromeAmbientAllowed.
//   PrepareRailArtNudge, RefreshRailArtClones, RailItem_MouseEnter, RailItem_MouseLeave,
//   ApplyRailHover, PremiumRailItems
//                        - the premium rail's hover lift and art nudge. WPF clones each chip's
//                          Background brush and puts a RelativeTransform on the CLONE. The chips
//                          (ChipTakeover and its eight siblings) live in SettingsTabView.axaml,
//                          whose hover handlers that view owns, and the gate is
//                          MotionFx.AllowTransitions (ConditioningControlPanel/Services/MotionFx.cs).
//   ApplyRailDotPulse, _pulsingRailDots
//                        - the rail's status dots pulse only while a feature is LIVE, and that
//                          liveness belongs to the feature services. A dot pulsing on a feature
//                          that is off says the feature is running.
//   ApplyBrowserFrameSweep, HookBrowserStatusWatcher, BrowserStatus_TextChanged,
//   ApplyBrowserStatusPulse
//                        - the browser card's frame sweep and status pulse. BrowserFrameStop* was
//                          another AVLN2000 casualty, and TxtBrowserStatus is written by
//                          MainShellWindow.Browser.cs, a stub.
//   DashboardFeatureCards, DashboardSplitCards, ApplyDashboardFxLoops
//                        - the re-evaluation funnel. Its only caller is ApplyChromeFxLoops
//                          (MainShellWindow.ChromeFx.cs, a stub) and every loop it re-evaluates is
//                          one of the notes above.
//
// Members dropped (70 of 72 - MosaicFxIntensity and MosaicFogPuffs live on SettingsTabView now,
// at the same values):
//   private const double LogoDriftScaleTo
//   private const double LogoDriftSeconds
//   private const int LogoDriftFrameRate
//   private const double LogoSheenSeconds
//   private const int LogoSheenMinGapSeconds
//   private const int LogoSheenMaxGapSeconds
//   private const double RailHoverArtNudge
//   private const int RailHoverMs
//   private const double DotPulseMinOpacity
//   private const double DotPulseSeconds
//   private const double BrowserFrameSweepSeconds
//   private const double BrowserFrameCycleSeconds
//   private const double BrowserStatusPulseSeconds
//   private bool _dashboardFxInitialized
//   private DispatcherTimer? _logoSheenTimer
//   private readonly Random _dashboardFxRng
//   private readonly List<Ellipse> _pulsingRailDots
//   private TextBlock? _browserStatusWatched
//   private IEnumerable<FeatureCard> DashboardFeatureCards
//   private IEnumerable<SplitFeatureCard> DashboardSplitCards
//   private IEnumerable<FrameworkElement> PremiumRailItems
//   internal void InitializeDashboardFx(…)
//   private void ApplyDashboardFxLoops(…)
//   private const double VaultCtaBreathTo
//   private const double VaultCtaBreathSeconds
//   private void ApplyVaultCtaBreath(…)
//   private const double MysteryGlowRestOpacity
//   private const double MysteryPopGlowMax
//   private const double MysteryPopBadgeTo
//   private static readonly TimeSpan MysteryPopHalfCycle
//   private const int MysteryFlipHalfMs
//   private const double MysterySkewDeg
//   private int _mysterySpinGen
//   private bool _mysteryShowingReveal
//   private bool _mysteryWantReveal
//   private bool _mysteryVisHooked
//   private bool _mysteryHoverHooked
//   private bool _mysteryFlipping
//   private bool _mysteryPopPlaying
//   private bool MysteryGiftUnopened
//   private void ApplyMysteryFx(…)
//   private void ApplyMysteryBreath(…)
//   private void ApplyMysteryBadgeVisibility(…)
//   internal void EnsureMysteryTileFx(…)
//   private void RequestMysteryFace(…)
//   private void RunMysteryFlip(…)
//   private void OpenMysteryPlate(…)
//   private void SettleMysteryPlate(…)
//   private bool MysteryPointerOverPlate(…)
//   private void ReconcileMysteryHover(…)
//   private void SetMysteryFace(…)
//   private void CancelMysteryReveal(…)
//   private void StartMysteryPop(…)
//   private void StopMysteryPop(…)
//   private void LogoFace_IsVisibleChanged(…)
//   private void ApplyLogoDrift(…)
//   private void ApplyLogoSheenTimer(…)
//   private TimeSpan NextLogoSheenGap(…)
//   private void SweepLogoSheen(…)
//   private readonly List<(…)
//   private void PrepareRailArtNudge(…)
//   private void RefreshRailArtClones(…)
//   private void RailItem_MouseEnter(…)
//   private void RailItem_MouseLeave(…)
//   private void ApplyRailHover(…)
//   private void ApplyRailDotPulse(…)
//   private void ApplyBrowserFrameSweep(…)
//   private void HookBrowserStatusWatcher(…)
//   private void BrowserStatus_TextChanged(…)
//   private void ApplyBrowserStatusPulse(…)

using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.

        private bool _dashboardFxInitialized;

        /// <summary>
        /// Enrols the mosaic's fog in the tab-level park/resume, once. SettingsTabView already
        /// composed it (its own AttachedToVisualTree), so this registers and deliberately does not
        /// start: a second StartLayers would reseed a composition the view already made.
        ///
        /// <para>Its caller is one line in EnsureTabFx - see the header. Called twice is a no-op.</para>
        /// </summary>
        private void EnsureDashboardFx()
        {
            if (_dashboardFxInitialized) return;
            _dashboardFxInitialized = true;
            try
            {
                // FindControl on both hops. Named<T> is the only way into this window (its x:Name
                // fields are never assigned); SettingsTabView does call InitializeComponent, so
                // its own field would work - FindControl is used anyway so the hop does not have
                // to be re-checked if that ctor ever changes.
                var canvas = Named<Tabs.SettingsTabView>("SettingsTab")
                    ?.FindControl<AmbientFxCanvas>("MosaicFx");
                RegisterTabFx("settings", canvas);
            }
            catch (Exception ex) { Log.Warning(ex, "EnsureDashboardFx failed"); }
        }
    }
}
