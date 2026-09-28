using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The three session surfaces open from the controls WPF opens them from, over Core data.</summary>
public sealed class SessionSurfacesTests
{
    private static SessionLog Log(string name, bool completed) => new()
    {
        SessionId = name, SessionName = name, SessionIcon = "🌀", Completed = completed, XPEarned = 77,
        StartedAt = new DateTime(2026, 1, 2, 3, 4, 0), Duration = TimeSpan.FromSeconds(125),
        Media = new List<MediaLogEntry>
        {
            new() { Type = MediaType.Video, FilePath = "/v/a.mp4", SessionTimeSeconds = 3 },
            new() { Type = MediaType.Image, FilePath = "/i/b.png", SessionTimeSeconds = 9 },
        },
    };

    [Fact]
    public Task SessionCompleteWindowShowsCoreSessionLog() => Run(() =>
    {
        var window = new SessionCompleteWindow(Log("Real Run", true), playSound: false);
        Assert.Equal("Real Run", Text(window, "TxtSessionName"));
        Assert.Equal("+77", Text(window, "TxtXP"));
        Assert.Equal("02:05", Text(window, "TxtDuration"));
        Assert.Equal(Loc.GetF("label_media_count_videos_images", 1, 1), Text(window, "TxtMediaCount"));
        window.Close();
    });

    [Fact]
    public Task SessionLogHistoryListsCoreLogsAndReopensTheRecap() => Run(() =>
    {
        var logs = new List<SessionLog> { Log("Newest", true), Log("Older", false) };
        var window = new SessionLogHistoryWindow(logs);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Loc.GetF("label_session_count", 2), Text(window, "TxtCount"));
        var rows = window.GetVisualDescendants().OfType<Button>().Where(b => b.Tag is HistoryRow).ToArray();
        Assert.Equal(new[] { "Newest", "Older" }, rows.Select(b => ((HistoryRow)b.Tag!).Name));

        rows[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var recap = Assert.Single(window.OwnedWindows.OfType<SessionCompleteWindow>());
        Assert.Equal("Older", Text(recap, "TxtSessionName"));
        recap.Close();
        window.Close();
    });

    [Fact]
    public Task PresetsTabOpensHistoryCreateAndEditSurfaces() => Run(() =>
    {
        var view = new PresetsTabView { Width = 1100, Height = 760 };
        var host = new Window { Width = 1100, Height = 760, Content = view };
        host.Show();
        Dispatcher.UIThread.RunJobs();

        view.FindControl<Button>("BtnSessionHistory")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(host.OwnedWindows.OfType<SessionLogHistoryWindow>()).Close();

        view.FindControl<Button>("BtnCreateSession")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var create = Assert.Single(host.OwnedWindows.OfType<SessionEditorWindow>());
        Assert.Equal(Loc.Get("label_my_new_session"), create.FindControl<TextBox>("TxtSessionName")!.Text);
        create.Close();
        Dispatcher.UIThread.RunJobs();

        var row = view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().First();
        var session = (Session)row.Tag!;
        var edit = row.GetVisualDescendants().OfType<Button>().First(b => b.IsEnabled);
        edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var editor = Assert.Single(host.OwnedWindows.OfType<SessionEditorWindow>());
        Assert.Equal(session.Name, editor.FindControl<TextBox>("TxtSessionName")!.Text);
        editor.Close();
        host.Close();
    });

    private static string? Text(Control root, string name) => root.FindControl<TextBlock>(name)!.Text;

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
