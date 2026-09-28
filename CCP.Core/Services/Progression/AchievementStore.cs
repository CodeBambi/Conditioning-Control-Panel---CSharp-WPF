using System;
using System.IO;
using System.Text;
using System.Text.Json;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The one reader/writer of achievements.json, lifted verbatim from WPF
/// <c>AchievementService.LoadProgress/WriteProgress</c> so every head shares the same bytes.
/// The path is a constructor argument: tests must never touch the real file
/// (see AchievementCatalogTests.cs:148). The real one is <see cref="DefaultPath"/> -
/// ApplicationData (Roaming on Windows, ~/.config on Linux), NOT CorePaths.UserData,
/// which is LocalAppData and would orphan every existing Windows user's progress.
/// </summary>
internal sealed class AchievementStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ConditioningControlPanel",
        "achievements.json");

    /// <summary>Identical for every write - build them once, not once per save.</summary>
    private static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = true };

    /// <summary>
    /// Serialises every writer of <see cref="_path"/>. Before this existed the 30s autosave
    /// timer's fire-and-forget <c>Task.Run</c> and the synchronous Save that TryUnlock / lock
    /// cards / video minutes call could both be inside <c>File.WriteAllText</c> on the SAME path
    /// at the same time. See <see cref="Write"/>.
    /// </summary>
    internal readonly object _saveLock = new(); // internal: tests assert the snapshot is taken under it
    private readonly string _path;

    public AchievementStore(string path) => _path = path;

    /// <summary>
    /// Loads achievements.json, falling back to the <c>.bak</c> sibling <see cref="Write"/>
    /// leaves behind.
    ///
    /// <para>A file that EXISTS but will not parse is logged at Error, loudly and by name. The
    /// old behaviour - swallow the JsonException and hand back a fresh, empty
    /// <see cref="AchievementProgress"/> - is what turned a single truncated write into permanent,
    /// unexplained total loss: every counter reset and, because TryUnlock early-returns on
    /// <c>IsUnlocked</c>, every already-earned achievement popped again on the next launch
    /// (#1071 / #1074). "No file yet" (a first launch) and "the file is corrupt" look like the same
    /// silence to the user, so they must not be the same silence in the log.</para>
    /// </summary>
    public AchievementProgress Load()
    {
        var backupPath = _path + ".bak";

        foreach (var (path, label) in new[] { (_path, "achievements.json"), (backupPath, "achievements.json.bak") })
        {
            if (!File.Exists(path)) continue;

            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AchievementProgress>(json);
                if (loaded == null)
                {
                    Log.Error("Achievement progress {File} parsed to null - treating it as corrupt", label);
                    continue;
                }

                if (!string.Equals(path, _path, StringComparison.Ordinal))
                {
                    Log.Warning(
                        "RECOVERED achievement progress from {File} after the main file failed to load. {Count} unlock(s) preserved.",
                        label, loaded.UnlockedAchievements?.Count ?? 0);
                }

                return loaded;
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "Achievement progress {File} EXISTS but failed to parse - this is data loss unless the backup loads", label);
            }
        }

        if (File.Exists(_path) || File.Exists(backupPath))
        {
            Log.Error(
                "Achievement progress could not be read from achievements.json OR its backup - starting EMPTY. Cloud sync will restore what it holds.");
        }

        return new AchievementProgress();
    }

    /// <summary>
    /// The ONE writer of achievements.json: serialised under <see cref="_saveLock"/>, and the bytes
    /// land atomically. Returns false (already logged) when the write failed, so the caller can
    /// re-arm its dirty flag and retry.
    ///
    /// <para>This is the #1071 / #1074 root cause. Every previous writer was a bare
    /// <c>File.WriteAllText</c>, which opens <c>FileMode.Create</c> and truncates the file BEFORE the
    /// new bytes land, and two of them ran unsynchronised; <c>App.OnExit</c> ends in
    /// <c>TerminateProcess</c>, which kills an in-flight background write outright. Either way the
    /// document left on disk was truncated, <see cref="Load"/> read it as "empty", and the user lost
    /// every counter and every unlock.</para>
    ///
    /// <para>Temp file, then an atomic replace, keeping the previous good document as a <c>.bak</c>
    /// sibling, because this file is the only local record of progress that cannot be recomputed.
    /// <c>File.Replace</c> does the swap and the backup in one atomic NTFS operation.</para>
    /// </summary>
    public bool Write(AchievementProgress progress) => Write(() => progress);

    /// <summary>
    /// As <see cref="Write(AchievementProgress)"/>, but reads the object to save INSIDE the lock. The
    /// engine swaps its progress on a logout Reset; resolving it here means a background autosave that
    /// was queued before the swap can never land the previous account's progress after it.
    /// </summary>
    public bool Write(Func<AchievementProgress> current)
    {
        lock (_saveLock)
        {
            try
            {
                var progress = current();
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Serialise INSIDE the lock. Progress is mutated on the UI thread, so a background
                // write that serialised outside it could capture a half-updated object graph.
                var json = JsonSerializer.Serialize(progress, SaveOptions);

                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);

                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(tmp, _path, _path + ".bak", ignoreMetadataErrors: true);
                    }
                    catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
                    {
                        // File.Replace is not supported everywhere (FAT32 media, some network
                        // redirectors). Losing the .bak is survivable; losing the save is not.
                        Log.Debug(ex, "File.Replace unavailable for achievements.json, falling back to move");
                        File.Move(tmp, _path, overwrite: true);
                    }
                }
                else
                {
                    File.Move(tmp, _path, overwrite: true);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save achievement progress");
                return false;
            }
        }
    }
}
