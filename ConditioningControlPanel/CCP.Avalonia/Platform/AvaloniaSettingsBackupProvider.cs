using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Settings backup provider for the Avalonia head. Composes two backup targets
/// (ProfileSync slice 7, plan §10.2):
/// <list type="number">
/// <item>a local timestamped copy of <c>settings.json</c> under the user's data path
/// (fast, offline-safe recovery — kept from the pre-cloud stopgap), and</item>
/// <item>the WPF-parity cloud backup via <see cref="IProfileSyncService.BackupSettingsAsync"/>
/// (gzip + base64 upload with the P0 <c>ExcludedBackupProperties</c> privacy strip and an
/// internal 5-minute debounce).</item>
/// </list>
/// The sync service is resolved lazily at call time to avoid the construction cycle
/// SettingsService → ISettingsBackupProvider → IProfileSyncService → ISettingsService.
/// Explicit user "Back Up Now" actions bypass this provider and call the sync service
/// with <c>force: true</c> directly.
/// </summary>
public sealed class AvaloniaSettingsBackupProvider : ISettingsBackupProvider
{
    private readonly IAppEnvironment _environment;
    private readonly IServiceProvider _services;
    private readonly ILogger<AvaloniaSettingsBackupProvider>? _logger;

    /// <summary>
    /// Number of backups to retain. Older backups are deleted after a new one is written.
    /// </summary>
    private const int MaxRetainedBackups = 20;

    public AvaloniaSettingsBackupProvider(
        IAppEnvironment environment,
        IServiceProvider services,
        ILogger<AvaloniaSettingsBackupProvider>? logger = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }

    private ISettingsService SettingsService => _services.GetRequiredService<ISettingsService>();

    /// <inheritdoc />
    public bool HasCloudIdentity => !string.IsNullOrEmpty(SettingsService.Current?.UnifiedId);

    /// <inheritdoc />
    public async Task BackupSettingsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() => BackupCore(cancellationToken), cancellationToken).ConfigureAwait(false);

        // Cloud copy (WPF parity). BackupSettingsAsync(force: false) applies its own 5-minute
        // debounce and the P0 exclusion-list strip; it swallows transport failures and never
        // logs auth material.
        try
        {
            var profileSync = _services.GetService<IProfileSyncService>();
            if (profileSync != null)
                await profileSync.BackupSettingsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("Cloud settings backup failed: {Error}", ex.Message);
        }
    }

    private void BackupCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourcePath = Path.Combine(_environment.UserDataPath, "settings.json");
        if (!File.Exists(sourcePath))
        {
            _logger?.LogDebug("AvaloniaSettingsBackupProvider: no settings.json to back up");
            return;
        }

        try
        {
            var backupDir = Path.Combine(_environment.UserDataPath, "settings-backups");
            Directory.CreateDirectory(backupDir);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var hash = ComputeFileHash(sourcePath);

            // Skip writing an identical copy if we already have a backup with the same content hash.
            if (HasBackupWithHash(backupDir, hash))
            {
                _logger?.LogDebug("AvaloniaSettingsBackupProvider: skipped backup (identical content hash {Hash})", hash);
                return;
            }

            var fileName = $"settings-{timestamp}-{hash}.json";
            var destPath = Path.Combine(backupDir, fileName);

            File.Copy(sourcePath, destPath, overwrite: true);
            _logger?.LogDebug("AvaloniaSettingsBackupProvider: backed up settings to {BackupPath}", destPath);

            PruneOldBackups(backupDir);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AvaloniaSettingsBackupProvider: local backup failed");
        }
    }

    private static bool HasBackupWithHash(string backupDir, string hash)
    {
        try
        {
            var suffix = $"-{hash}.json";
            return Directory.EnumerateFiles(backupDir, "settings-*.json")
                .Any(f => f.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static void PruneOldBackups(string backupDir)
    {
        try
        {
            var files = Directory
                .EnumerateFiles(backupDir, "settings-*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .ToList();

            foreach (var file in files.Skip(MaxRetainedBackups))
            {
                try { file.Delete(); }
                catch { /* best-effort cleanup */ }
            }
        }
        catch
        {
            // Pruning failures should not break the backup operation.
        }
    }

    private static string ComputeFileHash(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var hash = SHA256.HashData(fs);
            var sb = new StringBuilder(8);
            for (int i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
        catch
        {
            return "00000000";
        }
    }
}
