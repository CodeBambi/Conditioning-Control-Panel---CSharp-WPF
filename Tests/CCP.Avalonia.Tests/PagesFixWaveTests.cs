using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Fix wave 9 Oct 2026, lane f6-pages: the chess Leave frame, the Deeper device shortcut, the
/// export button, the cloud signpost, the Back Room race door, the Play tracker chip and the Devices
/// section's loc keys.</summary>
public sealed class PagesFixWaveTests
{
    private static void Boot()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task ChessLeaveFrameClosesTheWindow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Boot();
        var w = new GameWindow(GameWindow.Games["piecebypiece"]);
        w.Show();
        Assert.True(w.IsVisible);
        w.HandleMessage("{\"type\":\"pbp:exit\"}");
        Assert.False(w.IsVisible);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ExportMyDataIsOffered() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Boot();
        var section = new AccountSettingsSection();
        Assert.True(section.FindControl<Button>("BtnExportData")!.IsVisible);
        return Task.CompletedTask;
    });

    [Fact]
    public Task CloudSignpostHidesWhileTheAccountCardIsHidden() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Boot();
        // Alone (no settings page around it) there is no card to scroll to.
        var alone = new DataSettingsSection();
        var w1 = new Window { Width = 900, Height = 700, Content = alone };
        w1.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(alone.FindControl<Border>("CloudBackupSignpost")!.IsVisible);
        w1.Close();

        // Inside the page it follows the Account card both ways.
        var page = new AppSettingsTabView();
        var w2 = new Window { Width = 1200, Height = 800, Content = page };
        w2.Show();
        Dispatcher.UIThread.RunJobs();
        var row = page.FindControl<DataSettingsSection>("SectionData")!.FindControl<Border>("CloudBackupSignpost")!;
        var card = page.FindControl<AccountSettingsSection>("SectionAccount")!.FindControl<Border>("CloudSettingsBackupSection")!;
        Assert.Equal(card.IsVisible, row.IsVisible);
        var was = card.IsVisible;
        try
        {
            card.IsVisible = !was;
            Assert.Equal(!was, row.IsVisible);
        }
        finally { card.IsVisible = was; w2.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public void RaceDoorAnswersWithTheRealOwnership()
    {
        Assert.Equal("locked", RaceWindow.Refusal(_ => false, signedIn: true));
        Assert.Equal("locked", RaceWindow.Refusal(_ => true, signedIn: false));
        Assert.Null(RaceWindow.Refusal(id => id == RaceWindow.RacingTrack(0), signedIn: true));
    }

    [Fact]
    public Task PlayEyesFollowTheTracker() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Boot();
        var view = new PlayTabView();
        var w = new Window { Width = 1400, Height = 900, Content = view };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var gaze = view.FindControl<Border>("PlayGazeNeedsTracker")!;
        var focus = view.FindControl<Border>("PlayFocusNeedsTracker")!;
        var line = view.FindControl<TextBlock>("TxtWebcamStatusChipPlay")!;
        try
        {
            view.RefreshTrackerUi(live: true);
            Assert.False(gaze.IsVisible);
            Assert.False(focus.IsVisible);
            Assert.Equal(1.0, view.FindControl<Border>("PlayGazeCard")!.Opacity);
            var tracking = line.Text;

            view.RefreshTrackerUi(live: false);
            Assert.True(gaze.IsVisible);
            Assert.True(focus.IsVisible);
            Assert.Equal(0.62, view.FindControl<Border>("PlayFocusCard")!.Opacity);
            Assert.NotEqual(tracking, line.Text);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public void DevicesSectionCarriesNoOldLiterals_AndDeeperOpensDeviceSettings()
    {
        var views = Path.Combine(RepoRoot(), "CCP.Avalonia", "Views");
        var devices = File.ReadAllText(Path.Combine(views, "Controls", "AppSettings", "DevicesSettingsSection.axaml"));
        foreach (var literal in new[]
        {
            "Blink fast 6 times", "\"Blink to recalibrate\"", "Pick the capture device", "Re-scan connected",
            "\"Privacy info\"", "\"Calibrate\"", "\"Quick Recal\"", "One-dot drift", "Header=\"Advanced\"",
            "\"Tracker Test\"", "\"Revoke consent\"", "Stops webcam tracking", "\"Show debug gaze cursor\"",
            "Keeps the calibration fresh", "\"Auto drift correction\"", "Advanced: leave on",
            "Restrict gaze-reactive effects to the calibrated screen", "Pick the microphone", "\"System default\"",
            "Re-scan microphones", "Comma-separated wake", "Click, then press",
        })
            Assert.DoesNotContain(literal, devices);
        foreach (var key in new[] { "btn_devices_calibrate", "label_devices_advanced", "set2_devices_auto_drift", "tooltip_devices_ptt_key" })
            Assert.Contains("{loc:Str " + key + "}", devices);

        var deeper = File.ReadAllText(Path.Combine(views, "Tabs", "DeeperTabView.axaml.cs"));
        Assert.Contains("BtnOpenDeviceSettings_Click(object? sender, RoutedEventArgs e) => Owner?.OpenDeviceSettings();", deeper);
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
