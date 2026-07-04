using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Autonomy;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Core.Services.MindWipe;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.RemoteControl;
using ConditioningControlPanel.Core.Services.SessionLog;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Services.RemoteControl;

/// <summary>
/// Avalonia implementation of <see cref="IRemoteCommandExecutor"/>.
/// Maps remote-control commands from the server to the cross-platform service stack.
/// </summary>
public sealed class AvaloniaRemoteCommandExecutor : IRemoteCommandExecutor
{
    private readonly ISettingsService _settingsService;
    private readonly IFlashService? _flash;
    private readonly ISubliminalService? _subliminal;
    private readonly IOverlayService? _overlay;
    private readonly IBubbleService? _bubbles;
    private readonly IVideoService? _video;
    private readonly IHapticsService? _haptics;
    private readonly ISystemAudioDucker? _ducker;
    private readonly IAudioPlayer? _audioPlayer;
    private readonly IBubbleCountService? _bubbleCount;
    private readonly ILockCardService? _lockCard;
    private readonly IMindWipeService? _mindWipe;
    private readonly IBouncingTextService? _bouncingText;
    private readonly ISessionService? _session;
    private readonly IWallpaperProvider? _wallpaper;
    private readonly IBrowserHost? _browserHost;
    private readonly ISessionLogService? _sessionLog;
    private readonly IMultiMonitorVideoService? _multiMonitor;
    private readonly IAutonomyService? _autonomy;
    private readonly ILogger<AvaloniaRemoteCommandExecutor>? _logger;

    private bool _remoteBrowserVideoActive;

    public AvaloniaRemoteCommandExecutor(
        ISettingsService settingsService,
        IFlashService? flash = null,
        ISubliminalService? subliminal = null,
        IOverlayService? overlay = null,
        IBubbleService? bubbles = null,
        IVideoService? video = null,
        IHapticsService? haptics = null,
        ISystemAudioDucker? ducker = null,
        IAudioPlayer? audioPlayer = null,
        IBubbleCountService? bubbleCount = null,
        ILockCardService? lockCard = null,
        IMindWipeService? mindWipe = null,
        IBouncingTextService? bouncingText = null,
        ISessionService? session = null,
        IWallpaperProvider? wallpaper = null,
        IBrowserHost? browserHost = null,
        ISessionLogService? sessionLog = null,
        IMultiMonitorVideoService? multiMonitor = null,
        IAutonomyService? autonomy = null,
        ILogger<AvaloniaRemoteCommandExecutor>? logger = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _flash = flash;
        _subliminal = subliminal;
        _overlay = overlay;
        _bubbles = bubbles;
        _video = video;
        _haptics = haptics;
        _ducker = ducker;
        _audioPlayer = audioPlayer;
        _bubbleCount = bubbleCount;
        _lockCard = lockCard;
        _mindWipe = mindWipe;
        _bouncingText = bouncingText;
        _session = session;
        _wallpaper = wallpaper;
        _browserHost = browserHost;
        _sessionLog = sessionLog;
        _multiMonitor = multiMonitor;
        _autonomy = autonomy;
        _logger = logger;
    }

