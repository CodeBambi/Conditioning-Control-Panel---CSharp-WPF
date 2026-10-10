using System.Threading.Tasks;
using Avalonia;
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
}
