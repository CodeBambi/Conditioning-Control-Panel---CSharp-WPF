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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The shell half of Remote Control against a fake relay (no network): WPF
/// MainWindow.RemoteControl.cs overlay, toast, Start lock, big emotes and End Session.</summary>
public sealed class RemoteControlShellTests
{
    private sealed class FakeRelay : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        public string Poll = "{}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            lock (Paths) Paths.Add(path);
            var body = path switch { "/v2/remote/start" => "{\"code\":\"ABC123\"}", "/v2/remote/poll" => Poll, _ => "{}" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Step(TimeSpan t) => Now += t.Ticks;
    }

    [Fact]
    public Task Controller_gets_the_overlay_toast_start_lock_emotes_and_end_session() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (oldRelay, oldTime, oldProvider) = (RemoteControlTabView.Relay, MainShellWindow.RemoteOverlayTime, CoreSettings.ServiceProvider);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var f = new FakeRelay();
        var relay = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => { }, f) { AutoPoll = false };
        var emoteNow = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        relay.Now = () => emoteNow;
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        var clock = new SteppedClock();
        MainShellWindow.RemoteOverlayTime = clock;
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var overlay = shell.Named<Border>("RemoteControlOverlay")!;
            var start = shell.Named<Button>("BtnStart")!;
            Assert.False(overlay.IsVisible);

            Assert.Equal("ABC123", await relay.StartAsync("light"));
            f.Poll = "{\"controller_connected\":true}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();

            // Overlay up with code + PIN; Start disabled, green, "REMOTE CONNECTED" (WPF StartStop.cs:992).
            Assert.True(overlay.IsVisible);
            Assert.Equal(1, Target(overlay));
            Assert.Equal($"Session: A B C 1 2 3  PIN: {relay.ConnectPin}", shell.Named<TextBlock>("TxtOverlaySessionCode")!.Text);
            Assert.False(start.IsEnabled);
            Assert.Equal(Loc.Get("label_remote_connected"), shell.Named<TextBlock>("TxtStartLabel")!.Text);
            shell.UpdateStartButton();   // an engine refresh keeps the remote label (WPF :941)
            Assert.Equal("\U0001F3AE", shell.Named<TextBlock>("TxtStartIcon")!.Text);
            Assert.True(shell.Named<Control>("RemoteSessionIdle")!.IsVisible);   // no session running
            Assert.Equal(MainShellWindow.RemoteOverlaySlowTick, shell.RemoteOverlayInterval);

            // A loud verb toasts for 2 s; a quiet one does not.
            var toast = shell.Named<Border>("RemoteCommandNotification")!;
            f.Poll = "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"trigger_flash\"}]}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, Target(toast));
            f.Poll = "{\"controller_connected\":true,\"commands\":[{\"id\":\"2\",\"action\":\"show_spiral\"}]}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Target(toast));
            Assert.Equal(MainShellWindow.RemoteOverlayFastTick, shell.RemoteOverlayInterval);   // only while the toast is pending
            Assert.Equal(Loc.Get("cmd_spiral_enabled"), shell.Named<TextBlock>("TxtRemoteCommand")!.Text);
            clock.Step(TimeSpan.FromSeconds(1.9));
            shell.RemoteOverlayTick();
            Assert.Equal(1, Target(toast));
            clock.Step(TimeSpan.FromSeconds(0.1));
            shell.RemoteOverlayTick();
            Assert.Equal(0, Target(toast));
            Assert.Equal(MainShellWindow.RemoteOverlaySlowTick, shell.RemoteOverlayInterval);   // back to WPF's 1 s

            // Idle controller: the orange subtitle.
            f.Poll = "{\"controller_connected\":true,\"controller_idle\":true}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("label_controller_may_be_idle"), shell.Named<TextBlock>("TxtRemoteOverlaySubtitle")!.Text);

            // Big picker: three slots above End Session, two below; a click posts the emote and says so.
            var top = shell.Named<ItemsControl>("LstEmotePresetsBigTop")!;
            Assert.Equal(3, top.ItemCount);
            Assert.Equal(2, shell.Named<ItemsControl>("LstEmotePresetsBigBottom")!.ItemCount);
            Dispatcher.UIThread.RunJobs();
            var big = top.GetVisualDescendants().OfType<Button>().First(b => b.Tag is EmotePreset);
            big.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => f.Paths.Contains("/v2/remote/emote"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("status_emote_sent"), shell.Named<TextBlock>("TxtEmoteStatusBig")!.Text);

            // Enter in the big custom box sends it too, clears the box and keeps it as the ghost (WPF :362).
            emoteNow = emoteNow.AddSeconds(1);
            var box = shell.Named<TextBox>("TxtEmoteCustomBig")!;
            box.Text = "  on my way ";
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await WaitFor(() => f.Paths.Count(x => x == "/v2/remote/emote") == 2);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", box.Text);
            Assert.Equal("on my way", box.Watermark);

            // End Session: the relay stops, the overlay fades 200 ms then goes, Start comes back.
            var end = overlay.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == Loc.Get("btn_end_session"));
            end.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => !relay.IsActive);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("/v2/remote/stop", f.Paths);
            Assert.Equal(0, Target(overlay));
            Assert.True(overlay.IsVisible);   // still fading
            clock.Step(MainShellWindow.RemoteOverlayFadeOut);
            shell.RemoteOverlayTick();
            Assert.False(overlay.IsVisible);
            Assert.True(start.IsEnabled);
            Assert.Equal(Loc.Get("label_start"), shell.Named<TextBlock>("TxtStartLabel")!.Text);
        }
        finally
        {
            shell?.Close();
            relay.Dispose();
            (RemoteControlTabView.Relay, MainShellWindow.RemoteOverlayTime, CoreSettings.ServiceProvider) = (oldRelay, oldTime, oldProvider);
        }
    });

    /// <summary>The opacity the fade is heading to (the transition animates the visible value).</summary>
    private static double Target(Visual v) => v.GetBaseValue(Visual.OpacityProperty).Value;

    /// <summary>The click handlers are async void: wait on their side effect, not on time (P56).</summary>
    private static async Task WaitFor(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
        Assert.True(done());
    }
}
