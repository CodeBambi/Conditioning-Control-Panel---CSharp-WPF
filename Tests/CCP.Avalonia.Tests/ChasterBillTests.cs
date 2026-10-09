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
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Chaster slice 5: the exit bill, the import confirm, the account badge and the booked
/// flash, against a fake Chaster (never the real API) and a memory-only SecretStore.</summary>
public sealed class ChasterBillTests
{
    private sealed class FakeChaster : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.RequestUri!.AbsolutePath switch
            {
                "/auth/profile" => "{\"username\":\"kitten\"}",
                "/locks" => "[{\"_id\":\"l1\",\"title\":\"Test Cage\",\"status\":\"locked\",\"role\":\"wearer\",\"endDate\":\"" + DateTime.UtcNow.AddDays(2).ToString("o") + "\"}]",
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    /// <summary>A linked service, the tab on with prices, and a shown shell.</summary>
    internal static async Task Run(Func<ChasterService, MainShellWindow, Task> body)
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        var (oldLock, oldTab, oldPaused, oldPrices) = (s.ChasterLockId, s.ChasterTabEnabled, s.ChasterPaused, s.ChasterPrices);
        var (oldGet, oldSet) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        var secrets = new Dictionary<string, string?>();   // memory only: never the OS keyring
        CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        s.ChasterLockId = "l1";
        s.ChasterTabEnabled = true;
        s.ChasterPaused = false;
        s.ChasterPrices = new List<string> { "attention", "typo" };
        var dir = Directory.CreateTempSubdirectory("ccp-chaster-bill-").FullName;
        new SecretChasterTokenStore().Write(new ChasterStoredTokens("acc", "ref", DateTime.UtcNow.AddHours(1)));
        var chaster = new ChasterService(new ChasterClient(new FakeChaster()), new SecretChasterTokenStore(), Path.Combine(dir, "chaster_tab.json"),
            () => new ChasterOptions(CoreSettings.Current.ChasterTabEnabled, CoreSettings.Current.ChasterLockId,
                new HashSet<string>(CoreSettings.Current.ChasterPrices ?? new List<string>()), Paused: CoreSettings.Current.ChasterPaused));
        ChasterHead.Service = chaster;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            await body(chaster, shell);
        }
        finally
        {
            shell.Close();
            ChasterHead.Service = null;
            chaster.Dispose();
            new SecretChasterTokenStore().Clear();
            try { Directory.Delete(dir, true); } catch { }
            (s.ChasterLockId, s.ChasterTabEnabled, s.ChasterPaused, s.ChasterPrices) = (oldLock, oldTab, oldPaused, oldPrices);
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (oldGet, oldSet);
            service.SaveImmediate(); CoreSettings.ServiceProvider = null;
        }
    }

    internal static void Click(Window w, Control c)
    {
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task ExitHoldsForTheBillThenExitsOnAClick() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, shell) =>
    {
        Assert.True(chaster.Note("attention").Booked);
        var closed = false;
        shell.Closed += (_, _) => closed = true;

        shell.RequestExit();                           // the tray's Exit and Settings' Exit both land here
        shell.ExitBillTimer!.Stop();                   // a slow runner must not tick the bill shut before the click
        Dispatcher.UIThread.RunJobs();
        var root = shell.Named<Grid>("RootGrid")!;
        var overlay = root.Children.OfType<Grid>().Single(g => g.Name == "ExitBillOverlay");
        Assert.False(closed);                          // held: the bill is up
        var receipt = overlay.GetVisualDescendants().OfType<ChasterReceiptView>().Single();
        Assert.StartsWith(Loc.Get("chaster_bill_stamp") + " +", receipt.StampText);
        Assert.Contains(overlay.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.GetF("chaster_bill_closing", MainShellWindow.ExitBillSeconds));

        Click(shell, overlay);
        Assert.DoesNotContain(overlay, root.Children);
        Assert.True(closed);                           // the click runs the exit path again, past the bill
        await Task.CompletedTask;
    }));

    [Fact]
    public Task TheBillClosesByItselfAfterItsSeconds() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, shell) =>
    {
        chaster.Note("attention");
        var closed = false;
        shell.Closed += (_, _) => closed = true;
        shell.RequestExit();
        shell.ExitBillTimer!.Stop();                   // only the driven ticks below count
        var countdown = shell.Named<Grid>("RootGrid")!.Children.OfType<Grid>().Single(g => g.Name == "ExitBillOverlay")
            .GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Loc.GetF("chaster_bill_closing", MainShellWindow.ExitBillSeconds));
        for (var i = 1; i < MainShellWindow.ExitBillSeconds; i++) shell.ExitBillTick!();   // the timer's seconds, driven, no sleeps
        Assert.False(closed);
        Assert.Equal(Loc.GetF("chaster_bill_closing", 1), countdown.Text);
        shell.ExitBillTick!();
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
        Assert.DoesNotContain(shell.Named<Grid>("RootGrid")!.Children.OfType<Grid>(), g => g.Name == "ExitBillOverlay");
        await Task.CompletedTask;
    }));

    [Fact]
    public Task DoublePanicExitsAtOnceWithNoBill() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, shell) =>
    {
        chaster.Note("attention");                     // a bill that the user's own Exit would show
        var closed = false;
        shell.Closed += (_, _) => closed = true;
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
        shell.HandlePanicKeyPress(t0);
        shell.HandlePanicKeyPress(t0.AddSeconds(0.5));  // WPF MainWindow.xaml.cs:1841: straight to Shutdown
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
        Assert.DoesNotContain(shell.Named<Grid>("RootGrid")!.Children.OfType<Grid>(), g => g.Name == "ExitBillOverlay");
        await Task.CompletedTask;
    }));

    [Fact]
    public Task NoBillWhenTheTabIsOffOrTheBillIsEmpty() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, shell) =>
    {
        Assert.False(shell.TryShowExitBill(() => { }));   // nothing booked yet
        chaster.Note("attention");
        CoreSettings.Current.ChasterTabEnabled = false;
        Assert.False(shell.TryShowExitBill(() => { }));
        CoreSettings.Current.ChasterTabEnabled = true;
        Assert.True(shell.TryShowExitBill(() => { }));
        Assert.False(shell.TryShowExitBill(() => { }));    // once per run
        await Task.CompletedTask;
    }));

    [Fact]
    public Task BadgeShowsTheLinkedAccount() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, shell) =>
    {
        var badge = new ChasterAccountBadge();
        shell.Named<Grid>("RootGrid")!.Children.Add(badge);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.Get("chaster_account_name"), badge.NameText.Text);   // before the profile arrives
        await chaster.EnsureProfileAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("kitten", badge.NameText.Text);
        Assert.Contains(badge.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "K");
        Assert.NotNull(shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.ChasterTabView>("ChasterTab")!.FindControl<ChasterAccountBadge>("AccountBadge"));
    }));

    [Fact]
    public Task ABookingFloatsOffThePadlockAndCoalesces() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, shell) =>
    {
        shell.InitializeChasterFlash(chaster);
        var first = chaster.Note("attention");
        Dispatcher.UIThread.RunJobs();
        var layer = AdornerLayer.GetAdornerLayer(shell.Named<ChasterRailChip>("ChasterRail")!)!;
        var flash = layer.Children.OfType<ChasterBookedFlash>().Single();
        Assert.Equal(CircesTab.Format(first.AppliedSeconds), flash.Figure.Text);
        Assert.Equal(Color.FromUInt32(BookedFlashPlan.AddColour), ((ISolidColorBrush)flash.Figure.Foreground!).Color);

        var second = chaster.Note("typo");                 // inside the coalescing window: one figure, bigger number
        Dispatcher.UIThread.RunJobs();
        Assert.Same(flash, layer.Children.OfType<ChasterBookedFlash>().Single());
        Assert.Equal(CircesTab.Format(first.AppliedSeconds + second.AppliedSeconds), flash.Figure.Text);
        await Task.CompletedTask;
    }));

    [Fact]
    public Task ImportConfirmAllowsOnlyOnAYes() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var owner = new Window { Width = 800, Height = 600 };
        owner.Show();
        try
        {
            Assert.False(await ChasterImportConfirmDialog.AskAsync(owner, new KeywordTriggerChasterImport.Summary(0, 0)));
            Assert.Empty(owner.OwnedWindows);              // nothing to allow, nothing asked

            foreach (var (button, expected) in new[] { ("BtnAllow", true), ("BtnSkip", false) })
            {
                var ask = ChasterImportConfirmDialog.AskAsync(owner, new KeywordTriggerChasterImport.Summary(3, 45));
                Dispatcher.UIThread.RunJobs();
                var dlg = (ChasterImportConfirmDialog)owner.OwnedWindows.Single();
                Assert.Equal(Loc.GetF("chaster_import_detail", 3, 45), dlg.FindControl<TextBlock>("TxtDetail")!.Text);
                Click(dlg, dlg.FindControl<Button>(button)!);
                Assert.Equal(expected, await ask);
            }
        }
        finally { owner.Close(); }
    });

    /// <summary>The tab's own receipt (WPF StatRun_Click): the third number opens the bill under the
    /// numbers, a language switch repaints it, a second click folds it away.</summary>
    [Fact]
    public Task TheRunNumberOpensTheReceiptAndALanguageSwitchRepaintsIt() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, _) =>
    {
        Assert.True(chaster.Note("attention").Booked);
        var tab = new ConditioningControlPanel.Avalonia.Views.Tabs.ChasterTabView();
        var w = new Window { Width = 1200, Height = 900, Content = tab };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var host = tab.FindControl<Border>("ReceiptHost")!;
            var run = tab.FindControl<TextBlock>("TxtRun")!;
            Assert.Equal(CircesTab.Format(chaster.Bill().NetSeconds), run.Text);
            Assert.False(host.IsVisible);
            Click(w, tab.FindControl<Border>("StatRun")!);
            Assert.True(host.IsVisible);
            var receipt = tab.FindControl<ChasterReceiptView>("Receipt")!;
            Assert.Equal("NET " + CircesTab.Format(chaster.Bill().NetSeconds), receipt.StampText);

            LocalizationManager.Instance.SetLanguage("de");
            Dispatcher.UIThread.RunJobs();
            Assert.StartsWith("NETTO ", receipt.StampText);

            Click(w, tab.FindControl<Border>("StatRun")!);
            Assert.False(host.IsVisible);
        }
        finally { LocalizationManager.Instance.SetLanguage("en"); w.Close(); }
        await Task.CompletedTask;
    }));

    /// <summary>The calendar sheet (WPF BuildCalendar/Cell): one square per Core LockCalendar day, a
    /// served day crossed, tonight ringed, the rest padlocked, and gone with no lock.</summary>
    [Fact]
    public Task TheCalendarDrawsOneSquareADayFromCore() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, _) =>
    {
        var tab = new ConditioningControlPanel.Avalonia.Views.Tabs.ChasterTabView();
        var w = new Window { Width = 1200, Height = 900, Content = tab };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var today = new DateTime(2026, 9, 10, 12, 0, 0);
            var (start, end) = (today.AddDays(-2), today.AddDays(3));
            tab.BuildCalendar((start, end), today);
            var grid = tab.FindControl<UniformGrid>("Calendar")!;
            var cells = LockCalendar.CellsFor(start, end, today);
            Assert.True(tab.FindControl<Panel>("CalendarRow")!.IsVisible);
            Assert.Equal(cells.Count, grid.Children.Count);
            var tips = grid.Children.Select(c => ToolTip.GetTip(c) as string).ToList();
            Assert.Equal(Loc.GetF("chaster_cal_served", 1), tips[0]);
            Assert.Equal(Loc.Get("chaster_chain_tonight"), tips[2]);
            Assert.Equal(Loc.GetF("chaster_cal_locked", 4), tips[3]);
            Assert.Single(grid.Children, c => ((Border)c).GetVisualDescendants().OfType<Canvas>().Any() && ToolTip.GetTip(c) as string == tips[0]);

            tab.BuildCalendar(null, today);
            Assert.False(tab.FindControl<Panel>("CalendarRow")!.IsVisible);

            // CalendarSpan: Chaster's UTC instants become local days; no snapshot, no end or a hidden timer is no sheet.
            var (startUtc, endUtc) = (new DateTime(2026, 9, 8, 23, 30, 0, DateTimeKind.Utc), new DateTime(2026, 9, 13, 0, 30, 0, DateTimeKind.Utc));
            var snap = new LockSnapshot("l1", "Cage", endUtc, false, false, false, endUtc) { StartedAtUtc = startUtc };
            var span = ConditioningControlPanel.Avalonia.Views.Tabs.ChasterTabView.CalendarSpan(snap, today)!.Value;
            Assert.Equal((startUtc.ToLocalTime(), endUtc.ToLocalTime()), span);
            Assert.Equal(DateTimeKind.Local, span.Start.Kind);
            Assert.Equal(DateTimeKind.Local, span.End.Kind);
            Assert.Equal(today, ConditioningControlPanel.Avalonia.Views.Tabs.ChasterTabView.CalendarSpan(snap with { StartedAtUtc = null }, today)!.Value.Start);
            Assert.Null(ConditioningControlPanel.Avalonia.Views.Tabs.ChasterTabView.CalendarSpan(null, today));
            Assert.Null(ConditioningControlPanel.Avalonia.Views.Tabs.ChasterTabView.CalendarSpan(snap with { EndsAtUtc = null }, today));
            Assert.Null(ConditioningControlPanel.Avalonia.Views.Tabs.ChasterTabView.CalendarSpan(snap with { TimerHidden = true }, today));
            tab.BuildCalendar(snap with { StartedAtUtc = DateTime.UtcNow.AddDays(-2), EndsAtUtc = DateTime.UtcNow.AddDays(3) });
            Assert.True(tab.FindControl<Panel>("CalendarRow")!.IsVisible);
            tab.BuildCalendar((LockSnapshot?)null);                  // the null-snapshot path the tab takes with no lock
            Assert.False(tab.FindControl<Panel>("CalendarRow")!.IsVisible);
        }
        finally { w.Close(); }
        await Task.CompletedTask;
    }));

    /// <summary>The keys (WPF Preset_Click): a press rewrites the price list to the preset's set and
    /// lights only that key; the fourth key lights for a hand-built set and rewrites nothing.</summary>
    [Fact]
    public Task AKeyRewritesThePricesAndLightsItself() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, _) =>
    {
        var tab = new ConditioningControlPanel.Avalonia.Views.Tabs.ChasterTabView();
        var w = new Window { Width = 1200, Height = 1400, Content = tab };
        w.Show();
        var (oldDay, oldBacklog) = (CoreSettings.Current.ChasterDayLimit, CoreSettings.Current.ChasterBacklogLimit);
        try
        {
            Dispatcher.UIThread.RunJobs();
            var custom = tab.FindControl<ToggleButton>("BtnPresetCustom")!;
            Assert.True(custom.IsChecked);                       // attention + typo is no preset
            Click(w, custom);
            Assert.Equal(new[] { "attention", "typo" }, CoreSettings.Current.ChasterPrices);
            Assert.True(custom.IsChecked);

            // Gentle has no heat row, so Circe's mood hides; Strict has one, so the key shows it (WPF RefreshPresets :1123).
            var mood = tab.FindControl<CircesMoodMeter>("MoodMeter")!;
            Click(w, tab.FindControl<ToggleButton>("BtnPresetGentle")!);
            Assert.False(mood.IsVisible);

            var strict = tab.FindControl<ToggleButton>("BtnPresetStrict")!;
            (CoreSettings.Current.ChasterDayLimit, CoreSettings.Current.ChasterBacklogLimit) = (new LimitSetting(1, 0, null), new LimitSetting(1, 0, null));
            var (day, backlog) = TabPresets.RequestLimits(TabPresets.Find(TabPresets.Strict)!,
                CoreSettings.Current.ChasterDayLimit, CoreSettings.Current.ChasterBacklogLimit, DateTime.UtcNow);
            Assert.NotEqual((1, 0), (day.Minutes, day.PendingMinutes));          // the press has something to change
            Assert.NotEqual((1, 0), (backlog.Minutes, backlog.PendingMinutes));
            Click(w, strict);
            Assert.Equal(TabPresets.Apply(TabPresets.Strict), CoreSettings.Current.ChasterPrices);
            Assert.True(mood.IsVisible);
            Assert.Equal(Loc.Get(chaster.Mood!.Value.WordKey), mood.Word);
            Assert.Equal(chaster.Mood!.Value.FactorText, mood.Factor);
            Assert.Equal((day.Minutes, day.PendingMinutes), (CoreSettings.Current.ChasterDayLimit.Minutes, CoreSettings.Current.ChasterDayLimit.PendingMinutes));
            Assert.Equal((backlog.Minutes, backlog.PendingMinutes), (CoreSettings.Current.ChasterBacklogLimit.Minutes, CoreSettings.Current.ChasterBacklogLimit.PendingMinutes));
            Assert.True(strict.IsChecked);
            Assert.False(custom.IsChecked);
            Assert.False(tab.FindControl<ToggleButton>("BtnPresetGentle")!.IsChecked);
            Assert.False(string.IsNullOrEmpty(tab.FindControl<TextBlock>("TxtStakesStrict")!.Text));
        }
        finally
        {
            w.Close();
            (CoreSettings.Current.ChasterDayLimit, CoreSettings.Current.ChasterBacklogLimit) = (oldDay, oldBacklog);
        }
        await Task.CompletedTask;
    }));
}
