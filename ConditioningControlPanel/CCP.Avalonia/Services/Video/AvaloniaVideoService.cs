using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Services.Overlays;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Models;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Services.Video;

/// <summary>
/// Avalonia implementation of the mandatory-video effect engine.
/// Schedules random full-screen video overlays during a session and supports
/// one-off video playback via <see cref="PlaySpecificVideo"/> and <see cref="PlayUrl"/>.
/// </summary>
public sealed class AvaloniaVideoService : IVideoService, IDisposable
{
    private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv" };

    private readonly ISettingsService _settings;
    private readonly IAppEnvironment _environment;
    private readonly IScreenProvider _screens;
    private readonly IInteractionQueueService _interactionQueue;
    private readonly IOverlayService? _overlay;
    private readonly LibVLC _libVlc;
    private readonly VideoMetadataCache _metadataCache;
    private readonly IAudioDeviceService? _audioDeviceService;
    private readonly IModService? _mods;
    private readonly IAchievementService? _achievements;
    private readonly IProgressionService? _progression;
    private readonly ILogger<AvaloniaVideoService>? _logger;
    private readonly IMultiMonitorVideoService? _multiMonitor;
    private readonly Random _random = new();
    private readonly object _sync = new();
    private readonly List<string> _videoFiles = new();
    private readonly List<FloatingText> _attentionTargets = new();
    private readonly List<double> _attentionSpawnTimes = new();
    private readonly List<Window> _messageWindows = new();
    private readonly CompositorEngine? _compositor;
    private readonly VideoLayer? _videoLayer;
    private readonly MandatoryVideoLayer? _mandatoryVideoLayer;
    private readonly List<VideoOverlayWindow> _secondaryWindows = new();

    private CancellationTokenSource? _cts;
    private DispatcherTimer? _scheduledTimer;
    private DispatcherTimer? _safetyTimer;
    // Hard cap on a single mandatory video's runtime (VideoMaxDurationSeconds). When it fires the
    // video is stopped and the overlay torn down — it never chains into another video. This is the
    // "max length" contract: no mandatory video may stay on screen longer than the cap, even if a
    // too-long clip slipped through the (cold-cache) duration filter.
    private DispatcherTimer? _maxDurationTimer;
    // True while any video (mandatory compositor layer, multi-monitor, or window) is on screen.
    // Guards the scheduler so it never preempts a playing video with the next scheduled one.
    private bool _isVideoActive;
    private VideoOverlayWindow? _currentWindow;
    private string? _currentRetryPath;
    private bool _currentStrictMode;
    private double _currentDurationSeconds;
    private DateTime _videoStartTime;
    private int _attentionHits;
    private int _attentionSpawned;
    private int _attentionTotal;
    private int _attentionPenalties;
    private DispatcherTimer? _attentionTimer;
    private bool _isDisposed;
    private bool _codecWarningShown;

    // ---- chaos random-segment mode (one-shot) ----
    // The NEXT triggered video jumps to a random position leaving at least _segmentSec of
    // runway (the chaos 15s cap then ends it — so the player sees a random 15s slice, not
    // always the opening). One shared fraction keeps multi-monitor mirrors in sync. Armed
    // immediately before TriggerVideo by the chaos VideoPayload; disarmed in CloseAll.
    private double _segmentSec;
    private double _segmentFraction;
    private DateTime _segmentArmedAtUtc = DateTime.MinValue;
    private bool SegmentArmed => (DateTime.UtcNow - _segmentArmedAtUtc).TotalSeconds < 30;

    public AvaloniaVideoService(
        ISettingsService settings,
        IAppEnvironment environment,
        IScreenProvider screens,
        IInteractionQueueService interactionQueue,
        LibVLC libVlc,
        VideoMetadataCache metadataCache,
        IAudioDeviceService? audioDeviceService = null,
        IModService? mods = null,
        IAchievementService? achievements = null,
        IProgressionService? progression = null,
        ILogger<AvaloniaVideoService>? logger = null,
        IOverlayService? overlay = null,
        IMultiMonitorVideoService? multiMonitor = null,
        CompositorEngine? compositor = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _interactionQueue = interactionQueue ?? throw new ArgumentNullException(nameof(interactionQueue));
        _libVlc = libVlc ?? throw new ArgumentNullException(nameof(libVlc));
        SharedLibVLC = _libVlc;
        _metadataCache = metadataCache ?? throw new ArgumentNullException(nameof(metadataCache));
        _audioDeviceService = audioDeviceService;
        _mods = mods;
        _achievements = achievements;
        _progression = progression;
        _logger = logger;
        _overlay = overlay;
        _multiMonitor = multiMonitor;
        _compositor = compositor;
        // WS0 lot 4 (skia-rebuild-goal.md): the LEGACY video path (multi-monitor service /
        // per-window fallback) is the contract holder until UCE plan Phase E proves the
        // compositor video layer by running. VideoLayer/MandatoryVideoLayer exist but their
        // frame path is unproven (unified-compositor-engine skill "headline blockers"), so
        // routing video into them by default plays audio with no visible frames AND used to
        // strand the scheduler (_isVideoActive never cleared). Compositor video is therefore
        // an explicit WS1 dev opt-in via CCP_UCE_VIDEO=1; the default stays legacy.
        var useCompositorVideo = compositor != null &&
            string.Equals(Environment.GetEnvironmentVariable("CCP_UCE_VIDEO"), "1", StringComparison.Ordinal);
        _videoLayer = useCompositorVideo ? new VideoLayer(libVlc, _logger) : null;
        _mandatoryVideoLayer = useCompositorVideo ? new MandatoryVideoLayer(libVlc, _logger) : null;
        if (_videoLayer != null)
        {
            _compositor?.RegisterLayer(_videoLayer);
            _videoLayer.VideoStarted += (_, _) => VideoStarted?.Invoke(this, EventArgs.Empty);
            _videoLayer.VideoEnded += (_, _) => OnCompositorVideoEnded();
        }
        if (_mandatoryVideoLayer != null)
        {
            _compositor?.RegisterLayer(_mandatoryVideoLayer);
            _mandatoryVideoLayer.VideoStarted += (_, _) => VideoStarted?.Invoke(this, EventArgs.Empty);
            _mandatoryVideoLayer.VideoEnded += (_, _) => OnCompositorVideoEnded();
        }

        if (_multiMonitor != null)
        {
            _multiMonitor.PlaybackStarted += OnMultiMonitorPlaybackStarted;
            _multiMonitor.PlaybackEnded += OnMultiMonitorPlaybackEnded;
        }

        RefreshVideosPath();
    }

