using System;
using System.IO;
using System.Runtime.CompilerServices;
using ConditioningControlPanel;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CCP.Avalonia.GeneralSettings.Tests;

/// <summary>
/// Owns the disposable profile for this dedicated test host before CorePaths can initialize.
/// CorePaths captures its path once, so tests in this host never switch CCP_USERDATA_DIR per test.
/// </summary>
internal static class GeneralSettingsTestProfile
{
    private const string EnvironmentName = "CCP_USERDATA_DIR";
    private const string MarkerName = ".ccp-general-settings-test-profile";
    private static readonly string MarkerValue = Guid.NewGuid().ToString("N");

    internal static readonly string Root = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(), "ccp-general-settings-tests-" + Guid.NewGuid().ToString("N")));

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, MarkerName), MarkerValue);

        // This is the only environment write in the host, and it happens before any test type can
        // touch CorePaths.UserData. An externally supplied profile is never opened or cleaned.
        Environment.SetEnvironmentVariable(EnvironmentName, Root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
    }

    internal static void AssertOwned()
    {
        var markerPath = Path.Combine(Root, MarkerName);
        if (!File.Exists(markerPath) || !string.Equals(File.ReadAllText(markerPath), MarkerValue,
                StringComparison.Ordinal))
            throw new InvalidOperationException("General-settings test profile ownership marker is missing or changed.");

        if (!PathEquals(CorePaths.UserData, Root))
            throw new InvalidOperationException(
                $"CorePaths.UserData is not the owned general-settings profile: {CorePaths.UserData}");
    }

    /// <summary>
    /// Preserves any settings left by an earlier test, then quarantines files created by this
    /// scope. Nothing is deleted; process-exit cleanup removes only the marker-owned root.
    /// </summary>
    internal static IDisposable BeginSettingsScope()
    {
        AssertOwned();
        return new SettingsScope(Path.Combine(Root, "settings.json"));
    }

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
            // A locked temp file must not turn a completed test host into an exit failure.
        }
    }

    private sealed class SettingsScope : IDisposable
    {
        private readonly string _settingsPath;
        private readonly string? _previousPath;
        private bool _disposed;

        internal SettingsScope(string settingsPath)
        {
            _settingsPath = settingsPath;
            if (!File.Exists(settingsPath)) return;

            _previousPath = settingsPath + ".previous-" + Guid.NewGuid().ToString("N");
            File.Move(settingsPath, _previousPath);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            AssertOwned();

            if (File.Exists(_settingsPath))
            {
                var createdPath = _settingsPath + ".created-" + Guid.NewGuid().ToString("N");
                File.Move(_settingsPath, createdPath);
            }

            if (_previousPath is not null && File.Exists(_previousPath))
                File.Move(_previousPath, _settingsPath);
        }
    }
}
