using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The shell tray: WPF's menu order, Stop everything, X-to-tray, Show and the real Exit.</summary>
public sealed class ShellTrayTests
{
    [Fact]
    public async Task TrayMenuStopsOverlaysHidesOnCloseAndExits()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var shell = new MainShellWindow();
            shell.Show();
            shell.CreateTray();   // also proves the avares app.ico resource loads
            var items = shell.Tray!.Menu!.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).ToList();
            Assert.Equal(
                new[] { "tray_show", CoreSettings.Current.IsBambiMode ? "tray_wake_bambi" : "tray_wake", "tray_stop_everything", "tray_exit" }
                    .Select(Loc.Get),
                items.Select(i => i.Header));
            Assert.IsType<NativeMenuItemSeparator>(shell.Tray.Menu.Items[2]);

            // Stop everything: overlays and their schedules go down, the shell stays open.
            CoreFlash.Start();
            CoreSubliminal.Start();
            Assert.True(CoreFlash.IsRunning && CoreSubliminal.IsRunning);
            items[2].Command!.Execute(null);
            Assert.False(CoreFlash.IsRunning);
            Assert.False(CoreSubliminal.IsRunning);
            Assert.False(BouncingTextOverlay.IsRunning);   // headless never starts it (no X11); the live check does
            Assert.True(shell.IsVisible);

            // X goes to the tray; Show brings it back; Exit really closes.
            var closed = false;
            shell.Closed += (_, _) => closed = true;
            shell.Close();
            Assert.False(closed);
            Assert.False(shell.IsVisible);
            items[0].Command!.Execute(null);
            Assert.True(shell.IsVisible);
            items[3].Command!.Execute(null);
            Assert.True(closed);
            shell.Tray.Dispose();
            return Task.CompletedTask;
        });
    }
}
