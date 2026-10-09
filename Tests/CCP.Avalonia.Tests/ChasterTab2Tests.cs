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
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Chaster slice 2: unlink, the switch + consent, pause, the rail chip's peek and glow,
/// and the hooks - all against a fake Chaster.</summary>
public sealed class ChasterTab2Tests
{
    private sealed class FakeChaster : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        /// <summary>Extra JSON members on the lock (e.g. its permissions).</summary>
        public string LockExtra = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            lock (Paths) Paths.Add(r.RequestUri!.AbsolutePath);
            var ends = DateTime.UtcNow.AddDays(12).AddHours(4).ToString("o");
            var body = r.RequestUri!.AbsolutePath == "/locks"
                ? "[{\"_id\":\"l1\",\"title\":\"Test Cage\",\"status\":\"locked\",\"role\":\"wearer\",\"endDate\":\"" + ends + "\"" + LockExtra + "}]"
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    /// <summary>WPF 2b7d742d0: a pick on the page while the demo service runs never reaches the
    /// real settings (the demo picks its fake lock for itself); a real service saves it.</summary>
    [Fact]
    public Task DemoPickNeverSavesTheLockId() => AvaloniaTestDispatcher.RunAsync(() => Run((real, fake, s) =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-chaster-demo-").FullName;
        var demo = new ChasterService(new ChasterClient(fake), new SecretChasterTokenStore(), Path.Combine(dir, "chaster_tab.json"),
            () => ChasterOptions.Off) { IsDemo = true };
        var tab = new ChasterTabView();
        try
        {
            ChasterHead.Service = demo;
            tab.PickLock("demo1");
            Assert.Equal("l1", s.ChasterLockId);
            ChasterHead.Service = real;
            tab.PickLock("l2");
            Assert.Equal("l2", s.ChasterLockId);
        }
        finally
        {
            ChasterHead.Service = real;
            demo.Dispose();
            try { Directory.Delete(dir, true); } catch { }
        }
        return Task.CompletedTask;
    }));

    /// <summary>WPF RefreshSetupHint/RefreshPills after 61a331c1d and c8dead5b3: a tab that is on
    /// with no row switched on says nothing can count; once a row is on, a lock whose keyholder
    /// switched adding off says so on the line and with an "adds off" pill.</summary>
    [Fact]
    public Task SetupLineSaysNothingCountsThenAddsBlocked() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, fake, s) =>
    {
        s.ChasterTabEnabled = true;
        s.ChasterPrices = new List<string>();
        fake.LockExtra = ",\"permissions\":{\"grants\":[{\"resource\":\"lock.time.add\",\"subjects\":{\"wearer\":[]}}]}";
        var tab = new ChasterTabView();
        var w = new Window { Width = 1200, Height = 800, Content = tab };
        w.Show();
        try
        {
            await chaster.RefreshLockAsync();
            Dispatcher.UIThread.RunJobs();
            tab.Refresh();
            var hint = tab.FindControl<TextBlock>("SetupHint")!;
            Assert.True(hint.IsVisible);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("chaster_setup_nothing"), hint.Text);

            s.ChasterPrices = new List<string> { TabPrices.All.First(p => !TabPrices.NeverPriced.Contains(p.Id)).Id };
            tab.Refresh();
            Assert.True(chaster.AddsBlocked);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("chaster_state_keyholder_blocked"), hint.Text);
            Assert.Contains(ConditioningControlPanel.Localization.Loc.Get("chaster_pill_blocked"), tab.PillTexts);
        }
        finally { w.Close(); }
    }));

    /// <summary>A linked service whose options read CoreSettings, as ChasterHead's do.</summary>
    private static async Task Run(Func<ChasterService, FakeChaster, ConditioningControlPanel.Models.AppSettings, Task> body)
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.ChasterLockId = "l1";
        s.ChasterTabEnabled = false;
        s.ChasterConsentSeen = false;
        s.ChasterPaused = false;
        var dir = Directory.CreateTempSubdirectory("ccp-chaster2-").FullName;
        SecretStore.Seed();
        new SecretChasterTokenStore().Write(new ChasterStoredTokens("acc", "ref", DateTime.UtcNow.AddHours(1)));
        var fake = new FakeChaster();
        var chaster = new ChasterService(new ChasterClient(fake), new SecretChasterTokenStore(), Path.Combine(dir, "chaster_tab.json"),
            () => new ChasterOptions(CoreSettings.Current.ChasterTabEnabled, CoreSettings.Current.ChasterLockId,
                new HashSet<string>(CoreSettings.Current.ChasterPrices ?? new List<string>()), Paused: CoreSettings.Current.ChasterPaused));
        ChasterHead.Service = chaster;
        try { await body(chaster, fake, s); }
        finally
        {
            ChasterHead.Service = null;
            chaster.Dispose();
            new SecretChasterTokenStore().Clear();
            try { Directory.Delete(dir, true); } catch { }
            service.SaveImmediate(); CoreSettings.ServiceProvider = null;
        }
    }


    private static void Click(Window w, Control c)
    {
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    // WPF ChasterTabView.xaml.cs:99: the ticks run only while the page is on screen.
    [Fact]
    public Task HidingTheTabStopsBothTicksAndShowingResumesThem() => AvaloniaTestDispatcher.RunAsync(() => Run((_, _, _) =>
    {
        var tab = new ChasterTabView();
        var w = new Window { Width = 1200, Height = 800, Content = tab };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.ClockTicking && tab.SlowTick.IsEnabled);
            tab.IsVisible = false;
            Assert.False(tab.ClockTicking);
            Assert.False(tab.SlowTick.IsEnabled);
            tab.IsVisible = true;
            Assert.True(tab.ClockTicking && tab.SlowTick.IsEnabled);
        }
        finally { w.Close(); }
        Assert.False(tab.ClockTicking || tab.SlowTick.IsEnabled);
        return Task.CompletedTask;
    }));

    [Fact]
    public Task SlowTickRefreshesTheHero() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, _, _) =>
    {
        var tab = new ChasterTabView();
        var w = new Window { Width = 1200, Height = 800, Content = tab };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TimeSpan.FromSeconds(30), tab.SlowTick.Interval);
            var title = tab.FindControl<Control>("HeroTitle")!;
            Assert.True(title.IsVisible);
            title.IsVisible = false; // RefreshHero repaints it for a linked account
            tab.SlowTick.Interval = TimeSpan.FromMilliseconds(20);
            for (var i = 0; i < 60 && !title.IsVisible; i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
            Assert.True(title.IsVisible);
        }
        finally { w.Close(); }
    }));

    [Fact]
    public Task UnlinkAsksThenRevokesAndClearsTheToken() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, fake, _) =>
    {
        var tab = new ChasterTabView();
        var w = new Window { Width = 1200, Height = 800, Content = tab };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.FindControl<Border>("AccountStrip")!.IsVisible);

            Assert.False(await ChasterTabView.ConfirmAndUnlinkAsync(w, (_, _, _) => Task.FromResult(false)));
            Assert.True(chaster.IsLinked);
            Assert.DoesNotContain("/chaster/revoke", fake.Paths);

            string? asked = null;
            Assert.True(await ChasterTabView.ConfirmAndUnlinkAsync(w, (_, title, _) => { asked = title; return Task.FromResult(true); }));
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("chaster_unlink_confirm_title"), asked);
            Assert.False(chaster.IsLinked);
            Assert.Null(new SecretChasterTokenStore().Read());
            Assert.Contains("/chaster/revoke", fake.Paths);
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.FindControl<StackPanel>("UnlinkedPanel")!.IsVisible);
            Assert.False(tab.FindControl<Border>("AccountStrip")!.IsVisible);
            Assert.False(tab.FindControl<Border>("SwitchPill")!.IsVisible);
        }
        finally { w.Close(); }
    }));

    [Fact]
    public Task SwitchAsksConsentOnceAndPauseToggles() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, _, v) =>
    {
        var tab = new ChasterTabView();
        var w = new Window { Width = 1400, Height = 900, Content = tab };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var chk = tab.FindControl<CheckBox>("ChkTab")!;
            var consent = tab.FindControl<Border>("ConsentCard")!;
            Click(w, chk);
            Assert.True(consent.IsVisible);           // the facts first
            Assert.False(chk.IsChecked == true);
            Assert.False(v.ChasterTabEnabled);

            Click(w, tab.FindControl<Button>("BtnConsentOk")!);
            Assert.False(consent.IsVisible);
            Assert.True(v.ChasterTabEnabled && v.ChasterConsentSeen);
            Assert.True(chk.IsChecked == true);

            Click(w, chk);                            // off, no consent again
            Assert.False(v.ChasterTabEnabled);
            Click(w, chk);
            Assert.True(v.ChasterTabEnabled);
            Assert.False(consent.IsVisible);

            var txt = tab.FindControl<TextBlock>("TxtPause")!;
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("chaster_pause"), txt.Text);
            Click(w, tab.FindControl<Button>("BtnPause")!);
            Assert.True(v.ChasterPaused && chaster.IsPaused);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("chaster_paused"), txt.Text);
            Assert.True(tab.FindControl<global::Avalonia.Controls.Shapes.Path>("PlayArrow")!.IsVisible);
            Click(w, tab.FindControl<Button>("BtnPause")!);
            Assert.False(v.ChasterPaused);
            await Task.CompletedTask;
        }
        finally { w.Close(); }
    }));

    [Fact]
    public Task ChipPeeksOnHoverAndItsGlowFollowsTheRing() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, _, v) =>
    {
        var chip = new ChasterRailChip { Width = 72 };
        var w = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { chip } } };
        w.Show();
        try
        {
            await chaster.RefreshLockAsync();
            Dispatcher.UIThread.RunJobs();
            chip.Apply();
            Assert.Equal(Color.FromRgb(0xFF, 0x69, 0xB4), chip.GlowColour);

            w.MouseMove(chip.TranslatePoint(new Point(36, 20), w)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.True(chip.Peek.IsOpen);
            Assert.StartsWith("Test Cage|12|", chip.PeekText);

            v.ChasterPaused = true;
            chip.Apply();
            Assert.Equal(Color.FromRgb(0x9A, 0xA0, 0xB8), chip.GlowColour);
            Assert.EndsWith(ConditioningControlPanel.Localization.Loc.Get("chaster_chip_paused_tip"), chip.PeekText);

            w.MouseMove(new Point(390, 290));
            Dispatcher.UIThread.RunJobs();
            Assert.False(chip.Peek.IsOpen);
        }
        finally { w.Close(); }
    }));

    [Fact]
    public Task HooksBookALevelUp() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, _, v) =>
    {
        var s = v;
        s.ChasterTabEnabled = true;
        s.ChasterPrices = new List<string> { "levelup", "attention" };
        s.OfflineMode = true;
        s.OfflineUsername = "chaster-test";
        s.PlayerLevel = 3;
        Assert.True(chaster.Note("attention").Booked);   // something owed, so the earn has room
        var booked = new List<string>();
        chaster.Booked += (id, _) => booked.Add(id);
        var detach = ChasterHead.Attach(chaster, null);
        try
        {
            // attach-once (WPF's _attached guard): a second call adds no handler
            var levelUpHandlers = typeof(ProgressionBank).GetField("LevelUp", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            int Count() => ((Delegate?)levelUpHandlers.GetValue(null))?.GetInvocationList().Length ?? 0;
            var before = Count();
            ChasterHead.Attach(chaster, null);
            Assert.Equal(before, Count());
            ProgressionBank.Add(XpCurve.GetXPForLevel(3, XpCurve.EpochOf(s)), "Quest");
            Assert.Equal(4, s.PlayerLevel);
            Assert.Contains("levelup", booked);
        }
        finally { detach(); }
        await Task.CompletedTask;
    }));
}
