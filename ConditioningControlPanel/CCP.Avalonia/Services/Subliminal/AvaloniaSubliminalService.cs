using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Audio;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Services.Subliminal;

/// <summary>
/// Avalonia implementation of the subliminal-message effect engine.
/// Shows brief, centered text flashes via the unified compositor layer.
/// </summary>
public sealed class AvaloniaSubliminalService : ISubliminalService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IScreenProvider _screens;
    private readonly IProgressionService _progression;
    private readonly ISessionService? _session;
    private readonly ILogger<AvaloniaSubliminalService>? _logger;
    private readonly Random _random = new();
    private readonly object _sync = new();
    private readonly CompositorEngine? _compositor;
    private readonly SubliminalLayer? _subliminalLayer;
    // Whisper voice audio (WPF parity: SubliminalService.cs:78/408-502/504-543). Null when the
    // head did not wire the audio deps (tests/heads without assets) — the service stays text-only.
    private readonly IAppEnvironment? _environment;
    private readonly IModService? _mods;
    private readonly WhisperVoicePlayer? _whisperVoice;
    private readonly string _audioPath;

    private CancellationTokenSource? _cts;
    private DispatcherTimer? _scheduledTimer;
    private bool _disposed;

    public AvaloniaSubliminalService(
        ISettingsService settings,
        IScreenProvider screens,
        IProgressionService progression,
        ISessionService? session = null,
        ILogger<AvaloniaSubliminalService>? logger = null,
        CompositorEngine? compositor = null,
        IAppEnvironment? environment = null,
        IModService? mods = null,
        WhisperVoicePlayer? whisperVoice = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _session = session;
        _logger = logger;
        _compositor = compositor;
        _environment = environment;
        _mods = mods;
        _whisperVoice = whisperVoice;
        // Bundled subliminal whisper clips (WPF SubliminalService.cs:78).
        _audioPath = Path.Combine(environment?.BaseDirectory ?? AppContext.BaseDirectory, "Resources", "sub_audio");
        // Avalonia always renders subliminals on the single shared compositor canvas (SubliminalLayer) —
        // there is no per-screen keep-alive-window path. This is exactly what WPF's opt-in
        // SubliminalSolidMode achieves (SubliminalService.cs:622 `useHost = SubliminalSolidMode && !stealsFocus`:
        // one shared click-through host instead of many layered windows), so the feature is inherently
        // always-on here. #461: AppSettings.SubliminalSolidMode has no per-window fallback to gate and is
        // intentionally ignored, mirroring FlashSolidMode in AvaloniaFlashService.
        _subliminalLayer = compositor != null ? new SubliminalLayer() : null;
        if (_subliminalLayer != null)
            _compositor?.RegisterLayer(_subliminalLayer);
    }

    public bool IsRunning { get; private set; }

    public event EventHandler? SubliminalDisplayed;

    public void Start()
    {
        if (IsRunning) return;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            _logger?.LogDebug("AvaloniaSubliminalService: overlays are not supported on mobile; Start is a no-op");
            return;
        }

        IsRunning = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        ScheduleNext();
        _logger?.LogInformation("AvaloniaSubliminalService started");
    }

    public void FlashSubliminal()
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var pool = settings.SubliminalPool;
        var activeTexts = pool.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
        if (activeTexts.Count == 0)
        {
            _logger?.LogDebug("AvaloniaSubliminalService: no active subliminal texts");
            return;
        }

        var text = activeTexts[_random.Next(activeTexts.Count)];

        // WPF whisper-audio branch (SubliminalService.cs:200-231): when whispers are enabled
        // (SubAudioEnabled), master is not muted, and a linked trigger-phrase clip exists, play
        // the voice clip ALONGSIDE the text overlay and mark the bark-gate window for the clip
        // duration. Spec guardrail: master-muted ⇒ no audio + no mark (diverges from WPF, which
        // plays at vol 0 and still marks — SubliminalService.cs:515-517/534). The text overlay
        // path is unchanged (no WPF 50+250ms visual delay — spec: play alongside).
        var playedWhisper = false;
        if (_whisperVoice != null && settings.SubAudioEnabled && settings.MasterVolume > 0)
        {
            var audioPath = FindLinkedAudio(text);
            if (!string.IsNullOrEmpty(audioPath))
            {
                // WPF volume curve (SubliminalService.cs:515-517): pow(subVol * masterVol, 1.5) — no floor.
                var vol = Math.Pow((settings.SubAudioVolume / 100.0) * (settings.MasterVolume / 100.0), 1.5);
                _whisperVoice.Play(audioPath,
                    whispersEnabled: true,
                    masterMuted: settings.MasterVolume <= 0,
                    volume01: vol,
                    duckEnabled: settings.AudioDuckingEnabled,
                    duckLevel: settings.DuckingLevel);
                playedWhisper = true;
            }
        }

        ShowSubliminalVisuals(text);
        // WPF XP split (SubliminalService.cs:225/230): 20 with audio, 10 without.
        _progression.AddXP(playedWhisper ? 20 : 10, XPSource.Subliminal);
    }

    public void FlashSubliminalCustom(string text, int? opacity = null, int? overrideDurationMs = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        if (text.Length > 200) text = text.Substring(0, 200);
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "");
        ShowSubliminalVisuals(text, opacity, overrideDurationMs);
        _progression.AddXP(10, XPSource.Subliminal);
    }

    public void FlashSubliminalCustom(string text, int? overrideDurationMs = null, bool suppressHaptic = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        FlashSubliminalCustom(text, opacity: null, overrideDurationMs: overrideDurationMs);
    }

    public void SetEnabled(bool on)
    {
        var s = _settings.Current;
        if (s == null) return;

        if (s.SubliminalEnabled != on)
            s.SubliminalEnabled = on;

        if (_session?.State == SessionState.Running)
        {
            if (on && !IsRunning) Start();
            else if (!on && IsRunning) Stop();
        }

        _settings.Save();
        _logger?.LogInformation("AvaloniaSubliminalService: subliminals toggled: {Enabled}", on);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        _scheduledTimer?.Stop();
        _scheduledTimer = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _logger?.LogInformation("AvaloniaSubliminalService stopped");
    }

    // Resolve the whisper voice clip for a trigger-phrase text (WPF parity:
    // SubliminalService.cs:408-502 FindLinkedAudio/SearchAudioDirectory/GetModAudioPath).
    // Checks the active mod's flashes_audio directory first, then the bundled
    // Resources/sub_audio directory, matching the text against several case/apostrophe
    // variants. The WPF 60s Directory.GetFiles cache is omitted — subliminals fire on a
    // per-minute cadence, so the per-fire scan cost is negligible and this stays testable.
    private string? FindLinkedAudio(string text)
    {
        var cleanText = text.Trim();
        var extensions = new[] { ".mp3", ".wav", ".ogg", ".MP3", ".WAV", ".OGG" };
        var textVariants = new[]
        {
            cleanText,
            cleanText.ToUpper(),
            cleanText.ToLower(),
            cleanText.Replace("\u2019", "'"),
            cleanText.Replace("'", "\u2019"),
            cleanText.ToUpper().Replace("\u2019", "'"),
        };

        var modAudioPath = GetModAudioPath();
        if (modAudioPath != null)
        {
            var result = SearchAudioDirectory(modAudioPath, cleanText, textVariants, extensions);
            if (result != null) return result;
        }

        return SearchAudioDirectory(_audioPath, cleanText, textVariants, extensions);
    }

    // WPF parity: SubliminalService.cs:436-443 GetModAudioPath.
    private string? GetModAudioPath()
    {
        var modPath = _mods?.ActiveMod?.InstalledPath;
        if (string.IsNullOrEmpty(modPath)) return null;
        var modAudioDir = Path.Combine(modPath!, "resources", "sounds", "flashes_audio");
        return Directory.Exists(modAudioDir) ? modAudioDir : null;
    }

    // WPF parity: SubliminalService.cs:445-502 SearchAudioDirectory (without the time cache).
    private string? SearchAudioDirectory(string directory, string cleanText, string[] textVariants, string[] extensions)
    {
        foreach (var textVar in textVariants)
        {
            foreach (var ext in extensions)
            {
                var path = Path.Combine(directory, textVar + ext);
                if (File.Exists(path)) return path;
            }
        }

        try
        {
            if (Directory.Exists(directory))
            {
                var normalizedText = cleanText.ToUpperInvariant().Replace("\u2019", "'");
                foreach (var file in Directory.GetFiles(directory))
                {
                    var fileName = Path.GetFileNameWithoutExtension(file).ToUpperInvariant().Replace("\u2019", "'");
                    if (fileName == normalizedText) return file;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("AvaloniaSubliminalService: error searching audio directory {Dir}: {Error}", directory, ex.Message);
        }

        return null;
    }

    private void ShowSubliminalVisuals(string text, int? opacity = null, int? overrideDurationMs = null)
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var durationMs = overrideDurationMs.HasValue
            ? Math.Max(100, overrideDurationMs.Value)
            : Math.Max(100, settings.SubliminalDuration * 17);

        // WPF parity (SubliminalService.cs:583): targetOpacity = (override ?? SubliminalOpacity)/100,
        // read live per flash so preset/session/remote SubliminalOpacity changes apply immediately.
        var targetOpacity = Math.Clamp((opacity ?? settings.SubliminalOpacity) / 100.0, 0.0, 1.0);

        var bgColor = ParseColor(settings.SubBackgroundColor, Colors.Black);
        var textColor = ParseColor(settings.SubTextColor, Colors.Magenta);
        var bgTransparent = settings.SubBackgroundTransparent;

        _subliminalLayer?.Flash(text, bgColor, textColor, durationMs, bgTransparent, targetOpacity);
        SubliminalDisplayed?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleNext()
    {
        if (!IsRunning) return;

        var settings = _settings.Current;
        if (settings == null || !settings.SubliminalEnabled) return;

        _scheduledTimer?.Stop();

        var freq = Math.Max(1, settings.SubliminalFrequency);
        var baseInterval = 60.0 / freq;
        var variance = baseInterval * 0.3;
        var interval = baseInterval + (_random.NextDouble() * variance * 2 - variance);
        interval = Math.Max(1, interval);

        _scheduledTimer = StartOneShotTimer(TimeSpan.FromSeconds(interval), () =>
        {
            if (!IsRunning) return;
            var s = _settings.Current;
            if (s == null || !s.SubliminalEnabled) return;

            try { FlashSubliminal(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "AvaloniaSubliminalService: FlashSubliminal failed"); }
            ScheduleNext();
        });
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return Color.Parse(hex);
        }
        catch
        {
            return fallback;
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
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
