using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
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

    [Fact]
    public async Task HeldReaderReleaseAllowsPublicationAndCleansItsTempFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows File.Move sharing semantics are required for this regression.");
            return;
        }

        var service = Seed("ja");
        Task save;
        var cancellationToken = TestContext.Current.CancellationToken;
        using (var reader = HoldReader())
        {
            service.Current.Language = "fr";
            save = Task.Run(() => service.SaveImmediate(suppressCloudBackup: true), cancellationToken);
            WaitForPublicationTemp();
            Thread.Sleep(100);
        }

        await save.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
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
        var stopwatch = Stopwatch.StartNew();
        using (HoldReader(share))
        {
            service.Current.Language = "fr";
            service.SaveImmediate(suppressCloudBackup: true);
        }
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3),
            $"persistent publication failure was not bounded: {stopwatch.Elapsed}");
        Assert.Equal(previous, File.ReadAllText(SettingsPublicationTestProfile.SettingsPath));
        Assert.Empty(SettingsPublicationTestProfile.TempFiles());
        Assert.Equal("ja", new SettingsService().Current.Language);
    }

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
}
