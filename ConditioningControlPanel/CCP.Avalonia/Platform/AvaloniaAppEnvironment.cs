using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia desktop / mobile application environment paths.
/// Uses explicit platform conventions: Windows LocalAppData, macOS Library/Application Support,
/// and Linux XDG_DATA_HOME / ~/.local/share.
/// </summary>
public sealed class AvaloniaAppEnvironment : IAppEnvironment
{
    private readonly IServiceProvider? _services;

    public AvaloniaAppEnvironment(IServiceProvider? services = null)
    {
        _services = services;
    }

    private ISettingsService? SettingsService => _services?.GetService<ISettingsService>();

    public string BaseDirectory => AppContext.BaseDirectory;

    public string UserDataPath => GetUserDataPath();

    // ponytail: one user-data path; legacy WPF and Core services must share the
    // same Local folder. Roaming was a drift bug that split session logs / custom
    // sessions / moderation counter away from existing user data.
    public string ApplicationDataPath => UserDataPath;

    public string EffectiveAssetsPath
    {
        get
        {
            var customPath = SettingsService?.Current?.CustomAssetsPath;
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                try
                {
                    if (Directory.Exists(customPath))
                        return customPath;
                }
                catch
                {
                    // Fall back to default if the custom path is invalid.
                }
            }
            return Path.Combine(UserDataPath, "assets");
        }
    }

    private static string GetUserDataPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ConditioningControlPanel");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                "ConditioningControlPanel");
        }

        // Linux: prefer XDG_DATA_HOME, then fall back to ~/.local/share.
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdgDataHome))
        {
            return Path.Combine(xdgDataHome, "ConditioningControlPanel");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "share",
            "ConditioningControlPanel");
    }

    /// <summary>
    /// One-time migration: copy anything the Avalonia head previously wrote to the
    /// Windows Roaming folder into the Local folder so it shares the legacy WPF path.
    /// COPIES (never moves): the WPF reference head still reads achievements.json from
    /// Roaming during the port, so moving that file would silently reset the WPF head's
    /// achievements to zero. Every source file is left in place; the sentinel file in
    /// Local prevents the copy from re-running.
    /// Skipped if the sentinel already exists or the Roaming folder is absent.
    /// </summary>
    public static void MigrateFromLegacyRoamingPath()
    {
        if (!OperatingSystem.IsWindows()) return;

        var roaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConditioningControlPanel");
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel");

        if (string.Equals(roaming, local, StringComparison.OrdinalIgnoreCase)) return;
        if (!Directory.Exists(roaming)) return;

        Directory.CreateDirectory(local);
        var sentinel = Path.Combine(local, ".roaming-migrated");
        if (File.Exists(sentinel)) return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(roaming))
        {
            var name = Path.GetFileName(entry);
            var dest = Path.Combine(local, name);
            if (File.Exists(dest) || Directory.Exists(dest))
            {
                dest = Path.Combine(local, $"{name}.roaming-merge-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
            }

            try
            {
                CopyRecursive(entry, dest);
            }
            catch
            {
                // Locked or unreadable: skip this entry rather than crash;
                // the original stays in Roaming either way.
            }
        }

        try { File.WriteAllText(sentinel, DateTimeOffset.UtcNow.ToString("O")); }
        catch { /* best effort */ }
    }

    private static void CopyRecursive(string source, string destination)
    {
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            return;
        }

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destFile = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, overwrite: true);
        }
    }
}