    public bool IsRunning { get; private set; }
    public bool IsPlaying => _currentWindow?.IsPlaying ?? false;
    public string? LastVideoTitle => string.IsNullOrEmpty(_currentRetryPath) ? null : Path.GetFileNameWithoutExtension(_currentRetryPath);
    public string? LastVideoPath => _currentRetryPath;
    public int PlaythroughFailCount => _attentionPenalties;

    /// <summary>
    /// Primary (audio-bearing) media player, or null if no video is playing.
    /// Exposed for the Deeper EnhancementEngine to read playback time and
    /// drive Seek/Pause. Treat as read-only — the engine should not mutate
    /// state outside of Seek/Pause/Play helpers below.
    /// </summary>
    public MediaPlayer? PrimaryMediaPlayer => _currentWindow?.MediaPlayer;

    /// <summary>
    /// Primary video window (audio monitor), or null if no video is playing.
    /// Used to compute screen-space video rect for gaze-target rules.
    /// </summary>
    public Window? PrimaryVideoWindow => _currentWindow;

    /// <summary>
    /// The shared LibVLC instance used by this service (used by BubbleCountWindow and others).
    /// Set by the constructor from the injected instance.
    /// </summary>
    public static LibVLC SharedLibVLC { get; private set; } = null!;

    /// <summary>
    /// Cache of per-video duration metadata. Used by the min/max duration filter in LoadVideoFiles.
    /// </summary>
    public VideoMetadataCache MetadataCache => _metadataCache;

    /// <summary>
    /// Current primary-player playback time in milliseconds, or -1 if none.
    /// </summary>
    public long GetCurrentPlaybackTimeMs()
    {
        try { return PrimaryMediaPlayer?.Time ?? -1; }
        catch { return -1; }
    }

    /// <summary>
    /// Seek the primary player to the given absolute time. No-op if no
    /// video is active or the player rejects the seek (LibVLC will silently
    /// ignore for non-seekable streams).
    /// </summary>
    public void SeekPrimary(long ms)
    {
        try
        {
            var p = PrimaryMediaPlayer;
            if (p == null) return;
            if (!p.IsSeekable) return;
            p.Time = Math.Max(0, ms);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("AvaloniaVideoService.SeekPrimary failed: {Error}", ex.Message);
        }
    }

    /// <summary>Pause the primary player. No-op if none.</summary>
    public void PausePrimary()
    {
        try { PrimaryMediaPlayer?.Pause(); }
        catch (Exception ex) { _logger?.LogDebug("AvaloniaVideoService.PausePrimary failed: {Error}", ex.Message); }
    }

    /// <summary>Resume the primary player. No-op if none.</summary>
    public void PlayPrimary()
    {
        try { PrimaryMediaPlayer?.Play(); }
        catch (Exception ex) { _logger?.LogDebug("AvaloniaVideoService.PlayPrimary failed: {Error}", ex.Message); }
    }

    /// <summary>
    /// Chaos: make the next video start at a random position with at least
    /// <paramref name="segmentSec"/> seconds left to play.
    /// </summary>
    public void ArmRandomSegment(double segmentSec)
    {
        _segmentSec = Math.Max(1, segmentSec);
        _segmentFraction = Random.Shared.NextDouble();
        _segmentArmedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Snapshot of currently-active attention targets that should respond
    /// to Focus Gaze dwells. Returns empty when VideoGazeClickEnabled is
    /// off. Caller iterates in reverse for topmost-first selection.
    /// </summary>
    internal IReadOnlyList<FloatingText> GetGazeTargets()
    {
        if (_settings.Current?.VideoGazeClickEnabled != true)
            return Array.Empty<FloatingText>();
        lock (_attentionTargets)
        {
            return _attentionTargets.ToArray();
        }
    }

    /// <summary>
    /// Programmatic equivalent of a mouse click on an attention target.
    /// Runs the same idempotent Hit() pipeline (sound, onHit callback,
    /// fade). Safe to call against a target that's already been hit or
    /// destroyed.
    /// </summary>
    internal void GazeClick(FloatingText target)
    {
        if (target == null) return;
        target.Hit();
    }

    /// <summary>
    /// Raised when the primary player's playback position advances.
    /// Argument is current time in milliseconds. Fires from LibVLC's
    /// internal thread; subscribers must marshal to the UI thread.
    /// </summary>
    public event Action<long>? PrimaryPlaybackTimeMsChanged;

    public event EventHandler? VideoAboutToStart;
    public event EventHandler? VideoStarted;
    public event EventHandler? VideoEnded;

    public void Start()
    {
        if (IsRunning || _isDisposed) return;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            _logger?.LogDebug("AvaloniaVideoService: full-screen video overlays are not supported on mobile; Start is a no-op");
            return;
        }

        IsRunning = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        RefreshVideosPath();
        ScheduleNext();

        // NOTE: The background metadata prewarm (Task.Run → PrewarmAsync over hundreds of files)
        // was removed because the burst of LibVLC Media.Parse() native preparser threads on a
        // background task corrupts the native heap during session startup — the fault then
        // manifests non-deterministically as a 0xC0000005 access violation a few milliseconds
        // later (e.g. when the bubble service touches the corrupted heap). The runtime
        // max-duration timer (_maxDurationTimer) still enforces the cap for any too-long clip,
        // and durations are lazily computed on first actual playback via GetOrComputeDurationAsync
        // in the filter path, so no functionality is lost — the prewarm was purely an optimization.

        _logger?.LogInformation("AvaloniaVideoService started (videos: {Count})", _videoFiles.Count);
    }

    public void Stop()
    {
        if (!IsRunning && _currentWindow == null) return;
        IsRunning = false;

        _scheduledTimer?.Stop();
        _scheduledTimer = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        Dispatcher.UIThread.Invoke(() => CleanupInternal(notifyEnded: false));
        _logger?.LogInformation("AvaloniaVideoService stopped");
    }

    public void RefreshVideosPath()
    {
        LoadVideoFiles();
    }

    public void PlaySpecificVideo(string videoPath, bool strictMode)
    {
        if (_isDisposed) return;
        if (string.IsNullOrWhiteSpace(videoPath)) return;
        var path = videoPath;
        if (!Path.IsPathRooted(path))
            path = Path.Combine(_environment.EffectiveAssetsPath, "videos", path);
        if (!File.Exists(path))
        {
            _logger?.LogWarning("AvaloniaVideoService: PlaySpecificVideo path not found {Path}", path);
            return;
        }
        Dispatcher.UIThread.Invoke(() => PlayFile(path, strictMode));
    }

