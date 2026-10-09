using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Settings → Devices camera picker over a fake sysfs tree (no camera is opened):
/// lists capture nodes only, restores the saved one, saves a pick, and Refresh reports the real count.</summary>
public sealed class WebcamDevicePickerTests
{
    [Fact]
    public async Task PickerListsCaptureNodesAndSavesThePick()
    {
        var root = Directory.CreateTempSubdirectory("ccp-v4l2-").FullName;
        void Node(int n, string name, int index)
        {
            var d = Directory.CreateDirectory(Path.Combine(root, $"video{n}")).FullName;
            File.WriteAllText(Path.Combine(d, "name"), name + "\n");
            File.WriteAllText(Path.Combine(d, "index"), index + "\n");
        }
        Node(0, "Cam A", 0);
        Node(1, "Cam A", 1);     // metadata twin: must not be listed
        Node(2, "Cam B", 0);

        var oldRoot = V4l2Cameras.Root;
        var oldAvail = CoreWebcam.IsAvailableProvider;
        var s = CoreSettings.Current;
        int oldIdx = s.WebcamDeviceIndex; var oldName = s.WebcamDeviceName;
        try
        {
            V4l2Cameras.Root = root;
            CoreWebcam.IsAvailableProvider = () => true;
            s.WebcamDeviceIndex = 2;
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var section = new DevicesSettingsSection();
                var window = new Window { Content = section };
                window.Show();
                var cmb = section.FindControl<ComboBox>("CmbWebcamDevice")!;
                Assert.True(cmb.IsEnabled);
                Assert.Equal(new[] { "[0] Cam A", "[2] Cam B" },
                    cmb.Items.Cast<ComboBoxItem>().Select(i => i.Content!.ToString()).ToArray());
                Assert.Equal("[2] Cam B", ((ComboBoxItem)cmb.SelectedItem!).Content);
                Assert.Equal(2, OpenCvFrameSource.ResolveSavedIndex());
                s.WebcamDeviceIndex = -1;   // never picked: tracking opens the camera the picker shows first
                Assert.Equal(0, OpenCvFrameSource.ResolveSavedIndex());
                Directory.Delete(Path.Combine(root, "video0"), true);
                Assert.Equal(2, OpenCvFrameSource.ResolveSavedIndex());   // gap: no /dev/video0
                Node(0, "Cam A", 0);
                s.WebcamDeviceIndex = 2;

                cmb.SelectedIndex = 0;   // the user picks Cam A
                Assert.Equal(0, s.WebcamDeviceIndex);
                Assert.Equal("[0] Cam A", s.WebcamDeviceName);
                var log = section.FindControl<TextBlock>("TxtWebcamDebugLog")!;
                Assert.Contains("Camera set to [0] Cam A. Will be used on next Start.", log.Text);

                Directory.Delete(Path.Combine(root, "video2"), true);
                section.FindControl<Button>("BtnWebcamDeviceRefresh")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("Re-scanned cameras: 1 found.", log.Text);

                Directory.Delete(root, true);
                section.FindControl<Button>("BtnWebcamDeviceRefresh")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("Re-scanned cameras: none detected.", log.Text);
                Assert.Equal("(no cameras detected)", ((ComboBoxItem)cmb.SelectedItem!).Content);
                window.Close();
                return Task.CompletedTask;
            });
        }
        finally
        {
            V4l2Cameras.Root = oldRoot;
            CoreWebcam.IsAvailableProvider = oldAvail;
            s.WebcamDeviceIndex = oldIdx; s.WebcamDeviceName = oldName;
            CoreSettings.SaveImmediate();   // cancels the pick's 500 ms debounced write so it cannot land in a later test (P02)
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
