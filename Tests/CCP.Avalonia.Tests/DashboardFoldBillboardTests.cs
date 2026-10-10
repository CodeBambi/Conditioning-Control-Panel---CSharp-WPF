using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.DashboardFold + DashboardBillboard: the saved fold, the chevron, the
/// run-only reveal, and the billboard that fills the freed row with its gated 12 s clock.</summary>
public sealed class DashboardFoldBillboardTests
{
    private static void Click(Control c) => c.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task FoldChevronRevealAndBillboardFollowTheUser() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var oldOpen = MainShellWindow.BillboardOpenUrl;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.DashboardBrowserCollapsed = true;
        s.MotionLevel = MotionLevel.Off;   // no ease: the settle is immediate
        s.PerformanceMode = false;
        s.OfflineMode = false;
        var opened = new List<string>();
        MainShellWindow.BillboardOpenUrl = opened.Add;
        var clock = new SteppedClock();
        BillboardCardHost.Time = clock;
        s.BillboardSnoozedUntil.Clear();

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            var dash = shell.FindControl<SettingsTabView>("SettingsTab")!;
            T C<T>(string n) where T : Control => dash.FindControl<T>(n)!;
            var column = C<Grid>("BrowserColumn");
            var body = C<Border>("BrowserFoldBody");
            var billboard = C<Border>("DashBillboard");
            BillboardCardHost Board() => shell.BillboardHost!;
            void Step(double seconds) { clock.Now += (long)(seconds * TimeSpan.TicksPerSecond); Board().HoldTick(); }

            // Launch folded (the default): header only, the Tonight Board in the freed row, still at Motion Off.
            Assert.False(body.IsVisible);
            Assert.True(billboard.IsVisible);
            Assert.Equal("▾", C<TextBlock>("TxtFoldBrowser").Text);
            Assert.True(column.RowDefinitions[0].Height.IsAuto);
            Assert.True(column.RowDefinitions[1].Height.IsStar);
            Assert.Equal(Loc.Get("tooltip_browser_unfold"), ToolTip.GetTip(C<Button>("BtnFoldBrowser")));
            Assert.Same(Board(), C<Grid>("BillboardHostSlot").Children[0]);
            // Free tier, house and tips only: the pinned Discord card and one rotating house card.
            Assert.Equal(new[] { "house.discord", "house.webapp" }, Board().Deck.Cards.Select(c => c.Spec.Id));
            Assert.Equal("house.discord", Board().CurrentCard!.Spec.Id);
            Assert.Equal(2, Board().ChipButtons.Count);
            Assert.Equal(Loc.Get("billboard_discord_title"), ToolTip.GetTip((Button)Board().ChipButtons[0]));
            Assert.False(Board().HoldRunning);

            // Motion back to Full: the hold runs; 12 s on the stepped clock moves the deck on.
            s.MotionLevel = MotionLevel.Full;
            global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(Board().HoldRunning);
            Step(6);
            Assert.Equal(0, Board().Deck.Index);
            Assert.InRange(Board().HoldProgress, 0.49, 0.51);
            Step(6);
            Assert.Equal("house.webapp", Board().CurrentCard!.Spec.Id);

            // Another tab: the hold stops where it stands (P01); Home again: it runs on.
            Step(3);
            shell.ShowTab("quests");
            Dispatcher.UIThread.RunJobs();
            Assert.False(Board().HoldRunning);
            Step(30);
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.True(Board().HoldRunning);
            Assert.InRange(Board().HoldProgress, 0.24, 0.26);

            // A dot picks its card; the card's button opens its link through the one opener.
            s.MotionLevel = MotionLevel.Off;
            global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(Board().HoldRunning);
            Click((Button)Board().ChipButtons[0]);
            Assert.Equal("house.discord", Board().CurrentCard!.Spec.Id);
            Click(Board().Cta);
            Assert.Equal(new[] { DiscordLinks.Invite }, opened);

            // The snooze x: the card leaves for a week (saved), the toast names it.
            Click(Board().SnoozeButton);
            Assert.True(s.BillboardSnoozedUntil.ContainsKey("house.discord"));
            Assert.Equal(string.Format(Loc.Get("billboard_deck_snoozed"), Loc.Get("billboard_discord_title")), Board().ToastText);
            Assert.DoesNotContain(Board().Deck.Cards, c => c.Spec.Id == "house.discord");

            // The chevron writes the preference: open card, billboard gone, clock stopped. Motion Off
            // so the fold settles at once (at Full the 180 ms ease keeps the body hidden until it lands).
            s.MotionLevel = MotionLevel.Off;
            Click(C<Button>("BtnFoldBrowser"));
            Dispatcher.UIThread.RunJobs();
            Assert.False(s.DashboardBrowserCollapsed);
            Assert.True(body.IsVisible);
            Assert.False(billboard.IsVisible);
            s.MotionLevel = MotionLevel.Full;   // the board is hidden: no hold even at Full motion
            global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(Board().HoldRunning);
            s.MotionLevel = MotionLevel.Off;
            Assert.Equal("▴", C<TextBlock>("TxtFoldBrowser").Text);
            Assert.True(column.RowDefinitions[0].Height.IsStar);
            Assert.Equal(0, column.RowDefinitions[1].Height.Value);

            // Fold again, then the app calls the browser (reload): it opens for this run only.
            Click(C<Button>("BtnFoldBrowser"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(s.DashboardBrowserCollapsed);
            Assert.False(body.IsVisible);
            Click(C<Button>("BtnReloadBrowser"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(body.IsVisible);
            Assert.True(s.DashboardBrowserCollapsed);   // a reveal is not a preference
            Assert.False(shell.BrowserFolded);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            MainShellWindow.BillboardOpenUrl = oldOpen;
            BillboardCardHost.Time = TimeProvider.System;
            s.BillboardSnoozedUntil.Clear();
            s.DashboardBrowserCollapsed = true;
            s.MotionLevel = MotionLevel.Full;
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });

    /// <summary>A normal launch: folded at Full motion, the clock runs once the window is shown,
    /// with no motion-gate event behind it (review P1).</summary>
    [Fact]
    public Task FoldedLaunchAtFullMotionStartsTheClock() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.DashboardBrowserCollapsed = true;
        s.MotionLevel = MotionLevel.Full;
        s.PerformanceMode = false;
        var shell = new MainShellWindow();
        try
        {
            Assert.False(shell.BillboardHost?.HoldRunning ?? false);   // not on screen yet
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<Border>("DashBillboard")!.IsEffectivelyVisible);
            Assert.True(shell.BillboardHost!.HoldRunning);
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.BillboardHost.HoldRunning);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF c0c67ff47 (BoardSilentTests): the Tonight Board plays no sound. The host and its
    /// shell partial reach no audio at all (comments and strings stripped, P23).</summary>
    [Fact]
    public void TheTonightBoardPlaysNoSound()
    {
        foreach (var rel in new[] { "CCP.Avalonia/Controls/Billboard/BillboardCardHost.cs", "CCP.Avalonia/Views/Windows/MainShellWindow.DashboardBillboard.cs" })
        {
            var code = File.ReadAllText(Path.Combine(RepoRoot(), rel));
            code = Regex.Replace(code, @"//[^\n]*|/\*.*?\*/|""(?:\\.|[^""\\])*""", "", RegexOptions.Singleline);
            Assert.DoesNotMatch(@"Sfx|Audio|Sound|Chime|NAudio|(?<![A-Za-z])Play\(", code);   // a Play( call; Core ArtShouldPlay( is a rule, not a sound
        }
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "ConditioningControlPanel.sln"))) d = d.Parent;
        return d!.FullName;
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
