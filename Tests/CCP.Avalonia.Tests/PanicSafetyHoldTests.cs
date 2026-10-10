using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>HC1: every panic route arms Circe's ten-minute Chaster safety hold (WPF MainWindow.xaml.cs:1589).
/// HC2: the tray's Stop everything is never more permissive than the panic key (hard rule 6).
/// Owner, 2026-10-10: the spoken safe word answers to the same rule.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class PanicSafetyHoldTests
{
    private static void WithShell(Action<MainShellWindow, Func<int>, List<string>> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.PanicKeyEnabled, s.PanicKey, s.StrictLockEnabled);
        (s.PanicKeyEnabled, s.PanicKey, s.StrictLockEnabled) = (true, "F8", false);
        var (prevAll, prevHold) = (PanicSurfaces.All, PanicSurfaces.SafetyHold);
        int armed = 0;
        var stopped = new List<string>();
        PanicSurfaces.SafetyHold = () => armed++;
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => stopped.Add("probe")) };
        var shell = new MainShellWindow();
        shell.Show();
        try { body(shell, () => armed, stopped); }
        finally
        {
            (PanicSurfaces.All, PanicSurfaces.SafetyHold) = (prevAll, prevHold);
            shell.Close();
            (s.PanicKeyEnabled, s.PanicKey, s.StrictLockEnabled) = saved;
        }
    });

    [Fact]
    public void EveryPanicRouteArmsTheChasterSafetyHold() => WithShell((shell, armed, _) =>
    {
        foreach (var (name, panic) in new (string, Action)[]
        {
            ("panic key", () => shell.HandlePanicKeyPress(new DateTime(2026, 1, 1))),
            ("tray", MainShellWindow.StopEverything),
            ("voice safe word", shell.VoicePanic),
            ("6-blink stop", () => MainShellWindow.StopAllForRecalibration(shell)),
            ("stop-all route", () => PanicSurfaces.StopAll("test", shell)),
        })
        {
            int before = armed();
            panic();
            Assert.True(armed() > before, $"{name} did not arm the Chaster safety hold");
        }
    });

    [Fact]
    public void AFailingHoldNeverStopsThePanic() => WithShell((shell, _, stopped) =>
    {
        PanicSurfaces.SafetyHold = () => throw new InvalidOperationException("boom");
        shell.HandlePanicKeyPress(new DateTime(2026, 1, 1));
        Assert.Single(stopped);
    });

    [Theory]
    [InlineData(false, false)]   // panic key switched off
    [InlineData(true, true)]     // Strict Lock
    [InlineData(false, true)]
    public void TrayStopIsRefusedWheneverThePanicKeyWouldBe(bool panicKeyOn, bool strict) => WithShell((_, armed, stopped) =>
    {
        var s = CoreSettings.Current;
        (s.PanicKeyEnabled, s.StrictLockEnabled) = (panicKeyOn, strict);
        Assert.NotEqual(BlinkStopGate.Block.None, MainShellWindow.TrayStopBlock());
        MainShellWindow.StopEverything();
        Assert.Empty(stopped);
        Assert.Equal(0, armed());   // a refused stop is not a way out, so it arms nothing
    });

    [Theory]
    [InlineData(false, false)]   // panic key switched off
    [InlineData(true, true)]     // Strict Lock
    [InlineData(false, true)]
    public void TheSafeWordIsRefusedWheneverThePanicKeyWouldBe(bool panicKeyOn, bool strict) => WithShell((shell, armed, stopped) =>
    {
        var s = CoreSettings.Current;
        (s.PanicKeyEnabled, s.StrictLockEnabled) = (panicKeyOn, strict);
        Assert.NotEqual(BlinkStopGate.Block.None, MainShellWindow.VoiceStopBlock());
        Assert.Equal(MainShellWindow.TrayStopBlock(), MainShellWindow.VoiceStopBlock());   // one rule, not a copy
        shell.VoicePanic();
        Assert.Empty(stopped);
        Assert.Equal(0, armed());   // refused: nothing stopped, no Chaster safety hold
    });

    [Fact]
    public void TheSafeWordIsRefusedUnderLockdown() => WithShell((shell, armed, stopped) =>
    {
        using var ld = ConditioningControlPanel.Services.LockdownService.Current = new ConditioningControlPanel.Services.LockdownService();
        try
        {
            ld.Activate(TimeSpan.FromMinutes(30));
            Assert.Equal(BlinkStopGate.Block.Lockdown, MainShellWindow.VoiceStopBlock());
            shell.VoicePanic();
            Assert.Empty(stopped);
            Assert.Equal(0, armed());
        }
        finally { ConditioningControlPanel.Services.LockdownService.Current = null; }
    });

    [Fact]
    public void TheSafeWordRunsWhenThePanicKeyWould() => WithShell((shell, armed, stopped) =>
    {
        Assert.Equal(BlinkStopGate.Block.None, MainShellWindow.VoiceStopBlock());
        shell.VoicePanic();
        Assert.Single(stopped);
        Assert.Equal(1, armed());
    });

    [Fact]
    public void TrayStopRunsWhenThePanicKeyWould() => WithShell((_, armed, stopped) =>
    {
        Assert.Equal(BlinkStopGate.Block.None, MainShellWindow.TrayStopBlock());
        MainShellWindow.StopEverything();
        Assert.Single(stopped);
        Assert.Equal(1, armed());
    });

    [Fact]
    public void AKeyPressWithThePanicKeyOffArmsNothing() => WithShell((shell, armed, stopped) =>
    {
        CoreSettings.Current.PanicKeyEnabled = false;
        shell.HandlePanicKeyPress(new DateTime(2026, 1, 1));
        Assert.Empty(stopped);
        Assert.Equal(0, armed());
    });
}
