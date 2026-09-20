using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CCP.Core.Settings.Tests;

/// <summary>
/// Owns one fresh profile for this process before CorePaths can initialize. CorePaths is
/// immutable for the process lifetime, so these tests never switch CCP_USERDATA_DIR per test.
/// </summary>
internal static class SettingsPublicationTestProfile
{
    private const string EnvironmentName = "CCP_USERDATA_DIR";
    private const string MarkerName = ".ccp-settings-publication-test-profile";
    private static readonly string MarkerValue = Guid.NewGuid().ToString("N");

    internal static readonly string Root = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(), "ccp-settings-publication-tests-" + Guid.NewGuid().ToString("N")));

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, MarkerName), MarkerValue);
        Environment.SetEnvironmentVariable(EnvironmentName, Root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
    }

    internal static void AssertOwned()
    {
        var markerPath = Path.Combine(Root, MarkerName);
        if (!File.Exists(markerPath) || !string.Equals(File.ReadAllText(markerPath), MarkerValue,
                StringComparison.Ordinal))
            throw new InvalidOperationException("Settings-publication test profile ownership marker is missing or changed.");

        if (!PathEquals(CorePaths.UserData, Root))
            throw new InvalidOperationException(
                $"CorePaths.UserData is not the owned settings-publication profile: {CorePaths.UserData}");
    }

    internal static string SettingsPath => Path.Combine(Root, "settings.json");

    internal static string[] TempFiles() =>
        Directory.Exists(Root)
            ? Directory.GetFiles(Root, "settings.json.*.tmp")
            : Array.Empty<string>();

    private static bool PathEquals(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            comparison);
    }

    private static void Cleanup()
    {
        try
        {
            var markerPath = Path.Combine(Root, MarkerName);
            if (File.Exists(markerPath) && string.Equals(File.ReadAllText(markerPath), MarkerValue,
                    StringComparison.Ordinal))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // A locked file must not turn an otherwise completed host into an exit failure.
        }
    }
}

public sealed class SettingsPublicationTests
{
    [Fact]
    public void NoReaderPublishesTheLatestSettingsAndCleansItsTempFile()
    {
        var service = Seed("ja");
        service.Current.Language = "fr";

        service.SaveImmediate(suppressCloudBackup: true);

        Assert.Empty(SettingsPublicationTestProfile.TempFiles());
        Assert.Equal("fr", new SettingsService().Current.Language);
    }

