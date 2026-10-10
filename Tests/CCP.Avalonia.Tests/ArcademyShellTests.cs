using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.EmiDesk;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>IB9: the Arcademy and the rest of the desk (WPF ArcademyHostService.Launch / DisposeAll). The
/// panel is tucked while the campus is up and ALWAYS comes back; the desk mascot says goodbye and winks
/// off; a browser video takeover freezes the class under the protection.</summary>
[Collection(RunsAloneCollection.Name)]   // swaps the process-wide shell / desk seams and the bus sink
public sealed class ArcademyShellTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private sealed class Desk
    {
        public List<string> Shell = new();
        public List<string> Moments = new();
        public bool EmiOut;
        public int Dismissed;
        public string? TuckAnswer = "tray";
    }

    private static Task Campus(Action<Desk, Func<GameWindow>, List<JObject>> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var dir = Path.Combine(Path.GetTempPath(), "ccp-arcshell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var s = CoreSettings.Current;
        bool offline = s.OfflineMode, protect = s.ProtectBrowserVideoPlayback, audioOnly = s.AudioOnlySession;
        var sink = EmiDeskBus.Sink;
        var desk = new Desk();
        var open = new List<GameWindow>();
        var posted = new List<JObject>();
        GameWindow.ArcademyMetaPathOverride = Path.Combine(dir, "arcademy_meta.json");
        GameWindow.ArcShellTuck = () => { desk.Shell.Add("tuck"); return desk.TuckAnswer; };
        GameWindow.ArcShellRestore = how => desk.Shell.Add("restore:" + how);
        GameWindow.ArcEmiIsOut = () => desk.EmiOut;
        GameWindow.ArcEmiDismiss = () => { desk.Dismissed++; desk.EmiOut = false; };
        EmiDeskBus.Sink = (id, _) => desk.Moments.Add(id);
        s.OfflineMode = true;
        s.AudioOnlySession = false;
        try
        {
            body(desk, () =>
            {
                var w = new GameWindow(GameWindow.Games["arcademy"]);
                w.Posted += json => posted.Add(JObject.Parse(json));
                w.Show();
                open.Add(w);
                w.HandleMessage("{\"type\":\"ready\",\"protocol\":1}");
                return w;
            }, posted);
        }
        finally
        {
            foreach (var w in open) { try { w.Close(); } catch { } }
            BrowserVideoSignal.Set(false);
            GameWindow.ArcShellTuck = GameWindow.DefaultArcShellTuck;
            GameWindow.ArcShellRestore = GameWindow.DefaultArcShellRestore;
            GameWindow.ArcEmiIsOut = GameWindow.DefaultArcEmiIsOut;
            GameWindow.ArcEmiDismiss = GameWindow.DefaultArcEmiDismiss;
            EmiDeskBus.Sink = sink;
            GameWindow.ArcademyMetaPathOverride = null;
            s.OfflineMode = offline;
            s.ProtectBrowserVideoPlayback = protect;
            s.AudioOnlySession = audioOnly;
            CoreSettings.SaveImmediate();
            try { Directory.Delete(dir, true); } catch { }
        }
        return Task.CompletedTask;
    });

    private static List<JObject> Suspends(List<JObject> posted) => posted.Where(p => (string?)p["type"] == "suspend").ToList();

    [Fact]
    public Task ThePanelIsTuckedOnOpen_AndComesBackOnClose() => Campus((desk, open, _) =>
    {
        var w = open();
        Assert.Equal(new[] { "tuck" }, desk.Shell);
        Assert.Equal("tray", w.ArcademyShellTucked);
        w.Close();
        Assert.Equal(new[] { "tuck", "restore:tray" }, desk.Shell);
        Assert.Null(w.ArcademyShellTucked);
    });

    [Fact]
    public Task APanelThatWasNotTucked_IsNotBroughtBack() => Campus((desk, open, _) =>
    {
        desk.TuckAnswer = null;   // hidden or minimized already: the user's last word stands
        var w = open();
        w.Close();
        Assert.Equal(new[] { "tuck" }, desk.Shell);
    });

    [Fact]
    public Task Panic_BringsThePanelBack() => Campus((desk, open, _) =>
    {
        open();
        GameWindow.CloseAllForPanic();
        Assert.Contains("restore:tray", desk.Shell);
    });

    [Fact]
    public Task ABootError_BringsThePanelBack() => Campus((desk, open, _) =>
    {
        var w = new GameWindow(GameWindow.Games["arcademy"]);
        w.Show();
        Assert.Equal("failed", w.CheckArcademyBootDeadline(DateTime.UtcNow + GameWindow.ArcBootDeadline + TimeSpan.FromSeconds(1)));
        Assert.Equal(new[] { "tuck", "restore:tray" }, desk.Shell);
    });

    [Fact]
    public Task APageThatWentSilentTwice_IsClosed_AndThePanelComesBack() => Campus((desk, open, _) =>
    {
        var w = open();
        w.HandleMessage("{\"type\":\"heartbeat\"}");
        var late = DateTime.UtcNow + TimeSpan.FromHours(1);
        Assert.Equal("recovered", w.CheckHeartbeat(late));
        Assert.DoesNotContain("restore:tray", desk.Shell);   // one relaunch: the campus is still up
        w.HandleMessage("{\"type\":\"ready\",\"protocol\":1}");
        w.HandleMessage("{\"type\":\"heartbeat\"}");
        Assert.Equal("closed", w.CheckHeartbeat(late + TimeSpan.FromHours(1)));
        Assert.Contains("restore:tray", desk.Shell);
    });

    [Fact]
    public Task ARestoreThatThrows_NeverStopsTheClose() => Campus((desk, open, _) =>
    {
        var w = open();
        GameWindow.ArcShellRestore = _ => throw new InvalidOperationException("no shell");
        w.Close();
        Assert.Null(w.ArcademyShellTucked);
        Assert.False(w.ArcademyFarewellPending);
    });

    [Fact]
    public Task EmiAway_HearsOpened_AndNoGoodbye() => Campus((desk, open, _) =>
    {
        var w = open();
        Assert.False(w.ArcademyFarewellClaimed);
        Assert.Contains("arcademyOpened", desk.Moments);
        Assert.DoesNotContain(GameWindow.ArcademyByeMoment, desk.Moments);
        w.Close();
        Assert.Equal(0, desk.Dismissed);
        Assert.Contains("arcademyClosed", desk.Moments);
    });

    [Fact]
    public Task EmiOut_SaysGoodbye_ThenWinksOff_AndOpenedStaysQuiet() => Campus((desk, open, _) =>
    {
        desk.EmiOut = true;
        var w = open();
        Assert.True(w.ArcademyFarewellClaimed);
        Assert.Contains(GameWindow.ArcademyByeMoment, desk.Moments);
        Assert.DoesNotContain("arcademyOpened", desk.Moments);
        Assert.True(w.ArcademyFarewellPending);
        Assert.Equal(0, desk.Dismissed);
        w.StopArcademyFarewell(dismissNow: true);   // the timer's own tick
        Assert.Equal(1, desk.Dismissed);
        Assert.False(w.ArcademyFarewellPending);
        w.Close();
        Assert.Equal(1, desk.Dismissed);   // never twice
    });

    [Fact]
    public Task ClosingBeforeTheWink_StopsTheTimer_AndSendsHerOff() => Campus((desk, open, _) =>
    {
        desk.EmiOut = true;
        var w = open();
        w.Close();
        Assert.False(w.ArcademyFarewellPending);
        Assert.Equal(1, desk.Dismissed);
    });

    [Fact]
    public Task OpeningCountsTheArcademyForTheRing() => Campus((desk, open, _) =>
    {
        EmiState.Current.Usage.TryGetValue("arcademy", out int before);
        open();
        EmiState.Current.Usage.TryGetValue("arcademy", out int after);
        Assert.Equal(before + 1, after);
    });

    [Fact]
    public Task ABrowserVideo_FreezesTheClass_OnlyUnderTheProtection() => Campus((desk, open, posted) =>
    {
        CoreSettings.Current.ProtectBrowserVideoPlayback = false;
        open();
        posted.Clear();
        BrowserVideoSignal.Set(true);
        BrowserVideoSignal.Set(false);
        Assert.Empty(Suspends(posted));

        CoreSettings.Current.ProtectBrowserVideoPlayback = true;
        BrowserVideoSignal.Set(true);
        var on = Assert.Single(Suspends(posted));
        Assert.True((bool)on["on"]!);
        Assert.Equal("video", (string?)on["reason"]);
        BrowserVideoSignal.Set(false);
        var off = Suspends(posted).Last();
        Assert.False((bool)off["on"]!);
        Assert.Equal("video", (string?)off["reason"]);
    });

    [Fact]
    public Task ABrowserVideoEnding_NeverLiftsAnAudioOnlySession() => Campus((desk, open, posted) =>
    {
        CoreSettings.Current.ProtectBrowserVideoPlayback = true;
        open();
        BrowserVideoSignal.Set(true);
        posted.Clear();
        CoreSettings.Current.AudioOnlySession = true;
        BrowserVideoSignal.Set(false);
        Assert.DoesNotContain(Suspends(posted), p => (bool)p["on"]! == false && (string?)p["reason"] == "video");
    });

    [Fact]
    public Task ABrowserVideoAlreadyPlaying_FreezesAtInit() => Campus((desk, open, posted) =>
    {
        CoreSettings.Current.ProtectBrowserVideoPlayback = true;
        BrowserVideoSignal.Set(true);
        open();
        Assert.Contains(Suspends(posted), p => (bool)p["on"]! && (string?)p["reason"] == "video");
    });

    [Fact]
    public Task AClosedCampus_NoLongerListensForBrowserVideo() => Campus((desk, open, posted) =>
    {
        CoreSettings.Current.ProtectBrowserVideoPlayback = true;
        var w = open();
        w.Close();
        posted.Clear();
        BrowserVideoSignal.Set(true);
        Assert.Empty(posted);
    });
}
