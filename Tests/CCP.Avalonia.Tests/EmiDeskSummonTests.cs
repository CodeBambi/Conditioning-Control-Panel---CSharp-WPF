using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
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
            EventHandler<bool> onOut = (_, o) => heard = o;
            svc.OutChanged += onOut;
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
            svc.OutChanged -= onOut;
            host.Close();
        }
        finally
        {
            svc.Window?.ShutDown();
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    /// <summary>Headless app + fresh settings with a talking feature on and the mute setting on,
    /// so the prompt is asked; the prompt itself is <paramref name="ask"/>.</summary>
    private static Task Muted(Func<Task<EmiMuteChoice>> ask, Func<EmiDeskService, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var svc = EmiDeskService.Instance;
        var oldAsk = svc.AskMute;
        try
        {
            var s = CoreSettings.Current;
            s.EmiDeskEnabled = true;
            s.EmiDeskMuteAvatar = true;
            s.EmiDeskMuteDontAsk = false;
            s.AiChatEnabled = true;
            svc.ResetMutePrompt();
            svc.AskMute = ask;
            await body(svc);
        }
        finally
        {
            svc.AskMute = oldAsk;
            svc.ResetMutePrompt();
            svc.Window?.ShutDown();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    private static async Task Pump(int ms)
    {
        for (int t = 0; t < ms; t += 20)
        {
            await Task.Delay(20);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>WPF ShowGiggle (Speech.cs:461): with EMI out and the mute accepted, the tube does not speak.</summary>
    [Fact]
    public Task MuteAnswerSilencesTheTube() => Muted(() => Task.FromResult(EmiMuteChoice.Mute), async svc =>
    {
        var main = new Window { Width = 800, Height = 600 };
        main.Show();
        var tube = new AvatarTubeWindow(main);
        try
        {
            tube.GigglePriority("before", playSound: false);
            Assert.True(tube.IsSpeaking);   // control: she speaks while EMI is away

            await svc.Summon();
            Assert.True(svc.AvatarMuted);
            tube.GigglePriority("during", playSound: false);
            Assert.False(tube.IsSpeaking);
        }
        finally { tube.Close(); main.Close(); }
    });

    /// <summary>WPF :295 re-entry trap: a second summon while the prompt is up does not ask twice,
    /// a dismiss during it abandons the summon, and the prompt is asked once per session.</summary>
    [Fact]
    public Task MutePromptReentryIsGuarded() => AskedOnce(async (svc, asks, answer) =>
    {
        var first = svc.Summon();
        Assert.Equal(1, asks());
        await svc.Summon();          // chip again while the prompt is up
        Assert.Equal(1, asks());
        svc.Dismiss();               // the x while the prompt is up
        answer(EmiMuteChoice.Mute);
        await first;
        await Pump(1500);
        Assert.False(svc.Window!.IsVisible);
        Assert.False(svc.IsOut);

        await svc.Summon();          // same session: not asked again
        Assert.Equal(1, asks());
        Assert.True(svc.Window!.IsVisible);
    });

    private static Task AskedOnce(Func<EmiDeskService, Func<int>, Action<EmiMuteChoice>, Task> body)
    {
        int n = 0;
        var tcs = new TaskCompletionSource<EmiMuteChoice>();
        return Muted(() => { n++; return tcs.Task; }, svc => body(svc, () => n, c => tcs.TrySetResult(c)));
    }

    /// <summary>WPF EmiDeskSettings :123: switching the feature off sends her away.</summary>
    [Fact]
    public Task SettingsSwitchOffDismissesHer() => Muted(() => Task.FromResult(EmiMuteChoice.Keep), async svc =>
    {
        var section = new EmiDeskSettingsSection();
        var host = new Window { Width = 600, Height = 600, Content = section };
        host.Show();
        section.SyncFromSettings();
        Dispatcher.UIThread.RunJobs();
        try
        {
            await svc.Summon();
            Assert.True(svc.IsOut);
            section.FindControl<CheckBox>("ChkEnabled")!.IsChecked = false;
            await Pump(2000);
            Assert.False(svc.IsOut);
            Assert.False(svc.Window!.IsVisible);
        }
        finally { host.Close(); }
    });

    /// <summary>WPF App.OnExit closed her; here she goes with the main window.</summary>
    [Fact]
    public Task ClosingTheShellClosesHer() => Muted(() => Task.FromResult(EmiMuteChoice.Keep), svc =>
    {
        var main = new Window { Width = 400, Height = 300 };
        main.Show();
        var win = new EmiDeskWindow();
        bool closed = false;
        win.Closed += (_, _) => closed = true;
        EmiDeskService.HookShell(main, win);
        win.Show();
        main.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
        return Task.CompletedTask;
    });
}
