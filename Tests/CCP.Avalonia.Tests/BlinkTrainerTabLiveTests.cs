using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Blink Trainer page's camera-free half (WPF MainWindow.BlinkTrainer.cs +
/// TabNavigation RefreshBlinkTrainerTab): saved settings survive the load pass, the free-tier gate
/// is up over a running demo stage, and the status row follows consent and folders.</summary>
public sealed class BlinkTrainerTabLiveTests
{
    [Fact]
    public void ShowingTheTab_LoadsSettings_GatesAndDemos_AndStatusFollowsFolders()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            var s = ConditioningControlPanel.CoreSettings.Current;
            var old = (s.BlinkTrainerDurationMinutes, s.BlinkTrainerOpacity, s.BlinkTrainerMixImages,
                s.WebcamConsentGiven, s.WebcamConsentVersion, s.BlinkTrainerFolders.ToArray());
            var dir = Directory.CreateTempSubdirectory("ccp-blink-").FullName;
            File.WriteAllBytes(Path.Combine(dir, "a.png"), new byte[] { 1 });
            Window? host = null;
            try
            {
                if (Application.Current is null)
                    AppBuilder.Configure<AvApp>().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();

                s.BlinkTrainerDurationMinutes = 42;
                s.BlinkTrainerOpacity = 33;
                s.BlinkTrainerMixImages = true;
                s.WebcamConsentGiven = true;
                s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
                s.BlinkTrainerFolders.Clear();

                var tab = new BlinkTrainerTabView { IsVisible = false };
                host = new Window { Width = 1400, Height = 1000, Content = tab };
                host.Show();
                tab.IsVisible = true; // what ShowTab("blinktrainer") does
                Dispatcher.UIThread.RunJobs();

                // The load pass paints the saved values and does not write the markup defaults back.
                Assert.Equal(42, tab.FindControl<Slider>("SliderBlinkTrainerDurationNew")!.Value);
                Assert.Equal("42 min", tab.FindControl<TextBlock>("TxtBlinkTrainerDurationValue")!.Text);
                Assert.Equal("33%", tab.FindControl<TextBlock>("TxtBlinkTrainerOpacityValue")!.Text);
                Assert.Equal(42, s.BlinkTrainerDurationMinutes);
                Assert.Equal(33, s.BlinkTrainerOpacity);
                Assert.NotEqual(default, tab.FindControl<Border>("BlinkTrainerMixOptionMix")!.BoxShadow);

                // No entitlement on this head: gate up, gated editors off, demo stage cycling.
                Assert.True(tab.FindControl<Border>("BlinkTrainerGate")!.IsVisible);
                Assert.False(tab.FindControl<StackPanel>("BlinkTrainerGatedContent")!.IsEnabled);
                Assert.True(tab.DemoRunning);
                var a = tab.FindControl<Image>("BlinkTrainerStageImageA")!;
                Assert.NotNull(a.Source);
                tab.AdvanceDemo();
                Assert.NotNull(tab.FindControl<Image>("BlinkTrainerStageImageB")!.Source);

                // Consent given, no folders: the row asks for a folder and blocks the start.
                Assert.Equal(BlinkTrainerStatusState.NeedsFolders, tab.StatusState);
                Assert.True(tab.FindControl<Button>("BlinkTrainerStatusAction")!.IsVisible);
                Assert.False(tab.FindControl<Button>("BtnBlinkTrainerStartSession")!.IsEnabled);

                tab.AddFolder(dir);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(BlinkTrainerStatusState.IdleReady, tab.StatusState);
                Assert.Single(tab.FindControl<StackPanel>("BlinkTrainerFolderCardsHost")!.Children);
                Assert.Contains(dir, s.BlinkTrainerFolders);

                tab.IsVisible = false; // leaving the tab stops the loop
                Assert.False(tab.DemoRunning);
            }
            finally
            {
                host?.Close();
                (s.BlinkTrainerDurationMinutes, s.BlinkTrainerOpacity, s.BlinkTrainerMixImages,
                    s.WebcamConsentGiven, s.WebcamConsentVersion) = (old.Item1, old.Item2, old.Item3, old.Item4, old.Item5);
                s.BlinkTrainerFolders.Clear();
                s.BlinkTrainerFolders.AddRange(old.Item6);
                Directory.Delete(dir, true);
            }
        });
    }

    [Fact]
    public void Status_PriorityOrder_MatchesWpf()
    {
        Assert.Equal(BlinkTrainerStatusState.Running, BlinkTrainerState.Status(true, "x", false, 0, true, false));
        Assert.Equal(BlinkTrainerStatusState.Error, BlinkTrainerState.Status(false, "x", false, 0, true, false));
        Assert.Equal(BlinkTrainerStatusState.NeedsConsent, BlinkTrainerState.Status(false, null, false, 0, true, false));
        Assert.Equal(BlinkTrainerStatusState.NeedsFolders, BlinkTrainerState.Status(false, null, true, 0, true, false));
        Assert.Equal(BlinkTrainerStatusState.NeedsCalibration, BlinkTrainerState.Status(false, null, true, 1, true, false));
        Assert.Equal(BlinkTrainerStatusState.IdleReady, BlinkTrainerState.Status(false, null, true, 1, false, false));
        Assert.Null(BlinkTrainerState.FolderCountLine(null, false));
    }
}
