using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>THE SECTION RAIL (WPF MainWindow.NavRail.cs / NavSectionRailTests, cd426fe36): one
/// always-labelled row per NavSections section, Social included, the lit row following ShowTab, a row
/// returning to its section's last tab, Ctrl+1..7, the Back row keeping its space, and Ctrl+K held
/// toggling once. Driven from the shell constructor (InitializeNavRail) through real clicks/keys.</summary>
public sealed class NavSectionRailTests
{
    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }

    private static void Run(Action<MainShellWindow> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var lastBefore = s.NavLastTabBySection;
        s.NavLastTabBySection = null;
        var w = new MainShellWindow();
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            body(w);
        }
        finally
        {
            SettingsPaletteWindow.CloseIfOpen();
            SettingsPaletteWindow.ChordClock = TimeProvider.System;
            SettingsPaletteWindow.ChordReleased();
            w.Close();
            s.NavLastTabBySection = lastBefore;
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static string[] Lit(MainShellWindow w) =>
        w.NavSectionRowsForTests.Where(r => r.Button.Classes.Contains("active")).Select(r => r.Section).ToArray();

    [Fact]
    public void RowsFollowNavSectionsOrder_SocialIncluded_LabelsInCaps() => Run(w =>
    {
        Assert.Equal(NavSections.Order.Select(s => s.Key), w.NavSectionRowsForTests.Select(r => r.Section));
        foreach (var row in w.NavSectionRowsForTests)
        {
            var key = NavSections.Find(row.Section)!.LabelKey;
            Assert.Equal(Loc.Get(key).ToUpper(System.Globalization.CultureInfo.CurrentUICulture), row.Label!.Text);
            Assert.True(row.Button.Focusable);   // keyboard reach (P17)
            Assert.Equal(Loc.Get(key), global::Avalonia.Automation.AutomationProperties.GetName(row.Button));   // WPF MainWindow.xaml:475
        }
        Assert.Equal(new[] { NavSections.Home }, Lit(w));   // the app lands on the dashboard
    });

    [Fact]
    public void SocialRowOpensTheLobby_FriendsAndLeashArePillsThereNotUnderYou() => Run(w =>
    {
        Click(w.Named<Button>("DoorSocial")!);
        Assert.Equal("availablesubjects", w.CurrentTab);
        Assert.Equal(new[] { NavSections.Social }, Lit(w));
        Assert.Contains("friends", w.PageStrip!.PillKeys);
        Assert.Contains("leash", w.PageStrip.PillKeys);

        Click(w.PageStrip.PillFor("friends")!);
        Assert.Equal("friends", w.CurrentTab);
        Assert.Equal(new[] { NavSections.Social }, Lit(w));   // still Social, not You

        Click(w.Named<Button>("DoorYou")!);
        Assert.Equal(new[] { NavSections.You }, Lit(w));
        Assert.DoesNotContain("friends", w.PageStrip.PillKeys);
        Assert.DoesNotContain("leash", w.PageStrip.PillKeys);
    });

    [Fact]
    public void ARowReturnsToItsSectionsLastTab_TheGearOpensSettings() => Run(w =>
    {
        w.ShowTab("leash");
        w.ShowTab("presets");
        Assert.Equal(new[] { NavSections.Studio }, Lit(w));
        Click(w.Named<Button>("DoorSocial")!);
        Assert.Equal("leash", w.CurrentTab);
        Click(w.Named<Button>("DoorStudio")!);
        Assert.Equal("presets", w.CurrentTab);
        Click(w.Named<Button>("DoorSettings")!);
        Assert.Equal("appsettings", w.CurrentTab);
        Assert.Equal(new[] { NavSections.Settings }, Lit(w));
    });

    [Fact]
    public void CtrlDigitJumpsToTheRailSection() => Run(w =>
    {
        w.KeyPress(Key.D5, RawInputModifiers.Control, PhysicalKey.Digit5, "5");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("availablesubjects", w.CurrentTab);
        w.KeyPress(Key.D7, RawInputModifiers.Control, PhysicalKey.Digit7, "7");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("assets", w.CurrentTab);
    });

    [Fact]
    public void BackRowKeepsItsSpace_RowsNeverJump() => Run(w =>
    {
        var home = w.Named<Button>("DoorHome")!;
        Assert.False(w.Named<Button>("BtnNavBack")!.IsVisible);
        var before = home.TranslatePoint(new Point(0, 0), w)!.Value.Y;
        w.ShowTab("studio");
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.Named<Button>("BtnNavBack")!.IsVisible);
        Assert.Equal(before, home.TranslatePoint(new Point(0, 0), w)!.Value.Y, 3);
    });

    [Fact]
    public void PlayZonePillsScrollToTheirZone_GamesBackToTop() => Run(w =>
    {
        w.OpenNavSection(NavSections.Play);
        Dispatcher.UIThread.RunJobs();
        var play = w.Named<ConditioningControlPanel.Avalonia.Views.Tabs.PlayTabView>("PlayTab")!;
        var scroll = play.FindControl<ScrollViewer>("WallScroll")!;
        Assert.Equal(0, scroll.Offset.Y);

        foreach (var (pill, zone) in new[] { ("playeyes", "eyes"), ("playsessions", "sessions") })
        {
            Click(w.PageStrip!.PillFor(pill)!);
            Dispatcher.UIThread.RunJobs();
            var headerY = play.ZoneHeader(zone)!.TranslatePoint(default, scroll)!.Value.Y;
            Assert.True(scroll.Offset.Y > 0, $"{pill} did not scroll");
            // The header sits just under the strip, unless the wall's end stops the scroll first.
            bool atEnd = Math.Abs(scroll.Offset.Y - (scroll.Extent.Height - scroll.Viewport.Height)) < 1;
            Assert.True(Math.Abs(headerY - play_ZoneTopGap) < 1 || (atEnd && headerY > play_ZoneTopGap),
                $"{pill}: header at {headerY}, offset {scroll.Offset.Y}");
        }

        Click(w.PageStrip!.PillFor("play")!);   // Games: back to the top
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, scroll.Offset.Y);
    });

    private const double play_ZoneTopGap = ConditioningControlPanel.Avalonia.Views.Tabs.PlayTabView.ZoneTopGap;

    [Fact]
    public void CtrlKHeldTogglesOnce_AFreshPressCloses() => Run(w =>
    {
        var clock = new SteppedClock();
        SettingsPaletteWindow.ChordClock = clock;
        void Down(Interactive target) => target.RaiseEvent(new KeyEventArgs
            { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control });
        void Up(Interactive target) => target.RaiseEvent(new KeyEventArgs
            { RoutedEvent = InputElement.KeyUpEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control });

        Down(w);
        Assert.True(SettingsPaletteWindow.IsOpen);
        var palette = w.OwnedWindows.OfType<SettingsPaletteWindow>().Single();
        for (int i = 0; i < 10; i++) { clock.Now += 33; Down(palette); }   // the held key auto-repeats
        Assert.True(SettingsPaletteWindow.IsOpen);

        Up(palette);
        Down(palette);                                                      // a fresh press closes
        Assert.False(SettingsPaletteWindow.IsOpen);
        clock.Now += 33;
        Down(w);                                         // ...and its repeat, now on the shell, does not reopen
        Assert.False(SettingsPaletteWindow.IsOpen);

        // A release lost to another app heals: a press a second after the last one is fresh.
        clock.Now += 1000;
        Down(w);
        Assert.True(SettingsPaletteWindow.IsOpen);
    });
}
