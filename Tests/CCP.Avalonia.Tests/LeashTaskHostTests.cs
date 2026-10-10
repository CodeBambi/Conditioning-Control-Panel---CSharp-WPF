using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Leash;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Wave 5 r10 follow-up: WPF AppLeashTaskHost + LeashTaskRunner on this head. Panic closes
/// the video window first; a cut ends the task; videos open caged.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class LeashTaskHostTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static void Raise(Type t, string field)
    {
        var f = t.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!;
        (f.GetValue(null) as Action)?.Invoke();
    }

    [Fact]
    public async Task Start_PutsTheLeashStopFirstInThePanicRegistry_Once_AndPanicClosesTheCage()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var (oldAll, oldWeb) = (PanicSurfaces.All, LeashPunishWindow.CreateWeb);
            LeashPunishWindow.CreateWeb = false;
            try
            {
                LeashTaskHost.ResetForTests();
                var runner = LeashTaskHost.Start(null);
                Assert.Same(runner, LeashTaskHost.Runner);
                Assert.Same(runner, LeashTaskHost.Start(null));
                LeashTaskHost.HookPanic();
                Assert.Equal(new[] { "intake", LeashTaskHost.PanicSurfaceId }, PanicSurfaces.All.Take(2).Select(s => s.Id));
                Assert.Single(PanicSurfaces.All, s => s.Id == LeashTaskHost.PanicSurfaceId);
                Assert.Equal(oldAll.Count + 1, PanicSurfaces.All.Count);

                Assert.True(LeashPunishWindow.OpenUrl("https://hypnotube.com/video/7", "Vex", locked: true));
                PanicSurfaces.All[1].Stop(null);
                Assert.Null(LeashPunishWindow.Current);   // a locked punishment window still closes at once on panic
                Assert.False(runner.IsRunning);

                Assert.True(LeashPunishWindow.OpenUrl("https://hypnotube.com/video/8", "Vex", locked: true));
                LeashTaskHost.EndTask("cut");
                Assert.Null(LeashPunishWindow.Current);
            }
            finally
            {
                LeashPunishWindow.CloseNow();
                PanicSurfaces.All = oldAll;
                LeashPunishWindow.CreateWeb = oldWeb;
                LeashTaskHost.ResetForTests();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task OpenWatch_HtOpensTheCage_EndWatchCloses_AndTheWindowEndRaisesFinished()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var oldWeb = LeashPunishWindow.CreateWeb;
            LeashPunishWindow.CreateWeb = false;
            var host = new LeashTaskHost();
            try
            {
                Assert.False(host.OpenWatch(new LeashWatch("ht", "12ab", null)));   // grammar: digits only
                var w = new LeashWatch("ht", "123", null);
                LeashWatch? finished = null;
                host.WatchFinished += x => finished = x;
                Assert.True(host.OpenWatch(w));
                Assert.NotNull(LeashPunishWindow.Current);
                Assert.True(host.WatchCaged);
                Assert.True(host.WatchOpen);
                Assert.Null(host.SampleWatch(w));   // nothing sampled yet: the runner's playability clock runs

                Raise(typeof(LeashPunishWindow), "VideoEnded");
                Assert.Same(w, finished);

                host.EndWatch();
                Assert.Null(LeashPunishWindow.Current);
                Assert.False(host.WatchOpen);
            }
            finally
            {
                host.Dispose();
                LeashPunishWindow.CloseNow();
                LeashPunishWindow.CreateWeb = oldWeb;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task LockCards_CountOnlyCompletedRealCards_AndSessionsSayTheyCannotStartHere()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var host = new LeashTaskHost();
            try
            {
                Assert.Equal(0, host.LockCardsCompleted);
                Raise(typeof(LockCardWindow), "Completed");
                Raise(typeof(LockCardWindow), "Completed");
                Assert.Equal(2, host.LockCardsCompleted);
                Assert.False(host.StartSession(PunishKind.Pink, 10));
            }
            finally { host.Dispose(); }
            Raise(typeof(LockCardWindow), "Completed");
            Assert.Equal(2, host.LockCardsCompleted);   // unhooked on Dispose
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void ParseSample_AndLocalMedia_AsWpf()
    {
        var s = LeashTaskHost.ParseSample("\"12.5,300\"", true)!.Value;
        Assert.Equal(12.5, s.CurrentSeconds);
        Assert.Equal(300, s.DurationSeconds);
        Assert.True(s.Visible);
        Assert.Null(LeashTaskHost.ParseSample("", true));
        Assert.True(double.IsNaN(LeashTaskHost.ParseSample("0,NaN", false)!.Value.DurationSeconds));
        Assert.False(LeashPlayability.Playing(LeashTaskHost.ParseSample("0,NaN", false)!.Value));

        var dir = Path.Combine(Path.GetTempPath(), "ccp-leash-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var enh = Path.Combine(dir, "clip.ccpenh.json");
            File.WriteAllText(enh, "{\"mediaSource\":\"other.mp4\"}");
            Assert.Null(LeashTaskHost.LocalMediaFor(enh));
            File.WriteAllText(Path.Combine(dir, "clip.webm"), "x");
            Assert.Equal(Path.Combine(dir, "clip.webm"), LeashTaskHost.LocalMediaFor(enh));
            File.WriteAllText(Path.Combine(dir, "other.mp4"), "x");
            Assert.Equal(Path.Combine(dir, "other.mp4"), LeashTaskHost.LocalMediaFor(enh));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
