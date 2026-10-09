using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Workshop drawer's Library and Roster cells, reached the way a user does: shell ->
/// Companion tab -> open the drawer (WPF MainWindow.xaml.cs:3164 link pool editor,
/// MainWindow.CompanionTab.cs:85 UpdateCompanionCardsUI).</summary>
public sealed class WorkshopCellsTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    /// <summary>The Workshop's live cells. Under v2 (7.1.5) the room is collapsed and its accordion never
    /// realizes them; the Companion pages adopt the same instances.</summary>
    private static WorkshopShelfParts Shelf(MainShellWindow shell) =>
        ((WorkshopRuntimeVm)shell.GetLogicalDescendants().OfType<CompanionRoomView>().Single()
            .FindControl<WorkshopAccordion>("WorkshopZone")!.DataContext!).Parts;

    /// <summary>7.1.5: the Library cell's home is Companion &gt; Links ("Videos it can play").</summary>
    private static WorkshopLibraryCell OpenLibrary(MainShellWindow shell)
    {
        shell.Show();
        shell.ShowTab("companionlinks");
        Dispatcher.UIThread.RunJobs();
        return Shelf(shell).Library;
    }

    /// <summary>7.1.5: the roster has no page of its own (Personality's picker is the pick); the room
    /// still keeps the live cell and repaints it on the Companion tab's show edge (WPF
    /// UpdateCompanionCardsUI).</summary>
    private static WorkshopRosterCell OpenRoster(MainShellWindow shell)
    {
        shell.Show();
        shell.ShowTab("companion");
        Dispatcher.UIThread.RunJobs();
        return Shelf(shell).Roster;
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task LibraryEditsTheActiveModsLinkPool() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        if (CoreMods.Service is null) AvApp.StartMods();
        var s = CoreSettings.Current;
        var modId = CoreMods.Service!.ActiveModId;
        var before = s.VideoLinksByMod;
        s.VideoLinksByMod = new Dictionary<string, Dictionary<string, string>>
        {
            [modId] = new() { ["Kept"] = "https://hypnotube.com/video/kept-1.html", ["Browse"] = "https://hypnotube.com/videos/" },
        };
        var shell = new MainShellWindow();
        try
        {
            var cell = OpenLibrary(shell);
            Assert.NotNull(TopLevel.GetTopLevel(cell));
            Assert.Single(cell.Rows);                                   // the listing page is dropped
            Assert.Equal("Kept", cell.Rows[0].NameBox.Text);
            var placeholder = cell.FindControl<TextBlock>("TxtNoVideoLinks")!;
            Assert.False(placeholder.IsVisible);
            Assert.Equal(s.ContentModeDisplay, cell.FindControl<TextBlock>("TxtHypnotubeModeLabel")!.Text);

            Click(cell.FindControl<Button>("BtnAddVideoLink")!);       // the user's add
            Assert.Equal(2, cell.Rows.Count);
            var added = cell.Rows[1];
            added.UrlBox.Text = "not a link";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.55, ((Control)added.UrlBox.Parent!).Opacity); // greyed, dropped on save
            added.UrlBox.Text = "https://hypnotube.com/video/new-one-22.html";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.0, ((Control)added.UrlBox.Parent!).Opacity);
            added.UrlBox.Focus();
            added.NameBox.Focus();                                      // leaving the box saves
            Dispatcher.UIThread.RunJobs();
            var saved = s.VideoLinksByMod[modId];
            Assert.Equal(2, saved.Count);
            Assert.Contains("https://hypnotube.com/video/new-one-22.html", saved.Values);
            Assert.DoesNotContain("", saved.Keys);                      // blank name auto-titled

            // Bin the first row: gone from the editor and from the saved pool.
            var row0 = (Grid)cell.Rows[0].NameBox.Parent!;
            Click(row0.Children.OfType<Button>().Last());
            Assert.Single(cell.Rows);
            Assert.False(s.VideoLinksByMod[modId].ContainsKey("Kept"));
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            s.VideoLinksByMod = before;
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RosterShowsLevelsAndRingsTheActiveCompanion() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.ActiveCompanionId = 2;
        s.CompanionProgressData[2] = new CompanionProgress { Level = 7 };
        var shell = new MainShellWindow();
        try
        {
            var cell = OpenRoster(shell);
            var level = cell.FindControl<TextBlock>("TxtCompanion2Level")!;
            Assert.Equal("Lv.7", level.Text);
            Assert.False(string.IsNullOrEmpty(cell.FindControl<TextBlock>("TxtCompanion2Name")!.Text));
            Assert.NotEqual(Colors.Transparent, ((ISolidColorBrush)cell.FindControl<Border>("CompanionCard2")!.BorderBrush!).Color);
            Assert.Equal(Colors.Transparent, ((ISolidColorBrush)cell.FindControl<Border>("CompanionCard0")!.BorderBrush!).Color);
            Assert.NotNull(ToolTip.GetTip(cell.FindControl<Border>("CompanionCard2")!));

            shell.ShowTab("achievements");
            Dispatcher.UIThread.RunJobs();
            s.CompanionProgressData[2].Level = 9;   // changed while hidden
            shell.ShowTab("companion");             // re-read on return
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Lv.9", level.Text);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
