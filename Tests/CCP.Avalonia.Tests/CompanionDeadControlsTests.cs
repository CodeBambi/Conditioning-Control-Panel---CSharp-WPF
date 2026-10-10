using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Fix wave 9 Oct 2026, lane f7: on the Companion pages a control either works or is
/// hidden. The Look list is filled and picks through the tube; the roster card switches the
/// companion; rows with nothing behind them on this head (browser pause, camera shortcut, tutorial
/// chip, community prompts, the personality-file buttons) are not shown; the Awareness switches
/// that need a screen read say they are not available.</summary>
public sealed class CompanionDeadControlsTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task LookListOffersEverySetAndPicksThroughTheTube() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try
        {
            var picked = new List<int>();
            var picker = new CompanionPickerCard { SelectLook = set => { picked.Add(set); return false; } };

            picker.FillLooks(new[] { 1, 2, 3 }, 2);
            Assert.Equal(3, picker.CmbAvatar.Items.Count);
            Assert.True(picker.CmbAvatar.IsEnabled);
            Assert.False(picker.TxtAvatarHint.IsVisible);
            Assert.Equal(2, (picker.CmbAvatar.SelectedItem as ComboBoxItem)?.Tag);
            foreach (var item in picker.CmbAvatar.Items.OfType<ComboBoxItem>())
                Assert.False(string.IsNullOrWhiteSpace((item.Content as TextBlock)?.Text));
            Assert.Empty(picked);                                   // filling is not a pick

            picker.CmbAvatar.SelectedIndex = 2;                     // the user's pick
            Assert.Equal(new[] { 3 }, picked);

            // One look: nothing to pick, and the card says so.
            picker.FillLooks(new[] { 1 }, 1);
            Assert.Single(picker.CmbAvatar.Items);
            Assert.False(picker.CmbAvatar.IsEnabled);
            Assert.True(picker.TxtAvatarHint.IsVisible);
        }
        finally { CoreSettings.ServiceProvider = null; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RosterCardSwitchesTheCompanion() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        int before = s.ActiveCompanionId;
        try
        {
            var roster = new WorkshopRosterCell();
            roster.Refresh();
            var cards = Enumerable.Range(0, 5)
                .Where(i => roster.FindControl<Border>($"CompanionCard{i}")!.IsVisible).ToArray();
            Assert.NotEmpty(cards);
            int target = cards.Last();
            s.ActiveCompanionId = cards.First();

            WorkshopAccordion.SwitchCompanionFromCard(roster, target);

            Assert.Equal(target, s.ActiveCompanionId);
            if (cards.Length > 1)
            {
                var ring = roster.FindControl<Border>($"CompanionCard{target}")!.BorderBrush as ISolidColorBrush;
                Assert.NotNull(ring);
                Assert.NotEqual(Colors.Transparent, ring!.Color);
                var old = roster.FindControl<Border>($"CompanionCard{cards.First()}")!.BorderBrush as ISolidColorBrush;
                Assert.Equal(Colors.Transparent, old!.Color);
            }

            // The personality-file button waits for the community prompt service: hidden.
            for (int i = 0; i < 5; i++)
                Assert.False(roster.FindControl<Button>($"BtnCompanion{i}Personality")!.IsVisible);
        }
        finally
        {
            s.ActiveCompanionId = before;
            CoreSettings.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RowsWithNothingBehindThemAreHidden() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var room = shell.GetLogicalDescendants().OfType<CompanionRoomView>().Single();
            var shelf = ((WorkshopRuntimeVm)room.FindControl<WorkshopAccordion>("WorkshopZone")!.DataContext!).Parts;

            // Behaviour cell: no browser hook, no webcam hotkey. The chat shortcut stays.
            Assert.False(shelf.Behavior.FindControl<Grid>("RowPauseBrowser")!.IsVisible);
            Assert.False(shelf.Behavior.FindControl<Grid>("RowCameraShortcut")!.IsVisible);
            Assert.True(shelf.Behavior.FindControl<Button>("BtnChatShortcut")!.IsVisible);

            // Community prompts: four buttons with no service behind them. The card and its ? stay.
            foreach (var name in new[] { "BtnBrowsePrompts", "BtnImportPrompt", "BtnExportPrompt", "BtnRefreshPrompts" })
                Assert.False(shelf.Community.FindControl<Button>(name)!.IsVisible, name);
            Assert.True(shelf.Community.FindControl<Button>("HelpBtnPrompts")!.IsVisible);

            // The header tutorial chip is live again (tours are ported; TutorialTourTests).
            var hero = room.FindControl<CompanionHeroCard>("HeroZone")!;
            Assert.True(hero.FindControl<Button>("BtnCompanionTutorial")!.IsVisible);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ScreenReadSwitchesSayTheyAreNotAvailable() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try
        {
            var view = new AwarenessTabView();
            var note = ConditioningControlPanel.Localization.Loc.Get("exclusives_not_on_this_build");
            Assert.Equal(4, view.ScreenReadOnlyToggles.Length);
            foreach (var box in view.ScreenReadOnlyToggles)
            {
                Assert.False(box.IsEnabled, box.Name);
                var row = Assert.IsType<Grid>(box.Parent);
                Assert.Contains(row.Children.OfType<TextBlock>(), t => t.Text == note);
            }
            // Typed keywords work on this head: the master and the keyboard switch stay live.
            Assert.True(view.FindControl<CheckBox>("ChkAwarenessMaster")!.IsEnabled);
            Assert.True(view.FindControl<CheckBox>("ChkAwarenessKeyboard")!.IsEnabled);
        }
        finally { CoreSettings.ServiceProvider = null; }
        return Task.CompletedTask;
    });
}