    /// <summary>
    /// Deterministic recovery proof: the reader is released synchronously INSIDE the first actual
    /// failed publication attempt, so no polling, sleep or scheduling assumption is involved. It
    /// proves retry-after-failure recovery only — not adversarial ordering or lock contention.
    /// </summary>
    [Fact]
    public void ObservedPublicationFailureThatReleasesTheReaderRecoversAndPublishes()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows File.Move sharing semantics are required for this regression.");
            return;
        }

        var service = Seed("ja");
        var reader = HoldReader();
        var observed = 0;
        try
        {
            service.AtomicPublishFailureObserver = (_, _) =>
            {
                if (Interlocked.Increment(ref observed) == 1)
                    reader.Dispose();
            };

            service.Current.Language = "fr";
            service.SaveImmediate(suppressCloudBackup: true);
        }
        finally
        {
            service.AtomicPublishFailureObserver = null;
            reader.Dispose();
        }

        // No observed failure means the scenario never arose: that is a setup failure, not proof.
        Assert.True(observed > 0,
            "setup failure: the held reader did not make any publication attempt fail, so this run proves nothing.");
        Assert.Empty(SettingsPublicationTestProfile.TempFiles());
        Assert.Equal("fr", new SettingsService().Current.Language);
    }

    [Theory]
    [InlineData(FileShare.Read)]
    [InlineData(FileShare.Read | FileShare.Delete)]
    public void PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile(FileShare share)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows File.Move sharing semantics are required for this regression.");
            return;
        }

        var service = Seed("ja");
        var previous = File.ReadAllText(SettingsPublicationTestProfile.SettingsPath);
        var attempts = 0;
        var errors = new List<LogEvent>();
        var originalLogger = Log.Logger;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(new CollectingSink(errors))
                .CreateLogger();
            service.AtomicPublishFailureObserver = (_, _) => Interlocked.Increment(ref attempts);

            using (HoldReader(share))
            {
                service.Current.Language = "fr";
                service.SaveImmediate(suppressCloudBackup: true);
            }
        }
        finally
        {
            service.AtomicPublishFailureObserver = null;
            stopwatch.Stop();
            (Log.Logger as IDisposable)?.Dispose();
            Log.Logger = originalLogger;
        }

        // Six attempts total (initial + five retries) is the shipped policy; exhaustion must stay
        // observable through the existing "Could not save settings" error.
        Assert.Equal(6, attempts);
        var error = Assert.Single(errors, e => e.Level == LogEventLevel.Error);
        Assert.Equal("Could not save settings", error.MessageTemplate.Text);
        Assert.NotNull(error.Exception);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3),
            $"persistent publication failure was not bounded: {stopwatch.Elapsed}");
        Assert.Equal(previous, File.ReadAllText(SettingsPublicationTestProfile.SettingsPath));
        Assert.Empty(SettingsPublicationTestProfile.TempFiles());
        Assert.Equal("ja", new SettingsService().Current.Language);
    }

    /// <summary>
    /// End-state check only: after both saves complete, the latest requested value is what is
    /// readable and no temp file is left. It does NOT establish that the second save contended for
    /// the save lock, that the sequence guard ran, or that either save hit a failed rename.
    /// </summary>
    [Fact]
    public async Task LaterSaveWinsWhenAnEarlierPublicationWaitsForTheReader()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows File.Move sharing semantics are required for this regression.");
            return;
        }

        var service = Seed("ja");
        Task first;
        Task second;
        var cancellationToken = TestContext.Current.CancellationToken;
        using (var reader = HoldReader())
        {
            service.Current.Language = "fr";
            first = Task.Run(() => service.SaveImmediate(suppressCloudBackup: true), cancellationToken);
            WaitForPublicationTemp();

            service.Current.Language = "de";
            second = Task.Run(() => service.SaveImmediate(suppressCloudBackup: true), cancellationToken);
            Thread.Sleep(100);
        }

        await first.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
        await second.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
        Assert.Empty(SettingsPublicationTestProfile.TempFiles());
        Assert.Equal("de", new SettingsService().Current.Language);
    }

    /// <summary>
    /// Portable (Linux-runnable) proof of the seam itself: a non-transient publication failure
    /// notifies exactly once and is still propagated to the existing "Could not save settings"
    /// error, with the temp file cleaned up. Says nothing about Windows retry behaviour.
    /// </summary>
    [Fact]
    public void NonTransientPublicationFailureNotifiesOnceAndStaysObservable()
    {
        var service = Seed("ja");
        var attempts = 0;
        var errors = new List<LogEvent>();
        var originalLogger = Log.Logger;
        var blocker = SettingsPublicationTestProfile.SettingsPath;
        try
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(new CollectingSink(errors))
                .CreateLogger();
            service.AtomicPublishFailureObserver = (_, _) => Interlocked.Increment(ref attempts);

            // A directory where settings.json belongs makes the real File.Move fail for a reason
            // that is not on the transient list, on every OS.
            File.Delete(blocker);
            Directory.CreateDirectory(blocker);

            service.Current.Language = "fr";
            service.SaveImmediate(suppressCloudBackup: true);
        }
        finally
        {
            service.AtomicPublishFailureObserver = null;
            (Log.Logger as IDisposable)?.Dispose();
            Log.Logger = originalLogger;
            if (Directory.Exists(blocker)) Directory.Delete(blocker, recursive: true);
        }

        Assert.Equal(1, attempts);
        var error = Assert.Single(errors, e => e.Level == LogEventLevel.Error);
        Assert.Equal("Could not save settings", error.MessageTemplate.Text);
        Assert.NotNull(error.Exception);
        Assert.Empty(SettingsPublicationTestProfile.TempFiles());
    }

    private static SettingsService Seed(string language)
    {
        SettingsPublicationTestProfile.AssertOwned();
        var service = new SettingsService();
        service.Current.Language = language;
        service.SaveImmediate(suppressCloudBackup: true);
        Assert.True(File.Exists(SettingsPublicationTestProfile.SettingsPath));
        Assert.Empty(SettingsPublicationTestProfile.TempFiles());
        return service;
    }

    private static FileStream HoldReader(FileShare share = FileShare.Read) =>
        new(SettingsPublicationTestProfile.SettingsPath, FileMode.Open, FileAccess.Read, share);

    private static void WaitForPublicationTemp()
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 2.0);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (SettingsPublicationTestProfile.TempFiles().Any()) return;
            Thread.Sleep(10);
        }

        Assert.Fail(
            "SaveImmediate did not leave its flushed temp file behind while the settings reader was held.");
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events;

        internal CollectingSink(List<LogEvent> events) => _events = events;

        public void Emit(LogEvent logEvent)
        {
            lock (_events) _events.Add(logEvent);
        }
    }
}
