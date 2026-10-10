using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>u1 T11 / G3: the camera shortcut is a real binding, and both pills read the saved combo.
/// The toggle is swapped for a counter: no camera is opened.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class CameraShortcutTests
{
    [Fact]
    public async Task ShortcutParses_Binds_AndThePillFollows()
    {
        var p = CoreSettings.Current.CompanionPrompt!;
        var old = (p.CameraShortcutKey, p.CameraShortcutModifiers);
        int toggles = 0;
        try
        {
            MainShellWindow.CameraToggleOverride = () => toggles++;
            p.CameraShortcutKey = "K"; p.CameraShortcutModifiers = "Control,Alt";
            Assert.Equal("Ctrl+Alt+K", MainShellWindow.FormatCameraShortcut());
            p.CameraShortcutKey = "J"; p.CameraShortcutModifiers = "Windows,Shift";   // WPF's spelling of Meta
            Assert.Equal((Key.J, KeyModifiers.Shift | KeyModifiers.Meta), MainShellWindow.CurrentCameraShortcut());
            p.CameraShortcutKey = "nonsense"; p.CameraShortcutModifiers = "";
            Assert.Equal((Key.K, KeyModifiers.Control | KeyModifiers.Alt), MainShellWindow.CurrentCameraShortcut());

            p.CameraShortcutKey = "M"; p.CameraShortcutModifiers = "Control,Shift";
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var section = new DevicesSettingsSection();
                var window = new Window { Content = section };
                window.Show();
                Assert.Equal("Ctrl+Shift+M", section.FindControl<TextBlock>("TxtCameraShortcutLabelDevices")!.Text);

                MainShellWindow.ApplyCameraShortcutTo(window);
                MainShellWindow.ApplyCameraShortcutTo(window);   // never stacks
                var binding = Assert.Single(window.KeyBindings.Where(b => ReferenceEquals(b.Command, MainShellWindow.ToggleCameraCommand)));
                Assert.Equal(new KeyGesture(Key.M, KeyModifiers.Control | KeyModifiers.Shift), binding.Gesture);
                binding.Command!.Execute(null);
                window.Close();
                return Task.CompletedTask;
            });
            // No live shell in this test: the command finds no window and does nothing, never throws.
            Assert.Equal(MainShellWindow.Current == null ? 0 : 1, toggles);
        }
        finally
        {
            MainShellWindow.CameraToggleOverride = null;
            (p.CameraShortcutKey, p.CameraShortcutModifiers) = old;
            CoreSettings.SaveImmediate();
        }
    }
}
