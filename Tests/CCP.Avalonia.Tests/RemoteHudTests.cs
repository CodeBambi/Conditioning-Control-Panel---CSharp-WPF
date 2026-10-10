using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.RemoteHud;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Remote Control v2 HUD pill (WPF Windows/RemoteHud/RemoteHudWindow.cs) against a fake relay:
/// up while a controller holds the remote, who and for how long, More / Easy / Stop, and no way for the
/// controller to take it down.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class RemoteHudTests
{
    private sealed class FakeRelay : HttpMessageHandler
    {
        public readonly List<string> Bodies = new();
        public string Poll = "{}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            var body = await r.Content!.ReadAsStringAsync(ct);
            if (path == "/v2/remote/emote") lock (Bodies) Bodies.Add(body);
            var answer = path switch { "/v2/remote/start" => "{\"code\":\"ABC123\"}", "/v2/remote/poll" => Poll, _ => "{}" };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer) };
        }
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Step(TimeSpan t) => Now += t.Ticks;
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task The_pill_is_up_while_a_controller_holds_the_remote_and_its_buttons_act() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var (oldProvider, oldLevel) = (CoreSettings.ServiceProvider, RemoteHudWindow.Level);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        RemoteHudWindow.Level = () => MotionLevel.Off;   // no fades: the window state is the truth
        var f = new FakeRelay();
        var forced = new List<bool>();
        var relay = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, forced.Add, f) { AutoPoll = false };
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        relay.Now = () => now;
        var localStops = 0;
        var hud = new RemoteHudWindow(relay, () => localStops++, () => null) { UtcNow = () => now };
        try
        {
            hud.Refresh();
            Assert.False(hud.IsUp);                    // no session, no pill
            Assert.False(hud.IsVisible);

            await relay.StartAsync("full");
            hud.Refresh();
            Assert.False(hud.IsUp);                    // a session without a controller: still none

            f.Poll = "{\"controller_connected\":true,\"controller_name\":\"kira\"}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(hud.IsUp);
            Assert.True(hud.IsVisible);
            Assert.False(hud.ShowActivated);           // never takes focus
            Assert.True(hud.Topmost);
            Assert.False(hud.ShowInTaskbar);
            Assert.Equal(Loc.GetF("remote_hud_has_remote", "kira"), hud.Headline);
            Assert.Equal("K", hud.Initial);
            Assert.Equal("0:00", hud.Subline);
            Assert.True(hud.ButtonsVisible);           // the exit shows
            Assert.Null(hud.StrengthTagText);

            // A landed command is the "Last:" line; time counts from the connect.
            now = now.AddSeconds(65);
            f.Poll = "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"show_spiral\"}]}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("remote_hud_someone"), hud.Headline);
            Assert.StartsWith("1:05", hud.Subline);
            Assert.EndsWith(Loc.GetF("remote_hud_last", "Spiral"), hud.Subline);
            Assert.True(hud.IsUp);                     // nothing a controller sends takes it down

            // More: sent, confirmed on the pill for 1.6 s.
            await hud.SignalAsync("more");
            Assert.Contains("\"kind\":\"signal\"", f.Bodies.Single());
            Assert.Equal(Loc.GetF("remote_hud_sent", Loc.Get("remote_hud_more")), hud.Subline);
            now += RemoteHudRules.ConfirmFor;
            hud.Refresh();
            Assert.StartsWith("1:06", hud.Subline);

            // Easy: half, then quarter, tagged.
            await hud.SignalAsync("easy");
            Assert.Equal(Loc.Get("remote_hud_half"), hud.StrengthTagText);
            await hud.SignalAsync("easy");
            Assert.Equal(Loc.Get("remote_hud_quarter"), hud.StrengthTagText);

            // Folding is the player's own choice and hides the buttons; unfolding brings them back.
            hud.ToggleCollapsed();
            Assert.False(hud.ButtonsVisible);
            hud.ToggleCollapsed();
            Assert.True(hud.ButtonsVisible);

            // Stop: the local stop first, then the forced remote stop, once per 2.5 s (never a double panic).
            await hud.SignalAsync("stop");
            await hud.SignalAsync("stop");
            Assert.Equal(1, localStops);
            Assert.Equal(new[] { true }, forced);
            Assert.Equal(Loc.Get("remote_hud_stopped"), hud.Subline);
            Assert.True(relay.IsActive);               // Stop never ends the session by itself
            now += RemoteHudRules.StopDebounce;
            await hud.SignalAsync("stop");
            Assert.Equal(2, localStops);

            // The controller leaves: the pill goes. It comes back unfolded for the next one.
            hud.ToggleCollapsed();
            f.Poll = "{\"controller_connected\":false}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(hud.IsUp);
            Assert.False(hud.IsVisible);
            f.Poll = "{\"controller_connected\":true}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(hud.IsUp);
            Assert.True(hud.ButtonsVisible);

            // The session ends: gone, and Easy is back to full for the next session.
            await relay.StopAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(hud.IsUp);
            Assert.Equal(1.0, relay.EasyFactor);
        }
        finally
        {
            hud.Dispose();
            relay.Dispose();
            RemoteCommands.ResetEasy();
            (CoreSettings.ServiceProvider, RemoteHudWindow.Level) = (oldProvider, oldLevel);
        }
    });

    [Fact]
    public void The_pill_is_placed_top_centre_and_never_off_the_work_area()
    {
        var work = new PixelRect(1920, 40, 1920, 1040);
        var at = RemoteHudWindow.PlaceFor(work, 1.0, new Size(400, 76));
        Assert.Equal(1920 + (1920 - 400) / 2, at.X);
        Assert.Equal(40 - 2, at.Y);                    // 10 dip gap less the 12 dip shadow room
        var scaled = RemoteHudWindow.PlaceFor(work, 1.5, new Size(400, 76));
        Assert.Equal(1920 + (1920 - 600) / 2, scaled.X);
        // Wider than the screen: pinned to the left edge of the work area, never left of it.
        Assert.Equal(1920, RemoteHudWindow.PlaceFor(work, 1.0, new Size(4000, 76)).X);
    }

    [Fact]
    public Task The_shell_raises_the_pill_for_a_controller_and_closes_it_with_the_window() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var (oldRelay, oldProvider, oldLevel) = (RemoteControlTabView.Relay, CoreSettings.ServiceProvider, RemoteHudWindow.Level);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        RemoteHudWindow.Level = () => MotionLevel.Off;
        var f = new FakeRelay();
        var relay = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => { }, f) { AutoPoll = false };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        var (oldTime, oldFlash) = (MainShellWindow.RemoteOverlayTime, ConditioningControlPanel.Avalonia.Platform.TaskbarFlash.Override);
        var clock = new SteppedClock();
        MainShellWindow.RemoteOverlayTime = clock;
        var flashes = 0;
        ConditioningControlPanel.Avalonia.Platform.TaskbarFlash.Override = _ => { flashes++; return true; };
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(shell.RemoteHud);

            await relay.StartAsync("light");
            f.Poll = "{\"controller_connected\":true}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            var hud = shell.RemoteHud!;
            Assert.True(hud.IsUp);
            Assert.Null(hud.Owner);                    // unowned: showing it never lifts the panel
            Assert.Equal(0, flashes);                  // the panel is on screen: no taskbar flash

            // The browser blindfold (WPF :903): hidden under the card, back for the controller's own video.
            var browser = shell.Named<SettingsTabView>("SettingsTab")!.BrowserContainer!;
            Assert.False(browser.IsVisible);
            Assert.True(shell.RemoteBrowserBlindfolded);
            shell.RevealBrowserForRemoteVideo(true);
            Assert.True(browser.IsVisible);
            shell.OnRemoteControllerChanged();         // a reconnect mid-video does not blindfold it again
            Assert.True(browser.IsVisible);
            shell.RevealBrowserForRemoteVideo(false);
            Assert.False(browser.IsVisible);

            // A second join while the panel is minimised flashes the taskbar and restores nothing.
            shell.WindowState = WindowState.Minimized;
            shell.OnRemoteControllerChanged();
            Assert.Equal(1, flashes);
            Assert.Equal(WindowState.Minimized, shell.WindowState);
            // A remote stop brings a minimised panel back (WPF RestoreFromTrayForRemote).
            ((RemoteCommands.IRemoteHead)shell).RestoreWindow();
            Assert.Equal(WindowState.Normal, shell.WindowState);

            await relay.StopAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(hud.IsUp);
            clock.Step(MainShellWindow.RemoteOverlayFadeOut);
            shell.RemoteOverlayTick();
            Assert.True(browser.IsVisible);            // the overlay is gone: the browser is back

            shell.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(shell.RemoteHud);
            Assert.False(hud.IsVisible);
            shell = null;
        }
        finally
        {
            shell?.Close();
            relay.Dispose();
            (RemoteControlTabView.Relay, CoreSettings.ServiceProvider, RemoteHudWindow.Level) = (oldRelay, oldProvider, oldLevel);
            (MainShellWindow.RemoteOverlayTime, ConditioningControlPanel.Avalonia.Platform.TaskbarFlash.Override) = (oldTime, oldFlash);
        }
    });
}
