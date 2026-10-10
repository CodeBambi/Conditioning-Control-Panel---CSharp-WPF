using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Nav;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// WPF 7.1.5 SectionTabStrip.Fx.cs on the port: the hover lift and scale by motion level, the
/// active pill keeping still, the choose burst, and the 9 s sheen clock following visibility and
/// the motion level. The look itself is a desk check; these pin what a headless run can see.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class SectionTabStripFxTests
{
    private static async Task WithStrip(Func<SectionTabStrip, Window, Task> body)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var strip = new SectionTabStrip { MotionOverride = MotionLevel.Full, AmbientTierOverride = true };
            var w = new Window { Width = 1200, Height = 200, Content = strip };
            try
            {
                w.Show();
                strip.Show(NavSections.Studio, NavSections.DefaultTab(NavSections.Studio));
                Dispatcher.UIThread.RunJobs();
                await body(strip, w);
            }
            finally { w.Close(); }
        });
    }

    private static string Idle(SectionTabStrip s) => s.PillKeys.First(k => k != s.ActivePillKey);

    [Fact]
    public async Task HoverLiftsAnIdlePillByLevel_AndNeverTheActiveOne()
    {
        await WithStrip((strip, _) =>
        {
            string idle = Idle(strip), active = strip.ActivePillKey!;

            strip.FxHoverForTests(idle, true);
            Assert.Equal(-NavStripFxRules.HoverLiftPx, strip.HoverStateOf(idle).Lift, 3);
            Assert.Equal(NavStripFxRules.HoverScale, strip.HoverStateOf(idle).Scale, 3);
            strip.FxHoverForTests(idle, false);                       // the OUT
            Assert.Equal(0, strip.HoverStateOf(idle).Lift, 3);
            Assert.Equal(1, strip.HoverStateOf(idle).Scale, 3);

            strip.FxHoverForTests(active, true);                      // rides the fill: stays put
            Assert.Equal(0, strip.HoverStateOf(active).Lift, 3);
            Assert.Equal(1, strip.HoverStateOf(active).Scale, 3);

            strip.MotionOverride = MotionLevel.Reduced;               // the lift only
            strip.FxHoverForTests(idle, true);
            Assert.Equal(-NavStripFxRules.HoverLiftPx, strip.HoverStateOf(idle).Lift, 3);
            Assert.Equal(1, strip.HoverStateOf(idle).Scale, 3);

            strip.MotionOverride = MotionLevel.Off;                   // settles what Reduced left
            strip.FxHoverForTests(idle, true);
            Assert.Equal(0, strip.HoverStateOf(idle).Lift, 3);

            // FX law: the FX layer and the ring that moves wear no Effect (a locked pill's tier
            // sign keeps its own small static shadow, which is TierBadge's and not under a loop).
            Assert.DoesNotContain(strip.FxLayer!.GetVisualDescendants().OfType<Visual>().Append(strip.FxLayer!), v => v.Effect != null);
            Assert.Null(strip.RingOf(idle)!.Effect);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ChoosingAPillAsksForABurst_ExceptAtOff()
    {
        await WithStrip((strip, _) =>
        {
            Assert.NotNull(strip.FxSparks);
            Assert.False(strip.FxLayer!.IsHitTestVisible);
            int before = strip.BurstsFired;
            strip.ChooseForTests(Idle(strip));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before + 1, strip.BurstsFired);

            strip.MotionOverride = MotionLevel.Off;
            strip.ChooseForTests(Idle(strip));
            Assert.Equal(before + 1, strip.BurstsFired);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task SheenClockFollowsVisibilityAndLevel_AndASweepCleansUp()
    {
        await WithStrip(async (strip, _) =>
        {
            strip.VisibleOverride = true;
            strip.UpdateSheenClock();
            Assert.True(strip.SheenClockRunning);

            Assert.True(strip.SweepSheen());
            Assert.True(strip.SheenShown);
            Assert.InRange(strip.SheenBandOpacity, 0, 1);
            for (int i = 0; i < 80 && strip.SheenShown; i++)
            {
                await Task.Delay(16);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.False(strip.SheenShown);                           // the host dropped itself
            Assert.Equal(1, strip.SheensRun);

            strip.MotionOverride = MotionLevel.Reduced;               // Full only
            strip.UpdateSheenClock();
            Assert.False(strip.SheenClockRunning);

            strip.MotionOverride = MotionLevel.Full;
            strip.UpdateSheenClock();
            Assert.True(strip.SheenClockRunning);
            strip.VisibleOverride = false;                            // off screen: parked
            strip.UpdateSheenClock();
            Assert.False(strip.SheenClockRunning);

            strip.VisibleOverride = true;
            strip.AmbientTierOverride = false;                        // performance tier: parked
            strip.UpdateSheenClock();
            Assert.False(strip.SheenClockRunning);
        });
    }
    /// <summary>WPF TiltNavCoin: an idle rail coin leans toward the pointer by DepthRules.TiltDegrees
    /// at most, a lit coin stays level, and the gate is the shared TiltAllowed rule.</summary>
    [Fact]
    public async Task AnIdleRailCoinLeansTowardThePointer_ALitOneStaysLevel()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var w = new global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
            try
            {
                w.Show();
                w.ShowTab("presets");
                Dispatcher.UIThread.RunJobs();
                string lit = w.LitNavSection!;
                string idle = lit == NavSections.Social ? NavSections.You : NavSections.Social;
                bool tilts = NavRailRules.CoinTilts(false, global::ConditioningControlPanel.Avalonia.Controls.NavRail.NavPaint.Level,
                    global::ConditioningControlPanel.Models.PerformanceTier.Quality);
                double lean = w.NavCoinTiltForTests(idle, 1, -1);
                if (tilts && !double.IsNaN(lean) && lean != 0)
                    Assert.Equal(global::ConditioningControlPanel.Depth.DepthRules.TiltDegrees, lean, 3);
                Assert.InRange(System.Math.Abs(double.IsNaN(lean) ? 0 : lean), 0, global::ConditioningControlPanel.Depth.DepthRules.TiltDegrees);
                Assert.Equal(0, w.NavCoinTiltForTests(lit, 1, -1), 3);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF PaintSectionWash: the page wash travels to the new section hue and lands on it.</summary>
    [Fact]
    public async Task TheSectionWashTravelsToTheNewHue_AndLandsOnIt()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var w = new global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
            try
            {
                w.Show();
                w.ShowTab("presets");
                Dispatcher.UIThread.RunJobs();
                for (int i = 0; i < 60 && w.SectionWashTweening; i++) { await Task.Delay(16); Dispatcher.UIThread.RunJobs(); }
                uint first = w.SectionWashHueShown;
                Assert.Equal(NavStripRules.Accent(w.LitNavSection), first);

                w.ShowTab("quests");
                Dispatcher.UIThread.RunJobs();
                uint target = NavStripRules.Accent(w.LitNavSection);
                Assert.NotEqual(first, target);
                for (int i = 0; i < 60 && w.SectionWashTweening; i++) { await Task.Delay(16); Dispatcher.UIThread.RunJobs(); }
                Assert.False(w.SectionWashTweening);
                Assert.Equal(target, w.SectionWashHueShown);
            }
            finally { w.Close(); }
        });
    }

    /// <summary>WPF NavPillHelpTests: every pill with a help line wears a "?" badge that is a
    /// sibling of the pill (never inside it) and leaves the row its size; a pill that opens its
    /// own window asks first and only "Open it" raises the request.</summary>
    [Fact]
    public async Task HelpBadgesSitBesideThePills_AndAWindowPillAsksFirst()
    {
        await WithStrip((strip, _) =>
        {
            string key = strip.PillKeys.First();
            var badge = strip.HelpBadgeFor(key);
            Assert.NotNull(badge);
            var pill = strip.PillFor(key)!;
            Assert.Same(pill.Parent, badge!.Parent);                        // siblings in one host
            Assert.DoesNotContain(badge, pill.GetVisualDescendants());
            Assert.Equal(pill.Bounds.Width, ((Control)pill.Parent!).Bounds.Width, 1);
            Assert.False(string.IsNullOrEmpty(SectionTabStrip.HelpText(NavStripRules.Pills(NavSections.Studio).First(t => t.Key == key))));
            Assert.NotNull(ToolTip.GetTip(badge));

            var window = NavSections.AllTabs.FirstOrDefault(t => t.Kind == NavTabKind.Window);
            if (window != null && NavSections.SectionForTab(window.Key) is { } section
                && NavStripRules.Pills(section).Any(t => t.Key == window.Key))
            {
                strip.Show(section, NavSections.DefaultTab(section));
                Dispatcher.UIThread.RunJobs();
                int asked = 0;
                strip.TabRequested += t => { if (t.Key == window.Key) asked++; };
                strip.ChooseForTests(window.Key);
                Assert.Equal(window.Key, strip.ConfirmingKey);
                Assert.Equal(0, asked);
                strip.AnswerConfirmForTests(open: false);
                Assert.Null(strip.ConfirmingKey);
                Assert.Equal(0, asked);
                strip.ChooseForTests(window.Key);
                strip.AnswerConfirmForTests(open: true);
                Assert.Equal(1, asked);
                Assert.Null(strip.ConfirmingKey);
            }
            return Task.CompletedTask;
        });
    }
}
