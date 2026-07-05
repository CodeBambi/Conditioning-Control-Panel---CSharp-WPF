using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Core.Services.Deeper
{
    /// <summary>
    /// Per-dispatch context: the enhancement being driven (so actions can resolve
    /// region_id references), the time source (so seek/pause/loop_region target
    /// it), and the current playback time at the moment of fire.
    /// </summary>
    public sealed class EnhancementDispatchContext
    {
        public Enhancement Enhancement { get; }
        public IPlaybackTimeSource Source { get; }
        public double CurrentTimeSeconds { get; }
        public string? CurrentRegionId { get; }

        public EnhancementDispatchContext(Enhancement enh, IPlaybackTimeSource src, double t, string? regionId)
        {
            Enhancement = enh;
            Source = src;
            CurrentTimeSeconds = t;
            CurrentRegionId = regionId;
        }
    }

    public interface IActionDispatcher
    {
        // ct fires when the engine that owns the dispatcher is stopped; used so
        // long-running multi-step dispatches (haptic patterns, audio) abort
        // instead of running on after the user pressed stop.
        Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default);
    }

    /// <summary>
    /// Dry-run dispatcher used by the editor preview. Records every action it
    /// would have fired into <see cref="RecentActions"/> (capped at 50) so a
    /// debug overlay can show "last 10 fired actions" without touching real
    /// devices or audio.
    /// </summary>
    public sealed class LoggingActionDispatcher : IActionDispatcher
    {
        private const int MaxRecent = 50;
        private readonly Queue<string> _recent = new();
        private readonly object _gate = new();

        public IReadOnlyList<string> RecentActions
        {
            get { lock (_gate) return _recent.ToArray(); }
        }

        public event Action<string>? ActionLogged;

        public Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return Task.CompletedTask;
            // Per-tick opacity-ramp updates would flood the preview log; dry-run has
            // no real overlay to update anyway, so drop them silently.
            if (action is TriggerEffectAction { Phase: EffectPhase.Update }) return Task.CompletedTask;
            var line = $"t={ctx.CurrentTimeSeconds:0.00}s  {DescribeAction(action)}";
            lock (_gate)
            {
                _recent.Enqueue(line);
                while (_recent.Count > MaxRecent) _recent.Dequeue();
            }
            try { ActionLogged?.Invoke(line); }
            catch { /* ignored */ }
            return Task.CompletedTask;
        }

        public void Clear()
        {
            lock (_gate) _recent.Clear();
        }

        internal static string DescribeAction(EnhancementAction a) => a switch
        {
            SeekAction s when s.Target == SeekTargets.Time => $"seek → {s.Time ?? 0:0.00}s",
            SeekAction s => $"seek → {s.Target} of {s.RegionId ?? "?"}",
            LoopRegionAction lr => $"loop_region → {lr.RegionId ?? "(current)"}",
            PauseAction => "pause",
            PlayAudioAction pa => $"play_audio {Path.GetFileName(pa.Path)} vol={pa.Volume}{(pa.DuckOtherAudio ? " duck" : "")}",
            TriggerHapticAction h => $"haptic {(h.PatternName ?? "custom")} @ {h.Intensity:0.00} for {h.DurationMs}ms{PhaseSuffix(h.Phase, h.DurationMs)}",
            TriggerEffectAction te => DescribeTriggerEffect(te),
            ScreenShakeAction ss => $"screen_shake {ss.Intensity:0.00} for {ss.DurationMs}ms",
            SetIntensityAction si => $"set_intensity {si.Value:0.00}",
            NoOpEnhancementAction nop => $"<unknown action: {nop.OriginalType}>",
            _ => a.GetType().Name
        };

        private static string DescribeTriggerEffect(TriggerEffectAction te)
        {
            var phaseSuffix = PhaseSuffix(te.Phase, te.DurationMs);
            return te.EffectType switch
            {
                EffectTypes.Haptic     => $"effect haptic {(te.PatternName ?? "custom")} @ {te.Intensity:0.00} for {te.DurationMs}ms{phaseSuffix}",
                EffectTypes.Flash      => $"effect flash {(te.ImagePath ?? "random")} for {te.DurationMs}ms{phaseSuffix}",
                EffectTypes.Bubble     => $"effect bubbles x{te.MaxBubbles} for {te.DurationMs}ms{phaseSuffix}",
                EffectTypes.Subliminal => $"effect subliminal \"{te.Text}\" for {te.DurationMs}ms{phaseSuffix}",
                EffectTypes.Overlay    => $"effect overlay {te.OverlayKind} @ {te.Opacity:0.00} for {te.DurationMs}ms{phaseSuffix}",
                _ => $"effect {te.EffectType}{phaseSuffix}"
            };
        }

        private static string PhaseSuffix(EffectPhase phase, int durationMs) => phase switch
        {
            EffectPhase.Start   => " [start]",
            EffectPhase.Stop    => " [stop]",
            EffectPhase.Restart => $" [restart {durationMs}ms]",
            _                   => ""
        };
    }

    /// <summary>
    /// Decorator that records every action it forwards to an inner dispatcher.
    /// Used by editor preview mode to drive real devices via
    /// <see cref="RealActionDispatcher"/> while still surfacing the "last N
    /// fired actions" overlay that LoggingActionDispatcher provides for
    /// dry-run preview. RecentActions is capped at 50; ActionLogged fires
    /// after the inner dispatcher returns.
    /// </summary>
    public sealed class RecordingActionDispatcher : IActionDispatcher
    {
        private const int MaxRecent = 50;
        private readonly IActionDispatcher _inner;
        private readonly Queue<string> _recent = new();
        private readonly object _gate = new();

        public IReadOnlyList<string> RecentActions
        {
            get { lock (_gate) return _recent.ToArray(); }
        }

        public event Action<string>? ActionLogged;

        public RecordingActionDispatcher(IActionDispatcher inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public async Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return;
            // Forward per-tick ramp updates to the real dispatcher (so the overlay
            // actually ramps in editor preview) but don't record them — they'd flood
            // the "recent actions" overlay.
            if (action is TriggerEffectAction { Phase: EffectPhase.Update })
            {
                await _inner.DispatchAsync(action, ctx, ct);
                return;
            }
            var line = $"t={ctx.CurrentTimeSeconds:0.00}s  {LoggingActionDispatcher.DescribeAction(action)}";
            try { await _inner.DispatchAsync(action, ctx, ct); }
            finally
            {
                lock (_gate)
                {
                    _recent.Enqueue(line);
                    while (_recent.Count > MaxRecent) _recent.Dequeue();
                }
                try { ActionLogged?.Invoke(line); }
                catch { /* ignored */ }
            }
        }

        public void Clear()
        {
            lock (_gate) _recent.Clear();
        }
    }

    /// <summary>
    /// Production dispatcher. Delegates to existing CCP services. Every path
    /// is best-effort and silent on failure — a missing device or unsupported
    /// action must never throw out of the engine tick.
    /// </summary>
    public sealed class RealActionDispatcher : IActionDispatcher
    {
        private readonly ILogger<RealActionDispatcher>? _logger;
        private readonly IAppEnvironment _environment;
        private readonly ISettingsService _settings;
        private readonly IAudioPlayer _audioPlayer;
        private readonly IFlashService _flash;
        private readonly ISubliminalService _subliminal;
        private readonly IOverlayService _overlay;
        private readonly IHapticsService _haptics;
        private readonly IBubbleService _bubbles;
        private readonly ISpeakPromptHost? _speakHost;
        // Screen-shake seam (Q15). Optional: a head without a shakeable content root
        // leaves this null and the screen_shake action no-ops (WPF path always had a
        // service — Services/Deeper/IActionDispatcher.cs:233-234 App.ScreenShake?.Shake).
        private readonly IScreenShakeService? _screenShake;

        // Per-EffectId tracking for band-mode effects. Lets Stop hide the right
        // overlay kind and Restart re-issue a haptic with its previously dispatched
        // samples + intensity but a freshly-computed remaining duration.
        private readonly Dictionary<string, string> _overlayBandKind = new();
        private readonly Dictionary<string, BandHapticState> _hapticBandState = new();
        private readonly object _bandGate = new();

        private sealed class BandHapticState
        {
            public float[] Samples = System.Array.Empty<float>();
            public double Intensity = 1.0;
            public int OriginalDurationMs;
        }

        public RealActionDispatcher(
            IAppEnvironment environment,
            ISettingsService settings,
            IAudioPlayer audioPlayer,
            IFlashService flash,
            ISubliminalService subliminal,
            IOverlayService overlay,
            IHapticsService haptics,
            IBubbleService bubbles,
            ILogger<RealActionDispatcher>? logger = null,
            ISpeakPromptHost? speakHost = null,
            IScreenShakeService? screenShake = null)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
            _flash = flash ?? throw new ArgumentNullException(nameof(flash));
            _subliminal = subliminal ?? throw new ArgumentNullException(nameof(subliminal));
            _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            _haptics = haptics ?? throw new ArgumentNullException(nameof(haptics));
            _bubbles = bubbles ?? throw new ArgumentNullException(nameof(bubbles));
            _logger = logger;
            _speakHost = speakHost;
            _screenShake = screenShake;
        }

        public async Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                switch (action)
                {
                    case SeekAction seek:
                        DispatchSeek(seek, ctx);
                        break;

                    case LoopRegionAction loop:
                        DispatchLoopRegion(loop, ctx);
                        break;

                    case PauseAction:
                        ctx.Source.Pause();
                        break;

                    case PlayAudioAction pa:
                        DispatchPlayAudio(pa);
                        break;

                    case TriggerHapticAction haptic:
                        await DispatchHaptic(haptic);
                        break;

                    case TriggerEffectAction effect:
                        await DispatchTriggerEffect(effect, ctx.Source);
                        break;

                    case ScreenShakeAction shake:
                        // WPF Services/Deeper/IActionDispatcher.cs:233-234 — the Deeper path does
                        // NOT gate on any setting; it passes intensity/duration straight through
                        // and the service clamps them internally (ScreenShakeService.cs:46-47).
                        _screenShake?.Shake(shake.Intensity, shake.DurationMs);
                        break;

                    case SetIntensityAction:
                        // No central session-intensity setting yet; log so creators
                        // see firing without a runtime side-effect.
                        _logger?.LogDebug("Deeper: set_intensity action stubbed in cross-platform runtime");
                        break;

                    case NoOpEnhancementAction:
                        // Round-tripped placeholder — never dispatch.
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Deeper action dispatch error ({Type}): {Error}", action.Type, ex.Message);
            }
        }

        private static void DispatchSeek(SeekAction seek, EnhancementDispatchContext ctx)
        {
            var resolved = ResolveBand(ctx, seek.RegionId);
            double? target = seek.Target switch
            {
                SeekTargets.Time => seek.Time,
                SeekTargets.RegionStart => resolved?.start,
                SeekTargets.RegionEnd => resolved?.end,
                _ => null
            };
            if (target.HasValue) ctx.Source.Seek(target.Value);
        }

        private static void DispatchLoopRegion(LoopRegionAction loop, EnhancementDispatchContext ctx)
        {
            var id = loop.RegionId ?? ctx.CurrentRegionId;
            if (id == null) return;
            var resolved = ResolveBand(ctx, id);
            if (resolved == null) return;
            // Loop is implemented as a seek-back to region start; the engine
            // (not the dispatcher) is responsible for re-firing on the next
            // tick if it crosses the end again.
            ctx.Source.Seek(resolved.Value.start);
        }

        /// <summary>
        /// Resolves a region/band by id. Prefers the unified TimelineItems
        /// collection (always live during editor preview); falls back to the
        /// legacy Regions list for backwards compatibility.
        /// </summary>
        private static (double start, double end)? ResolveBand(EnhancementDispatchContext ctx, string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            foreach (var item in ctx.Enhancement.TimelineItems)
            {
                if (item.Kind != TimelineItemKind.Rule) continue;
                if (item.Duration <= 0 || item.Duration >= double.MaxValue) continue;
                if (item.Id == id) return (item.Start, item.Start + item.Duration);
            }

            var region = ctx.Enhancement.Regions.FirstOrDefault(r => r.Id == id);
            if (region != null) return (region.Start, region.End);
            return null;
        }

        private void DispatchPlayAudio(PlayAudioAction pa)
        {
            if (string.IsNullOrEmpty(pa.Path)) return;
            try
            {
                var path = pa.Path;
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(_environment.EffectiveAssetsPath, path);
                if (!File.Exists(path))
                {
                    _logger?.LogDebug("Deeper play_audio: file not found ({Path})", path);
                    return;
                }
                if (pa.DuckOtherAudio && _settings.Current.AudioDuckingEnabled)
                    _logger?.LogDebug("Deeper play_audio: audio ducking requested (level={Level}) — full ducker wiring is separate", _settings.Current.DuckingLevel);
                _ = _audioPlayer.PlayAsync(path);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Deeper play_audio error: {Error}", ex.Message);
            }
        }

        private async Task DispatchTriggerEffect(TriggerEffectAction effect, IPlaybackTimeSource? source = null)
        {
            try
            {
                switch (effect.EffectType)
                {
                    case EffectTypes.Haptic:
                        // Reuse the existing haptic dispatch path so timeline synthesis
                        // and rule-fired haptics share one code path. Forward Phase +
                        // EffectId so band lifecycle routing applies.
                        await DispatchHaptic(new TriggerHapticAction
                        {
                            PatternName = effect.PatternName,
                            CustomPattern = effect.CustomPattern,
                            Intensity = effect.Intensity,
                            DurationMs = effect.DurationMs,
                            Phase = effect.Phase,
                            EffectId = effect.EffectId
                        });
                        break;

                    case EffectTypes.Flash:
                        // Inherit the user's CCP Flashes settings: image pool,
                        // sound, scale, opacity all come from FlashService's
                        // normal random-image path (passing null path = random).
                        var flashSound = _settings.Current.FlashAudioEnabled;
                        _flash.TriggerFlashOnce(null, effect.DurationMs, flashSound, effect.SuppressHaptic);
                        break;

                    case EffectTypes.Bubble:
                        // maxBubbles is no longer per-effect; derive a sensible
                        // burst from the user's BubblesFrequency × segment width.
                        DispatchBubbleBurst(effect.DurationMs);
                        break;

                    case EffectTypes.Subliminal:
                        if (!string.IsNullOrWhiteSpace(effect.Text))
                            _subliminal.FlashSubliminalCustom(effect.Text!, overrideDurationMs: effect.DurationMs, suppressHaptic: effect.SuppressHaptic);
                        break;

                    case EffectTypes.Overlay:
                        DispatchOverlayEffect(effect);
                        break;

                    case EffectTypes.Speak:
                        // Deeper "say it for me" voice prompt — routed to the head's host (cue + recognizer).
                        if (effect.Phase == EffectPhase.Stop)
                            _speakHost?.StopSpeak(effect.EffectId);
                        else
                            _speakHost?.StartSpeak(effect, source);
                        break;

                    default:
                        _logger?.LogDebug("Deeper trigger_effect: unknown effect_type \"{Type}\"", effect.EffectType);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Deeper trigger_effect dispatch error: {Error}", ex.Message);
            }
        }

        private void DispatchOverlayEffect(TriggerEffectAction effect)
        {
            var kind = effect.OverlayKind ?? OverlayKinds.PinkFilter;
            switch (effect.Phase)
            {
                case EffectPhase.Start:
                    if (!string.IsNullOrEmpty(effect.EffectId))
                    {
                        lock (_bandGate) _overlayBandKind[effect.EffectId!] = kind;
                    }
                    _overlay.ShowOverlaySustained(kind, effect.Opacity);
                    break;

                case EffectPhase.Stop:
                    // Prefer the kind we remembered at Start in case the action's
                    // OverlayKind has since been mutated by the editor mid-preview.
                    var hideKind = kind;
                    if (!string.IsNullOrEmpty(effect.EffectId))
                    {
                        lock (_bandGate)
                        {
                            if (_overlayBandKind.TryGetValue(effect.EffectId!, out var remembered))
                                hideKind = remembered;
                            _overlayBandKind.Remove(effect.EffectId!);
                        }
                    }
                    _overlay.HideOverlaySustained(hideKind);
                    break;

                case EffectPhase.Restart:
                    // Overlay state has no "remaining time" — already shown, nothing
                    // to recompute. No-op.
                    break;

                case EffectPhase.Update:
                    // Per-tick opacity ramp: live-update the already-shown overlay.
                    _overlay.SetSustainedOverlayOpacity(kind, effect.Opacity);
                    break;

                case EffectPhase.OneShot:
                default:
                    _overlay.ShowOverlayTimed(kind, effect.DurationMs, effect.Opacity);
                    break;
            }
        }

        private void DispatchBubbleBurst(int durationMs)
        {
            // Bubbles have no per-event helper on the service; a brief Start/Stop
            // window with the scheduler-owned timer keeps the surface area small.
            // Density is governed by the user's CCP BubblesFrequency setting —
            // BubbleService.Start() reads that itself, so we just gate the
            // duration here.
            try
            {
                _bubbles.Start();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(50, durationMs)) };
                EventHandler? handler = null;
                handler = (_, _) =>
                {
                    timer.Stop();
                    timer.Tick -= handler;
                    try { _bubbles.Stop(); }
                    catch (Exception ex) { _logger?.LogDebug("DispatchBubbleBurst stop: {E}", ex.Message); }
                };
                timer.Tick += handler;
                timer.Start();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("DispatchBubbleBurst start: {E}", ex.Message);
            }
        }

        private async Task DispatchHaptic(TriggerHapticAction haptic)
        {
            try
            {
                // Band-mode Stop is just a cancel — no pattern lookup needed.
                if (haptic.Phase == EffectPhase.Stop)
                {
                    if (!string.IsNullOrEmpty(haptic.EffectId))
                    {
                        lock (_bandGate) _hapticBandState.Remove(haptic.EffectId!);
                    }
                    await _haptics.StopAsync();
                    return;
                }

                // Restart: recompute samples for the new remaining duration. We
                // could cache+reuse the original samples (they're flat-averaged
                // anyway), but resampling at the new duration is cheap and keeps
                // the path identical to Start.
                IList<double[]>? keyframes = null;
                if (haptic.CustomPattern != null && haptic.CustomPattern.Count > 0)
                    keyframes = haptic.CustomPattern;
                else if (!string.IsNullOrEmpty(haptic.PatternName)
                         && StockHapticPatterns.TryGet(haptic.PatternName, out var named) && named != null)
                    keyframes = named;

                if (keyframes == null)
                {
                    _logger?.LogDebug("Deeper haptic: skipped — no pattern (name='{Name}', custom={Count})",
                        haptic.PatternName, haptic.CustomPattern?.Count ?? 0);
                    return;
                }

                var samples = StockHapticPatterns.Sample(keyframes, haptic.Intensity, haptic.DurationMs);

                // Restart: send Vibrate:0 first to clear LovenseProvider's 1-second
                // same-level debounce — without this, a same-level re-issue after a
                // backward seek is silently dropped (see lovense_pattern_api_flat memory).
                if (haptic.Phase == EffectPhase.Restart)
                    await _haptics.StopAsync();

                if (!string.IsNullOrEmpty(haptic.EffectId))
                {
                    lock (_bandGate)
                    {
                        _hapticBandState[haptic.EffectId!] = new BandHapticState
                        {
                            Samples = samples,
                            Intensity = haptic.Intensity,
                            OriginalDurationMs = haptic.DurationMs
                        };
                    }
                }

                await _haptics.SetSyncPatternAsync(samples, haptic.DurationMs);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Deeper haptic dispatch error: {Error}", ex.Message);
            }
        }
    }
}
