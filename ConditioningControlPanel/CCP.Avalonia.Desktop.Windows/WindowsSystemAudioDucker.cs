using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// Windows-specific system audio ducker with WPF <c>AudioService</c> parity (WS0 lot 4).
/// Lowers each other application's session volume RELATIVE to its current level
/// (<c>new = current * (1 - strength/100)</c>) across ALL active render endpoints,
/// then restores the recorded originals when the last consumer releases.
/// </summary>
/// <remarks>
/// Behavior mirrored from the WPF head's <c>Services/AudioService.cs</c>:
/// <list type="bullet">
/// <item><description>Reference-counted Duck/Unduck — restore only when the last consumer
/// releases; <see cref="ForceUnduck"/> resets the count (panic/stop paths).</description></item>
/// <item><description>2.5s rescan timer ducks sessions created mid-duck (e.g. a Discord
/// notification) at the current strength.</description></item>
/// <item><description>5-minute watchdog force-unducks if a duck reference leaks.</description></item>
/// <item><description>Crash recovery: original volumes are persisted to
/// <c>ducking_recovery.json</c> in the shared user-data folder (same file the WPF head
/// uses) and restored best-effort on next construction.</description></item>
/// <item><description>Deferred restore: sessions absent/silent at unduck time are retried
/// every 5s for up to 3 minutes, with process-name fallback for restarted apps.</description></item>
/// <item><description>WebView2 (<c>msedgewebview2</c>) processes are skipped while
/// <c>ExcludeBambiCloudFromDucking</c> is enabled (default true).</description></item>
/// </list>
/// </remarks>
public sealed class WindowsSystemAudioDucker : ISystemAudioDucker, IDisposable
{
    /// <summary>Matches the WPF <c>DuckingLevel</c> default (AppSettings.cs).</summary>
    private const int DefaultDuckStrengthPercent = 80;

    /// <summary>Safety net for leaked duck refs; must exceed the longest video (AudioService.cs:40).</summary>
    private const int DuckWatchdogMs = 300_000;

    /// <summary>Rescan period while ducked, catching sessions created after Duck() (AudioService.cs:45).</summary>
    private const int DuckRescanIntervalMs = 2_500;

    /// <summary>Deferred-restore retry period (AudioService.cs:54).</summary>
    private const int RestoreRetryIntervalMs = 5_000;

    /// <summary>Deferred-restore retry window (AudioService.cs:55).</summary>
    private static readonly TimeSpan RestoreRetryWindow = TimeSpan.FromMinutes(3);

    /// <summary>WebView2 PID cache lifetime, avoiding Process.GetProcesses per duck (AudioService.cs:60).</summary>
    private static readonly TimeSpan WebView2CacheExpiry = TimeSpan.FromSeconds(30);

    private readonly ISettingsService _settingsService;
    private readonly ILogger<WindowsSystemAudioDucker>? _logger;

    /// <summary>
    /// Crash-recovery file. Lives in <see cref="IAppEnvironment.UserDataPath"/> (the Local
    /// ConditioningControlPanel folder) so it is the SAME file the WPF head reads/writes —
    /// either head can recover volumes after the other crashes mid-duck.
    /// </summary>
    private readonly string _recoveryFilePath;

    private readonly object _stateLock = new();
    private readonly Dictionary<int, float> _originalVolumes = new();
    private readonly Dictionary<int, string> _processNames = new(); // PID -> name, for fallback matching
    private readonly Dictionary<int, float> _pendingRestores = new();

    private MMDeviceEnumerator? _enumerator;
    private int _duckCount; // Reference count — unduck only when all duckers release
    private bool _isDucked;
    private float _duckAmount = DefaultDuckStrengthPercent / 100f;

    private System.Threading.Timer? _duckWatchdog;
    private System.Threading.Timer? _duckRescanTimer;
    private System.Threading.Timer? _restoreRetryTimer;
    private DateTime _restoreRetryDeadline;

    // Cached WebView2 process IDs to avoid slow Process enumeration on every duck.
    private HashSet<int> _webView2Pids = new();
    private DateTime _webView2PidsCacheTime = DateTime.MinValue;

    private bool _disposed;

    public WindowsSystemAudioDucker(
        ISettingsService settingsService,
        IAppEnvironment environment,
        ILogger<WindowsSystemAudioDucker>? logger = null)
    {
        _settingsService = settingsService;
        _logger = logger;
        _recoveryFilePath = Path.Combine(environment.UserDataPath, "ducking_recovery.json");

        try
        {
            _enumerator = new MMDeviceEnumerator();

            // Check for crash recovery — restore volumes if the app was killed while ducked.
            RecoverFromCrash();
        }
        catch (Exception ex)
        {
            // CoreAudio may be unavailable in some Windows configurations. Fail open.
            _logger?.LogWarning("Audio ducking not available: {Error}", ex.Message);
        }
    }

