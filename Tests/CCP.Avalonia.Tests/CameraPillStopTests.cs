using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>k4 HA7: the "Camera active" title pill is the privacy stop. As WPF (MainWindow.LabTab.cs:477),
/// Focus Gaze stands down first, then the Blink Trainer, then the tracker. No camera is opened here: the
/// engine is made "able" through its test seam and the tracker is never started.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class CameraPillStopTests
{
    [Fact]
    public async Task ThePill_StopsFocusGaze()
    {
        var focus = GazeFocusHead.Instance;
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                focus.CanRunOverride = () => true;
                focus.MasterEnabled = true;
                Assert.True(focus.IsActive);

                await MainShellWindow.StopCameraConsumersAsync();

                Assert.False(focus.IsActive);
                Assert.False(WebcamTracker.Instance.IsRunning);
            });
        }
        finally
        {
            focus.CanRunOverride = null;
            focus.MasterEnabled = false;
            focus.Stop();
        }
    }

    /// <summary>IA10: the pill stops Focus Gaze for good. The master and the saved intent are cleared, so
    /// a tracker that another feature restarts does not re-arm it, and the Play switch reads off.</summary>
    [Fact]
    public async Task ThePill_ClearsTheMaster_SoARestartedTrackerDoesNotReArmIt_AndTheSwitchShowsOff()
    {
        var focus = GazeFocusHead.Instance;
        var s = ConditioningControlPanel.CoreSettings.Current;
        bool intentWas = s.FocusGazeEnabled;
        var (c1, c2, c3, c4) = (s.FlashGazePopEnabled, s.FlashGazeLingerEnabled, s.BubbleGazePopEnabled, s.VideoGazeClickEnabled);
        // The per-effect gaze options are their own switches (they keep the engine wanted): off for this test.
        s.FlashGazePopEnabled = s.FlashGazeLingerEnabled = s.BubbleGazePopEnabled = s.VideoGazeClickEnabled = false;
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var play = new ConditioningControlPanel.Avalonia.Views.Tabs.PlayTabView();
                var box = play.FindControl<global::Avalonia.Controls.Primitives.ToggleButton>("ChkPlayFocusGaze")!;
                bool able = true;
                focus.CanRunOverride = () => able;
                s.FocusGazeEnabled = true;
                focus.MasterEnabled = true;
                play.FollowFocusGazeIntent();
                Assert.True(focus.IsActive);
                Assert.True(box.IsChecked);

                await MainShellWindow.StopCameraConsumersAsync();
                Assert.False(focus.IsActive);
                Assert.False(focus.MasterEnabled);
                Assert.False(s.FocusGazeEnabled);

                // Another feature brings the tracker back: Focus Gaze stays down.
                focus.EvaluateDesiredState();
                Assert.False(focus.IsActive);

                // Not running at the time of the press (camera up for something else): still cleared.
                able = false;
                s.FocusGazeEnabled = true;
                focus.MasterEnabled = true;
                play.FollowFocusGazeIntent();
                Assert.True(box.IsChecked);
                Assert.False(focus.IsActive);
                await MainShellWindow.StopCameraConsumersAsync();
                able = true;
                focus.EvaluateDesiredState();
                Assert.False(focus.IsActive);
                Assert.False(s.FocusGazeEnabled);

                play.FollowFocusGazeIntent();
                Assert.NotEqual(true, box.IsChecked);
            });
        }
        finally
        {
            focus.CanRunOverride = null;
            focus.MasterEnabled = false;
            focus.Stop();
            s.FocusGazeEnabled = intentWas;
            (s.FlashGazePopEnabled, s.FlashGazeLingerEnabled, s.BubbleGazePopEnabled, s.VideoGazeClickEnabled) = (c1, c2, c3, c4);
            ConditioningControlPanel.CoreSettings.SaveImmediate();
        }
    }
}
