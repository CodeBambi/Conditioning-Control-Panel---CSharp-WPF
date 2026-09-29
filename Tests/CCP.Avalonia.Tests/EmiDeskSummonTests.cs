using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF EmiDock.OnChipClick -> EmiDeskService.Toggle: the dock chip summons her, the chip
/// again sends her away through the outro, and a switched-off feature never summons.</summary>
public sealed class EmiDeskSummonTests
{
    [Fact]
    public Task DockChipTogglesHerThroughTheService() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var svc = EmiDeskService.Instance;
        try
        {
            CoreSettings.Current.EmiDeskMuteAvatar = false;   // no modal on the way out
            var dock = new EmiDock();
            var host = new Window { Width = 200, Height = 200, Content = dock };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var chip = dock.FindControl<Button>("BtnChip")!;
            void Click() { chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
            async Task Pump(int ms)
            {
                for (int t = 0; t < ms; t += 20)
                {
                    await Task.Delay(20);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                }
            }

            CoreSettings.Current.EmiDeskEnabled = false;
            Click();
            Assert.False(svc.IsOut);
            Assert.Null(svc.Window);

            CoreSettings.Current.EmiDeskEnabled = true;
            bool? heard = null;
            svc.OutChanged += (_, o) => heard = o;
            Click();
            await Pump(100);
            Assert.True(svc.IsOut);
            Assert.True(svc.Window!.IsVisible);
            Assert.True(heard);

            Click();
            await Pump(2000);   // wink + CRT off + burst sweep
            Assert.False(svc.IsOut);
            Assert.False(svc.Window!.IsVisible);
            Assert.False(heard);
            host.Close();
        }
        finally
        {
            svc.Window?.ShutDown();
            CoreSettings.ServiceProvider = oldSettings;
        }
    });
}
