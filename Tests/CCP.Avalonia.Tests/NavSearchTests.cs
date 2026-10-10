using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Main sync #6 lane sync6-nav-search: the Ctrl+K palette's new behaviour (empty state,
/// recents, old-name hint, row verbs, pin menu) and the What moved card, driven from the shell.</summary>
public sealed class NavSearchTests
{
    [Fact]
    public Task NoHitsShowsWhatWasTypedTheNearestGuessAndEveryPage() => Run(() =>
    {
        var palette = new SettingsPaletteWindow();
        palette.Show();
        palette.FindControl<TextBox>("TxtQuery")!.Text = "volme";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(SettingsPaletteIndex.Search("volme"));

        Assert.True(palette.FindControl<Control>("PanelEmpty")!.IsVisible);
        Assert.Equal(Loc.GetF("nav_search_none", "volme"), palette.FindControl<TextBlock>("TxtEmpty")!.Text);
        var near = SettingsPaletteIndex.Nearest("volme");
        Assert.NotNull(near);
        Assert.True(palette.FindControl<Button>("BtnTry")!.IsVisible);
        Assert.Equal(Loc.GetF("nav_search_try", near!.Value.Entry.Label), palette.FindControl<TextBlock>("TxtTry")!.Text);

        palette.FindControl<Button>("BtnAll")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var rows = palette.FindControl<ListBox>("ListResults")!.Items.Cast<PaletteRow>().Select(r => r.Entry.Id).ToArray();
        Assert.Equal(SettingsPaletteIndex.AllPages().Select(e => e.Id).ToArray(), rows);
        palette.Close();
    });

    [Fact]
    public Task RowsCarryTheOldNameHintAndAScreenReaderName() => Run(() =>
    {
        var palette = new SettingsPaletteWindow();
        palette.Show();
        palette.FindControl<TextBox>("TxtQuery")!.Text = "velvet vault";
        Dispatcher.UIThread.RunJobs();
        var rows = palette.FindControl<ListBox>("ListResults")!.Items.Cast<PaletteRow>().ToList();
        Assert.NotEmpty(rows);
        Assert.Contains(rows, r => r.WasHint.Length > 0);
        foreach (var r in rows)
        {
            var was = SettingsPaletteIndex.WasHint(r.Entry, "velvet vault");
            Assert.Equal(was == null ? "" : "  " + Loc.GetF("nav_was_hint", was), r.WasHint);
            Assert.Equal(r.Entry.Context.Length == 0 ? r.Label : r.Label + ", " + r.Entry.Context, r.AutomationName);
        }
        palette.Close();
    });

    [Fact]
    public Task EnterRemembersTheRowAndTheEmptyBoxListsItFirst() => Shell(shell =>
    {
        var target = SettingsPaletteIndex.Search("leaderboard").First();
        SettingsPaletteWindow.Toggle(shell);
        var palette = OpenPalette();
        palette.FindControl<TextBox>("TxtQuery")!.Text = "leaderboard";
        Dispatcher.UIThread.RunJobs();
        Enter(palette);
        Assert.False(SettingsPaletteWindow.IsOpen);
        Assert.Equal(target.TabKey, shell.CurrentTab);

        SettingsPaletteWindow.Toggle(shell);
        var again = OpenPalette();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(target.Id, again.FindControl<ListBox>("ListResults")!.Items.Cast<PaletteRow>().First().Entry.Id);
        SettingsPaletteWindow.CloseIfOpen();
    });