    /// <inheritdoc />
    public void Duck() => Duck(_settingsService.Current?.DuckingLevel ?? DefaultDuckStrengthPercent);

    /// <inheritdoc />
    public void Duck(int strengthPercent)
    {
        // Don't duck if master volume is 0% — nothing to play anyway (AudioService.cs:768).
        if ((_settingsService.Current?.MasterVolume ?? 100) == 0) return;
        if (_enumerator == null) return;

        lock (_stateLock)
        {
            if (_disposed) return;

            _duckCount++;
            if (_isDucked) return; // Already ducked — just bump the ref count

            _duckAmount = Math.Clamp(strengthPercent, 0, 100) / 100f;

            try
            {
                var currentProcessId = Environment.ProcessId;
                var excludeWebView2 = _settingsService.Current?.ExcludeBambiCloudFromDucking ?? true;
                RefreshWebView2PidCache(excludeWebView2);

                var sessions = CollectRenderSessions();
                int ducked = 0;
                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var session = sessions[i];
                        var processId = (int)session.GetProcessID;
                        if (ShouldSkipSession(session, processId, currentProcessId, excludeWebView2))
                            continue;

                        var currentVolume = session.SimpleAudioVolume.Volume;

                        // Store original volume + process name (for fallback matching at restore
                        // time if the PID changes — e.g. user restarts Firefox mid-session).
                        _originalVolumes[processId] = currentVolume;
                        _processNames[processId] = TryGetProcessName(processId);

                        // Relative reduction: new = current * (1 - strength/100) (AudioService.cs:836).
                        var newVolume = currentVolume * (1.0f - _duckAmount);
                        session.SimpleAudioVolume.Volume = Math.Max(0.0f, newVolume);
                        ducked++;
                    }
                    catch (Exception ex)
                    {
                        // Session may have ended.
                        _logger?.LogDebug("Failed to duck audio session: {Error}", ex.Message);
                    }
                }

                _isDucked = true;

                // Watchdog: force-unduck if ducking exceeds max duration. Catches leaked
                // ref counts from cancelled callbacks, missing Unduck on audio failure, etc.
                _duckWatchdog?.Dispose();
                _duckWatchdog = new System.Threading.Timer(_ =>
                {
                    if (_isDucked)
                    {
                        _logger?.LogWarning(
                            "[Ducking] Watchdog fired after {Ms}ms — force-unducking to prevent stuck volume",
                            DuckWatchdogMs);
                        ForceUnduck();
                    }
                }, null, DuckWatchdogMs, System.Threading.Timeout.Infinite);

                // Rescan for new sessions during the duck window — without this, a Discord
                // notification or Steam ping that creates an audio session AFTER Duck() ran
                // plays at full volume.
                _duckRescanTimer?.Dispose();
                _duckRescanTimer = new System.Threading.Timer(_ => RescanForNewSessions(),
                    null, DuckRescanIntervalMs, DuckRescanIntervalMs);

                // Save state for crash recovery.
                SaveDuckingState();

                _logger?.LogDebug("Ducked {Count} audio sessions by {Amount}%", ducked, strengthPercent);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Audio ducking failed: {Error}", ex.Message);
                // Duck failed — compensate for the increment so the ref count stays balanced.
                _duckCount = Math.Max(0, _duckCount - 1);
            }
        }
    }

    /// <inheritdoc />
    public void Unduck()
    {
        lock (_stateLock)
        {
            UnduckCore();
        }
    }

    /// <inheritdoc />
    public void ForceUnduck()
    {
        lock (_stateLock)
        {
            _duckCount = 1; // Force this release to actually restore (AudioService.cs ForceUnduck)
            UnduckCore();
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed)
                return;

            // Restore audio levels — force unduck regardless of ref count.
            if (_isDucked)
            {
                _duckCount = 1;
                UnduckCore();
            }

            _disposed = true;
            _duckWatchdog?.Dispose();
            _duckWatchdog = null;
            _duckRescanTimer?.Dispose();
            _duckRescanTimer = null;
            _restoreRetryTimer?.Dispose();
            _restoreRetryTimer = null;
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }

    /// <summary>
    /// Decrement the ref count and restore original volumes when it reaches zero.
    /// Must be called under <see cref="_stateLock"/>.
    /// </summary>
    private void UnduckCore()
    {
        if (!_isDucked || _enumerator == null)
        {
            _duckCount = Math.Max(0, _duckCount - 1);
            return;
        }

        _duckCount = Math.Max(0, _duckCount - 1);
        if (_duckCount > 0) return; // Other consumers still need ducking

        try
        {
            var sessions = CollectRenderSessions();

            var restored = new HashSet<int>();
            var nameToCurrentPid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < sessions.Count; i++)
            {
                try
                {
                    var session = sessions[i];
                    var processId = (int)session.GetProcessID;

                    if (_originalVolumes.TryGetValue(processId, out var originalVolume))
                    {
                        session.SimpleAudioVolume.Volume = originalVolume;
                        restored.Add(processId);
                    }
                    else
                    {
                        // Build a name index for fallback restoration of PIDs that no longer exist.
                        var name = TryGetProcessName(processId);
                        if (!string.IsNullOrEmpty(name) && !nameToCurrentPid.ContainsKey(name))
                            nameToCurrentPid[name] = processId;
                    }
                }
                catch (Exception ex)
                {
                    // Session may have ended.
                    _logger?.LogDebug("Failed to unduck audio session: {Error}", ex.Message);
                }
            }

            // Fallback: for stored PIDs whose original session is gone, try to find a current
            // session for the same process name (e.g. user restarted Firefox mid-session —
            // new PID, same app).
            foreach (var kv in _originalVolumes)
            {
                if (restored.Contains(kv.Key)) continue;
                if (!_processNames.TryGetValue(kv.Key, out var name) || string.IsNullOrEmpty(name)) continue;
                if (!nameToCurrentPid.TryGetValue(name, out var currentPid)) continue;

                try
                {
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if ((int)session.GetProcessID == currentPid)
                        {
                            session.SimpleAudioVolume.Volume = kv.Value;
                            restored.Add(kv.Key);
                            break;
                        }
                    }
                }
                catch { /* session may have ended between checks */ }
            }

            // Move any unrestored entries (PID gone, app silent / not playing) to pending and
            // let the retry timer re-attempt as the app resumes producing audio.
            foreach (var kv in _originalVolumes)
            {
                if (!restored.Contains(kv.Key))
                    _pendingRestores[kv.Key] = kv.Value;
            }

            _originalVolumes.Clear();
            _isDucked = false;
            _duckWatchdog?.Dispose();
            _duckWatchdog = null;
            _duckRescanTimer?.Dispose();
            _duckRescanTimer = null;

            if (_pendingRestores.Count > 0)
            {
                _logger?.LogInformation(
                    "[Ducking] {Count} session(s) had no current audio at unduck — will retry restore for up to {Min} min",
                    _pendingRestores.Count, RestoreRetryWindow.TotalMinutes);
                _restoreRetryDeadline = DateTime.UtcNow + RestoreRetryWindow;
                _restoreRetryTimer?.Dispose();
                _restoreRetryTimer = new System.Threading.Timer(_ => RestoreRetryTick(),
                    null, RestoreRetryIntervalMs, RestoreRetryIntervalMs);
            }
            else
            {
                // All restored — process names no longer needed.
                _processNames.Clear();
            }

            // Clear crash-recovery file.
            ClearDuckingState();

            _logger?.LogDebug("Audio unducked ({Restored} restored, {Pending} deferred)",
                restored.Count, _pendingRestores.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Audio unducking failed, preserving state for retry: {Error}", ex.Message);
            // CRITICAL: Do NOT clear _originalVolumes or set _isDucked=false here. If we do,
            // the next Duck() will re-read the currently-ducked volumes as "originals",
            // ratcheting volumes toward 0% over repeated cycles. Keep state intact so the
            // next Unduck/ForceUnduck can retry restoration.
            //
            // Restore _duckCount to 1 (not 0) so the system can recover: with _duckCount=0 +
            // _isDucked=true, Duck() silently returns and no future Unduck() can ever restore
            // volumes — audio would stay permanently ducked.
            _duckCount = 1;
            // Keep the recovery file so crash recovery can restore if the app exits.
        }
    }

    /// <summary>
    /// Periodically called while ducked to catch new audio sessions (e.g. Discord
    /// notification, Steam ping) that didn't exist when Duck() ran. Without this,
    /// those play un-ducked (AudioService.cs RescanForNewSessions).
    /// </summary>
    private void RescanForNewSessions()
    {
        lock (_stateLock)
        {
            if (!_isDucked || _enumerator == null || _disposed) return;

            try
            {
                var currentProcessId = Environment.ProcessId;
                var excludeWebView2 = _settingsService.Current?.ExcludeBambiCloudFromDucking ?? true;
                var sessions = CollectRenderSessions();

                int newlyDucked = 0;
                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var session = sessions[i];
                        var processId = (int)session.GetProcessID;

                        if (ShouldSkipSession(session, processId, currentProcessId, excludeWebView2)) continue;
                        if (_originalVolumes.ContainsKey(processId)) continue; // already ducked

                        var currentVolume = session.SimpleAudioVolume.Volume;
                        // Skip sessions already at 0 — most likely sessions we ducked in a prior
                        // generation whose PID got recycled; treating their 0 as "original"
                        // would silence the app forever.
                        if (currentVolume <= 0.001f) continue;

                        _originalVolumes[processId] = currentVolume;
                        _processNames[processId] = TryGetProcessName(processId);

                        var newVolume = currentVolume * (1.0f - _duckAmount);
                        session.SimpleAudioVolume.Volume = Math.Max(0.0f, newVolume);
                        newlyDucked++;
                    }
                    catch { /* session may have ended */ }
                }

                if (newlyDucked > 0)
                {
                    _logger?.LogDebug("[Ducking] Rescan ducked {Count} new session(s)", newlyDucked);
                    SaveDuckingState();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("[Ducking] Rescan failed: {Error}", ex.Message);
            }
        }
    }

    /// <summary>
    /// Retry restoring volumes for sessions that didn't have an active audio session at
    /// Unduck time. Fires every few seconds for a window after Unduck — when the app starts
    /// producing audio again, its session reappears and we restore the original volume
    /// (AudioService.cs RestoreRetryTick).
    /// </summary>
    private void RestoreRetryTick()
    {
        lock (_stateLock)
        {
            if (_disposed || _pendingRestores.Count == 0)
            {
                _restoreRetryTimer?.Dispose();
                _restoreRetryTimer = null;
                _processNames.Clear();
                return;
            }

            if (DateTime.UtcNow > _restoreRetryDeadline)
            {
                _logger?.LogWarning("[Ducking] Restore retry window expired with {Count} unrestored — giving up",
                    _pendingRestores.Count);
                _pendingRestores.Clear();
                _processNames.Clear();
                _restoreRetryTimer?.Dispose();
                _restoreRetryTimer = null;
                return;
            }

            if (_enumerator == null) return;

            try
            {
                var sessions = CollectRenderSessions();
                var restored = new List<int>();

                // Build a PID-by-name index so a stored PID can match a new PID for the same app.
                var nameToPid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var pid = (int)sessions[i].GetProcessID;
                        if (pid == 0) continue;
                        var name = TryGetProcessName(pid);
                        if (!string.IsNullOrEmpty(name) && !nameToPid.ContainsKey(name))
                            nameToPid[name] = pid;
                    }
                    catch { }
                }

                foreach (var kv in _pendingRestores)
                {
                    var storedPid = kv.Key;
                    var originalVolume = kv.Value;
                    int? targetPid = null;

                    // Try direct PID match first.
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            if ((int)sessions[i].GetProcessID == storedPid) { targetPid = storedPid; break; }
                        }
                        catch { }
                    }

                    // Fallback: process-name match (handles app restart with a new PID).
                    if (targetPid == null && _processNames.TryGetValue(storedPid, out var name)
                        && !string.IsNullOrEmpty(name) && nameToPid.TryGetValue(name, out var freshPid))
                    {
                        targetPid = freshPid;
                    }

                    if (targetPid == null) continue;

                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            var session = sessions[i];
                            if ((int)session.GetProcessID == targetPid.Value)
                            {
                                session.SimpleAudioVolume.Volume = originalVolume;
                                restored.Add(storedPid);
                                break;
                            }
                        }
                        catch { }
                    }
                }

                if (restored.Count > 0)
                {
                    foreach (var pid in restored)
                    {
                        _pendingRestores.Remove(pid);
                        _processNames.Remove(pid);
                    }
                    _logger?.LogInformation(
                        "[Ducking] Deferred-restore: recovered volume for {Count} session(s); {Remaining} still pending",
                        restored.Count, _pendingRestores.Count);
                }

                if (_pendingRestores.Count == 0)
                {
                    _processNames.Clear();
                    _restoreRetryTimer?.Dispose();
                    _restoreRetryTimer = null;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("[Ducking] Restore retry tick failed: {Error}", ex.Message);
            }
        }
    }

    /// <summary>
    /// Check if the app crashed while audio was ducked and restore volumes best-effort
    /// (AudioService.cs RecoverFromCrash). The file is deleted afterwards either way.
    /// </summary>
    private void RecoverFromCrash()
    {
        try
        {
            if (!File.Exists(_recoveryFilePath)) return;

            _logger?.LogInformation("Detected ducking recovery file - restoring audio from previous crash");

            var json = File.ReadAllText(_recoveryFilePath);
            var savedVolumes = JsonConvert.DeserializeObject<Dictionary<int, float>>(json);

            if (savedVolumes != null && savedVolumes.Count > 0 && _enumerator != null)
            {
                var sessions = CollectRenderSessions();

                int restoredCount = 0;
                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var session = sessions[i];
                        var processId = (int)session.GetProcessID;

                        if (savedVolumes.TryGetValue(processId, out var originalVolume))
                        {
                            session.SimpleAudioVolume.Volume = originalVolume;
                            restoredCount++;
                        }
                    }
                    catch { /* Session may have ended */ }
                }

                _logger?.LogInformation("Restored {Count} audio sessions from crash recovery", restoredCount);
            }

            // Delete the recovery file after restore.
            File.Delete(_recoveryFilePath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Failed to recover ducking state: {Error}", ex.Message);
            // Try to delete the file anyway to avoid repeated failures.
            try { File.Delete(_recoveryFilePath); } catch { }
        }
    }

    /// <summary>
    /// Save the current original volumes for crash recovery. Written atomically
    /// (tmp + move) per the project's settings-write convention. Same JSON shape
    /// as the WPF head: <c>{"pid": originalVolume, ...}</c>.
    /// </summary>
    private void SaveDuckingState()
    {
        try
        {
            var dir = Path.GetDirectoryName(_recoveryFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(_originalVolumes);
            var tempPath = _recoveryFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _recoveryFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("Failed to save ducking state: {Error}", ex.Message);
        }
    }

    /// <summary>Clear the crash-recovery file (called on successful unduck).</summary>
    private void ClearDuckingState()
    {
        try
        {
            if (File.Exists(_recoveryFilePath))
                File.Delete(_recoveryFilePath);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("Failed to clear ducking state: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Refresh the cached WebView2 PIDs if expired (avoids slow process enumeration
    /// per session). Matching by process name (msedgewebview2) mirrors the WPF head.
    /// </summary>
    private void RefreshWebView2PidCache(bool excludeWebView2)
    {
        if (!excludeWebView2 || DateTime.UtcNow - _webView2PidsCacheTime <= WebView2CacheExpiry)
            return;

        try
        {
            var newPids = new HashSet<int>();
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var name = proc.ProcessName.ToLowerInvariant();
                    if (name.Contains("msedgewebview2") || name.Contains("webview2"))
                        newPids.Add(proc.Id);
                }
                catch { }
                finally { proc.Dispose(); }
            }
            _webView2Pids = newPids;
            _webView2PidsCacheTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("Failed to refresh WebView2 PID cache: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Collect audio sessions across ALL active render endpoints — not just the default
    /// multimedia device. A user who routes their browser / media player to a secondary
    /// output would otherwise never get ducked, since those sessions live on a different
    /// device's session manager (parity with WPF bug #415).
    /// </summary>
    private List<AudioSessionControl> CollectRenderSessions()
    {
        var result = new List<AudioSessionControl>();
        if (_enumerator == null) return result;

        try
        {
            var endpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            for (int d = 0; d < endpoints.Count; d++)
            {
                try
                {
                    var sessions = endpoints[d].AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try { result.Add(sessions[i]); } catch { /* session ended mid-enumeration */ }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug("[Ducking] Failed to enumerate sessions for a render endpoint: {Error}", ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("[Ducking] Failed to enumerate render endpoints: {Error}", ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Skip our own process, the system-sounds session, sessions that aren't currently
    /// producing audio, and WebView2 processes when BambiCloud exclusion is on.
    /// </summary>
    private bool ShouldSkipSession(AudioSessionControl session, int processId, int currentProcessId, bool excludeWebView2)
    {
        try
        {
            if (processId == currentProcessId || processId == 0)
                return true;

            // Skip WebView2 processes if the setting is enabled (for BambiCloud audio).
            if (excludeWebView2 && _webView2Pids.Contains(processId))
                return true;

            if (session.IsSystemSoundsSession)
                return true;

            // Only duck sessions that are currently producing audio. Inactive sessions are
            // typically not audible; ones that start later are caught by the rescan timer.
            if (session.State != AudioSessionState.AudioSessionStateActive)
                return true;
        }
        catch
        {
            // If we cannot read session metadata, leave it alone.
            return true;
        }

        return false;
    }

    private static string TryGetProcessName(int processId)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            return proc.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
