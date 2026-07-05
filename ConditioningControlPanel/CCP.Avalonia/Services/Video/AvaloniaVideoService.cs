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
using ConditioningControlPanel.Avalonia.Chaos;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Services.Overlays;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Companion;
using ConditioningControlPanel.Core.Services.Content;
using ConditioningControlPanel.Core.Services.Flash;
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
    private readonly IModService? _mods;
    private readonly IAchievementService? _achievements;
    private readonly IProgressionService? _progression;
    private readonly ILogger<AvaloniaVideoService>? _logger;
    private readonly IInputHook? _inputHook;
    private readonly IContentPackService? _contentPacks;
    private readonly ISystemAudioDucker? _audioDucker;
    private readonly IFlashService? _flash;
    private readonly IBubbleService? _bubbles;
    private readonly ICompanionService? _companion;
    private readonly Random _random = new();
    private readonly object _sync = new();
    private readonly List<string> _videoFiles = new();
    private readonly List<FloatingText> _attentionTargets = new();
    private readonly List<double> _attentionSpawnTimes = new();
    private readonly List<Window> _messageWindows = new();
    private readonly CompositorEngine? _compositor;
    private readonly VideoLayer? _videoLayer;
    private readonly MandatoryVideoLayer? _mandatoryVideoLayer;

    private CancellationTokenSource? _cts;
    private DispatcherTimer? _scheduledTimer;
    private DispatcherTimer? _safetyTimer;
    // Hard cap on a single mandatory video's runtime (VideoMaxDurationSeconds). When it fires the
    // video is stopped and the overlay torn down — it never chains into another video. This is the
    // "max length" contract: no mandatory video may stay on screen longer than the cap, even if a
    // too-long clip slipped through the (cold-cache) duration filter.
    private DispatcherTimer? _maxDurationTimer;
    // True while any video (the mandatory or URL compositor layer) is on screen.
    // Guards the scheduler so it never preempts a playing video with the next scheduled one.
    private bool _isVideoActive;
    private string? _currentRetryPath;
    private bool _currentStrictMode;
    private double _currentDurationSeconds;
    private DateTime _videoStartTime;
    private int _attentionHits;
    private int _attentionSpawned;
    private int _attentionTotal;
    private int _attentionPenalties;
    // B2 parity (WPF Cleanup :2620 / ForceCleanup :895): true ONLY during an attention-fail →
    // retry bridge — from the fail-message ShowMessage teardown until the retry callback's
    // PlayFile has run its (penalty-preserving) CleanupInternal. CleanupInternal reads it to
    // decide whether to reset _attentionPenalties: the SAME obligation retrying must keep its
    // tally (mercy-at-3 across retries, WPF :2168-2170 — a naive reset in every teardown would
    // break this), while every other teardown (natural end, mercy fired, user/system stop,
    // max-duration cap, ForceCleanup, natural URL end) ends the obligation and resets. WPF's
    // retry path calls CloseAll (NOT Cleanup), so its penalty tally survives the retry bridge;
    // _retryPending is the Avalonia equivalent of that distinction.
    private bool _retryPending;
    private DispatcherTimer? _attentionTimer;
    private bool _isDisposed;
    private bool _codecWarningShown;
    // True while CleanupInternal tears down active video windows. Read by the post-session
    // summary defer (#462) so the modal never opens over a still-closing fullscreen video.
    // volatile: set on the UI thread, read from timer ticks; cleared in a finally.
    private volatile bool _isCleaningUp;

    // ---- WPF-parity playback state (WS0 lot 4 wave 3) ----
    // Shuffled no-repeat queues (WPF GetNextVideo/RefillVideoQueues, VideoService.cs:2628-2810):
    // each queue is consumed until empty, then refilled with a fresh Fisher-Yates shuffle.
    private Queue<string> _videoQueue = new();
    private Queue<(string PackId, PackFileEntry File)> _packVideoQueue = new();
    private readonly List<string> _tempPackFiles = new();
    // Duck guard so retries/attention loops never double-duck (WPF VideoService.cs:1156-1158).
    private bool _duckedForVideo;
    // Position-based watch-credit watermark (WPF FinalizeWatchCredit, VideoService.cs:2313-2340).
    private long _lastWatchPositionMs;
    private double _creditedWatchSeconds;
    // Invalidates a pending 1.3s pre-announce start when Stop/cleanup runs during the delay.
    private int _playRequestVersion;
    private DispatcherTimer? _preAnnounceTimer;

    // ---- chaos random-segment mode (one-shot) ----
    // The NEXT triggered video jumps to a random position leaving at least _segmentSec of
    // runway (the chaos 15s cap then ends it — so the player sees a random 15s slice, not
    // always the opening). One shared fraction keeps multi-monitor mirrors in sync. Armed
    // immediately before TriggerVideo by the chaos VideoPayload; disarmed in CloseAll.
    private double _segmentSec;
    private double _segmentFraction;
    private DateTime _segmentArmedAtUtc = DateTime.MinValue;
    private bool SegmentArmed => (DateTime.UtcNow - _segmentArmedAtUtc).TotalSeconds < 30;

    // ---- Phase B2: UCE layer-path orchestration state ----
    // True from the mandatory layer's first LibVLC Playing event (orchestration armed: safety
    // timer + attention scheduling running) until CleanupInternal tears the playback down.
    // Triple duty: double-run guard (LibVLC re-fires Playing after an unpause), stale-post
    // rejection (a VideoEnded post landing after a Stop()/panic teardown must not re-enter the
    // end pipeline — WPF OnEnded's !_videoPlaying guard), and the safety timer's layer-path
    // liveness check.
    private bool _layerOrchestrationActive;
    // Real media length reported by the layer's LengthChanged, in ms; -1 while unknown.
    private long _layerLengthMs = -1;
    // One-shot chaos random-segment state captured at layer playback start; the seek itself
    // runs from OnCompositorVideoLengthKnown once the real duration is known.
    private bool _pendingSegmentArmed;
    private double _pendingSegmentFraction;
    private double _pendingSegmentSec;
    private bool _pendingSegmentSeekApplied;

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
        CompositorEngine? compositor = null,
        IContentPackService? contentPacks = null,
        ISystemAudioDucker? audioDucker = null,
        IFlashService? flash = null,
        IBubbleService? bubbles = null,
        ICompanionService? companion = null,
        IInputHook? inputHook = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _interactionQueue = interactionQueue ?? throw new ArgumentNullException(nameof(interactionQueue));
        _libVlc = libVlc ?? throw new ArgumentNullException(nameof(libVlc));
        SharedLibVLC = _libVlc;
        _metadataCache = metadataCache ?? throw new ArgumentNullException(nameof(metadataCache));
        _ = audioDeviceService; // retained for ctor compatibility; no longer used directly
        _mods = mods;
        _achievements = achievements;
        _progression = progression;
        _logger = logger;
        _overlay = overlay;
        _compositor = compositor;
        _contentPacks = contentPacks;
        _audioDucker = audioDucker;
        _flash = flash;
        _bubbles = bubbles;
        _companion = companion;
        // UCE plan Phase E3: compositor video (VideoLayer/MandatoryVideoLayer) is the ONLY
        // video path — the legacy multi-monitor / per-window path was deleted here. The
        // compositor engine is registered unconditionally on every head, so this is always
        // true and the layers are always created.
        var useCompositorVideo = compositor != null;
        _videoLayer = useCompositorVideo ? new VideoLayer(libVlc, _logger) : null;
        _mandatoryVideoLayer = useCompositorVideo ? new MandatoryVideoLayer(libVlc, _logger) : null;
        if (_videoLayer != null)
        {
            _compositor?.RegisterLayer(_videoLayer);
            _videoLayer.VideoStarted += (_, _) => OnCompositorVideoStarted(mandatory: false);
            _videoLayer.VideoEnded += (_, _) => OnCompositorVideoEnded();
            // B2 review F1: a failed open/decode (corrupt file, missing codec) never fires
            // Playing/EndReached — without this the slot wedges (_isVideoActive stays true)
            // and the scheduler skips every later video. WPF routes EncounteredError → OnEnded
            // (VideoService.cs:1498-1511); we stop the errored layer, then run the same end
            // pipeline (evaluation when orchestrated, unwedge + VideoEnded otherwise).
            _videoLayer.EncounteredError += (_, _) => { try { _videoLayer.Stop(); } catch { } OnCompositorVideoEnded(); };
            // B2 residual (layer-path watch-position credit): feed the throttled layer relay
            // into the live watermark + PrimaryPlaybackTimeMsChanged — the same contract the
            // window path fulfills via VideoOverlayWindow's onPositionChanged (WPF mandatory
            // AND URL players both wire TimeChanged, VideoService.cs:1004/:1383). The URL
            // layer has no orchestration but still credits watch time and feeds Deeper.
            _videoLayer.PlaybackTimeChanged += (_, ms) => OnCompositorVideoTimeChanged(ms);
        }
        if (_mandatoryVideoLayer != null)
        {
            _compositor?.RegisterLayer(_mandatoryVideoLayer);
            _mandatoryVideoLayer.VideoStarted += (_, _) => OnCompositorVideoStarted(mandatory: true);
            _mandatoryVideoLayer.VideoEnded += (_, _) => OnCompositorVideoEnded();
            _mandatoryVideoLayer.LengthKnown += (_, lengthMs) => OnCompositorVideoLengthKnown(lengthMs);
            // B2 review F1: see the video-layer note above — same failed-open unwedge contract.
            _mandatoryVideoLayer.EncounteredError += (_, _) => { try { _mandatoryVideoLayer.Stop(); } catch { } OnCompositorVideoEnded(); };
            // B2 residual: see the _videoLayer note above — identical watermark + seam feed.
            _mandatoryVideoLayer.PlaybackTimeChanged += (_, ms) => OnCompositorVideoTimeChanged(ms);
        }

        // UCE video input (UCE plan Phase E prerequisite): the compositor video layers render
        // in a click-through / no-activate window that receives NO keyboard input, so the
        // legacy VideoOverlayWindow.OnKeyDown ESC-dismiss / panic-key contract has no receiver
        // on the layer path. Route those keys through the global low-level keyboard hook.
        _inputHook = inputHook;
        if (_inputHook != null)
            _inputHook.KeyPressed += OnCompositorVideoKey;

        RefreshVideosPath();
    }

    public bool IsRunning { get; private set; }
    // UCE plan Phase E3: video plays only through the UCE layers now (the window path was
    // deleted), so IsPlaying reflects the layers alone. Consumers (interaction queue,
    // session-summary defer via the HasOpenVideoWindows DIM (#462), scheduler guards) rely on
    // this reading true for the full life of a compositor video.
    public bool IsPlaying => _videoLayer?.IsPlaying == true
        || _mandatoryVideoLayer?.IsPlaying == true;
    // Mirrors WPF IsStrictActive => _videoPlaying && _strictActive (VideoService.cs:182). The
    // compositor window is permanently click-through/no-activate and receives no keyboard, so
    // strict ESC/panic blocking is inherently satisfied on this path (see the PlaySpecificVideo
    // strict-mode note at the _currentStrictMode assignment); this flag only reports the state
    // for the avatar quick-menu engine-stop guard (#479). _currentStrictMode is set when a strict
    // video starts and reset on every cleanup path, so this reads true only while one is playing.
    public bool IsStrictActive => IsPlaying && _currentStrictMode;
    // Real teardown/open-surface state for the #462 summary defer. With no video windows left,
    // this tracks the compositor layers: it reads true while either layer is playing so the
    // post-session summary can't pop over a still-active video surface.
    public bool IsCleaningUp => _isCleaningUp;
    public bool HasOpenVideoWindows => IsPlaying;
    public string? LastVideoTitle => string.IsNullOrEmpty(_currentRetryPath) ? null : Path.GetFileNameWithoutExtension(_currentRetryPath);
    public string? LastVideoPath => _currentRetryPath;
    public int PlaythroughFailCount => _attentionPenalties;

    /// <summary>Verification probe (--verify-video, non-gating): true while the Phase B2
    /// layer-path orchestration (safety timer + attention scheduling) is armed for the
    /// mandatory UCE video layer. Cleared by CleanupInternal.</summary>
    public bool LayerOrchestrationArmed => _layerOrchestrationActive;

    /// <summary>
    /// Primary (audio-bearing) media player. Always null since UCE plan Phase E3 removed the
    /// per-window video path: the compositor VideoLayer/MandatoryVideoLayer own their LibVLC
    /// players internally, so no window-level player is exposed. Deeper enhancement time rules
    /// read playback position via the <see cref="PrimaryPlaybackTimeMsChanged"/> seam instead.
    /// </summary>
    public MediaPlayer? PrimaryMediaPlayer => null;

    /// <summary>
    /// Primary video window (audio monitor). Always null since UCE plan Phase E3 removed the
    /// per-window video path; the compositor renders video into its own topmost surface.
    /// </summary>
    public Window? PrimaryVideoWindow => null;

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
        // Phase B2: a video started via PlaySpecificVideo/remote command (IsRunning=false) must
        // still be torn down when it plays through the UCE layers.
        if (!IsRunning && !_layerOrchestrationActive
            && _videoLayer?.IsPlaying != true && _mandatoryVideoLayer?.IsPlaying != true) return;
        IsRunning = false;

        _scheduledTimer?.Stop();
        _scheduledTimer = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        Dispatcher.UIThread.Invoke(() =>
        {
            // B2 parity: a user/app Stop ends the current obligation (WPF Stop→CloseAll is a
            // final teardown; penalties is reset on the next Cleanup). Clear the retry bridge
            // flag so CleanupInternal's penalty reset applies.
            _retryPending = false;
            CleanupInternal(notifyEnded: false);
        });

        // WPF ForceCleanup parity (VideoService.cs:888): a hard stop resets the duck
        // refcount immediately instead of releasing a single reference.
        _duckedForVideo = false;
        try { _audioDucker?.ForceUnduck(); } catch { /* best effort */ }
        _logger?.LogInformation("AvaloniaVideoService stopped");
    }

    public void RefreshVideosPath()
    {
        LoadVideoFiles();
        // Invalidate the shuffled queues so the next pick refills from the new file set.
        lock (_sync)
        {
            _videoQueue.Clear();
            _packVideoQueue.Clear();
        }
    }

    public void PlaySpecificVideo(string videoPath, bool strictMode)
    {
        if (_isDisposed) return;
        if (string.IsNullOrWhiteSpace(videoPath)) return;
        // Defense-in-depth (repo CLI-boundary policy): reject UNC (\\server) and
        // extended-length (\\?\) paths before any filesystem probe.
        if (videoPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            _logger?.LogWarning("AvaloniaVideoService: PlaySpecificVideo rejected UNC/extended-length path");
            return;
        }
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
        // Defense-in-depth (repo CLI-boundary policy): reject UNC and extended-length paths.
        if (url.StartsWith(@"\\", StringComparison.Ordinal))
        {
            _logger?.LogWarning("AvaloniaVideoService: PlayUrl rejected UNC/extended-length path");
            return;
        }
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
            // B2 parity (WPF ForceCleanup :895 resets _penalties): panic / session-lock /
            // power-suspend end the obligation — clear the retry bridge flag so CleanupInternal
            // resets the penalty tally.
            _retryPending = false;
            CleanupInternal(notifyEnded: true);
            try { _interactionQueue.CompleteIfCurrent("Video"); } catch { /* best effort */ }
            // #5/#14 parity (WPF ForceCleanup VideoService.cs:916): release the Video interaction
            // slot ONLY if we still hold it, so a BubbleCount/LockCard that took over isn't cleared.
            // ForceReset() (the prior Avalonia behavior) nuked the whole queue on every video
            // teardown — a divergence; CompleteIfCurrent is the WPF-faithful surgical release.
            // WPF ForceCleanup parity (VideoService.cs:888): reset the duck refcount.
            _duckedForVideo = false;
            try { _audioDucker?.ForceUnduck(); } catch { /* best effort */ }
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

        files = ApplyDisabledAssetFilter(files);
        files = ApplyDurationFilter(files);

        lock (_sync)
        {
            _videoFiles.Clear();
            _videoFiles.AddRange(files);
        }
    }

    /// <summary>
    /// Removes user-disabled videos from the folder-sourced list (blacklist; WPF
    /// RefillVideoQueues parity, VideoService.cs:2733-2755). Keys use the relative-path
    /// format written by the Assets tab (forward slashes, relative to the assets root)
    /// and compare case-insensitively so Windows-cased paths can't slip past.
    /// </summary>
    private List<string> ApplyDisabledAssetFilter(List<string> files)
    {
        var disabledPaths = _settings.Current?.DisabledAssetPaths;
        if (disabledPaths == null || disabledPaths.Count == 0) return files;

        static string Norm(string p) => p.Replace('\\', '/');
        var basePath = _environment.EffectiveAssetsPath;
        var disabled = new HashSet<string>(disabledPaths.Select(Norm), StringComparer.OrdinalIgnoreCase);
        var beforeCount = files.Count;
        var filtered = files.Where(f => !disabled.Contains(Norm(Path.GetRelativePath(basePath, f)))).ToList();
        if (filtered.Count != beforeCount)
        {
            _logger?.LogDebug("AvaloniaVideoService: {Before} -> {After} after disabled-asset filter",
                beforeCount, filtered.Count);
        }
        return filtered;
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
            // Pack-only libraries are valid: the queues refill lazily from active content
            // packs even when the videos folder is empty (WPF GetNextVideo parity).
            if (_videoFiles.Count == 0 && _contentPacks == null) return;
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
        var file = GetNextVideo();
        if (file == null)
        {
            _logger?.LogDebug("AvaloniaVideoService: PlayRandomVideo called but no videos are available");
            return;
        }
        var strict = _settings.Current?.StrictLockEnabled ?? false;
        Dispatcher.UIThread.Invoke(() => PlayFile(file, strict));
    }

    /// <summary>
    /// Dequeues the next video from the shuffled queues (WPF GetNextVideo parity,
    /// VideoService.cs:2628-2670): refill when both queues are empty, weight the
    /// regular-vs-pack choice by remaining count, and decrypt pack entries to a
    /// tracked temp file. Returns null when no videos are available at all.
    /// </summary>
    private string? GetNextVideo()
    {
        bool needRefill;
        lock (_sync)
        {
            needRefill = _videoQueue.Count == 0 && _packVideoQueue.Count == 0;
        }
        if (needRefill)
        {
            RefillVideoQueues();
        }

        string? regular = null;
        (string PackId, PackFileEntry File)? pack = null;
        lock (_sync)
        {
            if (_videoQueue.Count == 0 && _packVideoQueue.Count == 0) return null;

            bool usePackVideo = false;
            if (_videoQueue.Count > 0 && _packVideoQueue.Count > 0)
            {
                // Both available — pick randomly, weighted by remaining count.
                var totalCount = _videoQueue.Count + _packVideoQueue.Count;
                usePackVideo = _random.Next(totalCount) >= _videoQueue.Count;
            }
            else if (_packVideoQueue.Count > 0)
            {
                usePackVideo = true;
            }

            if (usePackVideo && _packVideoQueue.Count > 0)
            {
                pack = _packVideoQueue.Dequeue();
            }
            else if (_videoQueue.Count > 0)
            {
                regular = _videoQueue.Dequeue();
            }
        }

        if (pack is { } packVideo)
        {
            // Decrypt the pack video to a temp file; tracked for cleanup on refill/dispose.
            var tempPath = _contentPacks?.GetPackFileTempPath(packVideo.PackId, packVideo.File);
            if (!string.IsNullOrEmpty(tempPath))
            {
                _tempPackFiles.Add(tempPath);
                _logger?.LogDebug("AvaloniaVideoService: using pack video {Name} from pack {PackId}",
                    packVideo.File.OriginalName, packVideo.PackId);
                return tempPath;
            }
            // Decryption failed — fall back to the regular queue (WPF GetNextVideo contract).
            lock (_sync)
            {
                return _videoQueue.Count > 0 ? _videoQueue.Dequeue() : null;
            }
        }

        return regular;
    }

    /// <summary>
    /// Refills both shuffled queues (WPF RefillVideoQueues parity, VideoService.cs:2674-2810):
    /// rescans the videos folder (disabled-asset + duration filters applied in
    /// <see cref="LoadVideoFiles"/>), Fisher-Yates-shuffles the result, and appends the
    /// active content packs' videos as a separately shuffled queue. The pack service applies
    /// its own DisabledAssetPaths (pack:{id}/{name}) filter internally.
    /// </summary>
    private void RefillVideoQueues()
    {
        CleanupTempPackFiles();
        LoadVideoFiles();

        List<string> files;
        lock (_sync)
        {
            files = new List<string>(_videoFiles);
        }
        ShuffleList(files);

        var packVideos = new List<(string PackId, PackFileEntry File)>();
        if (_contentPacks != null)
        {
            try
            {
                packVideos = _contentPacks.GetAllActivePackVideos();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("AvaloniaVideoService: failed to enumerate pack videos: {Error}", ex.Message);
            }
        }
        ShuffleList(packVideos);

        lock (_sync)
        {
            _videoQueue = new Queue<string>(files);
            _packVideoQueue = new Queue<(string PackId, PackFileEntry File)>(packVideos);
        }

        _logger?.LogInformation("AvaloniaVideoService: queues refilled — {RegularCount} regular videos, {PackCount} pack videos",
            files.Count, packVideos.Count);
    }

    /// <summary>Fisher-Yates shuffle for reliable randomization (WPF ShuffleList parity).</summary>
    private void ShuffleList<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Best-effort deletion of decrypted pack temp files (WPF CleanupTempPackFiles parity).</summary>
    private void CleanupTempPackFiles()
    {
        foreach (var tempFile in _tempPackFiles)
        {
            try
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("AvaloniaVideoService: failed to delete temp pack file: {Error}", ex.Message);
            }
        }
        _tempPackFiles.Clear();
    }

    public void UpdateVolume()
    {
        // WPF contract (VideoService.UpdatePlayingVideosVolume): a live volume change only
        // writes Volume to active players. Re-applying the output device here would re-bind
        // the audio endpoint mid-playback on every slider drag (WS0 lot 4 V1-22).
        // Phase B audio parity: live volume updates reach the UCE layers (volume/mute only —
        // no output-device re-bind, same WPF contract as above).
        if (_videoLayer?.IsPlaying == true) _videoLayer.ApplyVolume(_settings.Current);
        if (_mandatoryVideoLayer?.IsPlaying == true) _mandatoryVideoLayer.ApplyVolume(_settings.Current);
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
        // Phase B2: when the mandatory layer ran the full orchestration, its natural end runs
        // the same evaluation body as the window path — attention pass/fail (XP, penalties,
        // retry loop, mercy) — and OnVideoEnded's CleanupInternal owns the teardown symmetry
        // (attention/safety/max-duration timers, targets, unduck, bubbles, VideoEnded, flash
        // restart, scheduler guard). _layerOrchestrationActive also rejects stale VideoEnded
        // posts that land after a Stop()/panic teardown (WPF OnEnded's !_videoPlaying guard,
        // VideoService.cs:2126) so the end pipeline can never run twice.
        if (_layerOrchestrationActive)
        {
            OnVideoEnded();
            return;
        }

        // B2 review F4: stale natural-end/error posts that land AFTER a teardown
        // (CleanupInternal already ran and cleared the slot) must not re-emit VideoEnded
        // to AvatarTube/Autonomy or re-resume bubbles — WPF OnEnded's guard returns with
        // nothing (VideoService.cs:2120-2130). Also makes EncounteredError + EndReached
        // double-delivery idempotent.
        if (!_isVideoActive) return;

        _isVideoActive = false;
        _maxDurationTimer?.Stop();
        _maxDurationTimer = null;
        FinalizeWatchCredit();
        // B2 parity: a natural URL-layer end is an obligation end — reset the penalty tally
        // (mirrors the window/multi-monitor URL paths, which route through CleanupInternal).
        // WPF's URL EndReached calls CloseAll only (VideoService.cs:1029-1032) and does NOT
        // reset _penalties; resetting here is the deliberate parity FIX (the obligation-end
        // contract), applied uniformly to both Avalonia paths.
        if (!_retryPending)
            _attentionPenalties = 0;
        if (_duckedForVideo)
        {
            _duckedForVideo = false;
            try { _audioDucker?.Unduck(); } catch { /* best effort */ }
        }
        try { _bubbles?.Resume(); } catch { /* best effort */ }
        VideoEnded?.Invoke(this, EventArgs.Empty);
        if (IsRunning && _settings.Current?.FlashEnabled == true)
        {
            try { _flash?.Start(); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Compositor-layer playback start (LibVLC Playing, marshaled to the UI thread by the
    /// layer). Phase B2: the mandatory layer runs the full orchestration body — start
    /// timestamp, VideoStarted, duration
    /// bookkeeping, interaction-queue timeout, safety timer, attention scheduling. The URL
    /// layer only raises VideoStarted: WPF PlayUrl is "No attention checks, no strict mode -
    /// just playback" (VideoService.cs:900-903).
    /// </summary>
    private void OnCompositorVideoStarted(bool mandatory)
    {
        if (!mandatory)
        {
            // URL layer: WPF PlayUrl is "no attention checks, no strict mode - just playback"
            // (VideoService.cs:900-903); no orchestration, so no double-run bookkeeping.
            _videoStartTime = DateTime.Now;
            VideoStarted?.Invoke(this, EventArgs.Empty);
            return;
        }

        // B2 review F2: guard BEFORE any side effect. LibVLC re-fires Playing after an
        // unpause/seek bounce; the start timestamp, the public VideoStarted event (session
        // log dedupe), and the attention-spawn clock must fire exactly once per video.
        if (_layerOrchestrationActive) return;
        _layerOrchestrationActive = true;
        _videoStartTime = DateTime.Now;
        VideoStarted?.Invoke(this, EventArgs.Empty);

        _overlay?.NotifyTopWindowOpened();

        var settings = _settings.Current;
        if (settings == null) return;

        // LengthChanged usually precedes Playing; fall back to a live player read, then to the
        // 600s safety default until OnCompositorVideoLengthKnown upgrades it (WPF fallback→
        // accurate safety-timer contract, VideoService.cs:1211/1437).
        var lengthMs = _layerLengthMs > 0 ? _layerLengthMs : (_mandatoryVideoLayer?.DurationMs ?? -1);
        _currentDurationSeconds = lengthMs > 0 ? lengthMs / 1000.0 : 0;

        BeginPlaybackOrchestration(settings);
    }

    /// <summary>
    /// Live playback-time relay from a UCE video layer (B2 residual). Mirrors the window path's
    /// <c>onPositionChanged</c> callback (CreateWindow): keep the furthest-reached position as
    /// the watch-credit watermark (FinalizeWatchCredit credits <c>_lastWatchPositionMs</c>) and
    /// fan out the Core seam <see cref="PrimaryPlaybackTimeMsChanged"/> for the Deeper
    /// enhancement time rules (VideoServiceTimeSource). WPF mandatory AND URL players both wire
    /// LibVLC <c>TimeChanged</c> this way (VideoService.cs:1004/:1383, #447). The layer
    /// throttles to ~1/s; FinalizeWatchCredit reads CurrentTimeMs directly at teardown so the
    /// throttle never under-credits. Runs on the UI thread (the layer marshals the relay).
    /// </summary>
    private void OnCompositorVideoTimeChanged(long ms)
    {
        if (ms > _lastWatchPositionMs) _lastWatchPositionMs = ms;
        try { PrimaryPlaybackTimeMsChanged?.Invoke(ms); } catch { /* no-op if no subscribers */ }
    }

    /// <summary>
    /// Real media length from the mandatory layer (ms, UI thread). Phase B2, WPF LengthChanged
    /// parity (VideoService.cs:1387-1451): upgrade the fallback safety timeout to the accurate
    /// duration, refresh the interaction-queue stuck timeout, and apply the one-shot chaos
    /// random-segment seek — deferred ~700ms until the player is rolling, exactly like
    /// VideoOverlayWindow.OnLengthChanged. SetupAttention runs 2s after start and reads the
    /// upgraded _currentDurationSeconds when it fires, matching the WPF ordering.
    /// </summary>
    private void OnCompositorVideoLengthKnown(long lengthMs)
    {
        if (lengthMs <= 0) return;
        _layerLengthMs = lengthMs;

        if (_layerOrchestrationActive)
        {
            _currentDurationSeconds = lengthMs / 1000.0;
            _interactionQueue.ExtendTimeout(TimeSpan.FromSeconds(Math.Max(60, _currentDurationSeconds + 30)));
            StartSafetyTimer(_currentDurationSeconds + 30);
        }

        if (!_pendingSegmentArmed || _pendingSegmentSeekApplied || _pendingSegmentSec <= 0) return;
        var segMs = (long)(_pendingSegmentSec * 1000);
        if (lengthMs <= segMs) return;
        var startMs = (long)((lengthMs - segMs) * _pendingSegmentFraction);
        if (startMs <= 500) return;
        _pendingSegmentSeekApplied = true;
        StartOneShotTimer(TimeSpan.FromMilliseconds(700), () =>
        {
            var layer = _mandatoryVideoLayer;
            if (layer == null || !layer.IsPlaying) return;
            layer.SeekTo(startMs);
            _logger?.LogInformation("AvaloniaVideoService: random segment — seeking to {Start}s of {Len}s (UCE layer)",
                startMs / 1000, lengthMs / 1000);
        });
    }

    private void PlayFile(string filePath, bool strictMode)
    {
        if (_isDisposed) return;
        CleanupInternal(notifyEnded: false);

        // Mark the slot active immediately (before the pre-announce delay) so the scheduler's
        // in-flight guard holds during the 1.3s window. Cleared in CleanupInternal.
        _isVideoActive = true;

        // WPF PlayVideo contract (VideoService.cs:1141-1170): announce the incoming video,
        // stop flashes, duck other apps, then delay playback 1.3s so the avatar/companion
        // can announce it. PlayUrl deliberately skips all of this (WPF parity).
        VideoAboutToStart?.Invoke(this, EventArgs.Empty);
        try { _flash?.Stop(); } catch { /* best effort */ }

        var settings = _settings.Current;
        if (settings?.AudioDuckingEnabled == true && !_duckedForVideo)
        {
            _duckedForVideo = true;
            try { _audioDucker?.Duck(settings.DuckingLevel); } catch { /* best effort */ }
        }

        var version = ++_playRequestVersion;
        _preAnnounceTimer?.Stop();
        _preAnnounceTimer = StartOneShotTimer(TimeSpan.FromSeconds(1.3), () =>
        {
            _preAnnounceTimer = null;
            // Stop()/cleanup during the delay bumps the version — never start a stale video.
            if (_isDisposed || version != _playRequestVersion) return;
            StartVideoPlayback(filePath, strictMode);
        });
    }

    private void StartVideoPlayback(string filePath, bool strictMode)
    {
        // Arm the hard cap so no mandatory video can overrun VideoMaxDurationSeconds.
        // Cleared in CleanupInternal.
        StartMaxDurationTimer();

        _compositor?.Start();
        if (_mandatoryVideoLayer != null)
        {
            // Phase B2: the UCE layer path runs the same interaction-queue + orchestration
            // contract as the legacy branches below. TryStart marks the Video interaction
            // in-flight (so queued interactions can't start mid-video and the orchestration's
            // ExtendTimeout has an interaction to extend); the playback state CreateWindow
            // seeds on the window path is seeded here for the layer.
            _interactionQueue.TryStart("Video", () =>
            {
                _currentRetryPath = filePath;
                // Phase B2: strict mode on the UCE path. The legacy paths use strictMode for
                // window-level key handling (strict: block ESC/panic/Alt+F4 while playing;
                // non-strict: ESC dismisses, panic force-stops). The compositor window is
                // permanently click-through + no-activate and receives no keyboard input, so
                // strict blocking is inherently satisfied (no key can dismiss the video);
                // the non-strict ESC-dismiss/panic keys are a documented gap until a global
                // key hook feeds this path (see unified-compositor-engine-plan.md Phase B).
                // NOTE (B2 review F6a, truthful): _currentStrictMode is recorded for parity
                // bookkeeping, but attention-fail retries do NOT currently inherit it —
                // CleanupInternal resets it to false before the retry callback reads it (same
                // pre-existing behavior as the Avalonia window path; WPF retries inherit
                // _strictActive, VideoService.cs:2186). Deferred — see plan-doc B2 residuals.
                _currentStrictMode = strictMode;
                _currentDurationSeconds = 0;
                _attentionHits = 0;
                _attentionSpawned = 0;
                _attentionTotal = 0;
                _attentionSpawnTimes.Clear();
                lock (_attentionTargets) { _attentionTargets.Clear(); }
                _layerOrchestrationActive = false;
                _layerLengthMs = -1;

                // Chaos random segment: capture-and-disarm exactly like the window branch
                // below (one-shot per video). The seek itself runs from
                // OnCompositorVideoLengthKnown once the real duration is known.
                _pendingSegmentArmed = SegmentArmed && _segmentSec > 0;
                _pendingSegmentFraction = _pendingSegmentArmed ? _segmentFraction : 0;
                _pendingSegmentSec = _pendingSegmentArmed ? _segmentSec : 0;
                _pendingSegmentSeekApplied = false;
                _segmentArmedAtUtc = DateTime.MinValue;

                _mandatoryVideoLayer.PlayVideo(filePath, withAudio: true, loop: false);
                // Phase B audio parity: apply volume/mute/output device to the layer's player.
                _mandatoryVideoLayer.ApplyAudioSettings(_settings.Current);
                // Ambient bubbles pause + clear so they don't fight the video for clicks or
                // z-order (WPF StartVideoPlayback, VideoService.cs:1270).
                try { _bubbles?.PauseAndClear(); } catch { /* best effort */ }
            });
            return;
        }

        // UCE plan Phase E3: the compositor mandatory video layer is the only video path (the
        // legacy multi-monitor / per-window branches were deleted here). The compositor engine
        // is registered on every head, so _mandatoryVideoLayer is only ever null in a degenerate
        // configuration — log rather than silently drop the request; do NOT recreate a window.
        _logger?.LogWarning("AvaloniaVideoService: no compositor video layer available; mandatory video not played");
    }

    private void PlayUrlCore(string url)
    {
        if (_isDisposed) return;
        CleanupInternal(notifyEnded: false);

        // Chaos/URL-triggered videos are not subject to the mandatory-video max cap, but they must
        // still mark the slot active so the scheduler doesn't preempt them mid-playback.
        _isVideoActive = true;

        _compositor?.Start();
        if (_videoLayer != null)
        {
            // Phase B2: WPF PlayUrl contract (VideoService.cs:900-903) — "No attention checks,
            // no strict mode - just playback" — so the URL layer path deliberately skips the
            // mandatory-video orchestration (safety timer / attention / segment / strict).
            // Retry/strict state is reset so a stale mandatory-video retry path can never leak in.
            _currentRetryPath = null;
            _currentStrictMode = false;
            _layerOrchestrationActive = false;
            _layerLengthMs = -1;
            _videoLayer.PlayVideo(url, withAudio: true, loop: false);
            // Phase B audio parity: apply volume/mute/output device to the layer's player.
            _videoLayer.ApplyAudioSettings(_settings.Current);
            return;
        }

        // UCE plan Phase E3: the compositor URL video layer is the only URL video path (the
        // legacy multi-monitor / per-window branches were deleted here). The compositor engine
        // is registered on every head, so _videoLayer is only ever null in a degenerate
        // configuration — log rather than silently drop the request; do NOT recreate a window.
        _logger?.LogWarning("AvaloniaVideoService: no compositor video layer available; URL video not played");
    }

    /// <summary>
    /// Shared start-of-playback orchestration body (Phase B2 UCE layer path):
    /// extends the interaction-queue stuck timeout past the video's runtime, arms the safety
    /// timer, and schedules attention-check setup. Callers seed _currentDurationSeconds first
    /// (0 = unknown → 600s safety fallback; the layer path upgrades it from
    /// OnCompositorVideoLengthKnown once LibVLC reports the real length).
    /// </summary>
    private void BeginPlaybackOrchestration(AppSettings settings)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(60, _currentDurationSeconds + 30));
        _interactionQueue.ExtendTimeout(timeout);
        StartSafetyTimer(_currentDurationSeconds > 0 ? _currentDurationSeconds + 30 : 600);

        if (settings.AttentionChecksEnabled)
        {
            StartOneShotTimer(TimeSpan.FromSeconds(2), SetupAttention);
        }
    }

    private void StartSafetyTimer(double timeoutSeconds)
    {
        _safetyTimer?.Stop();
        _safetyTimer = StartOneShotTimer(TimeSpan.FromSeconds(Math.Max(30, timeoutSeconds)), () =>
        {
            // Layer path (Phase B2): the orchestration flag is the liveness check —
            // CleanupInternal clears it, so a timer that outlives its playback no-ops while a
            // hung layer still gets force-cleaned.
            if (!_layerOrchestrationActive) return;
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
                            StartOneShotTimer(TimeSpan.FromMilliseconds(300), () =>
                            {
                                foreach (var t in remaining) t.BringToFront();
                            });
                        }
                    });

                spawnedTargets.Add(target);
                lock (_attentionTargets) { _attentionTargets.Add(target); }
            }

            var lifespan = Math.Max(1, settings.AttentionLifespan) * 1000;
            StartOneShotTimer(TimeSpan.FromMilliseconds(lifespan), () =>
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
                // Trainer companion penalty on a failed video attention check
                // (WPF VideoService.cs:2124-2138).
                _companion?.OnAttentionCheckFailed();
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
                // B2 parity: this ShowMessage teardown is a retry BRIDGE for the SAME obligation
                // (WPF's fail path calls CloseAll, NOT Cleanup, so _penalties survives across
                // retries — mercy-at-3 accumulates). _retryPending tells CleanupInternal to
                // preserve _attentionPenalties through the bridge; it is cleared once the
                // retry's PlayFile has run its preserving teardown.
                _retryPending = true;
                var message = troll
                    ? "GOOD GIRL!\nWATCH AGAIN 😜"
                    : (_mods?.GetAttentionCheckFailMessage() ?? "DUMB BAMBI!\nTRY AGAIN");
                ShowMessage(message, 2000, () =>
                {
                    _attentionHits = 0;
                    _attentionSpawnTimes.Clear();
                    _interactionQueue.ExtendTimeout(TimeSpan.FromMinutes(5));
                    var retryVideo = GetNextVideo();
                    // PlayFile's initial CleanupInternal runs while _retryPending is still true
                    // → penalties preserved (the retry IS the same obligation continuing).
                    PlayFile(string.IsNullOrEmpty(retryVideo) ? _currentRetryPath! : retryVideo, _currentStrictMode);
                    // Bridge done: subsequent teardowns belong to the retry video itself and
                    // reset normally (natural end / next fail / stop / panic).
                    _retryPending = false;
                });
            }
            return;
        }

        // Watch credit is position-based and handled by FinalizeWatchCredit inside
        // CleanupInternal (WPF parity: crediting the full duration here over-counted
        // dismissed/interrupted videos). Phase B2 residual (now wired): the UCE layer path
        // feeds _lastWatchPositionMs via the throttled PlaybackTimeChanged relay + the
        // CurrentTimeMs live read in FinalizeWatchCredit, so interrupted layer playback
        // (attention-fail retry, the 10% troll re-watch after a PASS, panic, Stop, max-duration
        // cap) credits actual watched ms — the same live-watermark contract WPF's player
        // TimeChanged gives (VideoService.cs:1380-1385). The natural-end seeding below is now
        // a last-resort fallback for the rare case the relay never fired AND the live read at
        // teardown returned -1 (e.g. a clip shorter than the ~1s throttle window that ended
        // before FinalizeWatchCredit could read it); it is guarded by _lastWatchPositionMs <= 0,
        // so with the watermark live it simply stops applying — the WPF MediaElement-fallback
        // contract (VideoService.cs:2196-2200) preserved exactly.
        if (_layerOrchestrationActive && _lastWatchPositionMs <= 0 && _layerLengthMs > 0)
            _lastWatchPositionMs = _layerLengthMs;
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

        StartOneShotTimer(TimeSpan.FromMilliseconds(ms), () =>
        {
            CloseMessageWindows();
            then();
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
        // Signal that a teardown is in flight so the post-session summary defers until the
        // dying fullscreen video surface has fully cleared (#462). Cleared in the finally so a
        // throw mid-teardown can never wedge the gate permanently.
        _isCleaningUp = true;
        try
        {
        // Invalidate any pending 1.3s pre-announce start and credit the seconds actually
        // watched BEFORE stopping the players (their clocks die with Stop()).
        _playRequestVersion++;
        _preAnnounceTimer?.Stop();
        _preAnnounceTimer = null;
        FinalizeWatchCredit();

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

        // UCE plan Phase E3: video plays only through the compositor layers now (the window /
        // multi-monitor teardown was deleted). Stop both layers.
        _videoLayer?.Stop();
        _mandatoryVideoLayer?.Stop();

        _currentStrictMode = false;
        // B2 parity (WPF Cleanup :2620 / ForceCleanup :895 reset _penalties): end the
        // attention-fail penalty tally when an obligation ends — EXCEPT an attention-fail retry
        // bridge of the same obligation (_retryPending, set in OnVideoEnded's retry branch),
        // which must keep its tally so mercy-at-3 accumulates across retries (WPF :2168-2170).
        // Without this, 3 cumulative fails EVER in a session permanently force mercy and the
        // pass-XP term (penalties+1)*50 inflates monotonically. ForceCleanup/Stop clear
        // _retryPending before calling here, so they always reset.
        if (!_retryPending)
            _attentionPenalties = 0;
        _currentDurationSeconds = 0;

        // Phase B2: layer-path orchestration teardown symmetry — the flag gates the safety
        // timer, stale VideoEnded posts, and the natural-end evaluation; segment state is
        // one-shot per playback.
        _layerOrchestrationActive = false;
        _layerLengthMs = -1;
        _pendingSegmentArmed = false;
        _pendingSegmentFraction = 0;
        _pendingSegmentSec = 0;
        _pendingSegmentSeekApplied = false;

        if (_duckedForVideo)
        {
            _duckedForVideo = false;
            try { _audioDucker?.Unduck(); } catch { /* best effort */ }
        }

        try { _interactionQueue.Complete("Video"); } catch { }

        if (notifyEnded)
        {
            // WPF Cleanup ordering (VideoService.cs:2595-2616): unduck, resume bubbles,
            // notify ended, then restart flashes only while the engine is still running
            // and the feature is enabled. Retry/preempt teardowns (notifyEnded=false)
            // keep bubbles paused and flashes stopped, exactly like WPF's CloseAll path.
            try { _bubbles?.Resume(); } catch { /* best effort */ }
            VideoEnded?.Invoke(this, EventArgs.Empty);
            if (IsRunning && _settings.Current?.FlashEnabled == true)
            {
                try { _flash?.Start(); } catch { /* best effort */ }
            }
        }
        }
        finally
        {
            _isCleaningUp = false;
        }
    }

    /// <summary>
    /// Credit the seconds actually watched of the current video toward stats/quests, then reset
    /// for the next video (WPF FinalizeWatchCredit, VideoService.cs:2313-2340). Position-based
    /// and watermarked via <see cref="_creditedWatchSeconds"/> so repeated teardown calls
    /// (natural end, ESC, panic, max-duration cap, attention retry) can't double-count, while an
    /// interrupted video still credits what was watched. Never throws.
    /// </summary>
    private void FinalizeWatchCredit()
    {
        try
        {
            // B2 residual: UCE layers relay TimeChanged at ~1 Hz (throttled); read the live
            // position directly at teardown so the watermark is exact regardless of when the
            // last relay landed. -1 (no player / post-Stop natural end) is a no-op. Only one
            // layer is ever active.
            try
            {
                var layerMs = _videoLayer?.CurrentTimeMs ?? -1;
                if (layerMs < 0) layerMs = _mandatoryVideoLayer?.CurrentTimeMs ?? -1;
                if (layerMs > _lastWatchPositionMs) _lastWatchPositionMs = layerMs;
            }
            catch { /* best effort */ }

            var watchedSec = _lastWatchPositionMs / 1000.0;
            var uncredited = watchedSec - _creditedWatchSeconds;
            if (uncredited >= 1.0)
            {
                _creditedWatchSeconds = watchedSec;
                _achievements?.TrackVideoWatched(uncredited);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("AvaloniaVideoService.FinalizeWatchCredit error: {Error}", ex.Message);
        }
        finally
        {
            _lastWatchPositionMs = 0;
            _creditedWatchSeconds = 0;
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

    // Global keyboard receiver for the COMPOSITOR video path only (the legacy window path
    // owns its keys via VideoOverlayWindow.OnKeyDown). Mirrors that non-strict contract
    // (WPF VideoService.cs:1822-1835): ESC dismisses the current video (the session keeps
    // running), the rebindable panic key force-ends. Strict mode is a no-op here: the layer
    // path has no window to dismiss, so ESC/panic are inherently inert (UCE plan B2 residual).
    // The low-level hook cannot swallow keys (AvaloniaInputHook.CanSuppressKeys=false), so we
    // never suppress; we only react while a compositor video is actually playing.
    private void OnCompositorVideoKey(object? sender, KeyboardHookEventArgs e)
    {
        var compositorVideoActive = _videoLayer?.IsPlaying == true || _mandatoryVideoLayer?.IsPlaying == true;
        if (!compositorVideoActive) return;
        if (_currentStrictMode) return; // strict: ESC/panic inert on the layer path

        const int VK_ESCAPE = 0x1B;
        var settings = _settings.Current;
        var isPanicKey = settings?.PanicKeyEnabled == true
            && AvaloniaKeyInterop.TryGetVirtualKeyCode(settings.PanicKey ?? string.Empty, out var panicVk)
            && panicVk == e.VirtualKeyCode;

        if (isPanicKey)
            Dispatcher.UIThread.Post(ForceCleanup);
        else if (e.VirtualKeyCode == VK_ESCAPE)
            Dispatcher.UIThread.Post(() => CleanupInternal(notifyEnded: true));
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
        // Shutdown safety: Stop() here (and the earlier session-stop) schedules a ~400 ms deferred
        // vmem teardown in each video layer. On app exit nothing else keeps the process alive for
        // it, so drain both before returning - otherwise the native LibVLC dispose / HGlobal free
        // races process teardown and segfaults (UCE plan Phase E shutdown-teardown follow-up).
        try { _videoLayer?.WaitForTeardown(); } catch { }
        try { _mandatoryVideoLayer?.WaitForTeardown(); } catch { }

        if (_inputHook != null)
            _inputHook.KeyPressed -= OnCompositorVideoKey;
        CleanupTempPackFiles();
    }

    internal sealed class FloatingText
    {
        // Win32 for reliable z-order management: re-assert HWND_TOPMOST periodically so
        // targets stay above subliminals and fullscreen video windows (WPF FloatingText
        // parity, VideoService.cs:3232-3245).
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HwndTopmost = new(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private readonly Window _win;
        private readonly DispatcherTimer _timer;
        private readonly Action _onHit;
        private readonly double _masterVolume;
        private double _x, _y, _vx, _vy;
        private readonly double _minX, _minY, _maxX, _maxY;
        private readonly double _widthPx, _heightPx;
        private IntPtr _hwnd;
        private int _tickCount;
        private bool _dead;
        private bool _clicked;

        public FloatingText(AppSettings settings, ScreenInfo screen, string text, int size, Action onHit)
        {
            _onHit = onHit;
            _masterVolume = settings.MasterVolume / 100.0;

            size = Math.Max(40, size);
            text = FormatTriggerText(text);

            // All movement math is in PHYSICAL pixels: Window.Position is a physical-pixel
            // PixelPoint, so mixing DIP-divided bounds into it drifted targets on scaled
            // monitors (WS0 lot 4 fix R1-2).
            var area = screen.WorkingArea;
            double areaX = area.X;
            double areaY = area.Y;
            double areaWidth = area.Width;
            double areaHeight = area.Height;

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
            var widthDip = Math.Max(formattedText.WidthIncludingTrailingWhitespace + outlineThickness * 2 + 60, 150);
            var heightDip = Math.Max(formattedText.Height + outlineThickness * 2 + 40, 60);
            // Window.Width/Height stay in DIPs; the clamping math below runs in physical px.
            _widthPx = widthDip * screen.Scaling;
            _heightPx = heightDip * screen.Scaling;

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
                Width = widthDip,
                Height = heightDip,
                Content = container,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            var spawnRangeX = Math.Max(0, (_maxX - _widthPx) - _minX);
            var spawnRangeY = Math.Max(0, (_maxY - _heightPx) - _minY);
            _x = _minX + Random.Shared.NextDouble() * spawnRangeX;
            _y = _minY + Random.Shared.NextDouble() * spawnRangeY;
            _x = Math.Clamp(_x, _minX, Math.Max(_minX, _maxX - _widthPx));
            _y = Math.Clamp(_y, _minY, Math.Max(_minY, _maxY - _heightPx));

            var angle = Random.Shared.NextDouble() * Math.PI * 2;
            // 3 DIP/tick in WPF — scaled to physical px so speed matches across DPI.
            _vx = Math.Cos(angle) * 3.0 * screen.Scaling;
            _vy = Math.Sin(angle) * 3.0 * screen.Scaling;

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
                if (_x + _widthPx > _maxX) { _x = _maxX - _widthPx; _vx = -Math.Abs(_vx); }
                if (_y < _minY) { _y = _minY; _vy = Math.Abs(_vy); }
                if (_y + _heightPx > _maxY) { _y = _maxY - _heightPx; _vy = -Math.Abs(_vy); }
                _win.Position = new PixelPoint((int)_x, (int)_y);

                // Re-assert topmost every ~2 ticks (32ms) so targets stay above subliminals
                // and fullscreen video windows (WPF VideoService.cs:3232-3245).
                _tickCount++;
                if (_tickCount >= 2 && _hwnd != IntPtr.Zero)
                {
                    _tickCount = 0;
                    SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            };

            _win.Opened += (_, _) =>
            {
                ApplyOverlayStyles();
                _timer.Start();
            };

            _win.Show();
        }

        public void BringToFront()
        {
            if (_dead) return;
            try
            {
                _win.Topmost = true;
                if (_hwnd != IntPtr.Zero)
                {
                    SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
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

        private void ApplyOverlayStyles()
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                _hwnd = _win.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (_hwnd == IntPtr.Zero) return;
                // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE via the shared chaos helper (targets stay
                // clickable — NOACTIVATE only prevents focus stealing), then flush the frame
                // change so the styles take effect immediately (WS0 lot 4 fix V1-17/R1-8).
                ChaosWin32Helper.ApplyOverlayExStyles(_win, transparent: false);
                SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            catch { }
        }
    }
}