    [Fact]
    public Task RowsOpenThroughTheirOwnVerb() => Shell(shell =>
    {
        // A Studio module lands on the Studio, a Library launcher opens its window.
        SettingsPaletteWindow.Navigate(shell, SettingsPaletteIndex.All.First(e => e.RackKey == "video"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("studio", shell.CurrentTab);

        SettingsPaletteWindow.Navigate(shell, SettingsPaletteIndex.All.First(e => e.LauncherKey == "medialog"));
        Dispatcher.UIThread.RunJobs();
        var log = shell.OwnedWindows.OfType<MediaHistoryWindow>().FirstOrDefault();
        Assert.NotNull(log);
        log!.Close();
    });

    [Fact]
    public Task CcLabsRowOpensTheLauncherAndGameRowsListOnlyStartableGames() => Shell(shell =>
    {
        try
        {
            SettingsPaletteWindow.Navigate(shell, SettingsPaletteIndex.ById("chrome.cclabs")!);
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.IsVisible);
            Assert.True(LauncherWindow.Instance?.IsVisible);

            // Unseeded, Core lists every catalogue game; seeded as App does, only what this head starts.
            SettingsPaletteIndex.GameAvailableProvider = null;
            Assert.Contains(SettingsPaletteIndex.Search("casino"), e => e.GameId == "backroom");
            SettingsPaletteIndex.GameAvailableProvider = LauncherWindow.CanStartGame;
            Assert.DoesNotContain(SettingsPaletteIndex.Search("casino"), e => e.GameId != null);
        }
        finally
        {
            SettingsPaletteIndex.GameAvailableProvider = null;
            LauncherWindow.Instance?.Close();
        }
    });

    [Fact]
    public Task ClickAwayClosesThePaletteAndLeavesThePanelInFront() => Shell(shell =>
    {
        var other = new Window();
        try
        {
            other.Show(shell);
            SettingsPaletteWindow.Toggle(shell);
            var palette = OpenPalette();
            palette.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.True(palette.IsActive, "palette never active");
            // The click lands on another of our windows. The headless platform keeps every window
            // active at once and never raises Deactivated, so the palette's handler is entered directly.
            var raised = 0;
            shell.Activated += (_, _) => raised++;
            typeof(SettingsPaletteWindow).GetMethod("Window_Deactivated", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(palette, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(SettingsPaletteWindow.IsOpen);
            Assert.True(raised > 0, "the panel was not brought back to the front");
        }
        finally { other.Close(); }
    });

    [Fact]
    public Task RightClickPinsTheRowToFavourites() => Shell(shell =>
    {
        CoreSettings.Current.RailFavorites.Clear();
        SettingsPaletteWindow.Toggle(shell);
        var palette = OpenPalette();
        palette.FindControl<TextBox>("TxtQuery")!.Text = "leaderboard";
        Dispatcher.UIThread.RunJobs();
        var list = palette.FindControl<ListBox>("ListResults")!;
        var index = list.Items.Cast<PaletteRow>().ToList().FindIndex(r => FavoritesRailRule.IsDestination(r.Entry.Id));
        var row = (PaletteRow)list.Items[index]!;
        // A real right-button press and release on the row, as the user does it.
        palette.UpdateLayout();
        var container = (Control)list.ContainerFromIndex(index)!;
        var at = container.TranslatePoint(new Point(container.Bounds.Width / 2, container.Bounds.Height / 2), palette)!.Value;
        palette.MouseDown(at, global::Avalonia.Input.MouseButton.Right);
        palette.MouseUp(at, global::Avalonia.Input.MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.True(SettingsPaletteWindow.IsOpen);   // a right-click pins, it never navigates
        var menu = palette.PinMenu;
        Assert.NotNull(menu);
        var item = menu!.Items.OfType<MenuItem>().Single();
        Assert.Equal(Loc.Get("rail_pin"), item.Header);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Contains(row.Entry.Id, CoreSettings.Current.RailFavorites);
        SettingsPaletteWindow.CloseIfOpen();
    });

    [Fact]
    public Task WhatMovedOpensOnceOnAnUpgradeAndShowMeLands() => Shell(shell =>
    {
        CoreSettings.Current.WhatMovedCardShown = 0;
        // Other startup cards (a feature intro) own the passive slot; this test is about What moved.
        foreach (var w in shell.OwnedWindows.OfType<ConditioningControlPanel.Services.Startup.IPassiveStartupSurface>().Cast<Window>().ToList()) w.Close();
        ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
        shell.OfferWhatMovedIfNeeded(freshInstall: false);
        Dispatcher.UIThread.RunJobs();
        var card = shell.OwnedWindows.OfType<WhatMovedCard>().SingleOrDefault();
        Assert.NotNull(card);
        Assert.False(card!.ReadMode);
        Assert.Equal(1, CoreSettings.Current.WhatMovedCardShown);
        var rows = card.FindControl<ItemsControl>("Rows")!.Items.Cast<WhatMovedRow>().Select(r => r.Id);
        Assert.Equal(WhatMovedPlan.Rows.Select(r => r.Id), rows);

        var showMe = card.GetVisualDescendants().OfType<Button>()
            .First(b => b.Tag is WhatMovedRow { Id: "lobby" });
        showMe.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(card.IsVisible);
        Assert.Equal("availablesubjects", shell.CurrentTab);

        // Spent: a second launch offers nothing.
        shell.OfferWhatMovedIfNeeded(freshInstall: false);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(shell.OwnedWindows.OfType<WhatMovedCard>());
    });

    [Fact]
    public Task WhatMovedOpensWhileThePanelSitsBehindTheLauncher() => Shell(shell =>
    {
        // Live run 2026-10-09: the boot hides the panel behind the launcher, and Show(hiddenOwner)
        // threw, spending the one-time flag on a card nobody saw.
        foreach (var w in shell.OwnedWindows.OfType<ConditioningControlPanel.Services.Startup.IPassiveStartupSurface>().Cast<Window>().ToList()) w.Close();
        ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
        WhatMovedCard? card = null;
        void Opened(object? s, RoutedEventArgs e) { if (s is WhatMovedCard c) card = c; }
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, e) => Opened(w, e));
        shell.Hide();
        CoreSettings.Current.WhatMovedCardShown = 0;
        shell.OfferWhatMovedIfNeeded(freshInstall: false);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(card);
        Assert.True(card!.IsVisible);
        card.Close();
    });

    [Fact]
    public Task HelpReplaysWhatMovedInReadMode() => Shell(shell =>
    {
        CoreSettings.Current.WhatMovedCardShown = 1;
        shell.SetTutorialOverlay(true);
        shell.Named<Button>("BtnHelpWhatMovedRow")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.Named<Control>("MainTutorialOverlay")!.IsVisible);
        var card = shell.OwnedWindows.OfType<WhatMovedCard>().Single();
        Assert.True(card.ReadMode);
        Assert.Equal(1, CoreSettings.Current.WhatMovedCardShown);
        card.Close();
    });

    private static SettingsPaletteWindow OpenPalette()
    {
        Dispatcher.UIThread.RunJobs();
        Assert.True(SettingsPaletteWindow.IsOpen);
        return (SettingsPaletteWindow)typeof(SettingsPaletteWindow)
            .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    }

    private static void Enter(Window w)
    {
        w.RaiseEvent(new global::Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter, Source = w,
        });
        Dispatcher.UIThread.RunJobs();
    }

    private static Task Shell(Action<MainShellWindow> body) => Run(() =>
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.Welcomed = true;
        CoreSettings.Current.HasAcceptedAgeVerification = true;
        CoreSettings.Current.WhatMovedCardShown = 1;
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            body(shell);
        }
        finally
        {
            SettingsPaletteWindow.CloseIfOpen();
            shell?.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    private static Task Run(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
        LocalizationManager.Instance.SetLanguage("en");
        body();
        return Task.CompletedTask;
    });
}
