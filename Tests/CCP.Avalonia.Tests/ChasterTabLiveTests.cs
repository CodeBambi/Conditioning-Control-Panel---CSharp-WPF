using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Circe's tab on this head: the head-built Core service never reaches the real
/// Chaster from a sandbox, and the rail padlock opens a tab that reads the lock.</summary>
public sealed class ChasterTabLiveTests
{
    /// <summary>A fake Chaster: records every URL and owns one lock twelve days out.</summary>
    private sealed class FakeChaster : HttpMessageHandler
    {
        public readonly List<Uri> Seen = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            lock (Seen) Seen.Add(r.RequestUri!);
            var ends = DateTime.UtcNow.AddDays(12).AddHours(4).ToString("o");
            var body = r.RequestUri!.AbsolutePath == "/locks"
                ? "[{\"_id\":\"l1\",\"title\":\"Test Cage\",\"status\":\"locked\",\"role\":\"wearer\",\"endDate\":\"" + ends + "\",\"isTestLock\":true}]"
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static void Link()
    {
        SecretStore.Seed();
        new SecretChasterTokenStore().Write(new ChasterStoredTokens("acc", "ref", DateTime.UtcNow.AddHours(1)));
    }

    [Fact]
    public async Task SandboxReachesOnlyTheLoopbackOverride()
    {
        Link();
        try
        {
            var fake = new FakeChaster();
            Assert.Null(await ChasterHead.Create("sandbox", null, fake).GetLocksAsync());
            Assert.Null(await ChasterHead.Create("sandbox", "https://evil.example/", fake).GetLocksAsync());
            Assert.Empty(fake.Seen);

            var locks = await ChasterHead.Create("sandbox", "http://127.0.0.1:47999/", fake).GetLocksAsync();
            Assert.Equal("Test Cage", Assert.Single(locks!).Title);
            Assert.Equal("http://127.0.0.1:47999/locks?status=active", Assert.Single(fake.Seen).ToString());

            // Not sandboxed: the real hosts, untouched.
            Assert.Equal(new Uri(ChasterClient.ProxyBase), ChasterHead.Target(null, null));
        }
        finally { new SecretChasterTokenStore().Clear(); }
    }

    [Fact]
    public Task RailPadlockOpensTheTabAndItReadsTheLock() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        var oldLock = s.ChasterLockId;
        var dir = Directory.CreateTempSubdirectory("ccp-chaster-").FullName;
        Link();
        var fake = new FakeChaster();
        var chaster = new ChasterService(new ChasterClient(fake), new SecretChasterTokenStore(),
            Path.Combine(dir, "chaster_tab.json"), () => new ChasterOptions(false, "l1", new HashSet<string>()));
        ChasterHead.Service = chaster;
        s.ChasterLockId = "l1";
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var chip = shell.Named<ChasterRailChip>("ChasterRail")!;
            Assert.True(chip.IsVisible);

            var p = chip.TranslatePoint(new Point(chip.Bounds.Width / 2, 20), shell)!.Value;
            shell.MouseDown(p, MouseButton.Left);
            shell.MouseUp(p, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("chaster", shell.CurrentTab);
            var tab = shell.Named<ChasterTabView>("ChasterTab")!;
            Assert.True(tab.IsVisible);

            await chaster.RefreshLockAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Test Cage", tab.PillTexts);
            Assert.StartsWith("12", tab.HeroClockText);
            chip.Apply();
            Assert.Equal(1.0, chip.Opacity);
            Assert.StartsWith("12d", chip.ClockText);

            new SecretChasterTokenStore().Clear();
            await chaster.RefreshLockAsync();
            tab.Refresh();
            chip.Apply();
            Assert.Equal(0.5, chip.Opacity);          // dim, not hidden: still the door
            Assert.Empty(tab.PillTexts);
            Assert.True(tab.FindControl<StackPanel>("UnlinkedPanel")!.IsVisible);
        }
        finally
        {
            shell.Close();
            ChasterHead.Service = null;
            chaster.Dispose();
            s.ChasterLockId = oldLock;
            CoreSettings.ServiceProvider = null;
            new SecretChasterTokenStore().Clear();
            try { Directory.Delete(dir, true); } catch { }
        }
    });
}
