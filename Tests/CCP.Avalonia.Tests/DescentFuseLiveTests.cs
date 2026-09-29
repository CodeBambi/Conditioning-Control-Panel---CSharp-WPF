using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>THE FUSE on this head: the sync response arms and kills the Core countdown
/// (WPF ProfileSyncService.HandleDescentCountdown), and the shell paints each phase as WPF
/// MainWindow.DescentFuse.cs ApplyFusePhase does.</summary>
public sealed partial class AccountSeedTests
{
    [Fact]
    public void SyncResponse_ArmsTheFuse_AndItsAbsenceIsTheKillSwitch() => WithFreshInstall(async () =>
    {
        var s = CoreSettings.Current;
        var old = s.DescentCeremonyAtUtc;
        using var fuse = new DescentCountdownService();
        try
        {
            s.DescentCeremonyAtUtc = null;
            var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
            sync.Countdown = fuse;
            var at = DateTime.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ");
            wire.SyncReply = "{\"success\":true,\"descent_countdown\":{\"ceremony_at\":\"" + at + "\"}}";
            Assert.True(await AccountSeed.LoadProfileAsync());
            Assert.Equal(at, s.DescentCeremonyAtUtc);
            Assert.Equal(DescentFusePhase.Vigil, fuse.LastAnnouncedPhase);

            wire.SyncReply = "{\"success\":true}";
            sync.UtcNow = () => DateTime.UtcNow.AddMinutes(5);   // past the 30 s cooldown
            Assert.True(await sync.PushAsync("heartbeat"));
            Assert.Null(s.DescentCeremonyAtUtc);
            Assert.Equal(DescentFusePhase.Dark, fuse.LastAnnouncedPhase);
        }
        finally { s.DescentCeremonyAtUtc = old; }
    });
}

public sealed class DescentFuseShellTests
{
    [Fact]
    public Task ShellPaintsEachFusePhaseLikeWpf() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var spark = shell.Named<Grid>("FuseSparkHost")!;
            var corner = shell.Named<Border>("FuseCornerReadout")!;
            var digits = shell.Named<TextBlock>("FuseCornerDigits")!;
            Assert.False(spark.IsVisible);   // Dark: every install today

            shell.ApplyFusePhase(DescentFusePhase.Whisper);
            Assert.True(spark.IsVisible);
            Assert.Null(ToolTip.GetTip(spark));   // no hover readout before Clock
            Assert.False(corner.IsVisible);

            shell.ApplyFusePhase(DescentFusePhase.Clock);
            Assert.NotNull(ToolTip.GetTip(spark));
            Assert.False(corner.IsVisible);

            shell.ApplyFusePhase(DescentFusePhase.Vigil);
            Assert.True(corner.IsVisible);
            Assert.Equal(1.25, ((ScaleTransform)spark.RenderTransform!).ScaleX);
            Assert.Equal(Color.FromRgb(0xC9, 0xC4, 0xD6), ((ISolidColorBrush)digits.Foreground!).Color);

            shell.ApplyFusePhase(DescentFusePhase.Terminal);
            Assert.Equal(Color.FromRgb(0xE0, 0xB0, 0x52), ((ISolidColorBrush)digits.Foreground!).Color);

            shell.ApplyFusePhase(DescentFusePhase.Dark);   // the kill switch, live
            Assert.False(spark.IsVisible);
            Assert.False(corner.IsVisible);
            Assert.Null(ToolTip.GetTip(spark));
        }
        finally { shell.Close(); CoreSettings.ServiceProvider = null; }
        return Task.CompletedTask;
    });
}
