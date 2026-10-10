using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>hunt3 IC8: one panic key press arms the safety hold ONCE (EMI heard "panicPressed" twice),
/// a press a lock card consumes still arms it, and every route drops EMI's own spiral hold with her
/// rain, never somebody else's.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class PanicHoldOnceTests
{
    private sealed record Probe(MainShellWindow Shell, Func<int> Holds, Func<int> Rains, Func<int> Spirals, List<string> Stopped);

    private static void WithShell(Action<Probe> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.PanicKeyEnabled, s.PanicKey, s.StrictLockEnabled, s.KeywordTriggersEnabled, s.ScreenOcrEnabled, s.KeywordTriggersOffByPanic);
        var (prevAll, prevHold, prevRain, prevSpiral, prevSources, prevFlag) =
            (PanicSurfaces.All, PanicSurfaces.SafetyHold, PanicSurfaces.StopEmiRain, PanicSurfaces.ReleaseEmiSpiral,
             PanicSurfaces.StopKeywordSources, PanicSurfaces.KeywordMasterOffByPanic);
        (s.PanicKeyEnabled, s.PanicKey, s.StrictLockEnabled) = (true, "F8", false);
        (s.KeywordTriggersEnabled, s.ScreenOcrEnabled) = (false, false);
        int holds = 0, rains = 0, spirals = 0;
        var stopped = new List<string>();
        PanicSurfaces.SafetyHold = () => holds++;
        PanicSurfaces.StopEmiRain = () => rains++;
        PanicSurfaces.ReleaseEmiSpiral = () => spirals++;
        PanicSurfaces.StopKeywordSources = () => { };
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => stopped.Add("probe")) };
        var shell = new MainShellWindow();
        shell.Show();
        try { body(new Probe(shell, () => holds, () => rains, () => spirals, stopped)); }
        finally
        {
            LockCardWindow.ForceCloseAll();
            (PanicSurfaces.All, PanicSurfaces.SafetyHold, PanicSurfaces.StopEmiRain, PanicSurfaces.ReleaseEmiSpiral,
             PanicSurfaces.StopKeywordSources, PanicSurfaces.KeywordMasterOffByPanic) = (prevAll, prevHold, prevRain, prevSpiral, prevSources, prevFlag);
            shell.Close();
            (s.PanicKeyEnabled, s.PanicKey, s.StrictLockEnabled, s.KeywordTriggersEnabled, s.ScreenOcrEnabled, s.KeywordTriggersOffByPanic) = saved;
            CoreSettings.SaveImmediate();
        }
    });

    [Fact]
    public void OneKeyPress_ArmsTheHoldOnce_AndStillRunsTheStopPass() => WithShell(p =>
    {
        p.Shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0));
        Assert.Equal(1, p.Holds());
        Assert.Equal(1, p.Rains());
        Assert.Equal(1, p.Spirals());
        Assert.Equal(new[] { "probe" }, p.Stopped);
    });

    [Fact]
    public void APressALockCardConsumes_StillArmsTheHold_Once() => WithShell(p =>
    {
        LockCardWindow.ShowOnAllMonitors("obey", 1, strictMode: false, isTest: true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(LockCardWindow.IsAnyOpen());
        p.Shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0));
        Assert.Equal(1, p.Holds());
        Assert.Equal(1, p.Rains());
        Assert.Equal(1, p.Spirals());
        Assert.Empty(p.Stopped);   // the card took the press: no stop pass
    });

    [Fact]
    public void EveryOtherRoute_StillArmsThroughStopAll_Once() => WithShell(p =>
    {
        PanicSurfaces.StopAll("tray", p.Shell);
        Assert.Equal(1, p.Holds());
        Assert.Equal(1, p.Spirals());
        PanicSurfaces.StopAll("safe word", p.Shell);
        Assert.Equal(2, p.Holds());
    });

    [Fact]
    public void ArmingDropsHerSpiralHold_AndNobodyElses() => AvaloniaTestDispatcher.Run(() =>
    {
        var (prevHold, prevRain) = (PanicSurfaces.SafetyHold, PanicSurfaces.StopEmiRain);
        PanicSurfaces.SafetyHold = () => { };
        PanicSurfaces.StopEmiRain = () => { };
        try
        {
            SpiralOverlay.ReleaseAllHolds();
            SpiralOverlay.Hold(null, EmiDeskService.EmiOwner, new SpiralHold(0.5), 6000);
            SpiralOverlay.Hold(null, SpiralOverlay.DeeperOwner, new SpiralHold(0.4));
            PanicSurfaces.ArmSafetyHold();
            Assert.False(SpiralOverlay.IsHeldBy(EmiDeskService.EmiOwner));
            Assert.True(SpiralOverlay.IsHeldBy(SpiralOverlay.DeeperOwner));   // the stop pass takes that one
        }
        finally
        {
            SpiralOverlay.ReleaseAllHolds();
            (PanicSurfaces.SafetyHold, PanicSurfaces.StopEmiRain) = (prevHold, prevRain);
        }
    });
}
