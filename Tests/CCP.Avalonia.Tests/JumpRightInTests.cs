using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Start caret's "Jump right in" (WPF MainWindow.StartStop.cs:132 RandomizeAndStart):
/// arms a random, bounded mix and starts the engine; refused whole while a session holds the lock.</summary>
public sealed class JumpRightInTests
{
    [Fact]
    public void JumpRightInArmsABoundedMixAndStarts_RefusedUnderTheSessionLock()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            var prevSettings = CoreSettings.ServiceProvider;
            CoreSettings.ServiceProvider = () => service;
            var prevSession = CoreSession.IsSessionRunningProvider;
            var s = service.Current;
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                var item = shell.Named<MenuItem>("MenuJumpRightIn")!;

                // A session holds the lock: nothing re-rolls, nothing starts, and the user is told why.
                CoreSession.IsSessionRunningProvider = () => true;
                (s.FlashEnabled, s.SubliminalEnabled, s.FlashFrequency) = (false, false, 5);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.False(CoreEngine.IsRunning);
                Assert.False(s.FlashEnabled || s.SubliminalEnabled);
                Assert.Equal(5, s.FlashFrequency);
                var notice = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
                notice.Close();

                // No session: the mix is armed inside WPF's ranges and the engine starts.
                CoreSession.IsSessionRunningProvider = null;
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.True(s.FlashEnabled && s.SubliminalEnabled);
                Assert.InRange(s.FlashFrequency, 20, 80);
                Assert.InRange(s.SimultaneousImages, 2, 8);
                Assert.InRange(s.SubliminalFrequency, 3, 12);
                Assert.True(CoreEngine.IsRunning);
            }
            finally
            {
                CoreEngine.Stop();
                CoreEngine.StoppedHook = null;
                CoreSession.IsSessionRunningProvider = prevSession;
                CoreSettings.ServiceProvider = prevSettings;
                shell.Close();
            }
        });
    }
}
