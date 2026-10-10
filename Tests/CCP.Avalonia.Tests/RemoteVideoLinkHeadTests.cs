using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The controller's play_hypnotube on the desktop head (owner, 2026-10-10: ported, site-locked):
/// the head re-checks the address, never covers a game, says why nothing opened, and a panic or any
/// remote stop takes the controller's video down without touching a page the subject opened.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class RemoteVideoLinkHeadTests
{
    private const string Good = "https://hypnotube.com/video/some-title-12345.html";

    [Fact]
    public void The_head_refuses_with_a_reason_and_panic_takes_the_video_down() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var (oldProvider, oldOwned) = (CoreSettings.ServiceProvider, MainShellWindow.RemoteVideoScreenOwned);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var head = (RemoteCommands.IRemoteHead)shell;

            // The head opens nothing the Core rule would not, whoever calls it.
            foreach (var bad in new[] { "http://hypnotube.com/video/1", "https://hypnotube.com.evil.example/video/1", "javascript:alert(1)", "https://evil.example/video/1" })
                Assert.Equal(RemoteVideoLink.Refused, head.PlayVideoLink(bad));
            Assert.False(shell.IsRemoteBrowserVideoActive);

            // A game owns the screen: refused with the reason, the game stays (WPF ccp-bugs#1138).
            MainShellWindow.RemoteVideoScreenOwned = () => true;
            Assert.Equal(MainShellWindow.GameOnScreen, head.PlayVideoLink(Good));
            Assert.False(shell.IsRemoteBrowserVideoActive);
            MainShellWindow.RemoteVideoScreenOwned = () => false;

            // Offline mode blocks the browser for a controller as for anyone: said, and no claim left standing.
            CoreSettings.Current.OfflineMode = true;
            Assert.Equal(MainShellWindow.BrowserCouldNotOpen, head.PlayVideoLink(Good));
            Assert.False(shell.IsRemoteBrowserVideoActive);
            CoreSettings.Current.OfflineMode = false;

            // Opened (or the headless browser could not, which must also leave no claim).
            var result = head.PlayVideoLink(Good);
            Assert.True(result is null or MainShellWindow.BrowserCouldNotOpen);
            Assert.Equal(result == null, shell.IsRemoteBrowserVideoActive);

            // Panic: the registered surface takes it down.
            shell.MarkRemoteVideoForTest();
            PanicSurfaces.All.Single(x => x.Id == "remote-overlays").Stop(shell);
            Assert.False(shell.IsRemoteBrowserVideoActive);

            // The remote stop paths (controller leaves, stop, trigger_panic) reach it through the head.
            shell.MarkRemoteVideoForTest();
            head.StopVideoLink();
            Assert.False(shell.IsRemoteBrowserVideoActive);
            head.StopVideoLink();   // nothing up: a no-op, the subject's own page is never touched
        }
        finally
        {
            shell?.Close();
            Dispatcher.UIThread.RunJobs();
            (CoreSettings.ServiceProvider, MainShellWindow.RemoteVideoScreenOwned) = (oldProvider, oldOwned);
        }
    });
}
