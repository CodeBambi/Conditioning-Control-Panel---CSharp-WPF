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
[Collection(RunsAloneCollection.Name)]
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
            CameraList.Override = V4l2Cameras.Enumerate;   // the fake sysfs tree on every OS
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
            CameraList.Override = null;
            CoreWebcam.IsAvailableProvider = oldAvail;
            s.WebcamDeviceIndex = oldIdx; s.WebcamDeviceName = oldName;
            CoreSettings.SaveImmediate();   // cancels the pick's 500 ms debounced write so it cannot land in a later test (P02)
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    /// <summary>u1 G4: the list is one seam on every OS, quiet when switched off or when the source throws.</summary>
    [Fact]
    public void CameraList_IsOneSeam_AndNeverThrows()
    {
        var old = CameraList.Override;
        try
        {
            CameraList.Override = null;
            Assert.True(CameraList.Disabled);                 // TestUserDataProfile: tests never ask the real machine
            Assert.Empty(CameraList.Enumerate());
            Assert.True(OpenCvFrameSource.Disabled);
            using (var src = new OpenCvFrameSource()) Assert.False(src.Open());   // and never open a real camera
            CameraList.Override = () => new[] { (0, "Integrated Camera"), (1, "OBS Virtual Camera") };
            Assert.Equal(2, CameraList.Enumerate().Count);
            CameraList.Override = () => throw new System.InvalidOperationException("driver wedged");
            Assert.Empty(CameraList.Enumerate());
        }
        finally { CameraList.Override = old; }
    }

    /// <summary>u1 G11: WPF GetWebcamCalibrationScreen. Primary by default, by name otherwise, Primary when gone.</summary>
    [Fact]
    public void TrackingMonitor_PicksPrimaryNamedOrFallsBack()
    {
        var screens = new[] { ("DISPLAY1", false), ("DISPLAY2", true), ("DISPLAY3", false) };
        Assert.Equal(1, WebcamScreen.Pick(screens, "Primary"));
        Assert.Equal(1, WebcamScreen.Pick(screens, ""));
        Assert.Equal(2, WebcamScreen.Pick(screens, "display3"));
        Assert.Equal(1, WebcamScreen.Pick(screens, "DISPLAY9"));
        Assert.Equal(-1, WebcamScreen.Pick(System.Array.Empty<(string, bool)>(), "Primary"));
    }

    /// <summary>u1 G11: the monitor combo and the debug cursor are live, the pill reads the tracker's state.</summary>
    [Fact]
    public async Task MonitorCursorAndPill_AreLive()
    {
        var oldAvail = CoreWebcam.IsAvailableProvider;
        var s = CoreSettings.Current;
        var oldScreen = s.WebcamCalibrationScreen;
        try
        {
            CoreWebcam.IsAvailableProvider = () => true;
            s.WebcamCalibrationScreen = "Primary";
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var section = new DevicesSettingsSection();
                var window = new Window { Content = section };
                window.Show();
                var mon = section.FindControl<ComboBox>("CmbWebcamMonitor")!;
                var cursor = section.FindControl<CheckBox>("ChkWebcamDebugCursor")!;
                var log = section.FindControl<TextBlock>("TxtWebcamDebugLog")!;
                Assert.True(mon.IsEnabled);
                Assert.True(cursor.IsEnabled);
                Assert.Equal("Primary", ((ComboBoxItem)mon.SelectedItem!).Tag);
                if (mon.Items.Count > 1)
                {
                    mon.SelectedIndex = 1;
                    Assert.Equal(((ComboBoxItem)mon.Items[1]!).Tag, s.WebcamCalibrationScreen);
                    Assert.Contains("Calibration monitor set to", log.Text);
                }

                Assert.Equal("rf_webcam_stopped", WebcamTracker.Instance.StateKey);
                Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("rf_webcam_stopped"),
                    section.FindControl<TextBlock>("TxtWebcamDebugStatus")!.Text);

                cursor.IsChecked = true;
                Assert.True(ConditioningControlPanel.Avalonia.Views.Overlays.GazeDebugCursor.IsShown);
                Assert.Contains("Debug cursor enabled.", log.Text);
                ConditioningControlPanel.Avalonia.Views.Overlays.GazeDebugCursor.OnGaze(new Point(120, 80));
                Assert.Equal(new Point(120, 80), ConditioningControlPanel.Avalonia.Views.Overlays.GazeDebugCursor.LastPoint);
                cursor.IsChecked = false;
                Assert.False(ConditioningControlPanel.Avalonia.Views.Overlays.GazeDebugCursor.IsShown);
                Assert.Null(ConditioningControlPanel.Avalonia.Views.Overlays.GazeDebugCursor.LastPoint);
                Assert.Contains("Debug cursor hidden.", log.Text);
                window.Close();
                return Task.CompletedTask;
            });
        }
        finally
        {
            ConditioningControlPanel.Avalonia.Views.Overlays.GazeDebugCursor.HideAll();
            CoreWebcam.IsAvailableProvider = oldAvail;
            s.WebcamCalibrationScreen = oldScreen;
            CoreSettings.SaveImmediate();
        }
    }
}
