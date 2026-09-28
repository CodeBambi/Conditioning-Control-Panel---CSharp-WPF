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
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Features;
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
            shell.TrayHostPresent = () => true;
            var items = shell.Tray!.Menu!.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).ToList();
            Assert.Equal(
                new[] { "tray_show", CoreSettings.Current.IsBambiMode ? "tray_wake_bambi" : "tray_wake", "tray_stop_everything", "tray_exit" }
                    .Select(Loc.Get),
                items.Select(i => i.Header));
            Assert.IsType<NativeMenuItemSeparator>(shell.Tray.Menu.Items[2]);

            // Stop everything: the three features untick, their schedules stop, queued flashes
            // are dropped, and the shell stays open.
            CoreSettings.Current.FlashEnabled = CoreSettings.Current.SubliminalEnabled = CoreSettings.Current.BouncingTextEnabled = true;
            CoreFlash.Start();
            CoreSubliminal.Start();
            Assert.True(CoreFlash.IsRunning && CoreSubliminal.IsRunning);
            var card = new BouncingTextFeatureControl();   // a shown card repaints from the flag
            var cardWindow = new Window { Content = card };
            cardWindow.Show();
            var cardEnable = card.GetVisualDescendants().OfType<CheckBox>().First(c => c.Name == "ChkEnable");
            Assert.True(cardEnable.IsChecked);
            var generation = FlashOverlay.Generation;
            items[2].Command!.Execute(null);
            Assert.False(CoreFlash.IsRunning);
            Assert.False(CoreSubliminal.IsRunning);
            Assert.Equal(generation + 1, FlashOverlay.Generation);
            Assert.False(CoreSettings.Current.FlashEnabled || CoreSettings.Current.SubliminalEnabled || CoreSettings.Current.BouncingTextEnabled);
            Assert.True(shell.IsVisible);
            Dispatcher.UIThread.RunJobs();
            Assert.False(cardEnable.IsChecked);
            cardWindow.Close();

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

    [Fact]
    public async Task CloseWithoutATrayHostReallyCloses()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var shell = new MainShellWindow();
            shell.Show();
            shell.CreateTray();
            shell.TrayHostPresent = () => false;   // Avalonia's tray fell back silently: nothing to hide behind
            var closed = false;
            shell.Closed += (_, _) => closed = true;
            shell.Close();
            Assert.True(closed);
            shell.ShowFromTray();                  // WPF _windowClosed: a late Show is a no-op, not a throw
            shell.Tray!.Dispose();
            return Task.CompletedTask;
        });
    }
}
