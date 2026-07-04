using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Settings;

/// <summary>
/// Cross-platform seam for cloud profile synchronization: pushes local progression
/// (XP, level, achievements, skills, quests) to the server, pulls and merges the cloud
/// profile, performs cloud settings backup/restore, and drives server-authoritative
/// actions (skill purchase, oopsie insurance, display-name change, GDPR delete/export).
///
/// Ported slice-by-slice from the WPF <c>ProfileSyncService</c>; see
/// <c>docs/profilesync-port-plan.md</c>. Async members are declared as default interface
/// methods so the seam can be introduced without an implementation forcing every consumer
/// to change at once. The two <c>event</c> members cannot be default interface members and
/// must be declared by implementations.
/// </summary>
public interface IProfileSyncService
{
    /// <summary>
    /// Whether cloud sync is currently enabled (user opted in AND authenticated).
    /// </summary>
    bool IsSyncEnabled { get; }

    /// <summary>
    /// Timestamp of the last successful sync round-trip. Drives the client-side cooldown.
    /// </summary>
    DateTime? LastSyncTime { get; }

    /// <summary>
    /// Last error surfaced to the UI (null when the last operation succeeded).
    /// </summary>
    string? LastSyncError { get; }

    /// <summary>
    /// Number of consecutive sync failures. Reset to 0 on success.
    /// </summary>
    int ConsecutiveSyncFailures { get; }

    /// <summary>
    /// Raised when sync health changes (failure count goes up or resets to 0).
    /// The parameter is the current failure count.
    /// </summary>
    event EventHandler<int>? SyncHealthChanged;

    /// <summary>
    /// Raised after a cloud profile is pulled and merged with local data.
    /// The UI should subscribe to refresh progression views.
    /// </summary>
    event EventHandler? ProfileLoaded;

    // ProfileSync slice 2: heartbeat timer lifecycle.
    /// <summary>
    /// Starts the periodic heartbeat that keeps the user showing as online.
    /// </summary>
    void StartHeartbeat() { }

    // ProfileSync slice 2: heartbeat timer lifecycle.
    /// <summary>
    /// Stops the heartbeat timer (logout / shutdown).
    /// </summary>
    void StopHeartbeat() { }

    // ProfileSync slice 3: pull + merge cloud profile.
    /// <summary>
    /// Pulls the cloud profile and merges it into local progression (take-higher / union).
    /// Returns true on success.
    /// </summary>
    Task<bool> LoadProfileAsync() => Task.FromResult(false);

    // ProfileSync slice 4: push local progression (also the leaderboard submit).
    /// <summary>
    /// Pushes local progression to the cloud (<c>/v2/user/sync</c>). Returns true on success.
    /// </summary>
    Task<bool> SyncProfileAsync() => Task.FromResult(false);

    // ProfileSync slice 6: server-validated streak recovery.
    /// <summary>
    /// Uses oopsie insurance to recover a streak for <paramref name="fixDate"/> (server-validated,
    /// costs XP). Returns (success, error?, newXp?).
    /// </summary>
    Task<(bool, string?, int?)> UseOopsieInsuranceAsync(string fixDate)
        => Task.FromResult((false, (string?)null, (int?)null));

    // ProfileSync slice 6: server-authoritative skill purchase.
    /// <summary>
    /// Purchases a skill server-authoritatively and reconciles skill points / unlocked skills.
    /// Returns (success, error?).
    /// </summary>
    Task<(bool, string?)> PurchaseSkillAsync(string skillId)
        => Task.FromResult((false, (string?)null));

    // ProfileSync slice 6: unique display-name change.
    /// <summary>
    /// Requests a unique display-name change. Returns (success, error?, newDisplayName?).
    /// </summary>
    Task<(bool, string?, string?)> ChangeDisplayNameAsync(string newName)
        => Task.FromResult((false, (string?)null, (string?)null));

    // ProfileSync slice 7: GDPR account deletion.
    /// <summary>
    /// Deletes the account (GDPR). Returns (success, error?).
    /// </summary>
    Task<(bool, string?)> DeleteAccountAsync()
        => Task.FromResult((false, (string?)null));

    // ProfileSync slice 7: GDPR data export.
    /// <summary>
    /// Exports all server-held user data as pretty-printed JSON (GDPR).
    /// Returns (success, error?, prettyJson?).
    /// </summary>
    Task<(bool, string?, string?)> ExportDataAsync()
        => Task.FromResult((false, (string?)null, (string?)null));

    // ProfileSync slice 5: cloud settings backup (gzip + base64, exclusion list, debounce).
    /// <summary>
    /// Backs up settings to the cloud. <paramref name="force"/> bypasses the debounce window.
    /// Returns true on success.
    /// </summary>
    Task<bool> BackupSettingsAsync(bool force = false) => Task.FromResult(false);

    // ProfileSync slice 5: cloud settings backup metadata probe.
    /// <summary>
    /// Fetches metadata about the current cloud settings backup, or null if none exists.
    /// </summary>
    Task<SettingsBackupInfo?> GetSettingsBackupInfoAsync()
        => Task.FromResult<SettingsBackupInfo?>(null);

    // ProfileSync slice 5: cloud settings restore.
    /// <summary>
    /// Downloads and decompresses the cloud settings backup into an <see cref="AppSettings"/>,
    /// or null if none exists / on failure.
    /// </summary>
    Task<AppSettings?> RestoreSettingsFromCloudAsync()
        => Task.FromResult<AppSettings?>(null);

    // ProfileSync slice 7: easter-egg reader counter.
    /// <summary>
    /// Records that the user read the easter egg and returns the current reader count
    /// (or -1 on failure).
    /// </summary>
    Task<int> RecordEasterEggReadAsync() => Task.FromResult(-1);
}
