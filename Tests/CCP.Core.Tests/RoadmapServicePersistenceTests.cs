using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Serilog;
using Serilog.Events;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CCP.Core.Tests;

internal static class RoadmapTestProfile
{
    internal static string DirectoryPath { get; } = Path.Combine(
        Path.GetTempPath(), "ccp-roadmap-tests-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Initialize() =>
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", DirectoryPath);
}

public sealed class RoadmapServicePersistenceTests
{
    private static readonly object LogSwap = new();

    [Fact]
    public void FailedAutosaveStaysDirtyForDisposeRetry()
    {
        var profilePath = RoadmapTestProfile.DirectoryPath;
        var progressPath = Path.Combine(profilePath, "roadmap.json");
        RoadmapService? roadmap = null;
        Timer? timer = null;
        var previousPostProvider = CoreDispatch.PostProvider;
        var previousLogger = Log.Logger;
        var sink = new ListSink();
        Action? dispatchedTimerAction = null;
        using var actionCaptured = new ManualResetEventSlim();

        lock (LogSwap)
        {
            try
            {
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .WriteTo.Sink(sink)
                    .CreateLogger();

                Directory.CreateDirectory(profilePath);
                Directory.CreateDirectory(progressPath); // A portable write obstacle: it is not a file.

                roadmap = new RoadmapService();
                roadmap.StartStep("t1_step1");

                CoreDispatch.PostProvider = action =>
                {
                    dispatchedTimerAction = action;
                    actionCaptured.Set();
                };

                timer = (Timer)typeof(RoadmapService)
                    .GetField("_saveTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(roadmap)!;
                timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
                Assert.True(actionCaptured.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
                    "Roadmap autosave timer did not dispatch");
                Assert.NotNull(dispatchedTimerAction);

                // Run the production dispatch action on this test thread, not inside the timer
                // provider, so the test proves the timer's real Save path without a new seam.
                dispatchedTimerAction!();

                var saveFailures = Snapshot(sink).Where(e =>
                    e.Level == LogEventLevel.Error &&
                    e.MessageTemplate.Text == "Failed to save roadmap progress");
                Assert.NotEmpty(saveFailures); // Assert before removing the write obstacle.

                Directory.Delete(progressPath);
                roadmap.Dispose();
                roadmap = null;

                Assert.True(File.Exists(progressPath), "Dispose did not retry the failed autosave");
                using (var reloaded = new RoadmapService())
                    Assert.NotNull(reloaded.GetStepProgress("t1_step1")?.StartedAt);
            }
            finally
            {
                if (timer is not null)
                    timer.DisposeAsync().AsTask().GetAwaiter().GetResult();

                // The reflected timer is drained before restoring the provider and deleting the
                // fixture, so no late callback can recreate roadmap.json as a directory.
                CoreDispatch.PostProvider = previousPostProvider;
                roadmap?.Dispose();
                Log.Logger = previousLogger;
                try { Directory.Delete(profilePath, recursive: true); } catch { }
            }
        }
    }

    private static List<LogEvent> Snapshot(ListSink sink)
    {
        lock (sink.Events)
            return sink.Events.ToList();
    }

    private sealed class ListSink : Serilog.Core.ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent)
        {
            lock (Events) Events.Add(logEvent);
        }
    }
}
