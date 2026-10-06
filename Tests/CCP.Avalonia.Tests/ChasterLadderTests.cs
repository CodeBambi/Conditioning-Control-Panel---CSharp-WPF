using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Chaster ladder slice: the heads-up clock, the raffle card under it and the pinned top-ten
/// scrap, driven by the head's own ChasterService against a loopback fake proxy.</summary>
public sealed class ChasterLadderTests
{
    private sealed class FakeProxy : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        public readonly List<string> OptIns = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            lock (Paths) Paths.Add(r.RequestUri!.Host + path);
            if (path == "/chaster/raffle/optin") lock (Paths) OptIns.Add(r.Content!.ReadAsStringAsync().Result);
            var body = path switch
            {
                "/chaster/raffle/me" => "{\"ok\":true,\"month\":\"2026-10\",\"days_in_month\":31,\"today\":5,\"days\":[1,2,3],\"total_seconds\":7200,\"need_days\":20,\"need_seconds\":36000}",
                "/chaster/raffle/top" => "{\"ok\":true,\"month\":\"2026-10\",\"rows\":[{\"rank\":1,\"name\":\"Alpha\",\"named\":true,\"added_seconds\":9000},"
                    + "{\"rank\":2,\"name\":\"Locked 1A\",\"added_seconds\":5000}],\"you\":{\"rank\":23,\"name\":\"Me\",\"named\":true,\"added_seconds\":100}}",
                "/chaster/raffle/optin" or "/chaster/raffle/name" => "{\"ok\":true}",
                "/chaster/raffle/verify" => "{\"ok\":true,\"added_seconds\":7200}",
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static async Task Run(Func<ChasterService, FakeProxy, Task> body, string? overrideUrl = "http://127.0.0.1:47999/")
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        SecretStore.Seed();   // before AuthToken: the token lives in the (memory) secret store
        var s = CoreSettings.Current;
        s.ChasterLockId = "l1";
        s.OfflineMode = false;
        s.UnifiedId = "u1";
        var oldToken = s.AuthToken;   // the secret store is process-global: restore it for later tests
        s.AuthToken = "t1";
        s.ChasterRafflePostDays = false;
        s.ChasterLadderShowName = false;
        new SecretChasterTokenStore().Write(new ChasterStoredTokens("acc", "ref", DateTime.UtcNow.AddHours(1)));
        var fake = new FakeProxy();
        var chaster = ChasterHead.Create("sandbox", overrideUrl, fake);
        ChasterHead.Service = chaster;
        try { await body(chaster, fake); }
        finally
        {
            ChasterHead.Service = null;
            chaster.Dispose();
            new SecretChasterTokenStore().Clear();
            s.AuthToken = oldToken;
            CoreSettings.ServiceProvider = null;
        }
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Dispatcher.UIThread.RunJobs();
    }

    private static Point Centre(Window w, Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;

    [Fact]
    public Task RaffleReachesOnlyTheLoopbackProxy() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        await Run(async (chaster, fake) =>
        {
            var card = await chaster.RaffleAsync();
            Assert.NotNull(card);
            Assert.Equal(7200, card!.TotalSeconds);
            Assert.All(fake.Paths, p => Assert.StartsWith("127.0.0.1/", p));
            Assert.Contains("127.0.0.1/chaster/raffle/me", fake.Paths);
        });
        // a sandbox without an honoured loopback url fails closed: nothing is sent at all
        await Run(async (chaster, fake) =>
        {
            Assert.Null(await chaster.RaffleAsync());
            Assert.Null(await chaster.LadderAsync());
            Assert.Empty(fake.Paths);
        }, overrideUrl: "https://evil.example/");
    });

    [Fact]
    public Task ClockHoverUnrollsTheCardAndTheScrapShowsTheBoard() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, _) =>
    {
        var tab = new ChasterTabView();
        var w = new Window { Width = 1200, Height = 900, Content = tab };
        w.Show();
        try
        {
            tab.OnTabShown();
            var rows = tab.FindControl<StackPanel>("LadderScrapRows")!;
            await Until(() => rows.Children.Count > 0);
            Assert.True(tab.FindControl<Grid>("AddedRow")!.IsVisible);
            // two of the top ten, the dotted gap, then my own row below them
            Assert.Equal(4, rows.Children.Count);
            Assert.False(tab.FindControl<TextBlock>("TxtLadderScrapState")!.IsVisible);

            var popup = tab.FindControl<Popup>("LadderPopup")!;
            var clock = tab.FindControl<Border>("AddedClock")!;
            w.MouseMove(Centre(w, clock));
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            var days = tab.FindControl<TextBlock>("TxtRaffleDays")!;
            await Until(() => !string.IsNullOrEmpty(days.Text));
            Assert.Equal(31, tab.FindControl<WrapPanel>("RafflePips")!.Children.Count);
            Assert.Contains("3", days.Text);

            // a click pins it; a press anywhere else closes the pinned card
            w.MouseDown(Centre(w, clock), MouseButton.Left);
            w.MouseUp(Centre(w, clock), MouseButton.Left);
            w.MouseMove(new Point(5, 5));
            await Task.Delay(400);
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            w.MouseDown(new Point(5, 5), MouseButton.Left);
            w.MouseUp(new Point(5, 5), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen);
        }
        finally { w.Close(); }
    }));

    /// <summary>Privacy: with the defaults (both opt-ins off) opening the tab and the card never
    /// tells the server to post days or show a name.</summary>
    [Fact]
    public Task DefaultsSendNoOptInOrName() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, fake) =>
    {
        var tab = new ChasterTabView();
        var w = new Window { Width = 1200, Height = 900, Content = tab };
        w.Show();
        try
        {
            tab.OnTabShown();
            await Until(() => fake.Paths.Contains("127.0.0.1/chaster/raffle/me") && fake.Paths.Contains("127.0.0.1/chaster/raffle/top"));
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("127.0.0.1/chaster/raffle/me", fake.Paths);
            Assert.DoesNotContain(fake.Paths, p => p.EndsWith("/optin") || p.EndsWith("/name"));
        }
        finally { w.Close(); }
    }));

    [Fact]
    public Task PostDaysToggleSendsTrueThenFalse() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, fake) =>
    {
        var tab = new ChasterTabView();
        var w = new Window { Width = 1200, Height = 900, Content = tab };
        w.Show();
        try
        {
            tab.OnTabShown();
            Dispatcher.UIThread.RunJobs();
            var chk = tab.FindControl<CheckBox>("ChkRafflePost")!;
            chk.IsChecked = true;
            await Until(() => fake.OptIns.Count >= 1);
            Assert.True(CoreSettings.Current.ChasterRafflePostDays);
            await Task.Delay(200);   // the re-read after a taken switch (it may re-send true: the fake keeps post_days off)
            Dispatcher.UIThread.RunJobs();
            var before = fake.OptIns.Count;
            Assert.All(fake.OptIns, o => Assert.Contains("\"post_days\":true", o));
            chk.IsChecked = false;
            await Until(() => fake.OptIns.Count > before);
            Assert.False(CoreSettings.Current.ChasterRafflePostDays);
            Assert.Contains("\"post_days\":false", fake.OptIns[before]);
        }
        finally { w.Close(); }
    }));
}
