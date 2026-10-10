using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Owner, 2026-10-10 ("stop until re-enabled"): every accepted panic press switches keyword
/// triggers off (the master, and the screen read as a saved setting), stops the reader and takes the
/// highlights down; nothing restarts them; the Awareness tab shows the switches off and says why;
/// the user switches them back on as usual. A refused press changes nothing. No test here reads a
/// screen or opens a microphone (ScreenOcrService.Disabled, probe surfaces).</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class PanicKeywordOffTests
{
    private sealed record Probe(MainShellWindow Shell, Func<int> SourcesStopped, Func<int> Repaints, List<string> Stopped);

    private static void WithShell(Action<Probe> body, bool realSources = false) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.PanicKeyEnabled, s.PanicKey, s.StrictLockEnabled, s.KeywordTriggersEnabled, s.ScreenOcrEnabled, s.KeywordTriggersOffByPanic);
        var (prevAll, prevHold, prevStop, prevFlag, prevPremium) =
            (PanicSurfaces.All, PanicSurfaces.SafetyHold, PanicSurfaces.StopKeywordSources, PanicSurfaces.KeywordMasterOffByPanic, CoreEntitlement.HasPremiumProvider);
        (s.PanicKeyEnabled, s.PanicKey, s.StrictLockEnabled) = (true, "F8", false);
        (s.KeywordTriggersEnabled, s.ScreenOcrEnabled, s.KeywordTriggersOffByPanic) = (true, true, false);
        PanicSurfaces.KeywordMasterOffByPanic = false;
        int sources = 0, repaints = 0;
        var stopped = new List<string>();
        PanicSurfaces.SafetyHold = () => { };
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => stopped.Add("probe")) };
        if (!realSources) PanicSurfaces.StopKeywordSources = () => sources++;
        Action onOff = () => repaints++;
        PanicSurfaces.KeywordTriggersSwitchedOff += onOff;
        var shell = new MainShellWindow();
        shell.Show();
        try { body(new Probe(shell, () => sources, () => repaints, stopped)); }
        finally
        {
            PanicSurfaces.KeywordTriggersSwitchedOff -= onOff;
            (PanicSurfaces.All, PanicSurfaces.SafetyHold, PanicSurfaces.StopKeywordSources, PanicSurfaces.KeywordMasterOffByPanic) = (prevAll, prevHold, prevStop, prevFlag);
            CoreEntitlement.HasPremiumProvider = prevPremium;
            shell.Close();
            (s.PanicKeyEnabled, s.PanicKey, s.StrictLockEnabled, s.KeywordTriggersEnabled, s.ScreenOcrEnabled, s.KeywordTriggersOffByPanic) = saved;
            CoreSettings.SaveImmediate();   // the switch-off saved: put the file back too
        }
    });

    private static Action Route(string name, MainShellWindow shell) => name switch
    {
        "key" => () => shell.HandlePanicKeyPress(new DateTime(2026, 1, 1)),
        "tray" => MainShellWindow.StopEverything,
        "voice" => shell.VoicePanic,
        "blink" => () => MainShellWindow.StopAllForRecalibration(shell),
        "stop-all" => () => PanicSurfaces.StopAll("test", shell),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [InlineData("key")]
    [InlineData("tray")]
    [InlineData("voice")]
    [InlineData("blink")]
    [InlineData("stop-all")]
    public void EveryAcceptedPanicRouteSwitchesKeywordTriggersOff(string route) => WithShell(p =>
    {
        var s = CoreSettings.Current;
        Route(route, p.Shell)();
        Assert.False(s.KeywordTriggersEnabled, $"{route}: the master is still on");
        Assert.False(s.ScreenOcrEnabled, $"{route}: the saved screen read is still on");
        Assert.True(s.KeywordTriggersOffByPanic);
        Assert.True(PanicSurfaces.KeywordMasterOffByPanic);
        Assert.True(p.SourcesStopped() >= 1, $"{route}: the reader and highlights were not stopped");
        Assert.Equal(1, p.Repaints());
        Assert.Single(p.Stopped);   // and the press still stopped everything else

        // A second press: already off, so nothing is written and nothing repaints again.
        Route(route, p.Shell)();
        Assert.Equal(1, p.Repaints());
        Assert.False(s.KeywordTriggersEnabled || s.ScreenOcrEnabled);
    });

    [Theory]
    [InlineData("key", false, false)]     // panic key switched off
    [InlineData("tray", false, false)]
    [InlineData("voice", false, false)]
    [InlineData("tray", true, true)]      // Strict Lock refuses the tray stop and the safe word
    [InlineData("voice", true, true)]
    public void ARefusedPanicPressChangesNothing(string route, bool panicKeyOn, bool strict) => WithShell(p =>
    {
        var s = CoreSettings.Current;
        (s.PanicKeyEnabled, s.StrictLockEnabled) = (panicKeyOn, strict);
        Route(route, p.Shell)();
        Assert.True(s.KeywordTriggersEnabled);
        Assert.True(s.ScreenOcrEnabled);
        Assert.False(s.KeywordTriggersOffByPanic);
        Assert.False(PanicSurfaces.KeywordMasterOffByPanic);
        Assert.Equal(0, p.SourcesStopped());
        Assert.Equal(0, p.Repaints());
        Assert.Empty(p.Stopped);
    });

    [Fact]
    public void ARefusedPressUnderLockdownChangesNothing() => WithShell(p =>
    {
        var s = CoreSettings.Current;
        using var ld = LockdownService.Current = new LockdownService();
        try
        {
            ld.Activate(TimeSpan.FromMinutes(30));
            (s.KeywordTriggersEnabled, s.ScreenOcrEnabled) = (true, true);
            p.Shell.HandlePanicKeyPress(new DateTime(2026, 1, 1));
            p.Shell.VoicePanic();
            Assert.True(s.KeywordTriggersEnabled && s.ScreenOcrEnabled);
            Assert.False(s.KeywordTriggersOffByPanic);
            Assert.Equal(0, p.SourcesStopped());
        }
        finally { LockdownService.Current = null; }
    });

    /// <summary>Nothing restarts it: not the reader's own sync, not the watchdog's recovery (which used to
    /// restart the reader). Switching both back on is what makes the reader want to run again.</summary>
    [Fact]
    public void NothingSwitchesItBackOnButTheUser() => WithShell(p =>
    {
        var s = CoreSettings.Current;
        Assert.True(ScreenOcrService.ShouldRun(s, hasAccess: true));
        PanicSurfaces.StopAll("test", p.Shell);
        Assert.False(ScreenOcrService.ShouldRun(s, hasAccess: true));
        Assert.False(ScreenOcrService.IsRunning);

        ScreenOcrService.Sync();
        KeywordTriggerHead.SyncSources();
        var (wasPost, queued) = (PanicWatchdog.PostToUi, new List<Action>());
        PanicWatchdog.PostToUi = queued.Add;
        try { PanicWatchdog.Teardown(); foreach (var a in queued) a(); }
        finally { PanicWatchdog.PostToUi = wasPost; }
        Assert.False(s.KeywordTriggersEnabled);
        Assert.False(s.ScreenOcrEnabled);
        Assert.False(ScreenOcrService.ShouldRun(s, hasAccess: true));
        Assert.False(ScreenOcrService.IsRunning);

        // The watchdog's recovery alone (the UI was stalled, the handler never ran) also ends with both off.
        (s.KeywordTriggersEnabled, s.ScreenOcrEnabled) = (true, true);
        queued.Clear();
        PanicWatchdog.PostToUi = queued.Add;
        try { PanicWatchdog.Teardown(); foreach (var a in queued) a(); }
        finally { PanicWatchdog.PostToUi = wasPost; }
        Assert.False(s.KeywordTriggersEnabled || s.ScreenOcrEnabled);

        // The other direction: the user's own two switches.
        (s.KeywordTriggersEnabled, s.ScreenOcrEnabled) = (true, true);
        Assert.True(ScreenOcrService.ShouldRun(s, hasAccess: true));
    }, realSources: true);

    [Fact]
    public void TheAwarenessTabShowsItOffSaysWhyAndSwitchesBackOnNormally() => WithShell(p =>
    {
        var s = CoreSettings.Current;
        CoreEntitlement.HasPremiumProvider = () => true;
        var view = new AwarenessTabView();
        var host = new Window { Content = view, Width = 900, Height = 700 };
        host.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            view.SyncAwarenessTabUi();
            var master = view.FindControl<CheckBox>("ChkAwarenessMaster")!;
            var ocr = view.FindControl<CheckBox>("ChkAwarenessOcr")!;
            var notice = view.FindControl<TextBlock>("TxtAwarenessPanicNotice")!;
            Assert.True(master.IsChecked == true && ocr.IsChecked == true);
            Assert.False(notice.IsVisible);

            PanicSurfaces.StopAll("test", p.Shell);
            Assert.False(master.IsChecked == true);   // the visible tab follows at once
            Assert.False(ocr.IsChecked == true);
            Assert.True(notice.IsVisible);
            Assert.False(s.KeywordTriggersEnabled || s.ScreenOcrEnabled);   // painting wrote nothing back

            master.IsChecked = true;                  // the user's own click
            Assert.True(s.KeywordTriggersEnabled);
            Assert.False(PanicSurfaces.KeywordMasterOffByPanic);
            Assert.False(s.ScreenOcrEnabled);         // the screen read is its own switch and stays off
            Assert.True(notice.IsVisible);            // so the notice still explains that one

            ocr.IsChecked = true;
            Assert.True(s.ScreenOcrEnabled);
            Assert.False(s.KeywordTriggersOffByPanic);
            Assert.False(notice.IsVisible);
            Assert.True(ScreenOcrService.ShouldRun(s, hasAccess: true));
        }
        finally { host.Close(); }
    }, realSources: true);

    [Fact]
    public void TheNoticeHasItsLineInAllTenLanguages()
    {
        var dir = System.IO.Path.Combine(RepoRoot(), "CCP.Core", "Localization", "Languages");
        var files = System.IO.Directory.GetFiles(dir, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var f in files)
        {
            var text = (string?)Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(f))["awareness_off_by_panic"];
            Assert.False(string.IsNullOrWhiteSpace(text), System.IO.Path.GetFileName(f));
            Assert.DoesNotContain("!", text);
            Assert.DoesNotContain("—", text);
            Assert.DoesNotContain("–", text);
        }
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "CCP.Core", "Localization", "Languages")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }
}
