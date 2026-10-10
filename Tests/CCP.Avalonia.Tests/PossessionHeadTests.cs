using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>HB13: the head's half of Possession. The director and its rules are Core
/// (PossessionDirectorTests, PossessionDeckTests there); here the shell draws the edge pulse with an IN
/// and an OUT, registers only display controls, gives a nudged control back exactly, panic clears it
/// all at once, the Lockdown readout follows the rung, and a Dose keeper start is not a session.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class PossessionHeadTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public void TheShellPulsesItsEdgeInAndOut_RegistersOnlyDisplayControls_AndPanicClearsEverything() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var saved = (s.LockdownPhotosafe, s.MicConsentGiven, s.TotalSessions);
        var prevDirector = PossessionDirector.Current;
        s.MicConsentGiven = false;
        s.LockdownPhotosafe = false;
        var shell = new MainShellWindow();
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var host = MainShellWindow.PossessionHostFor(() => shell);

            // The edge pulse: rises to the strength, falls to nothing, then leaves the tree.
            host.EdgePulse(0.8);
            var pulse = Assert.IsType<Border>(shell.PossessionPulse);
            Assert.False(pulse.IsHitTestVisible);
            Assert.Equal(0, pulse.Opacity);
            shell.PaintPossessionPulse(60);
            Assert.Equal(0.4, pulse.Opacity, 3);
            shell.PaintPossessionPulse(120);
            Assert.Equal(0.8, pulse.Opacity, 3);
            shell.PaintPossessionPulse(410);
            Assert.Equal(0.4, pulse.Opacity, 3);
            shell.PaintPossessionPulse(700);
            Assert.Null(shell.PossessionPulse);
            Assert.Null(pulse.Parent);

            // Photosafe: half the strength, never a blink.
            s.LockdownPhotosafe = true;
            host.EdgePulse(1.0);
            shell.PaintPossessionPulse(300);
            Assert.Equal(0.5, shell.PossessionPulse!.Opacity, 3);
            s.LockdownPhotosafe = false;

            // The registry: display controls only, and never a room the user has to be able to leave.
            var targets = host.Targets();
            Assert.NotEmpty(targets);
            Assert.All(targets, t =>
            {
                // k22 (reach = WPF): the rail doors and Start are enrolled; a safety control never is.
                Assert.False(PossessionOffLimits.IsSafetyName(t.Key));
                Assert.False(PossessionTree.IsSafety((Control)t.Element));
                Assert.False(t.Element is TextBox or Slider);
            });
            Assert.Same(targets[0], host.Targets()[0]);   // cached: a cooldown survives the next read

            // A nudge borrows the transform and gives it back exactly, at once when asked to.
            var victim = targets.First(t => t.Role == PossessionRole.Label);
            var control = (Control)victim.Element;
            var prior = control.RenderTransform;
            var nudge = new PossessionNudge();
            var ctx = new PossessionContext
            {
                Host = host, Rung = PossessionRung.Settle, Intensity = PossessionIntensity.Eerie, Photosafe = false,
                Rng = new Random(3), ElapsedFraction = 0, Remaining = TimeSpan.FromMinutes(20), Name = (_, _) => { },
            };
            nudge.ApplyAsync(ctx, victim, default).GetAwaiter().GetResult();
            Assert.True(nudge.IsLive);
            Assert.IsType<TranslateTransform>(control.RenderTransform);
            nudge.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.False(nudge.IsLive);
            Assert.Same(prior, control.RenderTransform);
            nudge.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();   // safe twice

            // Panic: the pulse is gone the same call, with or without a director.
            PossessionDirector.Current = null;
            MainShellWindow.StopPossessionForPanic(shell);
            Assert.Null(shell.PossessionPulse);
            Assert.Contains(PanicSurfaces.All, x => x.Id == "possession");

            // Row 1: the Dose keeper's start is a system start, not a session the user chose.
            var sessions = s.TotalSessions;
            MainShellWindow.LockdownDoseHostFor(() => shell).StartEngine();
            Dispatcher.UIThread.RunJobs();
            Assert.True(CoreEngine.IsRunning);
            Assert.Equal(sessions, s.TotalSessions);
            MainShellWindow.StopEngine();
            shell.StartEngine();                      // a press counts
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(sessions + 1, s.TotalSessions);
        }
        finally
        {
            MainShellWindow.StopEngine();
            PossessionDirector.Current = prevDirector;
            (s.LockdownPhotosafe, s.MicConsentGiven, s.TotalSessions) = saved;
            CoreSettings.SaveImmediate();
            shell.RequestExit();
        }
    });

    [Fact]
    public void TheLockdownReadoutFollowsTheRung_AndHidesWithPossessionOffOrTheLockdownOver() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var saved = (s.LockdownPossessionEnabled, s.LockdownPossessionIntensity, s.LockdownForceStrictLock, s.LockdownDisablePanicKey,
            s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownDoseKeeperEnabled);
        var prevDirector = PossessionDirector.Current;
        var prevLockdown = LockdownService.Current;
        s.LockdownForceStrictLock = s.LockdownDisablePanicKey = s.LockdownDoseKeeperEnabled = false;
        s.LockdownPossessionEnabled = true;
        s.LockdownPossessionIntensity = (int)PossessionIntensity.Eerie;
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var ld = LockdownService.Current = new LockdownService { UtcNow = () => now };
        var director = PossessionDirector.Current = new PossessionDirector(ld, Array.Empty<IPossessionEffect>(), new PossessionHost());
        var tab = new LockdownTabView();
        var win = new Window { Content = tab, Width = 1200, Height = 1000 };
        win.Show();
        try
        {
            Assert.False(tab.FindControl<TextBlock>("TxtPossessionRung")!.IsVisible);

            ld.Activate(TimeSpan.FromMinutes(20));
            Dispatcher.UIThread.RunJobs();
            var word = tab.FindControl<TextBlock>("TxtPossessionRung")!;
            var pips = tab.FindControl<StackPanel>("PossessionPips")!;
            Assert.True(word.IsVisible && pips.IsVisible);
            Assert.Contains(ConditioningControlPanel.Localization.Loc.Get("lockdown_poss_rung_0"), word.Text);
            Assert.Equal(1, Lit(pips));

            now = now.AddMinutes(10);                // half way: Melt
            director.Tick(TimeSpan.FromMinutes(10));
            tab.PaintPossessionReadout();
            Assert.Equal(PossessionRung.Melt, director.CurrentRung);
            Assert.Contains(ConditioningControlPanel.Localization.Loc.Get("lockdown_poss_rung_2"), word.Text);
            Assert.Equal(3, Lit(pips));

            s.LockdownPossessionEnabled = false;     // switched off: the row goes
            tab.PaintPossessionReadout();
            Assert.False(word.IsVisible || pips.IsVisible);
            s.LockdownPossessionEnabled = true;

            ld.Deactivate();
            Dispatcher.UIThread.RunJobs();
            Assert.False(word.IsVisible || pips.IsVisible);
            Assert.Equal("", word.Text);
            Assert.Equal(0, Lit(pips));
        }
        finally
        {
            if (ld.IsActive) ld.Deactivate();
            director.Dispose();
            ld.Dispose();
            PossessionDirector.Current = prevDirector;
            LockdownService.Current = prevLockdown;
            (s.LockdownPossessionEnabled, s.LockdownPossessionIntensity, s.LockdownForceStrictLock, s.LockdownDisablePanicKey,
                s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownDoseKeeperEnabled) = saved;
            CoreSettings.SaveImmediate();
            win.Close();
        }
    });

    private static int Lit(StackPanel pips) => pips.Children.OfType<Border>()
        .Count(b => b.Background is SolidColorBrush { Color: var c } && c == MainShellWindow.PossessionEmber);
}
