using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The four WPF FX decorators on their real hosts, from the shell: PremiumGateFx on a
/// premium gate (ShowTab -> RefreshEntitlementVeils), the CTA's CardSheenAdorner, the Studio
/// active tile's PerimeterCometAdorner (ShowTab("studio") -> OnTabShown) and the Presets session
/// row's RowSweepAdorner (a real pointer move). Time is a stepped clock; frames are Tick().</summary>
public sealed class GateFxAdornerTests
{
    [Fact]
    public Task PremiumGateWearsFogGlowAndCtaSheen_AndParksWhenHidden() => Run(shell =>
    {
        shell.ShowTab("lockdown");                                      // no daily-free key: always gated
        Settle(shell);
        var gate = shell.Named<Control>("LockdownTab")!.FindControl<Border>("LockdownGate")!;
        Assert.True(gate.IsVisible);                                   // free tier: padlocked
        var fx = PremiumGateFx.For(gate)!;                             // decorated by the shell's RefreshPremiumGate
        Assert.Same(fx.Fog, ((Grid)gate.Child!).Children[0]);           // fog sits behind the content
        Assert.Equal(AmbientFxLayers.FogDrift, fx.Fog.Layers);
        Assert.True(fx.IsRunning);
        Assert.True(fx.HasLockGlow);

        var sheen = fx.CtaSheen!;
        Assert.True(sheen.IsTicking);
        double rest = sheen.BandOffset;
        Clock.Now += TimeSpan.FromSeconds(0.65).Ticks;                  // mid-travel
        sheen.Tick();
        Assert.True(sheen.BandOffset > rest + 0.5, $"band at {sheen.BandOffset}");
        Clock.Now += TimeSpan.FromSeconds(2).Ticks;                     // into the rest
        sheen.Tick();
        Assert.Equal(rest, sheen.BandOffset, 6);

        shell.ShowTab("presets");                                       // the tab hides, the clocks park
        Settle(shell);
        Assert.False(fx.IsRunning);
        Assert.False(fx.HasLockGlow);
        Assert.Null(fx.CtaSheen);
        Assert.False(sheen.IsTicking);

        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;         // reduced motion: nothing starts
        shell.ShowTab("lockdown");
        Settle(shell);
        Assert.False(fx.IsRunning);
        Assert.Null(fx.CtaSheen);
    });

    [Fact]
    public Task StudioActiveTileWearsALappingComet_ThatMovesParksAndDegrades() => Run(shell =>
    {
        shell.ShowTab("studio");
        Settle(shell);
        var rack = shell.StudioRack!;
        var comet = rack.CometFor(rack.SelectedRackKey)!;
        Assert.True(comet.IsLapping);
        Clock.Now += TimeSpan.FromSeconds(1).Ticks;                     // a quarter of the 4s lap
        comet.Tick();
        Assert.Equal(0.25, comet.Phase, 3);

        string other = rack.SelectedRackKey == "spiral" ? "flash" : "spiral";
        rack.FocusRackEntry(other);
        Settle(shell);
        Assert.Null(rack.CometFor(other == "spiral" ? "flash" : "spiral"));
        var moved = rack.CometFor(other)!;
        Assert.True(moved.IsLapping);
        Assert.False(comet.IsTicking);                                  // the old one is off its layer

        shell.ShowTab("awareness");                                     // the door closes: the clock parks
        Settle(shell);
        Assert.False(moved.IsTicking);
        Assert.False(moved.IsLapping);

        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;         // degrade, never vanish
        shell.ShowTab("studio");
        Settle(shell);
        Assert.Same(moved, rack.CometFor(other));
        moved.Start();                                                  // what a fresh attach does
        Assert.False(moved.IsLapping);
        Assert.True(moved.IsVisible);
    });

    [Fact]
    public Task SessionRowSweepsInOnHoverAndOutOnLeave() => Run(shell =>
    {
        shell.ShowTab("presets");
        Settle(shell);
        var tab = shell.Named<PresetsTabView>("PresetsTab")!;
        var row = tab.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().First();
        var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), shell)!.Value;

        shell.MouseMove(centre);
        Settle(shell);
        var sweep = tab.RowSweep!;
        Assert.True(sweep.IsTicking);
        Clock.Now += TimeSpan.FromMilliseconds(110).Ticks;              // half of 220ms, eased out
        sweep.Tick();
        Assert.Equal(0.75, sweep.Progress, 3);
        Clock.Now += TimeSpan.FromMilliseconds(200).Ticks;
        sweep.Tick();
        Assert.Equal(1.0, sweep.Progress, 3);
        Assert.False(sweep.IsTicking);                                  // no clock at rest

        shell.MouseMove(new Point(2, 2));                               // leave
        Settle(shell);
        Clock.Now += TimeSpan.FromMilliseconds(170).Ticks;
        sweep.Tick();
        Assert.Equal(0.0, sweep.Progress, 3);

        CoreSettings.Current.MotionLevel = MotionLevel.Off;             // snaps, still lands
        shell.MouseMove(centre);
        Settle(shell);
        Assert.Equal(1.0, tab.RowSweep!.Progress, 3);
        Assert.False(tab.RowSweep.IsTicking);
    });

    private static readonly SteppedClock Clock = new();

    private static void Settle(Window w) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }

    private static Task Run(Action<MainShellWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.MotionLevel, s.PerformanceMode);
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        FxAdorner.Time = Clock;
        MainShellWindow? shell = null;
        try
        {
            (s.MotionLevel, s.PerformanceMode) = (MotionLevel.Full, false);
            CoreEntitlement.HasPremiumProvider = null;                  // free tier: gates up
            shell = new MainShellWindow { Width = 1400, Height = 900 };
            shell.Show(); Settle(shell);
            body(shell);
        }
        finally
        {
            shell?.Close();
            FxAdorner.Time = TimeProvider.System;
            CoreEntitlement.HasPremiumProvider = oldPremium;
            (s.MotionLevel, s.PerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
