using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The panic press on this head: WPF HandlePanicKeyPress + RunPanicStopTail with no engine
/// running - each press stops everything, a second press within 2 s exits, a press spent on a lock
/// card never counts, and a disabled key or a rebind in progress does nothing.</summary>
public sealed class PanicKeyTests
{
    [Fact]
    public async Task EachPressStopsEverythingAndASecondWithinTwoSecondsExits()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            s.PanicKeyEnabled = true;
            s.PanicKey = "F8";
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                void Arm() { s.FlashEnabled = s.BouncingTextEnabled = true; shell.StartEngine(); }
                var closed = false;
                shell.Closed += (_, _) => closed = true;

                // Disabled key, and a rebind in progress: nothing stops.
                Arm();
                s.PanicKeyEnabled = false;
                shell.HandlePanicKeyPress(t0);
                Assert.True(CoreFlash.IsRunning);
                s.PanicKeyEnabled = true;
                MainShellWindow.CapturingPanicKey = true;
                shell.HandlePanicKeyPress(t0);
                Assert.True(CoreFlash.IsRunning);
                MainShellWindow.CapturingPanicKey = false;

                // Press 1 stops everything and the app stays up; press 2 three seconds later is a new
                // first press, not an exit.
                shell.HandlePanicKeyPress(t0);
                Assert.False(CoreFlash.IsRunning || CoreEngine.IsRunning);
                Assert.True(s.FlashEnabled && s.BouncingTextEnabled);   // panic never unticks saved flags
                Arm();
                shell.HandlePanicKeyPress(t0.AddSeconds(3));
                Assert.False(CoreFlash.IsRunning);
                Assert.False(closed);

                // A press spent dismissing a lock card does not count: the next press is a first press.
                LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: true);
                Assert.True(LockCardWindow.IsAnyOpen());
                shell.HandlePanicKeyPress(t0.AddSeconds(10));
                Assert.False(LockCardWindow.IsAnyOpen());
                shell.HandlePanicKeyPress(t0.AddSeconds(10.5));
                Assert.False(closed);

                // A second counted press inside 2 s exits.
                shell.HandlePanicKeyPress(t0.AddSeconds(11));
                Assert.True(closed);
            }
            finally
            {
                CoreEngine.StoppedHook = null;
                CoreEngine.Stop();
            }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task AnAbandonedRebindIsCancelledWhenTheWindowLosesFocus()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            s.PanicKeyEnabled = true;
            s.PanicKey = "F8";
            var section = new DevicesSettingsSection();
            var window = new Window { Content = section };
            window.Show();
            window.Activate();
            var button = section.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BtnPanicKey");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(MainShellWindow.CapturingPanicKey);

            // The user clicks into another window instead of pressing a key. Headless never
            // deactivates a window, so raise it the way the platform does (WindowBase.HandleDeactivated).
            typeof(WindowBase).GetMethod("HandleDeactivated",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, null);
            Assert.False(MainShellWindow.CapturingPanicKey);
            Assert.Equal("🔑 F8", ((TextBlock)button.Content!).Text);

            // ...and the panic key still works.
            var shell = new MainShellWindow();
            shell.Show();
            s.FlashEnabled = true;
            CoreFlash.Start();
            shell.HandlePanicKeyPress(new DateTime(2026, 1, 1));
            Assert.False(CoreFlash.IsRunning);
            shell.Close();
            window.Close();
            return Task.CompletedTask;
        });
    }

    /// <summary>Audit #1790: WPF's rebind click only raises a flag, so clicking it twice is one
    /// capture. Two handlers here would leave the second attached after the first key, and the
    /// NEXT key press anywhere would silently rebind the panic key again.</summary>
    [Fact]
    public async Task ClickingRebindTwiceCapturesOnlyTheNextKey()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var oldKey = s.PanicKey;
            var section = new DevicesSettingsSection();
            var window = new Window { Content = section };
            try
            {
                s.PanicKey = "F8";
                window.Show();
                window.Activate();
                var button = section.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BtnPanicKey");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                void Press(global::Avalonia.Input.Key key) => window.RaiseEvent(new global::Avalonia.Input.KeyEventArgs
                {
                    RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent,
                    Key = key,
                });
                Press(global::Avalonia.Input.Key.F9);
                Assert.Equal("F9", s.PanicKey);
                Press(global::Avalonia.Input.Key.F10);
                Assert.Equal("F9", s.PanicKey);
            }
            finally
            {
                window.Close();
                s.PanicKey = oldKey;
                MainShellWindow.CapturingPanicKey = false;
            }
            return Task.CompletedTask;
        });
    }
}
