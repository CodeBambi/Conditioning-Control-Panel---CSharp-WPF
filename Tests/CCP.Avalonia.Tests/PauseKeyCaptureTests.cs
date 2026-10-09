using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Input;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>platform#11: Settings > Devices "Pause key" captures the next key (WPF BtnPauseKey_Click +
/// OnGlobalKeyPressed's capture branch); Escape clears it; the pause press path stays quiet meanwhile.</summary>
public sealed class PauseKeyCaptureTests
{
    [Fact]
    public async Task TheNextKeyBindsEscapeClearsAndThePressPathWaits()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldProvider = CoreSettings.ServiceProvider;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            Window? window = null;
            try
            {
                var s = CoreSettings.Current;
                s.PanicKeyEnabled = true;
                s.PanicKey = "F8";
                s.PauseKey = "F9";
                var section = new DevicesSettingsSection();
                window = new Window { Content = section };
                window.Show();
                window.Activate();
                Dispatcher.UIThread.RunJobs();
                var button = section.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BtnPauseKey");

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(DevicesSettingsSection.CapturingPauseKey);
                Win32Input.ResetPauseKeyForTest();
                Assert.False(Win32Input.OnPauseKeyDown(VirtualKeys.Of("F9")));   // capture first, as WPF
                Win32Input.ResetPauseKeyForTest();

                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.P });
                Assert.Equal("P", s.PauseKey);
                Assert.Equal("⏸ P", ((TextBlock)button.Content!).Text);

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                Assert.Equal("", s.PauseKey);   // Escape unbinds
            }
            finally
            {
                window?.Close();
                Win32Input.ResetPauseKeyForTest();
                service.SealForReset();
                CoreSettings.ServiceProvider = oldProvider;
            }
            return Task.CompletedTask;
        });
    }
}
