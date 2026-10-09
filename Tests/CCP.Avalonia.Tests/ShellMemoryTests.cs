using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

// Runs alone: the test yields between shells, and a shell another class opens in that gap reads as
// a rooted closed shell here.
[Collection(RunsAloneCollection.Name)]
public sealed class ShellMemoryTests(ITestOutputHelper output)
{
    [Fact]
    public Task ClosedShellsReleaseMemory() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        CoreSettings.Current.Welcomed = true;
        CoreSettings.Current.HasAcceptedAgeVerification = true;
        CoreSettings.Current.AvatarEnabled = false;
        var windows = new WeakReference[6];
        for (var i = 0; i < windows.Length; i++)
        {
            windows[i] = OpenAndClose();
            await Task.Yield();
            // Finalizers/compositor disposal can queue another dispatcher callback.
            // Drain those as well; no real-time sleeps or RSS thresholds.
            for (var pass = 0; pass < 3; pass++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            GC.Collect();
            using var process = Process.GetCurrentProcess();
            output.WriteLine($"iteration {i}: rss={process.WorkingSet64 / 1048576} MiB managed={GC.GetTotalMemory(false) / 1048576} MiB alive={Array.FindAll(windows, w => w?.IsAlive == true).Length}");
        }
        // A shell starts background work at open (art decodes, fetches) that holds it until the work
        // lands. Earlier shells had the later iterations to finish; the last one gets a bounded wait.
        // Run alone this passed; in the full suite it failed about one run in two without the wait.
        for (var wait = 0; wait < 60 && Array.Exists(windows, w => w.IsAlive); wait++)
        {
            await Task.Delay(250);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        // Object liveness, not an allocator/OS-dependent RSS threshold, is the regression guard.
        Assert.All(windows, w => Assert.False(w.IsAlive, "A closed shell is still rooted."));
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OpenAndClose()
    {
        var shell = new MainShellWindow();
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        var closed = false;
        shell.Closed += (_, _) => closed = true;
        shell.Close();
        Assert.True(closed);
        return new WeakReference(shell);
    }
}
