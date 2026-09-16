using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PresetsSessionCatalogueTests
{
    [Fact]
    public async Task MountedCatalogueUsesCoreModelsAndPointerSelectionChangesDetails()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var previousLanguage = LocalizationManager.Instance.CurrentLanguage;
            Window? host = null;
            try
            {
                EnsureAvalonia();
                Assert.Null(CoreSession.IsEngineRunningProvider);
                Assert.False(CoreSession.IsEngineRunning);
                Assert.False(CoreSettings.HasProvider);
                var settingsProvider = CoreSettings.ServiceProvider;
                var all = Session.GetAllSessions();
                var available = all.Where(session => session.IsAvailable).ToArray();
                Assert.NotEmpty(available);
                Assert.Contains(all, session => !session.IsAvailable);
                var view = new PresetsTabView { Width = 1100, Height = 760 };
                host = new Window { Width = 1100, Height = 760, Content = view };
                host.Show();
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                var panel = view.FindControl<StackPanel>("SessionRackPanel");
                Assert.NotNull(panel);
                var rows = panel!.Children.OfType<Border>().ToArray();
                Assert.Equal(available.Length, rows.Length);
                Assert.Equal(available.Select(session => session.Id),
                    rows.Select(row => (row.Tag as Session)?.Id));
                Assert.DoesNotContain(rows, row => row.Tag is Session session && !session.IsAvailable);

                var start = view.FindControl<Button>("BtnStartSession");
                Assert.NotNull(start);
                Assert.False(start!.IsEnabled);
                Assert.False(view.FindControl<StackPanel>("SessionButtonsPanel")!.IsVisible);
                Assert.False(view.FindControl<Button>("BtnRevealSpoilers")!.IsEnabled);
                Assert.False(view.FindControl<Button>("BtnExportSession")!.IsEnabled);
                Assert.False(view.FindControl<Button>("BtnSessionHistory")!.IsEnabled);
                Assert.False(view.FindControl<Button>("BtnCreateSession")!.IsEnabled);
                Assert.False(view.FindControl<Border>("SessionDropZone")!.IsEnabled);
                Assert.False(view.FindControl<ComboBox>("CmbRackSort")!.IsEnabled);
                Assert.False(view.FindControl<TextBox>("TxtRackSearch")!.IsEnabled);
                Assert.All(view.FindControl<StackPanel>("RackSourceChips")!.Children.OfType<ToggleButton>(),
                    chip => Assert.False(chip.IsEnabled));
                Assert.All(view.FindControl<StackPanel>("RackDifficultyChips")!.Children.OfType<ToggleButton>(),
                    chip => Assert.False(chip.IsEnabled));

                var first = available[0];
                var second = available.First(session => session.Id != first.Id);
                var firstRow = rows.Single(row => (row.Tag as Session)?.Id == first.Id);
                var secondRow = rows.Single(row => (row.Tag as Session)?.Id == second.Id);

                Click(host, firstRow);
                AssertDetails(view, first);
                Assert.False(CoreSession.IsEngineRunning);
                Assert.Same(settingsProvider, CoreSettings.ServiceProvider);

                LocalizationManager.Instance.SetLanguage("zh-CN");
                Dispatcher.UIThread.RunJobs();
                AssertDetails(view, first);

                Click(host, secondRow);
                AssertDetails(view, second);
                Assert.NotEqual(first.Id, second.Id);
                Assert.False(CoreSession.IsEngineRunning);
                Assert.Same(settingsProvider, CoreSettings.ServiceProvider);

                // The row is a normal focusable Avalonia input element, so Enter follows the same
                // keyboard path as a user tabbing to it rather than calling selection directly.
                firstRow.Focus();
                host.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
                Dispatcher.UIThread.RunJobs();
                AssertDetails(view, first);
            }
            finally
            {
                try
                {
                    host?.Close();
                    Dispatcher.UIThread.RunJobs();
                }
                finally
                {
                    LocalizationManager.Instance.SetLanguage(previousLanguage);
                    Dispatcher.UIThread.RunJobs();
                }
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task MountedCatalogueRefreshesRackAndDetailsWithoutReloadingOrLosingFocus()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var previousLanguage = LocalizationManager.Instance.CurrentLanguage;
            var root = Path.Combine(Path.GetTempPath(), "ccp-mounted-language-tests-" + Guid.NewGuid().ToString("N"));
            var builtInFolder = Path.Combine(root, "built-in");
            var customFolder = Path.Combine(root, "custom");
            Window? host = null;
            Window? reattachedHost = null;
            try
            {
                EnsureAvalonia();
                Directory.CreateDirectory(builtInFolder);
                Directory.CreateDirectory(customFolder);
                var service = new SessionFileService(customFolder, builtInFolder);
                service.ExportSession(Session.MorningDrift,
                    Path.Combine(builtInFolder, "morning_drift.session.json"));
                service.ExportSession(new SessionDefinition
                {
                    Id = "live_language_custom",
                    Name = "Raw Custom Name",
                    Icon = "🧩",
                    Description = "Raw custom description\nSecond line",
                    DurationMinutes = 37,
                    Difficulty = SessionDifficulty.Hard,
                    BonusXP = 777,
                    IsAvailable = true
                }, Path.Combine(customFolder, "live_language_custom.session.json"));

                var manager = new SessionManager(service);
                manager.LoadAllSessions();
                var builtIn = Assert.Single(manager.AllSessions, session => session.Id == "morning_drift");
                var custom = Assert.Single(manager.AllSessions, session => session.Id == "live_language_custom");
                var view = new PresetsTabView { Width = 1100, Height = 760 };
                view.UseSessionManager(manager);
                host = new Window { Width = 1100, Height = 760, Content = view };
                host.Show();
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                var panel = view.FindControl<StackPanel>("SessionRackPanel")!;
                var builtInRow = Assert.Single(panel.Children.OfType<Border>(), row => row.Tag is Session session && session.Id == builtIn.Id);
                var customRow = Assert.Single(panel.Children.OfType<Border>(), row => row.Tag is Session session && session.Id == custom.Id);
                Assert.Same(builtIn, builtInRow.Tag);
                Assert.Same(custom, customRow.Tag);

                Click(host, builtInRow);
                builtInRow.Focus();
                Assert.True(builtInRow.IsFocused);
                Assert.Equal("Morning Drift", RowText(builtInRow, 2));
                Assert.Equal("BUILT-IN", SourceText(builtInRow));
                Assert.Equal("⭐ Easy", PillText(builtInRow, 4));
                Assert.Equal("30 min", RowText(builtInRow, 5));
                Assert.Equal("+400 XP", RowText(builtInRow, 6));

                LocalizationManager.Instance.SetLanguage("zh-CN");
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                Assert.True(builtInRow.IsFocused);
                Assert.Same(builtInRow, panel.Children.Single(row => row.Tag is Session session && session.Id == builtIn.Id));
                Assert.Same(builtIn, builtInRow.Tag);
                Assert.Equal("晨曦漫游", RowText(builtInRow, 2));
                Assert.StartsWith("让清晨轻轻带你进入", RowText(builtInRow, 3));
                Assert.Equal("⭐ 简单", PillText(builtInRow, 4));
                Assert.Equal("30 分钟", RowText(builtInRow, 5));
                Assert.Equal("+400 XP", RowText(builtInRow, 6));
                Assert.Equal("内置", SourceText(builtInRow));
                Assert.Equal("Raw Custom Name", RowText(customRow, 2));
                Assert.Equal("Raw custom description", RowText(customRow, 3));
                Assert.Equal("⭐⭐⭐ 困难", PillText(customRow, 4));
                Assert.Equal("你的", SourceText(customRow));
                Assert.Equal(new[] { "全部  2", "内置  1", "你的  1", "目录  0" },
                    view.FindControl<StackPanel>("RackSourceChips")!.Children.OfType<ToggleButton>()
                        .Select(chip => ((TextBlock)chip.Content!).Text));
                Assert.Equal("2 个会话", view.FindControl<TextBlock>("TxtRackCount")!.Text);
                Assert.Equal(new[] { "简单", "中等", "困难", "极限" },
                    view.FindControl<StackPanel>("RackDifficultyChips")!.Children.OfType<ToggleButton>()
                        .Select(dot => ToolTip.GetTip(dot)?.ToString()));
                AssertDetails(view, builtIn);

                // Detaching removes the event handler. The same row and Session stay alive while
                // the language changes, then the attach refresh reads the current language once.
                host.Content = null;
                Dispatcher.UIThread.RunJobs();
                LocalizationManager.Instance.SetLanguage("de");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("晨曦漫游", RowText(builtInRow, 2));

                reattachedHost = new Window { Width = 1100, Height = 760, Content = view };
                reattachedHost.Show();
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(builtIn.LocalizedName, RowText(builtInRow, 2));
                Assert.Equal(custom.Name, RowText(customRow, 2));
                Assert.Equal(Loc.Get("rack_src_builtin"), SourceText(builtInRow));
                Assert.Equal(Loc.GetF("rack_duration", builtIn.DurationMinutes), RowText(builtInRow, 5));
                AssertDetails(view, builtIn);

                builtInRow.Focus();
                LocalizationManager.Instance.SetLanguage("en");
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                Assert.True(builtInRow.IsFocused);
                Assert.Same(builtIn, builtInRow.Tag);
                Assert.Equal("Morning Drift", RowText(builtInRow, 2));
                Assert.Equal("BUILT-IN", SourceText(builtInRow));
                Assert.Equal("30 min", RowText(builtInRow, 5));
                Assert.Equal("2 sessions", view.FindControl<TextBlock>("TxtRackCount")!.Text);
            }
            finally
            {
                try
                {
                    host?.Close();
                    reattachedHost?.Close();
                    Dispatcher.UIThread.RunJobs();
                }
                finally
                {
                    LocalizationManager.Instance.SetLanguage(previousLanguage);
                    Dispatcher.UIThread.RunJobs();
                    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                }
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task MountedExplicitCatalogueUsesCustomRowsAndAvailabilityGate()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var previousLanguage = LocalizationManager.Instance.CurrentLanguage;
            var root = Path.Combine(Path.GetTempPath(), "ccp-mounted-session-tests-" + Guid.NewGuid().ToString("N"));
            var builtInFolder = Path.Combine(root, "built-in");
            var customFolder = Path.Combine(root, "custom");
            Window? host = null;
            try
            {
                EnsureAvalonia();
                Directory.CreateDirectory(builtInFolder);
                Directory.CreateDirectory(customFolder);
                var service = new SessionFileService(customFolder, builtInFolder);
                service.ExportSession(new SessionDefinition
                {
                    Id = "fixture_builtin",
                    Name = "Fixture Built In",
                    Icon = "🧪",
                    Description = "Built-in fixture description",
                    DurationMinutes = 11,
                    BonusXP = 111,
                    IsAvailable = true
                }, Path.Combine(builtInFolder, "fixture_builtin.session.json"));
                service.ExportSession(new SessionDefinition
                {
                    Id = "fixture_custom",
                    Name = "Raw User Name",
                    Icon = "🧩",
                    Description = "Raw user description\nSecond line",
                    DurationMinutes = 37,
                    Difficulty = SessionDifficulty.Hard,
                    BonusXP = 777,
                    IsAvailable = true
                }, Path.Combine(customFolder, "fixture_custom.session.json"));
                service.ExportSession(new SessionDefinition
                {
                    Id = "fixture_unavailable",
                    Name = "Unavailable Fixture",
                    IsAvailable = false
                }, Path.Combine(customFolder, "fixture_unavailable.session.json"));
                File.WriteAllText(Path.Combine(customFolder, "malformed.session.json"), "{ not valid json");

                var manager = new SessionManager(service);
                manager.LoadAllSessions();
                var available = manager.AllSessions.Where(session => session.IsAvailable).ToArray();
                var builtIn = manager.AllSessions.Single(session => session.Id == "fixture_builtin");
                var custom = manager.AllSessions.Single(session => session.Id == "fixture_custom");
                Assert.Equal(3, manager.AllSessions.Count);
                Assert.Equal(SessionSource.BuiltIn, builtIn.Source);
                Assert.Equal(Path.Combine(builtInFolder, "fixture_builtin.session.json"), builtIn.SourceFilePath);
                Assert.Equal(SessionSource.Custom, custom.Source);
                Assert.Equal(Path.Combine(customFolder, "fixture_custom.session.json"), custom.SourceFilePath);
                Assert.Contains(manager.AllSessions, session => !session.IsAvailable);

                var view = new PresetsTabView { Width = 1100, Height = 760 };
                view.UseSessionManager(manager);
                host = new Window { Width = 1100, Height = 760, Content = view };
                host.Show();
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                var panel = view.FindControl<StackPanel>("SessionRackPanel")!;
                var rows = panel.Children.OfType<Border>().ToArray();
                Assert.Equal(available.Select(session => session.Id),
                    rows.Select(row => (row.Tag as Session)?.Id));
                Assert.DoesNotContain(rows, row => (row.Tag as Session)?.Id == "fixture_unavailable");
                Assert.Same(custom, rows.Single(row => (row.Tag as Session)?.Id == custom.Id).Tag);

                var customRow = rows.Single(row => (row.Tag as Session)?.Id == custom.Id);
                Click(host, customRow);
                Assert.Equal("🧩 Raw User Name", view.FindControl<TextBlock>("TxtDetailTitle")!.Text);
                Assert.Equal("Raw user description\nSecond line",
                    view.FindControl<TextBlock>("TxtSessionDescription")!.Text);

                customRow.Focus();
                host.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("🧩 Raw User Name", view.FindControl<TextBlock>("TxtDetailTitle")!.Text);
                Assert.Equal("Raw user description\nSecond line",
                    view.FindControl<TextBlock>("TxtSessionDescription")!.Text);
                Assert.Equal(Loc.GetF("rack_duration", 37),
                    view.FindControl<TextBlock>("TxtSessionDuration")!.Text);
                Assert.Equal(Loc.GetF("rack_xp", 777),
                    view.FindControl<TextBlock>("TxtSessionXP")!.Text);
                Assert.Equal(custom.GetDifficultyText(),
                    view.FindControl<TextBlock>("TxtSessionDifficulty")!.Text);
                Assert.False(view.FindControl<Button>("BtnStartSession")!.IsEnabled);
                Assert.False(view.FindControl<StackPanel>("SessionButtonsPanel")!.IsVisible);
                Assert.False(view.FindControl<Button>("BtnRevealSpoilers")!.IsEnabled);
                Assert.False(view.FindControl<Button>("BtnExportSession")!.IsEnabled);
                Assert.False(view.FindControl<Button>("BtnSessionHistory")!.IsEnabled);
                Assert.False(view.FindControl<Button>("BtnCreateSession")!.IsEnabled);
                Assert.False(view.FindControl<Border>("SessionDropZone")!.IsEnabled);
                Assert.False(view.FindControl<ComboBox>("CmbRackSort")!.IsEnabled);
                Assert.False(view.FindControl<TextBox>("TxtRackSearch")!.IsEnabled);
                Assert.All(view.FindControl<StackPanel>("RackSourceChips")!.Children.OfType<ToggleButton>(),
                    chip => Assert.False(chip.IsEnabled));
                Assert.All(view.FindControl<StackPanel>("RackDifficultyChips")!.Children.OfType<ToggleButton>(),
                    chip => Assert.False(chip.IsEnabled));
                Assert.All(rows, row =>
                {
                    var rowGrid = Assert.IsType<Grid>(row.Child);
                    var actions = Assert.IsType<StackPanel>(rowGrid.Children[8]);
                    Assert.Equal(2, actions.Children.Count);
                    Assert.All(actions.Children.OfType<Button>(), button => Assert.False(button.IsEnabled));
                });

                LocalizationManager.Instance.SetLanguage("zh-CN");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("🧩 Raw User Name", view.FindControl<TextBlock>("TxtDetailTitle")!.Text);
                Assert.Equal("Raw user description\nSecond line",
                    view.FindControl<TextBlock>("TxtSessionDescription")!.Text);
                Assert.Equal(custom.GetDifficultyText(),
                    view.FindControl<TextBlock>("TxtSessionDifficulty")!.Text);
                Assert.Equal(Loc.GetF("rack_duration", 37),
                    view.FindControl<TextBlock>("TxtSessionDuration")!.Text);
                Assert.Equal(Loc.GetF("rack_xp", 777),
                    view.FindControl<TextBlock>("TxtSessionXP")!.Text);
            }
            finally
            {
                try
                {
                    host?.Close();
                    Dispatcher.UIThread.RunJobs();
                }
                finally
                {
                    LocalizationManager.Instance.SetLanguage(previousLanguage);
                    Dispatcher.UIThread.RunJobs();
                    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                }
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task MountedCataloguePreservesCallerLanguageStartingInGerman()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var previousLanguage = LocalizationManager.Instance.CurrentLanguage;
            try
            {
                LocalizationManager.Instance.SetLanguage("de");
                await MountedCatalogueUsesCoreModelsAndPointerSelectionChangesDetails();
                Assert.Equal("de", LocalizationManager.Instance.CurrentLanguage);
            }
            finally
            {
                LocalizationManager.Instance.SetLanguage(previousLanguage);
            }
        });
    }

    private static void AssertDetails(PresetsTabView view, Session expected)
    {
        Assert.Equal($"{expected.Icon} {expected.LocalizedName}",
            view.FindControl<TextBlock>("TxtDetailTitle")!.Text);
        Assert.Equal(Loc.GetF("rack_duration", expected.DurationMinutes),
            view.FindControl<TextBlock>("TxtSessionDuration")!.Text);
        Assert.Equal(Loc.GetF("rack_xp", expected.BonusXP),
            view.FindControl<TextBlock>("TxtSessionXP")!.Text);
        Assert.Equal(expected.GetDifficultyText(),
            view.FindControl<TextBlock>("TxtSessionDifficulty")!.Text);
        Assert.Contains(expected.LocalizedDescription,
            view.FindControl<TextBlock>("TxtSessionDescription")!.Text);
        Assert.False(view.FindControl<StackPanel>("SessionSpoilerPanel")!.IsVisible);
        Assert.True(view.FindControl<ScrollViewer>("SessionDetailScroller")!.IsVisible);
        Assert.False(view.FindControl<ScrollViewer>("PresetDetailScroller")!.IsVisible);
    }

    private static string RowText(Border row, int column)
    {
        var grid = Assert.IsType<Grid>(row.Child);
        var text = Assert.IsType<TextBlock>(grid.Children.Single(child => Grid.GetColumn(child) == column));
        return text.Text!;
    }

    private static string PillText(Border row, int column)
    {
        var grid = Assert.IsType<Grid>(row.Child);
        var pill = Assert.IsType<Border>(grid.Children.Single(child => Grid.GetColumn(child) == column));
        return Assert.IsType<TextBlock>(pill.Child).Text!;
    }

    private static string SourceText(Border row)
    {
        var grid = Assert.IsType<Grid>(row.Child);
        var badges = Assert.IsType<StackPanel>(grid.Children.Single(child => Grid.GetColumn(child) == 7));
        return Assert.IsType<TextBlock>(Assert.IsType<Border>(badges.Children[0]).Child).Text!;
    }

    private static void EnsureAvalonia()
    {
        Assert.True(AvaloniaTestDispatcher.IsDispatcherThread);
        if (Application.Current is null)
        {
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
        LocalizationManager.Instance.SetLanguage("en");
    }

    private static void Click(TopLevel host, Control target)
    {
        var point = target.TranslatePoint(
            new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), host);
        Assert.True(point.HasValue, "could not translate row into host");
        host.MouseMove(point!.Value, RawInputModifiers.None);
        host.MouseDown(point.Value, MouseButton.Left, RawInputModifiers.None);
        host.MouseUp(point.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }
}