    public void PlayUrl(string url)
    {
        if (_isDisposed) return;
        if (string.IsNullOrWhiteSpace(url)) return;
        Dispatcher.UIThread.Invoke(() => PlayUrlCore(url));
    }

    public void TriggerVideo()
    {
        if (_isDisposed) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            CleanupInternal(notifyEnded: false);
            PlayRandomVideo();
        });
    }

    public void ForceCleanup()
    {
        if (_isDisposed) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            CleanupInternal(notifyEnded: true);
            try { _interactionQueue.ForceReset(); } catch { /* best effort */ }
        });
    }

    private void LoadVideoFiles()
    {
        List<string> files;
        lock (_sync)
        {
            _videoFiles.Clear();
            var folders = new[]
            {
                Path.Combine(_environment.EffectiveAssetsPath, "videos"),
                Path.Combine(_environment.BaseDirectory, "Resources", "videos")
            };

            files = new List<string>();
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var ext in VideoExtensions)
                {
                    try
                    {
                        files.AddRange(Directory.GetFiles(folder, $"*{ext}", SearchOption.AllDirectories)
                            .Where(f => IsPathSafe(f, _environment.EffectiveAssetsPath) || IsPathSafe(f, _environment.BaseDirectory)));
                    }
                    catch { }
                }
            }
        }

        files = ApplyDurationFilter(files);

        lock (_sync)
        {
            _videoFiles.Clear();
            _videoFiles.AddRange(files);
        }
    }

    /// <summary>
    /// Filters videos by <see cref="AppSettings.VideoMinDurationSeconds"/> and
    /// <see cref="AppSettings.VideoMaxDurationSeconds"/> using the on-disk metadata
    /// cache. Videos with no cached duration are included and parsed in the background
    /// so they are filtered correctly on the next refresh.
    /// </summary>
    private List<string> ApplyDurationFilter(List<string> files)
    {
        var settings = _settings.Current;
        var minSec = settings?.VideoMinDurationSeconds ?? 0;
        var maxSec = settings?.VideoMaxDurationSeconds ?? 0;
        if ((minSec <= 0 && maxSec <= 0) || _metadataCache == null)
            return files;

        var beforeCount = files.Count;
        var filtered = files.Where(f =>
        {
            var dur = _metadataCache.TryGetDuration(f);
            if (dur == null)
            {
                _ = _metadataCache.GetOrComputeDurationAsync(f);
                return true;
            }
            if (minSec > 0 && dur.Value < minSec) return false;
            if (maxSec > 0 && dur.Value > maxSec) return false;
            return true;
        }).ToList();

        _logger?.LogDebug("AvaloniaVideoService: {Before} -> {After} after duration filter [{Min}s, {Max}s]",
            beforeCount, filtered.Count, minSec, maxSec);
        return filtered;
    }

    private static bool IsPathSafe(string path, string allowedBasePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var fullPath = Path.GetFullPath(path);
            var basePath = Path.GetFullPath(allowedBasePath);
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()) && !basePath.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;
            return fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void ScheduleNext()
    {
        if (!IsRunning) return;

        var settings = _settings.Current;
        if (settings == null || !settings.MandatoryVideosEnabled) return;
        lock (_sync)
        {
            if (_videoFiles.Count == 0) return;
        }

        _scheduledTimer?.Stop();

        var freq = Math.Max(1, settings.VideosPerHour);
        var baseInterval = 3600.0 / freq;
        var variance = baseInterval * 0.3;
        var interval = baseInterval + (Random.Shared.NextDouble() * variance * 2 - variance);
        interval = Math.Max(10, interval);

        _scheduledTimer = StartOneShotTimer(TimeSpan.FromSeconds(interval), () =>
        {
            if (!IsRunning) return;
            // Never preempt a video that is already on screen (mandatory or chaos): let it finish
            // (or hit the max-duration cutoff) and wait for the next interval. Without this guard
            // the scheduler would tear down the current video and play another on top of it.
            if (_isVideoActive)
            {
                ScheduleNext();
                return;
            }
            try
            {
                var s = _settings.Current;
                if (s != null && s.MandatoryVideosEnabled)
                    PlayRandomVideo();
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "AvaloniaVideoService: scheduled playback failed"); }
            ScheduleNext();
        });
    }

    public void PlayRandomVideo()
    {
        string? file;
        lock (_sync)
        {
            if (_videoFiles.Count == 0)
            {
                _logger?.LogDebug("AvaloniaVideoService: PlayRandomVideo called but no videos are available");
                return;
            }
            file = _videoFiles[Random.Shared.Next(_videoFiles.Count)];
        }
        var strict = _settings.Current?.StrictLockEnabled ?? false;
        Dispatcher.UIThread.Invoke(() => PlayFile(file, strict));
    }

    public void UpdateVolume()
    {
        // WPF contract (VideoService.UpdatePlayingVideosVolume): a live volume change only
        // writes Volume to active players. Re-applying the output device here would re-bind
        // the audio endpoint mid-playback on every slider drag (WS0 lot 4 V1-22).
        _currentWindow?.ApplyVolume();
        if (_multiMonitor?.IsPlaying == true)
        {
            _multiMonitor.SetVolume(LibVlcAudioHelper.GetEffectiveVolume(_settings.Current));
        }
    }

    /// <summary>
    /// Compositor-path natural end: clear the scheduler's in-flight guard and disarm the
    /// max-duration cap (the legacy window/multi-monitor paths do this via CleanupInternal,
    /// which the compositor path never reaches). Runs on the UI thread — the layers post
    /// VideoEnded via the dispatcher. WS0 lot 4 fix (R1-3): without this, the first natural
    /// compositor end left _isVideoActive true forever and the scheduler skipped every
    /// subsequent video.
    /// </summary>
    private void OnCompositorVideoEnded()
    {
        _isVideoActive = false;
        _maxDurationTimer?.Stop();
        _maxDurationTimer = null;
        VideoEnded?.Invoke(this, EventArgs.Empty);
    }

    private string? PickRandomVideo()
    {
        lock (_sync)
        {
            if (_videoFiles.Count == 0) return null;
            return _videoFiles[Random.Shared.Next(_videoFiles.Count)];
        }
    }

    private void PlayFile(string filePath, bool strictMode)
    {
        if (_isDisposed) return;
        CleanupInternal(notifyEnded: false);

        // Mark the slot active (for the scheduler's in-flight guard) and arm the hard cap so no
        // mandatory video can overrun VideoMaxDurationSeconds. Both are cleared in CleanupInternal.
        _isVideoActive = true;
        StartMaxDurationTimer();

        _compositor?.Start();
        _mandatoryVideoLayer?.PlayVideo(filePath, withAudio: true, loop: false);

        // When the unified compositor is available it already renders the video layer
        // on every monitor, so the pink filter and other overlays sit on top. Skip the
        // legacy multi-monitor windows to avoid them covering the compositor.
        if (OperatingSystem.IsWindows() && _multiMonitor != null && _mandatoryVideoLayer == null)
        {
            _interactionQueue.TryStart("Video", () =>
            {
                _multiMonitor.PlayFile(filePath);
                _multiMonitor.SetVolume(LibVlcAudioHelper.GetEffectiveVolume(_settings.Current));
                _multiMonitor.SetAudioOutputDevice(_settings.Current?.AudioOutputDeviceId);
            });
            return;
        }

        // Fallback per-window path only when neither compositor nor multi-monitor service is available.
        if (_mandatoryVideoLayer == null)
        {
            _interactionQueue.TryStart("Video", () =>
            {
                var screen = GetPrimaryScreen();
                _currentWindow = CreateWindow(screen, filePath, fromUrl: false, strictMode);
                _currentWindow.Show();
                SpawnSecondaryWindows(filePath, fromUrl: false, strictMode);
                // Disarm random segment now that a video is playing
                _segmentArmedAtUtc = DateTime.MinValue;
            });
        }
    }

    private void PlayUrlCore(string url)
    {
        if (_isDisposed) return;
        CleanupInternal(notifyEnded: false);

        // Chaos/URL-triggered videos are not subject to the mandatory-video max cap, but they must
        // still mark the slot active so the scheduler doesn't preempt them mid-playback.
        _isVideoActive = true;

        _compositor?.Start();
        _videoLayer?.PlayVideo(url, withAudio: true, loop: false);

        // When the unified compositor is available it already renders the video layer
        // on every monitor, so the pink filter and other overlays sit on top. Skip the
        // legacy multi-monitor windows to avoid them covering the compositor.
        if (OperatingSystem.IsWindows() && _multiMonitor != null && _videoLayer == null)
        {
            _interactionQueue.TryStart("Video", () =>
            {
                _multiMonitor.PlayUrl(url);
                _multiMonitor.SetVolume(LibVlcAudioHelper.GetEffectiveVolume(_settings.Current));
                _multiMonitor.SetAudioOutputDevice(_settings.Current?.AudioOutputDeviceId);
            });
            return;
        }

        // Fallback per-window path only when neither compositor nor multi-monitor service is available.
        if (_videoLayer == null)
        {
            _interactionQueue.TryStart("Video", () =>
            {
                var screen = GetPrimaryScreen();
                _currentWindow = CreateWindow(screen, url, fromUrl: true, strictMode: false);
                _currentWindow.Show();
                SpawnSecondaryWindows(url, fromUrl: true, strictMode: false);
            });
        }
    }

    private void OnMultiMonitorPlaybackStarted(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => VideoStarted?.Invoke(this, EventArgs.Empty));
    }

    private void OnMultiMonitorPlaybackEnded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try { _interactionQueue.Complete("Video"); } catch { }
            VideoEnded?.Invoke(this, EventArgs.Empty);
        });
    }

    private void SpawnSecondaryWindows(string source, bool fromUrl, bool strictMode)
    {
        try
        {
            var settings = _settings.Current;
            if (settings == null || !settings.DualMonitorEnabled) return;

            var allScreens = _screens.GetAllScreens();
            if (allScreens.Count <= 1) return;

            var primary = _screens.GetPrimaryScreen() ?? allScreens[0];
            foreach (var screen in allScreens)
            {
                if (screen == primary) continue;
                var win = new VideoOverlayWindow(_settings, _libVlc, screen, source, fromUrl, strictMode, () => { }, _logger, withAudio: false, isPrimary: false);
                _secondaryWindows.Add(win);
                win.Show();
            }

            _logger?.LogInformation("AvaloniaVideoService: spawned {Count} secondary video window(s)", _secondaryWindows.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AvaloniaVideoService: failed to spawn secondary windows");
        }
    }

    private VideoOverlayWindow CreateWindow(ScreenInfo screen, string source, bool fromUrl, bool strictMode)
    {
        _currentRetryPath = fromUrl ? null : source;
        _currentStrictMode = strictMode;
        _currentDurationSeconds = 0;
        _attentionHits = 0;
        _attentionSpawned = 0;
        _attentionTotal = 0;
        _attentionSpawnTimes.Clear();
        lock (_attentionTargets) { _attentionTargets.Clear(); }

        // Check if segment is armed (30s window)
        var isSegmentArmed = (DateTime.UtcNow - _segmentArmedAtUtc).TotalSeconds < 30 && _segmentSec > 0;
        var segFraction = isSegmentArmed ? _segmentFraction : 0;

        var win = new VideoOverlayWindow(
            _settings,
            _libVlc,
            screen,
            source,
            fromUrl,
            strictMode,
            () => { },
            _logger,
            withAudio: true,
            isPrimary: true,
            onCodecWarning: onWarning =>
            {
                if (onWarning && !_codecWarningShown)
                {
                    _codecWarningShown = true;
                    _logger?.LogWarning("AvaloniaVideoService: codec warning — video may not play correctly. Verify libvlc runtime.");
                }
            },
            onPositionChanged: ms =>
            {
                try { PrimaryPlaybackTimeMsChanged?.Invoke(ms); } catch { /* no-op if no subscribers */ }
            },
            segmentArmed: isSegmentArmed,
            segmentFraction: segFraction,
            segmentSec: isSegmentArmed ? _segmentSec : 0);
        win.VideoStarted += OnVideoWindowStarted;
        win.VideoEnded += OnVideoWindowEnded;
        return win;
    }

    private void OnVideoWindowStarted()
    {
        _videoStartTime = DateTime.Now;
        VideoStarted?.Invoke(this, EventArgs.Empty);
        _overlay?.NotifyTopWindowOpened();

        var settings = _settings.Current;
        if (settings == null) return;

        if (_currentWindow?.MediaPlayer is { } player && player.Length > 0)
        {
            _currentDurationSeconds = player.Length / 1000.0;
        }
        else
        {
            _currentDurationSeconds = 0;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(60, _currentDurationSeconds + 30));
        _interactionQueue.ExtendTimeout(timeout);
        StartSafetyTimer(_currentDurationSeconds > 0 ? _currentDurationSeconds + 30 : 600);

        if (settings.AttentionChecksEnabled)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                Dispatcher.UIThread.Post(SetupAttention);
            });
        }
    }

    private void OnVideoWindowEnded()
    {
        _overlay?.NotifyTopWindowClosed();
        OnVideoEnded();
    }

    private void StartSafetyTimer(double timeoutSeconds)
    {
        _safetyTimer?.Stop();
        _safetyTimer = StartOneShotTimer(TimeSpan.FromSeconds(Math.Max(30, timeoutSeconds)), () =>
        {
            if (_currentWindow == null) return;
            _logger?.LogWarning("AvaloniaVideoService: safety timeout triggered, forcing cleanup");
            CleanupInternal(notifyEnded: true);
        });
    }

    /// <summary>
    /// Arms a one-shot cutoff at <see cref="ConditioningControlPanel.Core.Models.AppSettings.VideoMaxDurationSeconds"/>
    /// so no single mandatory video can overrun the cap — even if a too-long clip slipped through the
    /// (cold-cache) duration filter or the source is longer than expected. When it fires the video is
    /// stopped and the overlay torn down (desktop freed); it never chains into another video. No-op
    /// when the cap is 0 (disabled).
    /// </summary>
    private void StartMaxDurationTimer()
    {
        _maxDurationTimer?.Stop();
        var max = _settings.Current?.VideoMaxDurationSeconds ?? 0;
        if (max <= 0) return;
        _maxDurationTimer = StartOneShotTimer(TimeSpan.FromSeconds(Math.Max(1, max)), () =>
        {
            _logger?.LogInformation("AvaloniaVideoService: max-duration cutoff ({Max}s) reached — stopping video", max);
            CleanupInternal(notifyEnded: true);
        });
    }

    private void SetupAttention()
    {
        try
        {
            var settings = _settings.Current;
            if (settings == null || !IsPlaying) return;

            lock (_attentionTargets)
            {
                foreach (var t in _attentionTargets.ToList()) t.Destroy();
                _attentionTargets.Clear();
            }

            _attentionSpawned = 0;
            _attentionHits = 0;
            _attentionSpawnTimes.Clear();

            var duration = _currentDurationSeconds > 0 ? _currentDurationSeconds : 60;
            var maxTargets = Math.Max(1, settings.AttentionDensity);
            _attentionTotal = settings.RandomizeAttentionTargets
                ? _random.Next(1, maxTargets + 1)
                : maxTargets;

            var availableWindow = Math.Max(1, duration - 8);
            const double minGap = 3.0;
            for (int i = 0; i < _attentionTotal; i++)
            {
                _attentionSpawnTimes.Add(3 + _random.NextDouble() * availableWindow);
            }
            _attentionSpawnTimes.Sort();
            for (int i = 1; i < _attentionSpawnTimes.Count; i++)
            {
                if (_attentionSpawnTimes[i] - _attentionSpawnTimes[i - 1] < minGap)
                    _attentionSpawnTimes[i] = _attentionSpawnTimes[i - 1] + minGap;
            }

            _attentionTimer?.Stop();
            _attentionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            _attentionTimer.Tick += (_, _) => CheckSpawnTargets();
            _attentionTimer.Start();

            _logger?.LogInformation("Attention: {Count} targets over {Duration}s", _attentionTotal, (int)duration);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("AvaloniaVideoService.SetupAttention failed: {Error}", ex.Message);
        }
    }

    private void CheckSpawnTargets()
    {
        if (!IsPlaying) return;
        var elapsed = (DateTime.Now - _videoStartTime).TotalSeconds;
        while (_attentionSpawnTimes.Count > 0 && elapsed >= _attentionSpawnTimes[0])
        {
            _attentionSpawnTimes.RemoveAt(0);
            SpawnTarget();
        }
    }

    private void SpawnTarget()
    {
        try
        {
            var settings = _settings.Current;
            if (settings == null) return;

            var pool = settings.AttentionPool?.Where(p => p.Value).Select(p => p.Key).ToList()
                ?? new List<string>();
            var text = pool.Count > 0 ? pool[_random.Next(pool.Count)] : "CLICK ME";

            var screens = settings.DualMonitorEnabled ? _screens.GetAllScreens().ToArray() : new[] { GetPrimaryScreen() };
            if (screens.Length == 0 || screens[0] == null)
            {
                _logger?.LogWarning("AvaloniaVideoService.SpawnTarget: no screens available");
                return;
            }

            _attentionSpawned++;
            var spawnedTargets = new List<FloatingText>();
            bool hitRegistered = false;

            _logger?.LogDebug("Spawning attention target: '{Text}' on {ScreenCount} screen(s) ({Spawned}/{Total})",
                text, screens.Length, _attentionSpawned, _attentionTotal);

            foreach (var screen in screens)
            {
                if (screen == null) continue;
                FloatingText? target = null;
                target = new FloatingText(
                    _libVlc,
                    _audioDeviceService,
                    _environment,
                    settings,
                    screen,
                    text,
                    settings.AttentionSize,
                    () =>
                    {
                        if (hitRegistered) return;
                        hitRegistered = true;
                        _attentionHits++;
                        _progression?.AddXP(15, XPSource.Video);

                        lock (_attentionTargets)
                        {
                            foreach (var t in spawnedTargets)
                            {
                                if (_attentionTargets.Contains(t))
                                {
                                    _attentionTargets.Remove(t);
                                    if (t != target) t.Destroy();
                                }
                            }
                        }

                        List<FloatingText> remaining;
                        lock (_attentionTargets) { remaining = _attentionTargets.ToList(); }
                        _logger?.LogInformation("ATTENTION: Hit {Hits}/{Spawned}, {Remaining} targets remaining", _attentionHits, _attentionSpawned, remaining.Count);

                        if (remaining.Count > 0)
                        {
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(300);
                                Dispatcher.UIThread.Post(() =>
                                {
                                    foreach (var t in remaining) t.BringToFront();
                                });
                            });
                        }
                    });

                spawnedTargets.Add(target);
                lock (_attentionTargets) { _attentionTargets.Add(target); }
            }

            var lifespan = Math.Max(1, settings.AttentionLifespan) * 1000;
            _ = Task.Run(async () =>
            {
                await Task.Delay(lifespan);
                Dispatcher.UIThread.Post(() =>
                {
                    lock (_attentionTargets)
                    {
                        foreach (var t in spawnedTargets)
                        {
                            if (_attentionTargets.Contains(t))
                            {
                                _attentionTargets.Remove(t);
                                t.Destroy();
                            }
                        }
                    }
                });
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("AvaloniaVideoService.SpawnTarget failed: {Error}", ex.Message);
        }
    }

    private void OnVideoEnded()
    {
        _attentionTimer?.Stop();
        _attentionTimer = null;
        _safetyTimer?.Stop();
        _safetyTimer = null;

        var settings = _settings.Current;
        bool loop = false, troll = false;

        if (settings != null && settings.AttentionChecksEnabled && _attentionSpawned > 0)
        {
            bool passed = _attentionHits >= _attentionSpawned;
            _logger?.LogInformation("Attention result: {Hits}/{Spawned} (of {Total} scheduled) = {Result}",
                _attentionHits, _attentionSpawned, _attentionTotal, passed ? "PASS" : "FAIL");

            if (passed)
            {
                var xpForPlays = (_attentionPenalties + 1) * 50;
                _progression?.AddXP(xpForPlays + 200, XPSource.Video);
                _achievements?.TrackAttentionCheckPassed(isVideo: true);
                if (_random.NextDouble() < 0.1)
                {
                    loop = true;
                    troll = true;
                }
            }
            else
            {
                loop = true;
                _achievements?.TrackAttentionCheckFailed();
                _achievements?.TrackVideoAttentionCheckFailed();
            }
        }

        if (loop && !string.IsNullOrEmpty(_currentRetryPath))
        {
            _attentionPenalties++;
            if (_attentionPenalties >= 3 && settings?.MercySystemEnabled == true)
            {
                ShowMessage(_mods?.GetAttentionCheckMercyMessage() ?? "BAMBI GETS MERCY", 2500, () => CleanupInternal(notifyEnded: true));
            }
            else
            {
                var message = troll
                    ? "GOOD GIRL!\nWATCH AGAIN 😜"
                    : (_mods?.GetAttentionCheckFailMessage() ?? "DUMB BAMBI!\nTRY AGAIN");
                ShowMessage(message, 2000, () =>
                {
                    _attentionHits = 0;
                    _attentionSpawnTimes.Clear();
                    _interactionQueue.ExtendTimeout(TimeSpan.FromMinutes(5));
                    var retryVideo = PickRandomVideo();
                    PlayFile(string.IsNullOrEmpty(retryVideo) ? _currentRetryPath! : retryVideo, _currentStrictMode);
                });
            }
            return;
        }

        if (_currentDurationSeconds > 0)
        {
            _achievements?.TrackVideoWatched(_currentDurationSeconds);
        }

        CleanupInternal(notifyEnded: true);
    }

    private void ShowMessage(string text, int ms, Action then)
    {
        CleanupInternal(notifyEnded: false);

        var screens = _settings.Current?.DualMonitorEnabled == true
            ? _screens.GetAllScreens().ToArray()
            : new[] { GetPrimaryScreen() };
        if (screens.Length == 0 || screens[0] == null)
        {
            then();
            return;
        }

        foreach (var screen in screens)
        {
            if (screen == null) continue;
            var win = new Window
            {
                WindowDecorations = WindowDecorations.None,
                Background = new SolidColorBrush(Colors.Black),
                Topmost = true,
                ShowInTaskbar = false,
                CanResize = false,
                ShowActivated = false,
                WindowState = WindowState.FullScreen,
                Content = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(
                        global::Avalonia.Application.Current?.TryGetResource("PinkColor", global::Avalonia.Styling.ThemeVariant.Default, out var v) == true && v is Color c
                            ? c
                            : new Color(0xFF, 0xFF, 0x00, 0xFF)),
                    FontSize = 64,
                    FontWeight = FontWeight.Bold,
                    FontFamily = new FontFamily("Impact, Arial"),
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                }
            };
            win.ConstrainToScreen(screen);
            win.Show();
            _messageWindows.Add(win);
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(ms);
            Dispatcher.UIThread.Post(() =>
            {
                CloseMessageWindows();
                then();
            });
        });
    }

    private void CloseMessageWindows()
    {
        foreach (var w in _messageWindows.ToList())
        {
            try { w.Close(); } catch { }
        }
        _messageWindows.Clear();
    }

    private void CleanupInternal(bool notifyEnded)
    {
        _multiMonitor?.Stop();

        _attentionTimer?.Stop();
        _attentionTimer = null;
        _safetyTimer?.Stop();
        _safetyTimer = null;
        _maxDurationTimer?.Stop();
        _maxDurationTimer = null;
        _isVideoActive = false;

        lock (_attentionTargets)
        {
            foreach (var t in _attentionTargets.ToList()) t.Destroy();
            _attentionTargets.Clear();
        }

        CloseMessageWindows();

        foreach (var win in _secondaryWindows.ToList())
        {
            try { win.Close(); } catch { }
        }
        _secondaryWindows.Clear();

        _videoLayer?.Stop();
        _mandatoryVideoLayer?.Stop();

        if (_currentWindow != null)
        {
            try
            {
                _currentWindow.VideoStarted -= OnVideoWindowStarted;
                _currentWindow.VideoEnded -= OnVideoWindowEnded;
                _currentWindow.Close();
            }
            catch { }
            _currentWindow = null;
            _overlay?.NotifyTopWindowClosed();
        }

        _currentStrictMode = false;
        _currentDurationSeconds = 0;

        try { _interactionQueue.Complete("Video"); } catch { }

        if (notifyEnded)
        {
            VideoEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private ScreenInfo GetPrimaryScreen()
    {
        try
        {
            return _screens.GetPrimaryScreen()
                ?? _screens.GetAllScreens().FirstOrDefault()
                ?? new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("AvaloniaVideoService: could not get primary screen: {Error}", ex.Message);
            return new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0);
        }
    }

    private static DispatcherTimer StartOneShotTimer(TimeSpan dueTime, Action callback)
    {
        var timer = new DispatcherTimer { Interval = dueTime };
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= handler;
            callback();
        };
        timer.Tick += handler;
        timer.Start();
        return timer;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();

        if (_multiMonitor != null)
        {
            _multiMonitor.PlaybackStarted -= OnMultiMonitorPlaybackStarted;
            _multiMonitor.PlaybackEnded -= OnMultiMonitorPlaybackEnded;
        }
    }

    private sealed class VideoOverlayWindow : Window
    {
        private readonly ISettingsService _settings;
        private readonly LibVLC _libVlc;
        private readonly string _source;
        private readonly bool _fromUrl;
        private readonly bool _strictMode;
        private readonly Action _onClosed;
        private readonly VideoView _videoView;
        private readonly ILogger<AvaloniaVideoService>? _logger;
        private readonly bool _withAudio;
        private readonly bool _isPrimary;
        private readonly Action<bool>? _onCodecWarning;
        private readonly Action<long>? _onPositionChanged;
        private readonly bool _segmentArmed;
        private readonly double _segmentFraction;
        private readonly double _segmentSec;
        private MediaPlayer? _mediaPlayer;
        private Media? _media;
        private bool _isPlaying;

        public VideoOverlayWindow(ISettingsService settings, LibVLC libVlc, ScreenInfo screen, string source, bool fromUrl, bool strictMode, Action onClosed, ILogger<AvaloniaVideoService>? logger, bool withAudio = true, bool isPrimary = false, Action<bool>? onCodecWarning = null, Action<long>? onPositionChanged = null, bool segmentArmed = false, double segmentFraction = 0, double segmentSec = 0)
        {
            _settings = settings;
            _libVlc = libVlc;
            _source = source;
            _fromUrl = fromUrl;
            _strictMode = strictMode;
            _onClosed = onClosed;
            _logger = logger;
            _withAudio = withAudio;
            _isPrimary = isPrimary;
            _onCodecWarning = onCodecWarning;
            _onPositionChanged = onPositionChanged;
            _segmentArmed = segmentArmed;
            _segmentFraction = segmentFraction;
            _segmentSec = segmentSec;

            WindowDecorations = WindowDecorations.None;
            Topmost = false;
            ShowInTaskbar = false;
            CanResize = false;
            ShowActivated = false;
            WindowState = WindowState.Normal;

            this.ConstrainToScreen(screen);

            _videoView = new VideoView
            {
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch
            };
            Content = _videoView;

            Opened += OnOpened;
            Closing += OnClosing;
            Closed += OnClosed;
            KeyDown += OnKeyDown;
        }

        public bool IsPlaying => _isPlaying;
        public MediaPlayer? MediaPlayer => _mediaPlayer;

        public void ApplyAudioSettings() => _mediaPlayer?.ApplyAudioSettings(_settings.Current, _withAudio, _logger);

        /// <summary>Volume/mute only — no output-device re-bind (live slider path).</summary>
        public void ApplyVolume() => _mediaPlayer?.ApplyVolume(_settings.Current, _withAudio);

        public event Action? VideoStarted;
        public event Action? VideoEnded;

        private void OnOpened(object? sender, EventArgs e)
        {
            try
            {
                _mediaPlayer = new MediaPlayer(_libVlc)
                {
                    EnableHardwareDecoding = true
                };
                _mediaPlayer.ApplyAudioSettings(_settings.Current, _withAudio, _logger);
                _mediaPlayer.EndReached += OnEndReached;
                _mediaPlayer.LengthChanged += OnLengthChanged;
                _mediaPlayer.Playing += OnPlaying;
                if (_isPrimary)
                {
                    _mediaPlayer.PositionChanged += OnPrimaryPositionChanged;
                }

                _media = _fromUrl
                    ? new Media(_libVlc, new Uri(_source))
                    : new Media(_libVlc, _source, FromType.FromPath);

                _videoView.MediaPlayer = _mediaPlayer;
                _mediaPlayer.Play(_media);

                // Apply random-segment seek if chaos armed (must run before first frame decodes)
                if (_isPrimary && _segmentArmed && _segmentSec > 0)
                {
                    try
                    {
                        var length = _mediaPlayer?.Length ?? 0;
                        if (length > 0)
                        {
                            var targetMs = (long)(_segmentFraction * (length - _segmentSec * 1000));
                            if (targetMs >= 0)
                            {
                                if (_mediaPlayer != null)
                            {
                                _mediaPlayer.Time = Math.Max(0, targetMs);
                            }
                            }
                        }
                    }
                    catch { /* no-op on seek failure */ }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "AvaloniaVideoService: failed to start video {Source}", _source);
                OnCodecWarningIfNeeded();
                Close();
            }
        }

        private void OnCodecWarningIfNeeded()
        {
            if (!_isPrimary || _onCodecWarning == null) return;
            _onCodecWarning(true);
        }

        private void OnPlaying(object? sender, EventArgs e)
        {
            _isPlaying = true;
            // Re-apply audio settings once playback is active; some LibVLC aout backends
            // only honor volume/output-device changes after the audio stream has started.
            _mediaPlayer?.ApplyAudioSettings(_settings.Current, _withAudio, _logger);
            VideoStarted?.Invoke();
        }

        private void OnPrimaryPositionChanged(object? sender, MediaPlayerPositionChangedEventArgs e)
        {
            if (_isPrimary && _onPositionChanged != null)
            {
                // MediaPlayer.Time is already milliseconds — the event contract is ms
                // (WS0 lot 4 fix V1-6/R1-5: the old *1000 emitted microseconds and broke
                // Deeper enhancement time rules by three orders of magnitude).
                Dispatcher.UIThread.Post(() => _onPositionChanged(_mediaPlayer?.Time ?? -1));
            }
        }

        private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
        {
            // Length is in ms.
        }

        private void OnEndReached(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _isPlaying = false;
                try { Close(); } catch { }
            });
        }

        private void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_strictMode && _isPlaying)
            {
                e.Cancel = true;
                return;
            }
            _isPlaying = false;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _isPlaying = false;
            try
            {
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.EndReached -= OnEndReached;
                    _mediaPlayer.LengthChanged -= OnLengthChanged;
                    _mediaPlayer.Playing -= OnPlaying;
                    _mediaPlayer.Stop();
                }
                _videoView.MediaPlayer = null;
                _mediaPlayer?.Dispose();
                _mediaPlayer = null;
                _media?.Dispose();
                _media = null;
            }
            catch { }
            VideoEnded?.Invoke();
            _onClosed();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (!_strictMode) return;
            if (e.Key == Key.Escape ||
                (e.Key == Key.Tab && (e.KeyModifiers == KeyModifiers.Alt || e.KeyModifiers == KeyModifiers.Control)) ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                e.Handled = true;
            }
        }
    }

    internal sealed class FloatingText
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private readonly Window _win;
        private readonly DispatcherTimer _timer;
        private readonly Action _onHit;
        private readonly LibVLC _libVlc;
        private readonly IAudioDeviceService? _audioDeviceService;
        private readonly string _baseDirectory;
        private readonly double _masterVolume;
        private double _x, _y, _vx, _vy;
        private readonly double _minX, _minY, _maxX, _maxY;
        private readonly double _width, _height;
        private bool _dead;
        private bool _clicked;

        public FloatingText(LibVLC libVlc, IAudioDeviceService? audioDeviceService, IAppEnvironment environment, AppSettings settings, ScreenInfo screen, string text, int size, Action onHit)
        {
            _libVlc = libVlc;
            _audioDeviceService = audioDeviceService;
            _baseDirectory = environment.BaseDirectory;
            _onHit = onHit;
            _masterVolume = settings.MasterVolume / 100.0;

            size = Math.Max(40, size);
            text = FormatTriggerText(text);

            var area = screen.WorkingArea;
            double areaX = area.X / screen.Scaling;
            double areaY = area.Y / screen.Scaling;
            double areaWidth = area.Width / screen.Scaling;
            double areaHeight = area.Height / screen.Scaling;

            var marginX = Math.Min(150, areaWidth * 0.08);
            var marginY = Math.Min(100, areaHeight * 0.08);
            _minX = areaX + marginX;
            _minY = areaY + marginY;
            _maxX = areaX + areaWidth - marginX;
            _maxY = areaY + areaHeight - marginY;

            Color color1, color2, textColor, borderColor;
            try
            {
                color1 = Color.Parse(settings.AttentionColor1);
                color2 = Color.Parse(settings.AttentionColor2);
                textColor = Color.Parse(settings.AttentionTextColor);
                borderColor = Color.Parse(settings.AttentionBorderColor);
            }
            catch
            {
                color1 = AppColor("DarkPinkColor", Color.FromRgb(255, 20, 147));
                color2 = AppColor("PinkColor", Color.FromRgb(255, 105, 180));
                textColor = AppColor("TextLight", Color.FromRgb(255, 20, 147));
                borderColor = AppColor("DarkPinkColor", Color.FromRgb(255, 20, 147));
            }

            var isFloating = settings.AttentionFloatingText;

            var typeface = new Typeface(
                string.IsNullOrWhiteSpace(settings.AttentionFont) ? FontFamily.Default : new FontFamily(settings.AttentionFont),
                FontStyle.Normal,
                FontWeight.Bold);

            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                size,
                Brushes.White);
            formattedText.TextAlignment = TextAlignment.Center;

            const double outlineThickness = 7.5;
            _width = formattedText.WidthIncludingTrailingWhitespace + outlineThickness * 2 + 60;
            _height = formattedText.Height + outlineThickness * 2 + 40;
            _width = Math.Max(_width, 150);
            _height = Math.Max(_height, 60);

            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = FontWeight.Bold,
                FontFamily = typeface.FontFamily,
                Foreground = new SolidColorBrush(textColor),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };

            var border = new Border
            {
                Background = isFloating ? null : new LinearGradientBrush
                {
                    GradientStops = new GradientStops { new GradientStop(color1, 0), new GradientStop(color2, 1) }
                },
                CornerRadius = isFloating ? new CornerRadius(0) : new CornerRadius(20),
                BorderBrush = (settings.AttentionShowBorder && !isFloating) ? new SolidColorBrush(borderColor) : null,
                BorderThickness = (settings.AttentionShowBorder && !isFloating) ? new Thickness(3) : new Thickness(0),
                Padding = isFloating ? new Thickness(0) : new Thickness(20, 10, 20, 10),
                Child = textBlock
            };

            var container = new Panel();
            var hitZone = new global::Avalonia.Controls.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
            };
            container.Children.Add(hitZone);
            container.Children.Add(border);

            _win = new Window
            {
                WindowDecorations = WindowDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = null,
                Topmost = true,
                ShowInTaskbar = false,
                CanResize = false,
                ShowActivated = false,
                Width = _width,
                Height = _height,
                Content = container,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            var spawnRangeX = Math.Max(0, (_maxX - _width) - _minX);
            var spawnRangeY = Math.Max(0, (_maxY - _height) - _minY);
            _x = _minX + Random.Shared.NextDouble() * spawnRangeX;
            _y = _minY + Random.Shared.NextDouble() * spawnRangeY;
            _x = Math.Clamp(_x, _minX, Math.Max(_minX, _maxX - _width));
            _y = Math.Clamp(_y, _minY, Math.Max(_minY, _maxY - _height));

            var angle = Random.Shared.NextDouble() * Math.PI * 2;
            _vx = Math.Cos(angle) * 3.0;
            _vy = Math.Sin(angle) * 3.0;

            _win.Position = new PixelPoint((int)_x, (int)_y);

            _win.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(_win).Properties.IsLeftButtonPressed)
                {
                    e.Handled = true;
                    Hit();
                }
            };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) =>
            {
                if (_dead) return;
                _x += _vx;
                _y += _vy;
                if (_x < _minX) { _x = _minX; _vx = Math.Abs(_vx); }
                if (_x + _width > _maxX) { _x = _maxX - _width; _vx = -Math.Abs(_vx); }
                if (_y < _minY) { _y = _minY; _vy = Math.Abs(_vy); }
                if (_y + _height > _maxY) { _y = _maxY - _height; _vy = -Math.Abs(_vy); }
                _win.Position = new PixelPoint((int)_x, (int)_y);
            };

            _win.Opened += (s, e) =>
            {
                ApplyToolWindowStyle(_win);
                _timer.Start();
            };

            _win.Show();
        }

        public void BringToFront()
        {
            if (_dead) return;
            try { _win.Topmost = true; }
            catch { }
        }

        public void Destroy()
        {
            if (_dead) return;
            _dead = true;
            _timer.Stop();
            try { _win.Close(); } catch { }
        }

        internal void Hit()
        {
            if (_clicked || _dead) return;
            _clicked = true;
            PlayPopSound();
            try { _onHit?.Invoke(); } catch { }
            FadeOut();
        }

        private void FadeOut()
        {
            _timer.Stop();
            var fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            fade.Tick += (_, _) =>
            {
                _win.Opacity -= 0.15;
                if (_win.Opacity <= 0.1)
                {
                    fade.Stop();
                    Destroy();
                }
            };
            fade.Start();
        }

        private void PlayPopSound()
        {
            try
            {
                var volume = (float)(0.6 * _masterVolume);
                App.Services?.GetService<ISfxPlayer>()?.Play("pop", volume);
            }
            catch { }
        }

        private static string FormatTriggerText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 2) return $"{words[0]}\n{words[1]}";
            if (words.Length >= 4)
            {
                int mid = words.Length / 2;
                return $"{string.Join(" ", words.Take(mid))}\n{string.Join(" ", words.Skip(mid))}";
            }
            return text;
        }

        private static Color AppColor(string key, Color fallback)
        {
            if (global::Avalonia.Application.Current?.TryGetResource(key, global::Avalonia.Styling.ThemeVariant.Default, out var v) == true && v is Color c)
                return c;
            return fallback;
        }

        private static void ApplyToolWindowStyle(Window window)
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd == IntPtr.Zero) return;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_TOOLWINDOW;
                exStyle &= ~WS_EX_APPWINDOW;
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
            }
            catch { }
        }
    }
}
