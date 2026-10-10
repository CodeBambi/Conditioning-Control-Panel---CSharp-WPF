using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace CCP.Avalonia.Tests.Ship;

/// <summary>The Windows ship path: one version source above 7.1.5, WPF's identity, and a head that a
/// running WPF 7.1.5 (and the installer) sees as the same app.</summary>
public sealed class ShipIdentityTests
{
    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));

    private static string PropsVersion() =>
        Regex.Match(File.ReadAllText(Path.Combine(RepoRoot(), "Version.props")), "<Version>([^<]+)</Version>").Groups[1].Value;

    [Fact]
    public void TheVersionIsAboveTheWpfReleaseItUpgrades()
    {
        Assert.True(Version.Parse(PropsVersion()) > new Version(7, 1, 5), "Version.props must be above 7.1.5");
    }

    [Fact]
    public void TheHeadReadsItsVersionFromVersionProps()
    {
        var v = typeof(AppIdentity).Assembly.GetName().Version!;
        Assert.Equal(PropsVersion(), $"{v.Major}.{v.Minor}.{v.Build}");
        Assert.StartsWith(PropsVersion(), AppIdentity.VersionLabel);
    }

    [Theory]
    [InlineData("7.2.0", "7.2.0-parity", "7.2.0-parity")]
    [InlineData("7.2.0", "7.2.0-parity+abc123", "7.2.0-parity")]
    [InlineData("7.2.0", "7.2.0", "7.2.0")]
    [InlineData("7.2.0", "7.2.0+abc123", "7.2.0")]
    [InlineData("7.2.0", null, "7.2.0")]
    [InlineData("7.2.0", "9.9.9-other", "7.2.0")]
    public void TheLabelIsTheNumberPlusAnUnreleasedTag(string number, string? informational, string expected) =>
        Assert.Equal(expected, AppIdentity.LabelFrom(number, informational));

    [Fact]
    public void TheIdlePillNamesThisBuild()
    {
        Assert.Equal("v7.2.0 IS OUT", AppIdentity.StampVersion("v7.1.5 IS OUT", "7.2.0"));
        Assert.Equal("v7.2.0 - Stay Tuned", AppIdentity.StampVersion("v7.1.5 - Stay Tuned", "7.2.0"));
        Assert.Equal("no number", AppIdentity.StampVersion("no number", "7.2.0"));
    }

    [Fact]
    public void TheNamesAreWpfs()
    {
        // WPF 7.1.5 App.xaml.cs:52-61 and installer.iss AppMutex / MyAppExeName.
        Assert.Equal("ConditioningControlPanel_SingleInstance_Mutex", SingleInstance.MutexName);
        Assert.Equal("ConditioningControlPanel_ShowWindow_Signal", AppIdentity.ShowSignalName);
        Assert.Equal("ConditioningControlPanel_ShowAck_Signal", AppIdentity.ShowAckSignalName);
        Assert.Equal("ConditioningControlPanel.exe", AppIdentity.InstalledExeName);
        var iss = File.ReadAllText(Path.Combine(RepoRoot(), "installer.iss"));
        Assert.Contains("AppMutex=" + SingleInstance.MutexName, iss);
    }

    [Fact]
    public void AWpfLaunchFindsThisHeadAndGetsItsAck()
    {
        if (!OperatingSystem.IsWindows()) return;
        var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
        string? got = "unset";
        // Past startup the ack comes only after the route ran (a wedged UI thread never acks).
        var phase = WpfInstanceBridge.StartupPhase;
        WpfInstanceBridge.StartupPhase = false;
        try
        {
        using var primary = SingleInstance.Claim(suffix, p => { got = p; return Task.CompletedTask; });
        Assert.NotNull(primary);

        // What WPF's second instance does (App.xaml.cs:1452-1503): open the ack, reset it, set show, wait.
        Assert.True(EventWaitHandle.TryOpenExisting(AppIdentity.ShowAckSignalName + suffix, out var ack));
        Assert.True(EventWaitHandle.TryOpenExisting(AppIdentity.ShowSignalName + suffix, out var show));
        using (ack) using (show)
        {
            ack!.Reset();
            show!.Set();
            Assert.True(ack.WaitOne(TimeSpan.FromSeconds(10)), "the head never acked WPF's show signal");
        }
        Assert.Equal(WpfInstanceBridge.HandoffFileMarker, got);   // the router then reads the handoff file
        }
        finally { WpfInstanceBridge.StartupPhase = phase; }
    }

    [Fact]
    public void ThisHeadFindsARunningWpfAndExits()
    {
        if (!OperatingSystem.IsWindows()) return;
        var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
        try { File.Delete(FileOpenHandoff.PathIn(CorePaths.UserData)); } catch { }

        // A stand-in WPF primary: holds the mutex and the two events, acks a show signal.
        using var ready = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        (string? action, string? path) seen = (null, null);
        var wpf = new Thread(() =>
        {
            using var mutex = new Mutex(true, SingleInstance.MutexName + suffix, out _);
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset, AppIdentity.ShowSignalName + suffix);
            using var ack = new EventWaitHandle(false, EventResetMode.ManualReset, AppIdentity.ShowAckSignalName + suffix);
            ready.Set();
            if (show.WaitOne(TimeSpan.FromSeconds(15)))
            {
                seen = FileOpenHandoff.Consume(CorePaths.UserData);
                ack.Set();
            }
            done.Wait(TimeSpan.FromSeconds(15));
            mutex.ReleaseMutex();
        });
        wpf.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));

        bool exited = false;
        var t = new Thread(() =>
        {
            var second = SingleInstance.Claim(suffix, _ => Task.CompletedTask, "game:race");
            exited = second is null;
            second?.Dispose();
        });
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(30)));
        done.Set();
        wpf.Join(TimeSpan.FromSeconds(10));

        Assert.True(exited, "the head did not take WPF's ack as a live primary");
        Assert.Equal((LauncherHandoff.Action, "game:race"), seen);         // the surface rode WPF's file
    }
}
