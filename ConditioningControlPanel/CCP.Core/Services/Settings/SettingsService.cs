using System.Diagnostics;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Core.Services.Settings;

/// <summary>
/// Cross-platform settings persistence service.
/// Loads, migrates, and saves <see cref="AppSettings"/> as JSON in the user data folder.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly IAppEnvironment _environment;
    private readonly ISecretStore _secretStore;
    private readonly ISettingsBackupProvider? _backupProvider;
    private readonly string _settingsPath;
    private long _lastBackupAttemptTicks;
    private System.Threading.Timer? _saveDebounceTimer;
    private volatile bool _savePending;
    private volatile bool _suppressCloudBackupPending;

    /// <inheritdoc />
    public AppSettings Current { get; private set; }

    /// <inheritdoc />
    public event Action? CurrentReplaced;

    /// <inheritdoc />
    public bool WasSettingsFileMissing { get; private set; }

    /// <inheritdoc />
    public List<string> PendingPresetReinstalls { get; } = new();

    public SettingsService(IAppEnvironment environment, ISecretStore secretStore, ISettingsBackupProvider? backupProvider = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _backupProvider = backupProvider;
        _settingsPath = Path.Combine(environment.UserDataPath, "settings.json");

        MigrateSettingsFromOldLocation();
        Current = Load();
    }

    private void MigrateSettingsFromOldLocation()
    {
        try
        {
            var oldSettingsPath = Path.Combine(_environment.BaseDirectory, "settings.json");

            if (File.Exists(_settingsPath)) return;

            if (File.Exists(oldSettingsPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                File.Copy(oldSettingsPath, _settingsPath);
                Log.Information("Migrated settings from {Old} to {New}", oldSettingsPath, _settingsPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to migrate settings");
        }
    }

    private AppSettings Load()
    {
        try
        {
            var tempPath = _settingsPath + ".tmp";
            if (File.Exists(tempPath) && !File.Exists(_settingsPath))
            {
                Log.Information("Recovering settings from interrupted save (temp file)");
                File.Move(tempPath, _settingsPath);
            }
            else if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }

            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var serializerSettings = new JsonSerializerSettings
                {
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };

                var settings = JsonConvert.DeserializeObject<AppSettings>(json, serializerSettings);
                if (settings != null)
                {
                    Log.Information("Settings loaded from {Path} (Triggers: {TriggerCount})",
                        _settingsPath, settings.CustomTriggers?.Count ?? 0);

                    MigrateAuthToken(json);
                    MigrateKeywordTriggerActions(settings);
                    MergeBuiltInAwarenessPresets(settings);
                    settings.MigrateFromContentModeToMod();
                    settings.MigrateLoudnessThreshold();
                    return settings;
                }

                QuarantineCorruptSettingsFile();
            }
            else
            {
                WasSettingsFileMissing = true;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load settings");
            QuarantineCorruptSettingsFile();
        }

        WasSettingsFileMissing = true;
        Log.Information("Using default settings (fresh install detected)");
        var fresh = new AppSettings();
        MergeBuiltInAwarenessPresets(fresh);
        return fresh;
    }

    /// <summary>
    /// An unreadable settings file would otherwise be clobbered with defaults by the
    /// first debounced save, silently destroying years of user data that may be
    /// hand-recoverable (truncated tail, encoding glitch). Preserve a copy first.
    /// </summary>
    private void QuarantineCorruptSettingsFile()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var quarantinePath = _settingsPath + ".corrupt";
            File.Copy(_settingsPath, quarantinePath, overwrite: true);
            Log.Warning("Settings file unreadable; preserved a copy at {Path} before falling back to defaults", quarantinePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to preserve unreadable settings file");
        }
    }

    private void MigrateAuthToken(string rawJson)
    {
        try
        {
            var obj = JObject.Parse(rawJson);
            var token = obj["auth_token"]?.ToString();
            if (string.IsNullOrEmpty(token)) return;

            var existing = _secretStore.Retrieve("auth_token");
            if (existing == null || existing.Length == 0)
            {
                _secretStore.Store("auth_token", System.Text.Encoding.UTF8.GetBytes(token));
                Log.Information("Migrated auth token from plaintext settings to encrypted storage");
            }

            Save();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Auth token migration failed");
        }
    }

    private void MigrateKeywordTriggerActions(AppSettings settings)
    {
        try
        {
            var triggers = settings.KeywordTriggers;
            if (triggers == null || triggers.Count == 0) return;

            int migrated = 0;
            foreach (var trigger in triggers)
            {
                if (trigger == null) continue;
                if (trigger.Actions != null && trigger.Actions.Count > 0) continue;

                KeywordTriggerMigrationHelper.RebuildActionsFromFlatFields(trigger);
                migrated++;
            }

            if (migrated > 0)
                Log.Information("Migrated {Count} keyword triggers to action-list model", migrated);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Keyword trigger action migration failed");
        }
    }

    private void MergeBuiltInAwarenessPresets(AppSettings settings)
    {
        try
        {
            const string RetiredTestLabId = "builtin.testlab";
            var stored = settings.KeywordTriggerPresets.FirstOrDefault(p => p.Id == RetiredTestLabId);
            if (stored != null)
            {
                var triggerPrefix = "preset:" + RetiredTestLabId + ":";
                settings.KeywordTriggers.RemoveAll(t =>
                    t.Id?.StartsWith(triggerPrefix, StringComparison.Ordinal) == true);

                var phrasePrefix = "preset:" + RetiredTestLabId + ":phrase:";
                settings.CustomCompanionPhrases.RemoveAll(p =>
                    p.Id?.StartsWith(phrasePrefix, StringComparison.Ordinal) == true);

                settings.KeywordTriggerPresets.Remove(stored);
                Log.Information("MergeBuiltInAwarenessPresets: removed retired {Id}", RetiredTestLabId);
            }

            var dir = Path.Combine(_environment.BaseDirectory, "Resources", "AwarenessPresets");
            if (!Directory.Exists(dir))
            {
                Log.Debug("MergeBuiltInAwarenessPresets: no directory at {Dir}", dir);
                return;
            }

            var files = Directory.GetFiles(dir, "*.json");
            if (files.Length == 0) return;

            var serializer = new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace
            };

            int added = 0;
            int refreshed = 0;
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var preset = JsonConvert.DeserializeObject<KeywordTriggerPreset>(json, serializer);
                    if (preset == null || string.IsNullOrEmpty(preset.Id)) continue;

                    preset.IsBuiltIn = true;

                    if (preset.Triggers != null)
                    {
                        foreach (var t in preset.Triggers)
                        {
                            if (t == null) continue;
                            if (t.Actions == null || t.Actions.Count == 0)
                                KeywordTriggerMigrationHelper.RebuildActionsFromFlatFields(t);
                        }
                    }

                    if (settings.RemovedBuiltInPresetIds.Contains(preset.Id))
                        continue;

                    var existing = settings.KeywordTriggerPresets.FirstOrDefault(p => p.Id == preset.Id);
                    if (existing == null)
                    {
                        preset.MasterEnabled = false;
                        settings.KeywordTriggerPresets.Add(preset);
                        added++;
                    }
                    else if (preset.Version > existing.Version)
                    {
                        var wasInstalled = existing.MasterEnabled;

                        existing.Name = preset.Name;
                        existing.Icon = preset.Icon;
                        existing.Description = preset.Description;
                        existing.LongDescription = preset.LongDescription;
                        existing.Author = preset.Author;
                        existing.Version = preset.Version;
                        existing.RequiresAi = preset.RequiresAi;
                        existing.AvatarPromptTemplate = preset.AvatarPromptTemplate;
                        existing.PhrasePools = preset.PhrasePools;
                        existing.CannedPhrases = preset.CannedPhrases;
                        existing.Triggers = preset.Triggers ?? new List<KeywordTrigger>();
                        refreshed++;

                        if (wasInstalled)
                        {
                            existing.MasterEnabled = false;
                            if (!PendingPresetReinstalls.Contains(existing.Id))
                                PendingPresetReinstalls.Add(existing.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to load preset file {File}", file);
                }
            }

            if (added > 0 || refreshed > 0)
                Log.Information("Awareness presets merged: {Added} added, {Refreshed} refreshed",
                    added, refreshed);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "MergeBuiltInAwarenessPresets failed");
        }
    }

    /// <inheritdoc />
    public void Save() => Save(false);

    /// <inheritdoc />
    public void Save(bool suppressCloudBackup = false)
    {
        _savePending = true;
        if (suppressCloudBackup)
            _suppressCloudBackupPending = true;

        _saveDebounceTimer?.Dispose();
        _saveDebounceTimer = new System.Threading.Timer(_ =>
        {
            if (_savePending)
            {
                _savePending = false;
                var suppress = _suppressCloudBackupPending;
                _suppressCloudBackupPending = false;
                SaveImmediate(suppress);
            }
        }, null, 500, Timeout.Infinite);
    }

    /// <inheritdoc />
    public void SaveImmediate(bool suppressCloudBackup = false)
    {
        _savePending = false;
        _suppressCloudBackupPending = false;
        _saveDebounceTimer?.Dispose();
        _saveDebounceTimer = null;

        try
        {
            Log.Debug("Settings.Save: ActivePackIds BEFORE serialize: [{Ids}]",
                string.Join(", ", Current.ActivePackIds ?? new List<string>()));

            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var json = JsonConvert.SerializeObject(Current, Formatting.Indented);
            var tempPath = _settingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsPath, overwrite: true);

            Log.Debug("Settings saved to {Path} (Triggers: {TriggerCount}, ActivePacks: {PackCount})",
                _settingsPath, Current.CustomTriggers?.Count ?? 0, Current.ActivePackIds?.Count ?? 0);

            if (!suppressCloudBackup && _backupProvider != null && _backupProvider.HasCloudIdentity)
            {
                var nowTicks = DateTime.UtcNow.Ticks;
                var lastTicks = Interlocked.Read(ref _lastBackupAttemptTicks);
                var elapsedSeconds = (nowTicks - lastTicks) / TimeSpan.TicksPerSecond;
                if (elapsedSeconds >= 30 &&
                    Interlocked.CompareExchange(ref _lastBackupAttemptTicks, nowTicks, lastTicks) == lastTicks)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await _backupProvider.BackupSettingsAsync(); }
                        catch (Exception backupEx)
                        {
                            Log.Debug(backupEx, "Auto settings backup failed");
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not save settings");
        }
    }

    /// <inheritdoc />
    public void RestoreFrom(AppSettings settings)
    {
        Current = settings ?? throw new ArgumentNullException(nameof(settings));
        // Notify listeners (re-binding to the new Current instance) BEFORE we persist,
        // so any backups they seed get written too.
        try { CurrentReplaced?.Invoke(); } catch (Exception ex) { Log.Warning("CurrentReplaced handler failed: {Error}", ex.Message); }
        SaveImmediate();
        Log.Information("Settings restored from external source");
    }

    /// <inheritdoc />
    public void Reset()
    {
        Current = new AppSettings();
        try { CurrentReplaced?.Invoke(); } catch (Exception ex) { Log.Warning("CurrentReplaced handler failed: {Error}", ex.Message); }
        SaveImmediate();
    }
}
