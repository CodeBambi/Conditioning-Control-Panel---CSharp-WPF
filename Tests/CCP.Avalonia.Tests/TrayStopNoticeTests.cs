using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Owner, 10 Oct 2026: a tray "Stop everything" that is refused says why in one line (the
/// panic key is off, or Strict Lock is on). The refusal itself is unchanged, and a stop that runs
/// says nothing.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class TrayStopNoticeTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public void ARefusedTrayStopSaysWhy_AndAStopThatRunsSaysNothing() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        bool panicWas = s.PanicKeyEnabled, strictWas = s.StrictLockEnabled;
        var noticeWas = MainShellWindow.TrayNotice;
        var said = new List<string>();
        MainShellWindow.TrayNotice = (_, body) => said.Add(body);
        try
        {
            s.PanicKeyEnabled = false;
            s.StrictLockEnabled = false;
            Assert.Equal(BlinkStopGate.Block.NoEscape, MainShellWindow.TrayStopBlock());
            MainShellWindow.StopEverything();
            Assert.Equal(new[] { Loc.Get("tray_stop_refused_panic_off") }, said);

            said.Clear();
            s.PanicKeyEnabled = true;
            s.StrictLockEnabled = true;
            Assert.Equal(BlinkStopGate.Block.StrictLock, MainShellWindow.TrayStopBlock());
            MainShellWindow.StopEverything();
            Assert.Equal(new[] { Loc.Get("tray_stop_refused_strict") }, said);

            said.Clear();
            s.StrictLockEnabled = false;
            MainShellWindow.StopEverything();   // it runs: nothing to say
            Assert.Empty(said);
        }
        finally
        {
            MainShellWindow.TrayNotice = noticeWas;
            s.PanicKeyEnabled = panicWas;
            s.StrictLockEnabled = strictWas;
            CoreSettings.SaveImmediate();
        }
    });

    /// <summary>IA7 (WPF LeashPanicKeyWhilePanicOff): a refused tray stop parks a running leash task,
    /// as the refused key and the refused safe word do. Strict Lock parks nothing; a stop that runs
    /// leaves the park to the panic's own leash surface.</summary>
    [Fact]
    public void ARefusedTrayStopParksALeashTask_AsTheRefusedKeyDoes() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        bool panicWas = s.PanicKeyEnabled, strictWas = s.StrictLockEnabled;
        var (noticeWas, leashedWas, parkWas) = (MainShellWindow.TrayNotice, MainShellWindow.RefusedStopLeashed, MainShellWindow.RefusedStopPark);
        bool leashed = true;
        int parks = 0;
        MainShellWindow.TrayNotice = (_, _) => { };
        MainShellWindow.RefusedStopLeashed = () => leashed;
        MainShellWindow.RefusedStopPark = () => parks++;
        try
        {
            s.PanicKeyEnabled = false;
            s.StrictLockEnabled = false;
            MainShellWindow.StopEverything();          // panic off: refused, the task parks
            Assert.Equal(1, parks);

            leashed = false;
            MainShellWindow.StopEverything();          // no leash: nothing to park
            Assert.Equal(1, parks);

            leashed = true;
            s.PanicKeyEnabled = true;
            s.StrictLockEnabled = true;
            MainShellWindow.StopEverything();          // Strict Lock refusal: as on the key, no park
            Assert.Equal(1, parks);

            s.StrictLockEnabled = false;
            MainShellWindow.StopEverything();          // it runs: the panic's own leash surface parks, not this
            Assert.Equal(1, parks);

            Assert.True(MainShellWindow.ParkLeashOnRefusedStop(BlinkStopGate.Block.Lockdown));
            Assert.Equal(2, parks);
            Assert.False(MainShellWindow.ParkLeashOnRefusedStop(BlinkStopGate.Block.None));
            Assert.False(MainShellWindow.ParkLeashOnRefusedStop(BlinkStopGate.Block.StrictLock));
            Assert.False(MainShellWindow.ParkLeashOnRefusedStop(BlinkStopGate.Block.BlinkTrainer));
            Assert.Equal(2, parks);
        }
        finally
        {
            (MainShellWindow.TrayNotice, MainShellWindow.RefusedStopLeashed, MainShellWindow.RefusedStopPark) = (noticeWas, leashedWas, parkWas);
            s.PanicKeyEnabled = panicWas;
            s.StrictLockEnabled = strictWas;
            CoreSettings.SaveImmediate();
        }
    });

    [Fact]
    public void OnlyTheTwoRefusalsHaveALine()
    {
        Assert.Equal("tray_stop_refused_panic_off", MainShellWindow.TrayStopNoticeKey(BlinkStopGate.Block.NoEscape));
        Assert.Equal("tray_stop_refused_strict", MainShellWindow.TrayStopNoticeKey(BlinkStopGate.Block.StrictLock));
        Assert.Null(MainShellWindow.TrayStopNoticeKey(BlinkStopGate.Block.None));
        Assert.Null(MainShellWindow.TrayStopNoticeKey(BlinkStopGate.Block.Lockdown));       // Lockdown keeps its own message
        Assert.Null(MainShellWindow.TrayStopNoticeKey(BlinkStopGate.Block.BlinkTrainer));
    }

    [Fact]
    public void BothLinesAreInAllTenLanguages_NextToTheTrayItem_InTheHouseVoice()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Core", "Localization", "Languages"))) dir = dir.Parent;
        var files = Directory.GetFiles(Path.Combine(dir!.FullName, "CCP.Core", "Localization", "Languages"), "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            int at = Array.FindIndex(lines, l => l.StartsWith("  \"tray_stop_everything\":", StringComparison.Ordinal));
            Assert.True(at >= 0, file);
            Assert.StartsWith("  \"tray_stop_refused_panic_off\": \"", lines[at + 1]);
            Assert.StartsWith("  \"tray_stop_refused_strict\": \"", lines[at + 2]);
            foreach (var line in new[] { lines[at + 1], lines[at + 2] })
            {
                Assert.DoesNotContain('!', line);
                Assert.DoesNotContain('—', line);
                Assert.DoesNotContain('–', line);
                Assert.DoesNotContain('！', line);
                Assert.Equal(1, line.Count(c => c == '\n' || c == '\\') + 1);   // one line, no escapes
            }
        }
    }
}
