// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.ChromeFx.cs (914 lines) -
// PR-1 of the FX overhaul, the shell's own chrome: tab transitions, the nav rail's glow and icon
// nudge, the START button sheen and the XP meter.
//
// ONE member is live, and it is the one MainShellWindow.axaml actually names:
// StartSheenHost_SizeChanged. Not decoration - it is the CLIP that keeps the sheen band inside the
// START button's rounded rect, and without it the band pokes out of the corners the moment
// anything sweeps it. Fully portable: WPF's RectangleGeometry(rect, 8, 8) is spelled the same in
// Avalonia 12 (Controls/TierFxBorder.cs:673 makes the same call).
//
// NOTHING ELSE IN THIS FILE HAS AN ENTRY POINT ON THIS HEAD - the honest reason the rest is a note
// and not a shortage of Avalonia. WPF's whole chrome-FX lifecycle hangs off InitializeChromeFx(),
// called from MainWindow's constructor; this window's three lifecycle overrides are already taken
// (OnLoaded -> .Marquee.cs, OnAttachedToVisualTree -> .NavRail.cs, OnOpened -> .WorkAreaFit.cs)
// and the constructor lives in MainShellWindow.axaml.cs. A fourth override is a compile error, not
// a design choice. So the loops below would be dead code a render cannot disprove - exactly the
// failure the port has already hit once, where three animations threw into a catch and the badge
// was inert and silent about it. When a chrome-FX init hook exists, restore in this order
// (62 members):
//
//   1. the gate. ChromeAmbientAllowed is `_chromeFxWindowActive && MotionFx.AllowAmbientLoops`;
//      Services/MotionFx.cs is not in Core, so the portable half is IsActive && not-minimised -
//      exactly MainShellWindow.TabFxTakeoverLabStatus.cs's Pr4aAmbientAllowed. Reuse it.
//   2. the sheens - SweepSheen, SweepStartSheen, SweepBannerSheen, ApplyXpSheen, ParkSheen,
//      _startSheenTimer, _lastBannerSheenUtc, and the Start/Banner/Xp sheen constants. One-shot
//      TranslateTransform slides plus an opacity fade: an Avalonia keyframe Animation cancelled
//      through a CancellationTokenSource, the pattern .DeeperFx.cs already uses. The band, its
//      SkewTransform and its FxGlowColor stop are already in the axaml.
//   3. the breathing glows - ApplyNavGlowBreath, ApplyStartButtonGlow and their six
//      Min/MaxOpacity/BreathSeconds constants. PlaybackDirection.Alternate +
//      IterationCount.Infinite over the effect's Opacity, per CLAUDE.md.
//   4. the tab transition and card stagger - AnimateTabIn, SlideTabIn, FadeOutgoingTab,
//      CollapseOutgoingTab, EnsureTabTranslate, ResetTabSlide, StaggerTabCards,
//      StaggerCleanupTimer_Tick, CancelStaggerCleanup, FindStaggerTargets, _pendingTabKey,
//      _activeTabKey, _activeTabElement, _staggerCleanupTimer, _staggeredElements, TabFadeOutMs/
//      TabFadeInMs/TabSlidePx. Caller is ShowTab, which today snaps panels on and off and says so.
//   5. the nav rail's hover and active state - NavButtons, NavButton_MouseEnter/MouseLeave,
//      NudgeNavIcon, EnsureIconScale, NavButtonForTab, ApplyNavActiveGlow, SetNavIndicator,
//      _navGlowButton/_navGlowDoor/_navActiveBar, NavIconHoverScale/NavIconHoverMs.
//      .TabNavigation.cs already records that nothing highlights the active rail item;
//      ApplyNavActiveGlow is that missing piece.
//   6. the XP meter - AnimateXpDisplay, FillXpBarTo, _lastXpShown, _lastXpLevelShown. A width
//      tween plus a number roll, portable on its own; its caller is MainWindow.Progression's XP
//      refresh, which is not on this head.
//
// Gone for good, not deferred: _chromeFxWindowActive / OnChromeFxWindowStateish's three
// subscriptions are re-expressed by Pr4aAmbientAllowed and AmbientFxCanvas.Evaluate().

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Corner radius of the START button, and therefore of the sheen host's clip.
        /// Must MATCH the button's own rounding or the band's corners show through.</summary>
        private const double StartSheenCornerRadius = 8;

        /// <summary>
        /// Clips the START sheen host to the button's rounded rect, so the band cannot poke out of
        /// the corners as it sweeps. Named by MainShellWindow.axaml:2682 and driven by layout, so
        /// this is live whether or not the sweep itself is ever restored - and it has to be: the
        /// host is a sibling overlay sharing the button's cell, not button content, so nothing
        /// else clips it.
        /// </summary>
        private void StartSheenHost_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            try
            {
                if (sender is not Control host) return;
                var size = e.NewSize;
                if (size.Width <= 0 || size.Height <= 0) { host.Clip = null; return; }
                host.Clip = new RectangleGeometry(
                    new Rect(0, 0, size.Width, size.Height),
                    StartSheenCornerRadius, StartSheenCornerRadius);
            }
            catch (Exception ex) { Log.Debug("StartSheenHost_SizeChanged: {E}", ex.Message); }
        }
    }
}
