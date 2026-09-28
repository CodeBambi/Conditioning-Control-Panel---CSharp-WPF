using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
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
            void Arm() { s.FlashEnabled = s.BouncingTextEnabled = true; CoreFlash.Start(); }

            var shell = new MainShellWindow();
            shell.Show();
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
            Assert.False(CoreFlash.IsRunning);
            Assert.False(s.FlashEnabled || s.BouncingTextEnabled);
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
            return Task.CompletedTask;
        });
    }
}
