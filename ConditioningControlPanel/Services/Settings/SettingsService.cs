using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        private long _lastBackupAttemptTicks = 0;
        private System.Threading.Timer? _saveDebounceTimer;
        private volatile bool _savePending;
        private volatile bool _suppressCloudBackupPending;
        // One-way kill switch, set by SealForReset() just before the factory reset moves
        // settings.json aside. See that method for why nothing may write after it.
        private volatile bool _sealed;

        // Serializes the rotate+write half of SaveImmediate. Without it two overlapping saves
        // (debounce timer thread + a direct UI-thread call) could File.Move each other's
        // half-written temp over settings.json (#761). Never taken while _timerLock is held,
        // and never held across the UI-thread serialize hop — both would deadlock the shutdown
        // path, where SaveImmediate runs ON the UI thread.
        private readonly object _saveLock = new();
        private readonly object _timerLock = new();
        private long _saveSequence;
        private long _lastWrittenSaveSequence;

        // Bounded wait for the UI-thread re-serialize retry; a busy (or wedged) UI thread must
        // never stall the save path indefinitely.
        private static readonly TimeSpan SerializeMarshalTimeout = TimeSpan.FromSeconds(2);

        public AppSettings Current { get; private set; }

        /// <summary>
        /// Raised after <see cref="Current"/> is swapped for a different instance
        /// (cloud restore via <see cref="RestoreFrom"/>, or <see cref="Reset"/>).
        /// Listeners that subscribed to the previous instance's events (e.g.
        /// <c>ModService</c>'s <c>PropertyChanged</c> hook) must re-bind to the new
        /// <see cref="Current"/>, otherwise their edits silently stop persisting.
        /// </summary>
        public event Action? CurrentReplaced;

        /// <summary>
        /// True if the settings file did not exist when the service was initialized (fresh install).
        /// </summary>
        public bool WasSettingsFileMissing { get; private set; }

        /// <summary>
        /// True if a settings file was present but could not be parsed and was preserved as a
        /// <c>.corrupt-*.json</c> backup rather than overwritten. Distinct from a genuine fresh
        /// install (<see cref="WasSettingsFileMissing"/>).
        /// </summary>
        public bool WasSettingsFileCorrupt { get; private set; }

        /// <summary>
        /// Path of the most recent <c>.corrupt-*.json</c> backup created by
        /// <see cref="PreserveCorruptSettingsFile"/>, or null if none.
        /// </summary>
        public string? LastCorruptBackupPath { get; private set; }

        /// <summary>
        /// True when <see cref="Current"/> came out of a rolling daily backup rather than
        /// settings.json (see <see cref="TryLoadFromDailyBackup"/>). A backup can be up to three
        /// calendar days old and restores progression WHOLESALE, so the level/XP/skills in memory
        /// may predate a season rollover — <c>ProfileSyncService</c> reconciles against the server
        /// before pushing anything, otherwise the rollback becomes permanent (#761).
        /// Survives a restart via a marker file next to settings.json; cleared by
        /// <see cref="ClearRestoredFromBackupFlag"/> once the reconcile has run.
        /// </summary>
        public bool RestoredFromBackup { get; private set; }

        /// <summary>UTC time of the restore recorded by <see cref="RestoredFromBackup"/>.</summary>
        public DateTime? RestoredFromBackupUtc { get; private set; }

        /// <summary>Path of the daily backup <see cref="Current"/> was restored from, if any.</summary>
        public string? RestoredBackupPath { get; private set; }

        /// <summary>
        /// The season key the restored backup carried, captured at restore time. The reconcile
        /// must compare the SERVER key against this and not against the live
        /// <c>CurrentSeason</c>: a login (V2AuthService.ApplyUserDataToSettings) or the #293 boot
        /// heal can advance the live key first, which would hide the rollover the restored
        /// level/XP actually belong to (#761).
        /// </summary>
        public string? RestoredSeason { get; private set; }

        /// <summary>
        /// Preset IDs that need a re-install pass after services are wired up. Populated by
        /// <see cref="MergeBuiltInAwarenessPresets"/> when an installed preset's <c>Version</c>
        /// is bumped — we can't call <c>App.KeywordPresets.InstallPreset</c> from inside
        /// <c>Load()</c> because that service hasn't been constructed yet. Drained by
        /// <see cref="App.OnStartup"/> immediately after <c>App.KeywordPresets</c> exists.
        /// </summary>
        public List<string> PendingPresetReinstalls { get; } = new();

        public SettingsService()
        {
            // Store settings in user data folder (persists across updates)
            _settingsPath = Path.Combine(App.UserDataPath, "settings.json");

            // Migrate settings from old location if needed
            MigrateSettingsFromOldLocation();

            Current = Load();

            // A restore from an earlier run that never reached the reconcile (app closed, or
            // offline the whole session) still needs the sync guard — the flag lives in memory,
            // the marker on disk.
            if (!RestoredFromBackup) TryReadRestoreMarker();
        }

        /// <summary>
        /// Migrate settings from old install directory location to persistent user data folder.
        /// </summary>
        private void MigrateSettingsFromOldLocation()
        {
            try
            {
                var oldSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

                // If new location already has settings, don't overwrite
                if (File.Exists(_settingsPath)) return;

                // If old location has settings, copy them
                if (File.Exists(oldSettingsPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                    File.Copy(oldSettingsPath, _settingsPath);
                    App.Logger?.Information("Migrated settings from {Old} to {New}", oldSettingsPath, _settingsPath);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to migrate settings: {Error}", ex.Message);
            }
        }

        private AppSettings Load()
        {
            try
            {
                // Recover from an interrupted atomic write: if a temp file exists but the main
                // one doesn't, the app died after the write but before the rename. Saves use a
                // per-write temp name now, so glob (the legacy fixed settings.json.tmp matches too)
                // and only adopt a candidate that still parses — a truncated temp must never be
                // promoted to settings.json.
                RecoverAndCleanTempFiles();

                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);

                    // Use explicit settings to ensure lists are REPLACED, not merged with defaults.
                    // Error handler makes the load RESILIENT: a single unparseable member (e.g. an
                    // enum value renamed/removed in a new release, which StringEnumConverter throws
                    // on) must NOT discard the whole document and reset every phrase/pool to
                    // factory defaults. We swallow per-member errors and keep everything that DID
                    // parse. This is the fix for "my lock card phrases / subliminals get wiped
                    // every time I update" — a new release's enum change used to throw here and
                    // fall through to `new AppSettings()`, whose defaults then overwrote the file.
                    var badMembers = new List<string>();
                    var serializerSettings = new JsonSerializerSettings
                    {
                        ObjectCreationHandling = ObjectCreationHandling.Replace,
                        Error = (sender, args) =>
                        {
                            var path = args.ErrorContext.Path;
                            badMembers.Add(path);
                            App.Logger?.Warning(
                                "Skipping unparseable settings member '{Path}' (kept the rest): {Error}",
                                path, args.ErrorContext.Error.Message);
                            args.ErrorContext.Handled = true;
                        }
                    };

                    var settings = JsonConvert.DeserializeObject<AppSettings>(json, serializerSettings);
                    if (settings != null)
                    {
                        if (badMembers.Count > 0)
                            App.Logger?.Warning(
                                "Settings loaded with {Count} skipped member(s); the rest were preserved: {Members}",
                                badMembers.Count, string.Join(", ", badMembers.Distinct()));
                        App.Logger?.Information("Settings loaded from {Path} (Triggers: {TriggerCount})",
                            _settingsPath, settings.CustomTriggers?.Count ?? 0);
                        // NOTE: Don't validate level-locked features here - cloud sync hasn't happened yet.
                        // The cloud level may be higher than the local level, and we don't want to
                        // incorrectly disable features. Validation happens after cloud sync completes.

                        // Migrate plaintext auth_token from settings.json to DPAPI-encrypted storage
                        MigrateAuthToken(json);

                        // New-default subliminal top-up now happens in
                        // ModService.RestorePoolsFromSettings (mod-aware, after ModService is
                        // initialized). It used to run here, but settings load precedes
                        // ModService, so it had no active mod and wrongly merged Bambi defaults
                        // into every mode's pool.

                        // Synthesize Actions lists on any keyword triggers that predate the
                        // action-list refactor so the dispatcher can always iterate Actions.
                        MigrateKeywordTriggerActions(settings);

                        // Merge built-in Awareness preset packs (4 shipped presets).
                        MergeBuiltInAwarenessPresets(settings);

                        // Migrate legacy ContentMode-based settings to mod-based settings
                        settings.MigrateFromContentModeToMod();

                        // Relax the old 0.04 mic loudness gate (too high — rejected normal speech) for
                        // existing users to the gentler default. One-shot.
                        settings.MigrateLoudnessThreshold();

                        // Compositor is the blessed render path now that #550 is fixed: re-enable
                        // it once for users the 6.3.4 hotfix force-migrated off (persisted false).
                        // One-shot — turning the toggle off later sticks.
                        settings.MigrateEnableUnifiedOverlayHost();

                        // Off-thread present is the #550 proper fix; 6.4.0 shipped the compositor on
                        // but this off, so the spiral rastered on the UI thread (#588/#586/#587).
                        // Re-enable once; the Settings > System toggle lets it be turned back off.
                        settings.MigrateEnableCompositorOffThreadPresent();

                        return settings;
                    }
                }
                else
                {
                    WasSettingsFileMissing = true;
                }
            }
            catch (Exception ex)
            {
                // The file existed but could not be parsed even with per-member error tolerance
                // (e.g. truncated/malformed JSON, or a top-level structural error). Do NOT let the
                // fresh defaults below get silently written over it — preserve the original so the
                // user's phrases/pools stay recoverable instead of being lost forever.
                App.Logger?.Error(ex, "Could not load settings — preserving the unparseable file");
                PreserveCorruptSettingsFile();
            }

            // If a settings file is present here, we reached the fallback because it failed to load
            // (not because it was missing) — preserve it before defaults overwrite it on next save.
            if (File.Exists(_settingsPath) && !WasSettingsFileCorrupt)
            {
                App.Logger?.Error("Settings file present but did not load — preserving it before applying defaults");
                PreserveCorruptSettingsFile();
            }

            // Last resort before factory defaults: the file was corrupt (not missing), so
            // try the rolling daily backups — losing up to a day beats losing everything.
            if (WasSettingsFileCorrupt)
            {
                var restored = TryLoadFromDailyBackup();
                if (restored != null) return restored;
            }

            if (!WasSettingsFileCorrupt)
                WasSettingsFileMissing = true;
            App.Logger?.Information("Using default settings ({Reason})",
                WasSettingsFileCorrupt ? "previous file unparseable, preserved a backup" : "fresh install detected");
            var fresh = new AppSettings();
            MergeBuiltInAwarenessPresets(fresh);
            return fresh;
        }

        /// <summary>
        /// Promotes the newest still-parseable <c>settings.json*.tmp</c> left behind by an
        /// interrupted save when settings.json itself is gone, then deletes every leftover temp.
        /// </summary>
        private void RecoverAndCleanTempFiles()
        {
            try
            {
                var dir = Path.GetDirectoryName(_settingsPath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

                var temps = Directory.GetFiles(dir, Path.GetFileName(_settingsPath) + "*.tmp");
                if (temps.Length == 0) return;

                if (!File.Exists(_settingsPath))
                {
                    foreach (var candidate in temps.OrderByDescending(File.GetLastWriteTimeUtc))
                    {
                        try
                        {
                            // Well-formedness check only — per-member tolerance is the loader's job.
                            JObject.Parse(File.ReadAllText(candidate));
                        }
                        catch
                        {
                            App.Logger?.Warning("Discarding partial settings temp file {Temp}", candidate);
                            continue;
                        }

                        App.Logger?.Information("Recovering settings from interrupted save ({Temp})", candidate);
                        File.Move(candidate, _settingsPath);
                        break;
                    }
                }

                foreach (var leftover in temps)
                {
                    if (!File.Exists(leftover)) continue;
                    try { File.Delete(leftover); } catch { }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Settings temp-file recovery failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Tries settings.bak-1.json .. settings.bak-3.json (newest first) after the main
        /// file failed to parse. Returns the first backup that deserializes, with the same
        /// post-load migrations the normal path applies, or null if none work.
        /// </summary>
        private AppSettings? TryLoadFromDailyBackup()
        {
            for (var i = 1; i <= 3; i++)
            {
                var bakPath = _settingsPath.Replace(".json", $".bak-{i}.json");
                try
                {
                    if (!File.Exists(bakPath)) continue;
                    var json = File.ReadAllText(bakPath);
                    var serializerSettings = new JsonSerializerSettings
                    {
                        ObjectCreationHandling = ObjectCreationHandling.Replace,
                        Error = (sender, args) => { args.ErrorContext.Handled = true; }
                    };
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json, serializerSettings);
                    if (settings == null) continue;

                    MigrateKeywordTriggerActions(settings);
                    MergeBuiltInAwarenessPresets(settings);
                    settings.MigrateFromContentModeToMod();
                    settings.MigrateLoudnessThreshold();
                    settings.MigrateEnableUnifiedOverlayHost();
                    settings.MigrateEnableCompositorOffThreadPresent();

                    // Record the restore: the backup restores progression wholesale, so sync must
                    // reconcile with the server before pushing any of it back up (#761).
                    RestoredFromBackup = true;
                    RestoredFromBackupUtc = DateTime.UtcNow;
                    RestoredBackupPath = bakPath;
                    RestoredSeason = settings.CurrentSeason;
                    WriteRestoreMarker();

                    App.Logger?.Warning("Settings RESTORED from daily backup {Backup} (main file was unparseable)", bakPath);
                    return settings;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("Daily backup {Backup} also failed to load: {Error}", bakPath, ex.Message);
                }
            }
            return null;
        }

        private string RestoreMarkerPath => _settingsPath + ".restored";

        private void WriteRestoreMarker()
        {
            try
            {
                // utc|backup path|season key as restored. '|' cannot occur in a Windows path.
                File.WriteAllText(RestoreMarkerPath,
                    $"{RestoredFromBackupUtc:o}|{RestoredBackupPath}|{RestoredSeason}");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Could not write settings restore marker: {Error}", ex.Message);
            }
        }

        private void TryReadRestoreMarker()
        {
            try
            {
                if (!File.Exists(RestoreMarkerPath)) return;

                var parts = File.ReadAllText(RestoreMarkerPath).Split('|');
                RestoredFromBackup = true;
                RestoredFromBackupUtc = DateTime.TryParse(parts[0],
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var stamp)
                    ? stamp
                    : (DateTime?)null;
                RestoredBackupPath = parts.Length > 1 ? parts[1] : null;
                RestoredSeason = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : null;

                App.Logger?.Warning("Settings were restored from daily backup {Backup} (season {Season}) on a previous run and have not been reconciled with the cloud profile yet",
                    RestoredBackupPath, RestoredSeason);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Could not read settings restore marker: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Called by <c>ProfileSyncService</c> once the restored profile has been reconciled
        /// against the server, so the one-shot guard doesn't re-run on every later sync (#761).
        /// </summary>
        public void ClearRestoredFromBackupFlag()
        {
            RestoredFromBackup = false;
            try
            {
                if (File.Exists(RestoreMarkerPath)) File.Delete(RestoreMarkerPath);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Could not clear settings restore marker: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Renames an unparseable settings.json to settings.corrupt-&lt;timestamp&gt;.json so the fresh
        /// defaults the caller is about to use don't overwrite (and destroy) the user's real data.
        /// The backup can be inspected or hand-restored, and gives an import path a source to pull
        /// phrases back from. Sets <see cref="WasSettingsFileCorrupt"/>.
        /// </summary>
        private void PreserveCorruptSettingsFile()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return;
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var backupPath = _settingsPath.Replace(".json", $".corrupt-{stamp}.json");
                // Move (not copy) so the next SaveImmediate writes a clean file to _settingsPath.
                File.Move(_settingsPath, backupPath, overwrite: true);
                LastCorruptBackupPath = backupPath;
                WasSettingsFileCorrupt = true;
                App.Logger?.Warning("Preserved unparseable settings to {Backup}", backupPath);
            }
            catch (Exception ex)
            {
                // If we can't even rename it, leave it in place rather than deleting anything.
                App.Logger?.Error(ex, "Failed to preserve unparseable settings file");
            }
        }

        /// <summary>
        /// Migrate plaintext auth_token from settings.json to DPAPI-encrypted storage.
        /// Runs once — after migration, auth_token is removed from the JSON file on next save.
        /// </summary>
        private void MigrateAuthToken(string rawJson)
        {
            try
            {
                var obj = JObject.Parse(rawJson);
                var token = obj["auth_token"]?.ToString();
                if (!string.IsNullOrEmpty(token))
                {
                    // Only migrate if encrypted store is empty (don't overwrite a newer token)
                    if (string.IsNullOrEmpty(SecureAuthTokenStore.Retrieve()))
                    {
                        SecureAuthTokenStore.Store(token);
                        App.Logger?.Information("Migrated auth token from plaintext settings to DPAPI-encrypted storage");
                    }

                    // Re-save settings to strip the plaintext auth_token from the JSON file
                    // (AuthToken is now [JsonIgnore] so it won't be written back)
                    Save();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Auth token migration failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Feature level gating has been removed — every feature is available from level 1,
        /// so there is nothing to validate on load. Stub kept for call-site compatibility.
        /// </summary>
        private void ValidateLevelLockedFeatures(AppSettings settings)
        {
        }

        /// <summary>
        /// Loads built-in Awareness preset pack JSON files from
        /// <c>Resources/AwarenessPresets/*.json</c> and merges them into
        /// <see cref="AppSettings.KeywordTriggerPresets"/>.
        ///
        /// - Presets the user has explicitly removed (<see cref="AppSettings.RemovedBuiltInPresetIds"/>) are skipped.
        /// - New presets are appended with <c>MasterEnabled = false</c>.
        /// - If a built-in's <see cref="KeywordTriggerPreset.Version"/> is newer than the stored copy's, the
        ///   stored copy's <c>Triggers</c> / <c>CannedPhrases</c> are refreshed in place (keeping MasterEnabled state).
        /// </summary>
        private void MergeBuiltInAwarenessPresets(AppSettings settings)
        {
            try
            {
                // Migration: drop the retired "builtin.testlab" preset if it's still
                // stored from a prior version. Also strips its cloned triggers and
                // any canned phrases it injected so nothing ghosts around in the UI.
                const string RetiredTestLabId = "builtin.testlab";
                var stored = settings.KeywordTriggerPresets
                    .FirstOrDefault(p => p.Id == RetiredTestLabId);
                if (stored != null)
                {
                    var triggerPrefix = "preset:" + RetiredTestLabId + ":";
                    settings.KeywordTriggers.RemoveAll(t =>
                        t.Id?.StartsWith(triggerPrefix, StringComparison.Ordinal) == true);

                    var phrasePrefix = "preset:" + RetiredTestLabId + ":phrase:";
                    settings.CustomCompanionPhrases.RemoveAll(p =>
                        p.Id?.StartsWith(phrasePrefix, StringComparison.Ordinal) == true);

                    settings.KeywordTriggerPresets.Remove(stored);
                    App.Logger?.Information("MergeBuiltInAwarenessPresets: removed retired {Id}", RetiredTestLabId);
                }

                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "AwarenessPresets");
                if (!Directory.Exists(dir))
                {
                    App.Logger?.Debug("MergeBuiltInAwarenessPresets: no directory at {Dir}", dir);
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
                        var preset = JsonConvert.DeserializeObject<Models.KeywordTriggerPreset>(json, serializer);
                        if (preset == null || string.IsNullOrEmpty(preset.Id)) continue;

                        preset.IsBuiltIn = true;

                        // Ensure each trigger inside the preset has an action list — preset
                        // JSONs may ship only the composable form, but legacy tooling can
                        // still set flat fields.
                        if (preset.Triggers != null)
                        {
                            foreach (var t in preset.Triggers)
                            {
                                if (t == null) continue;
                                if (t.Actions == null || t.Actions.Count == 0)
                                    KeywordTriggerService.RebuildActionsFromFlatFields(t);
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
                            // Refresh the preset definition in place. Prior versions only
                            // updated the metadata here — the cloned triggers in
                            // KeywordTriggers were left stale, so users who installed v2
                            // never picked up v3's new keywords/actions.
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
                            existing.Triggers = preset.Triggers;
                            refreshed++;

                            // If the user already had this preset installed, queue a
                            // re-install so the new triggers reach the live KeywordTriggers
                            // list. We flip MasterEnabled = false now so InstallPreset's
                            // "already installed" guard (line 64 of KeywordTriggerPresetService)
                            // won't bail out, and we can't call InstallPreset directly here
                            // because App.KeywordPresets is constructed later in OnStartup.
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
                        App.Logger?.Warning("Failed to load preset file {File}: {Error}", file, ex.Message);
                    }
                }

                if (added > 0 || refreshed > 0)
                    App.Logger?.Information("Awareness presets merged: {Added} added, {Refreshed} refreshed",
                        added, refreshed);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("MergeBuiltInAwarenessPresets failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Synthesize a composable <see cref="KeywordTrigger.Actions"/> list from the
        /// flat audio/visual/haptic/xp fields for any trigger loaded from an older save
        /// that pre-dates the action-list refactor.
        ///
        /// Delegates to <see cref="KeywordTriggerService.RebuildActionsFromFlatFields"/>
        /// so load-time migration and the editor's synth-on-save path stay in sync.
        /// </summary>
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

                    KeywordTriggerService.RebuildActionsFromFlatFields(trigger);
                    migrated++;
                }

                if (migrated > 0)
                    App.Logger?.Information("Migrated {Count} keyword triggers to action-list model", migrated);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Keyword trigger action migration failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Debounced save — coalesces rapid calls into a single disk write after 500ms of quiet.
        /// Use <see cref="SaveImmediate"/> for shutdown or other critical paths that must flush now.
        /// </summary>
        public void Save(bool suppressCloudBackup = false)
        {
            if (_sealed) return;   // factory reset in progress — see SealForReset
            _savePending = true;
            if (suppressCloudBackup)
                _suppressCloudBackupPending = true;

            lock (_timerLock)
            {
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
        }

        /// <summary>
        /// One-way kill switch for the factory reset (Settings ▸ Data ▸ Danger Zone).
        ///
        /// <para>The reset moves <c>settings.json</c> aside and then hard-exits. Without this, the
        /// debounce timer is a <see cref="System.Threading.Timer"/> on the THREAD POOL: anything
        /// that called <see cref="Save"/> in the preceding 500 ms fires between the File.Move and
        /// the exit and re-creates <c>settings.json</c> with the pre-reset content, so the reset
        /// silently does nothing. Async continuations (profile sync, XP awards) can call Save after
        /// the move for the same reason, which a flush alone would not cover.</para>
        ///
        /// <para>Disarms the pending debounce and makes every later <see cref="Save"/> /
        /// <see cref="SaveImmediate"/> a no-op. On the happy path the only thing that runs after it
        /// is <c>Environment.Exit</c>; the single way back is
        /// <see cref="UnsealAfterFailedReset"/>, which the reset's own failure path calls so a
        /// still-running app does not lose its saves for the rest of the session.</para>
        /// </summary>
        public void SealForReset()
        {
            _sealed = true;
            _savePending = false;
            _suppressCloudBackupPending = false;
            lock (_timerLock)
            {
                _saveDebounceTimer?.Dispose();
                _saveDebounceTimer = null;
            }
            App.Logger?.Warning("Settings sealed for factory reset — every further save is a no-op");
        }

        /// <summary>
        /// Undoes <see cref="SealForReset"/>. The ONLY legitimate caller is the factory reset's own
        /// failure path: if the file could not be moved aside the app keeps running, and a settings
        /// service that silently never saves again would be a worse bug than the failed reset.
        /// </summary>
        public void UnsealAfterFailedReset()
        {
            _sealed = false;
            App.Logger?.Warning("Settings unsealed — the factory reset did not complete");
        }

        /// <summary>
        /// Writes settings to disk immediately (no debounce). Called by the debounce timer,
        /// on shutdown, and from RestoreFrom/Reset where the write must happen now.
        /// </summary>
        public void SaveImmediate(bool suppressCloudBackup = false)
        {
            if (_sealed) return;   // factory reset in progress — see SealForReset

            // Cancel any pending debounce — we're flushing now
            _savePending = false;
            _suppressCloudBackupPending = false;
            lock (_timerLock)
            {
                _saveDebounceTimer?.Dispose();
                _saveDebounceTimer = null;
            }

            try
            {
                // Debug-level: this fires every time settings change during normal play (XP
                // ticks, achievement progress, etc.) and at Information level it floods
                // bug-report activity logs with hundreds of identical lines per minute.
                App.Logger?.Debug("Settings.Save: ActivePackIds BEFORE serialize: [{Ids}]",
                    string.Join(", ", Current.ActivePackIds ?? new List<string>()));

                // Serialized OUTSIDE _saveLock: the snapshot can hop to the UI thread, and holding
                // the write lock across that hop deadlocks against a UI-thread SaveImmediate.
                // The sequence number keeps an older snapshot from landing after a newer one.
                var sequence = Interlocked.Increment(ref _saveSequence);
                var json = SerializeCurrentSnapshot();

                lock (_saveLock)
                {
                    if (sequence < Interlocked.Read(ref _lastWrittenSaveSequence))
                    {
                        App.Logger?.Debug("Settings save #{Sequence} dropped — a newer snapshot already reached disk", sequence);
                        return;
                    }

                    // Snapshot the previous good file into the rolling daily backups before the
                    // first overwrite of the day. The atomic write below protects against a crash
                    // MID-write, but not against the file being destroyed by something else
                    // entirely (support: preset wiped by a PC crash) — this gives Load() and the
                    // user up to 3 previous days to fall back on.
                    RotateDailyBackupBeforeWrite();

                    // Atomic write: write to a per-write temp file then replace, so neither a
                    // crash mid-write nor a concurrent save can publish a half-written file
                    // (a shared temp name let two saves rename each other's partial write over
                    // settings.json — #761). Flush(true) commits the bytes to the device before
                    // the rename makes them the live settings.
                    var tempPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
                        {
                            writer.Write(json);
                            writer.Flush();
                            stream.Flush(true);
                        }
                        File.Move(tempPath, _settingsPath, overwrite: true);
                    }
                    catch
                    {
                        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                        throw;
                    }

                    Interlocked.Exchange(ref _lastWrittenSaveSequence, sequence);
                }

                App.Logger?.Debug("Settings saved to {Path} (Triggers: {TriggerCount}, ActivePacks: {PackCount})",
                    _settingsPath, Current.CustomTriggers?.Count ?? 0, Current.ActivePackIds?.Count ?? 0);

                // Auto-backup settings to cloud (fire-and-forget, debounced)
                // suppressCloudBackup is used when auth token was just cleared (401 handler) to prevent
                // a 401 → Save() → backup → 401 storm loop
                // Uses Interlocked.CompareExchange for thread-safe gate (multiple async paths call Save concurrently)
                if (!suppressCloudBackup && App.HasCloudIdentity && App.ProfileSync != null)
                {
                    var nowTicks = DateTime.UtcNow.Ticks;
                    var lastTicks = Interlocked.Read(ref _lastBackupAttemptTicks);
                    var elapsedSeconds = (nowTicks - lastTicks) / TimeSpan.TicksPerSecond;
                    if (elapsedSeconds >= 30 &&
                        Interlocked.CompareExchange(ref _lastBackupAttemptTicks, nowTicks, lastTicks) == lastTicks)
                    {
                        _ = Task.Run(async () =>
                        {
                            try { await App.ProfileSync.BackupSettingsAsync(); }
                            catch (Exception backupEx)
                            {
                                App.Logger?.Debug("Auto settings backup failed: {Error}", backupEx.Message);
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Could not save settings");
            }
        }

        /// <summary>
        /// Serializes <see cref="Current"/> for the on-disk write. The pools/phrase lists are
        /// edited on the UI thread, so a debounced background save can hit one mid-edit while
        /// the user pastes a long list — <c>List&lt;T&gt;</c> enumeration throws on structural
        /// modification, which used to be swallowed and the save silently skipped (#761).
        /// Serializing on the UI thread unconditionally would put the whole settings graph on
        /// the UI thread twice a second during play (Save fires constantly), so the throw is
        /// caught and only the RETRY hops to the UI thread, where no edit can be in flight.
        /// </summary>
        private string SerializeCurrentSnapshot()
        {
            try
            {
                return JsonConvert.SerializeObject(Current, Formatting.Indented);
            }
            catch (Exception ex)
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.CheckAccess())
                    throw;

                App.Logger?.Debug("Settings serialize raced a UI-thread edit ({Error}) — retrying on the UI thread", ex.Message);

                var op = dispatcher.InvokeAsync(() => JsonConvert.SerializeObject(Current, Formatting.Indented));
                // Observe a faulted op even if the wait below times out, otherwise it resurfaces
                // through TaskScheduler.UnobservedTaskException with no useful context.
                op.Task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

                if (op.Task.Wait(SerializeMarshalTimeout))
                    return op.Task.Result;

                // Bounded: a busy (or wedged) UI thread must never stall the save path. Drop this
                // save rather than write a snapshot we could not take cleanly — the next one wins.
                op.Abort();
                App.Logger?.Warning("Settings serialize could not reach the UI thread in time — skipping this save");
                throw;
            }
        }

        /// <summary>
        /// Keeps settings.bak-1.json (newest) .. settings.bak-3.json (oldest): one snapshot
        /// of the previous settings file per calendar day, rotated on the first save of the
        /// day. Daily (not per-save) so a corruption doesn't immediately propagate into
        /// every backup before anyone notices.
        /// </summary>
        // Date of the last successful rotation this process; spares the save hot path
        // (SaveImmediate fires constantly during play) the per-save filesystem stats.
        private DateTime _lastBackupRotationDate = DateTime.MinValue;

        private void RotateDailyBackupBeforeWrite()
        {
            if (_lastBackupRotationDate == DateTime.Now.Date) return;
            try
            {
                if (!File.Exists(_settingsPath)) return;
                var bak1 = _settingsPath.Replace(".json", ".bak-1.json");
                if (File.Exists(bak1) && File.GetLastWriteTime(bak1).Date == DateTime.Now.Date)
                {
                    _lastBackupRotationDate = DateTime.Now.Date;
                    return;
                }

                var bak2 = _settingsPath.Replace(".json", ".bak-2.json");
                var bak3 = _settingsPath.Replace(".json", ".bak-3.json");
                if (File.Exists(bak2)) File.Move(bak2, bak3, overwrite: true);
                if (File.Exists(bak1)) File.Move(bak1, bak2, overwrite: true);
                File.Copy(_settingsPath, bak1, overwrite: true);
                // File.Copy preserves the SOURCE mtime — stamp the rotation time or the
                // date check above re-rotates on the next save and burns a second slot.
                try { File.SetLastWriteTime(bak1, DateTime.Now); } catch { }
                _lastBackupRotationDate = DateTime.Now.Date;
                App.Logger?.Information("Settings daily backup rotated: {Backup}", bak1);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Settings backup rotation failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Replace current settings with the given settings object and save to disk.
        /// Used by cloud restore to apply restored settings.
        /// </summary>
        public void RestoreFrom(AppSettings settings)
        {
            Current = settings ?? throw new ArgumentNullException(nameof(settings));
            // Notify listeners (ModService re-binds its per-mod pool sync + re-derives the
            // active pools) BEFORE we persist, so any backups they seed get written too.
            try { CurrentReplaced?.Invoke(); } catch (Exception ex) { App.Logger?.Warning("CurrentReplaced handler failed: {Error}", ex.Message); }
            SaveImmediate();
            App.Logger?.Information("Settings restored from external source");
        }

        public void Reset()
        {
            Current = new AppSettings();
            try { CurrentReplaced?.Invoke(); } catch (Exception ex) { App.Logger?.Warning("CurrentReplaced handler failed: {Error}", ex.Message); }
            SaveImmediate();
        }
    }
}