    public async Task ExecuteCommandAsync(string action, JObject? parameters)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                ExecuteCommandInternal(action, parameters);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[RemoteControl] Error executing command: {Action}", action);
            }
        });
    }

    public async Task StopAllRemoteEffectsAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                StopAllRemoteEffectsInternal();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[RemoteControl] Failed to stop remote effects");
            }
        });
    }

    public async Task HandleControllerDisconnectAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (_settingsService.Current?.StopEffectsOnRemoteDisconnect == true)
                    StopRemoteTriggeredEffectsInternal();
                // Default continuity mode leaves remote effects running so a new
                // controller can see the current state, matching WPF behavior.
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[RemoteControl] Failed to handle controller disconnect");
            }
        });
    }

    private void ExecuteCommandInternal(string action, JObject? parameters)
    {
        var settings = _settingsService.Current;
        if (settings == null) return;

        switch (action)
        {
            // Light tier
            case "trigger_flash":
                _flash?.TriggerFlashOnce(null, 0, false, false);
                break;

            case "trigger_subliminal":
                // LOT9 L2-03: pool-random flash (WPF App.Subliminal?.FlashSubliminal()),
                // not an empty custom string.
                _subliminal?.FlashSubliminal();
                break;

            case "start_flash":
                settings.FlashEnabled = true;
                _flash?.Start();
                Save();
                break;

            case "stop_flash":
                _flash?.Stop();
                break;

            case "start_subliminal":
                settings.SubliminalEnabled = true;
                _subliminal?.Start();
                Save();
                break;

            case "stop_subliminal":
                _subliminal?.Stop();
                break;

            case "trigger_custom_subliminal":
                var customText = parameters?["text"]?.ToString();
                if (!string.IsNullOrWhiteSpace(customText))
                    _subliminal?.FlashSubliminalCustom(customText);
                break;

            case "show_pink_filter":
                settings.PinkFilterEnabled = true;
                if (_overlay != null) _overlay.BypassLevelCheck = true;
                _overlay?.Start();
                _overlay?.RefreshOverlays();
                Save();
                break;

            case "stop_pink_filter":
                settings.PinkFilterEnabled = false;
                _overlay?.RefreshOverlays();
                Save();
                break;

            case "show_spiral":
                settings.SpiralEnabled = true;
                if (_overlay != null) _overlay.BypassLevelCheck = true;
                _overlay?.Start();
                _overlay?.RefreshOverlays();
                Save();
                break;

            case "stop_spiral":
                settings.SpiralEnabled = false;
                _overlay?.RefreshOverlays();
                Save();
                break;

            case "set_pink_opacity":
                var pinkVal = parameters?["value"]?.Value<int>() ?? 25;
                settings.PinkFilterOpacity = Math.Clamp(pinkVal, 0, 50);
                _overlay?.RefreshOverlays();
                Save();
                break;

            case "set_spiral_opacity":
                var spiralVal = parameters?["value"]?.Value<int>() ?? 25;
                settings.SpiralOpacity = Math.Clamp(spiralVal, 0, 50);
                _overlay?.RefreshOverlays();
                Save();
                break;

            case "start_bubbles":
                // LOT9 L2-05: needs IBubbleService.Start(bypassLevelCheck) overload;
                // seam has no bypassLevelCheck param (WPF passes bypassLevelCheck: true). DEFERRED.
                _bubbles?.Start();
                break;

            case "stop_bubbles":
                _bubbles?.Stop();
                break;

            // Standard tier
            case "trigger_video":
                // LOT9 L2-04: one-shot mandatory video (WPF App.Video?.TriggerVideo()),
                // not the recurring scheduler (Start()).
                _video?.TriggerVideo();
                break;

            case "start_video":
                _video?.Start();
                break;

            case "stop_video":
                _video?.Stop();
                break;

            case "play_hypnotube":
                var htUrl = parameters?["url"]?.ToString();
                if (!string.IsNullOrWhiteSpace(htUrl) && IsEligibleHtUrl(htUrl))
                {
                    _logger?.LogInformation("[RemoteControl] play_hypnotube id={Id}", TryExtractHtVideoId(htUrl));

                    // On Windows, route direct media URLs through the single-stream multi-monitor
                    // mirroring service. HypnoTube HTML pages still use the embedded browser.
                    if (OperatingSystem.IsWindows() && _multiMonitor != null && IsDirectVideoUrl(htUrl))
                    {
                        _remoteBrowserVideoActive = true;
                        _multiMonitor.PlaybackEnded += OnRemoteVideoEnded;
                        _multiMonitor.PlayUrl(htUrl);
                    }
                    else
                    {
                        _remoteBrowserVideoActive = true;
                        _ = _browserHost?.NavigateAsync(new Uri(htUrl));
                    }
                }
                else
                {
                    _logger?.LogWarning("[RemoteControl] Rejected play_hypnotube — not an eligible HypnoTube URL");
                }
                break;

            case "trigger_haptic":
                _ = _haptics?.TestAsync(70, 2000);
                break;

            case "duck_audio":
                // Duck at the user's configured strength (WPF RemoteControlService.cs:1115
                // hardcodes Duck(80); DuckingLevel defaults to 80, honoring user overrides).
                _ducker?.Duck(_settingsService.Current?.DuckingLevel ?? 80);
                break;

            case "unduck_audio":
                _ducker?.Unduck();
                break;

            // Full tier
            case "start_autonomy":
                // LOT9 L2-01: mirror WPF App.Autonomy?.Start().
                _autonomy?.Start();
                break;

            case "stop_autonomy":
                // LOT9 L2-01: mirror WPF App.Autonomy?.Stop().
                _autonomy?.Stop();
                break;

            case "trigger_bubble_count":
                _bubbleCount?.TriggerGame(forceTest: true);
                break;

            case "trigger_lock_card":
                _lockCard?.ShowLockCard();
                break;

            case "start_lock_card":
                settings.LockCardEnabled = true;
                _lockCard?.Start();
                Save();
                break;

            case "stop_lock_card":
                _lockCard?.Stop();
                break;

            case "trigger_mind_wipe":
                // LOT9 L2-07: gate on available audio phrases (WPF AudioFileCount > 0).
                if (_mindWipe?.AudioFileCount > 0)
                    _mindWipe.TriggerOnce();
                break;

            case "start_mind_wipe":
                var freq = settings.MindWipeFrequency;
                var vol = settings.MindWipeVolume / 100.0;
                _mindWipe?.Start(freq, vol);
                break;

            case "stop_mind_wipe":
                _mindWipe?.Stop();
                break;

            case "start_bounce_text":
                // LOT9 L2-02: needs IBouncingTextService.Start(bypassLevelCheck) support;
                // seam only takes an optional textPool (WPF passes bypassLevelCheck: true). DEFERRED.
                _bouncingText?.Start();
                break;

            case "stop_bounce_text":
                _bouncingText?.Stop();
                break;

            case "start_session":
                _ = StartSessionFromRemoteAsync(parameters);
                break;

            case "pause_session":
                _session?.PauseSession();
                break;

            case "resume_session":
                _session?.ResumeSession();
                break;

            case "stop_session":
                _session?.StopSession(completed: false);
                break;

            case "enable_strict_lock":
                settings.StrictLockEnabled = true;
                Save();
                break;

            case "disable_strict_lock":
                settings.StrictLockEnabled = false;
                Save();
                break;

            case "disable_panic":
                settings.PanicKeyEnabled = false;
                Save();
                break;

            case "enable_panic":
                settings.PanicKeyEnabled = true;
                Save();
                break;

            case "trigger_wallpaper":
                // LOT9 L2-05: needs IWallpaperProvider activate/shuffle-from-pool method;
                // seam only exposes SetWallpaper(path)/RestoreOriginalWallpaper(). DEFERRED.
                _wallpaper?.RestoreOriginalWallpaper();
                break;

            case "stop_wallpaper":
                _wallpaper?.RestoreOriginalWallpaper();
                break;

            case "trigger_panic":
                StopAllRemoteEffectsInternal();
                break;

            default:
                _logger?.LogWarning("[RemoteControl] Unknown action: {Action}", action);
                break;
        }
    }

    private async Task StartSessionFromRemoteAsync(JObject? parameters)
    {
        var settings = _settingsService.Current;
        if (settings == null || _session == null) return;

        var sessionId = parameters?["session_id"]?.ToString();

        // Build a generic remote session preserving current settings.
        var session = new Session
        {
            Id = "remote_session",
            Name = "Remote Session",
            Icon = "🎮",
            DurationMinutes = 30,
            Difficulty = SessionDifficulty.Medium,
            BonusXP = 200,
            Settings = new SessionSettings
            {
                FlashEnabled = settings.FlashEnabled,
                FlashPerHour = settings.FlashFrequency,
                FlashOpacity = settings.FlashOpacity,
                FlashImages = settings.SimultaneousImages,
                FlashClickable = settings.FlashClickable,
                FlashAudioEnabled = settings.FlashAudioEnabled,
                SubliminalEnabled = settings.SubliminalEnabled,
                SubliminalPerMin = settings.SubliminalFrequency,
                SubliminalOpacity = settings.SubliminalOpacity,
                SubliminalFrames = settings.SubliminalDuration,
                MandatoryVideosEnabled = settings.MandatoryVideosEnabled,
                BubblesEnabled = settings.BubblesEnabled,
            }
        };

        try
        {
            await _session.StartSessionAsync(session);

            if (parameters?["strict_lock"]?.Value<bool>() == true)
            {
                settings.StrictLockEnabled = true;
                Save();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[RemoteControl] Failed to start remote session");
        }
    }

    private void StopAllRemoteEffectsInternal()
    {
        var settings = _settingsService.Current;
        _logger?.LogInformation("[RemoteControl] Stopping all remote effects");

        _audioPlayer?.Stop();
        _video?.Stop();
        StopBrowserVideoInternal();
        _flash?.Stop();
        _subliminal?.Stop();
        _bubbles?.Stop();
        _bouncingText?.Stop();
        _bubbleCount?.Stop();
        _mindWipe?.Stop();
        _lockCard?.Stop();
        _wallpaper?.RestoreOriginalWallpaper();

        if (settings != null)
        {
            settings.PinkFilterEnabled = false;
            settings.SpiralEnabled = false;
            settings.StrictLockEnabled = false;
            settings.PanicKeyEnabled = true;
            Save();
        }

        _overlay?.RefreshOverlays();
        _session?.StopSession(completed: false);
    }

    private void StopRemoteTriggeredEffectsInternal()
    {
        var settings = _settingsService.Current;
        _logger?.LogInformation("[RemoteControl] Controller disconnected — cleaning up remote effects only");

        _video?.Stop();
        StopBrowserVideoInternal();
        _flash?.Stop();
        _subliminal?.Stop();
        _bubbles?.Stop();
        _bouncingText?.Stop();
        _bubbleCount?.Stop();
        _mindWipe?.Stop();
        _lockCard?.Stop();
        _wallpaper?.RestoreOriginalWallpaper();

        if (settings != null)
        {
            settings.PinkFilterEnabled = false;
            settings.SpiralEnabled = false;
            settings.StrictLockEnabled = false;
            settings.PanicKeyEnabled = true;
            Save();
        }

        _overlay?.RefreshOverlays();
    }



    private void StopBrowserVideoInternal()
    {
        if (!_remoteBrowserVideoActive) return;
        _remoteBrowserVideoActive = false;
        try
        {
            _multiMonitor?.Stop();
            if (_multiMonitor != null) _multiMonitor.PlaybackEnded -= OnRemoteVideoEnded;
            _ = _browserHost?.NavigateAsync(new Uri("about:blank"));
            _logger?.LogInformation("[RemoteControl] Stopped remote browser video");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("StopBrowserVideo failed: {Error}", ex.Message);
        }
    }

    private void OnRemoteVideoEnded(object? sender, EventArgs e)
    {
        if (_multiMonitor != null)
            _multiMonitor.PlaybackEnded -= OnRemoteVideoEnded;
        _remoteBrowserVideoActive = false;
    }

    private static bool IsDirectVideoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        var path = uri.AbsolutePath;
        var directExtensions = new[] { ".mp4", ".webm", ".mkv", ".avi", ".mov", ".m4v", ".flv", ".m3u8", ".m3u" };
        return directExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private void Save()
    {
        try
        {
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[RemoteControl] Failed to save settings");
        }
    }

    #region HypnoTube URL validation (mirrors WPF HtUrlHelper)

    private static readonly Regex HtPatternA =
        new(@"^/video/(?<id>\d+)(?:[/\.\-]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HtPatternB =
        new(@"^/video/.+-(?<id>\d+)\.html$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsEligibleHtUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (url == "*") return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.Host.ToLowerInvariant();
        if (host != "hypnotube.com" && !host.EndsWith(".hypnotube.com", StringComparison.Ordinal))
            return false;

        var path = uri.AbsolutePath;
        return HtPatternA.IsMatch(path) || HtPatternB.IsMatch(path);
    }

    private static string? TryExtractHtVideoId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var host = uri.Host.ToLowerInvariant();
        if (host != "hypnotube.com" && !host.EndsWith(".hypnotube.com", StringComparison.Ordinal))
            return null;

        var path = uri.AbsolutePath;
        var matchA = HtPatternA.Match(path);
        if (matchA.Success) return matchA.Groups["id"].Value;
        var matchB = HtPatternB.Match(path);
        if (matchB.Success) return matchB.Groups["id"].Value;
        return null;
    }

    #endregion
}
