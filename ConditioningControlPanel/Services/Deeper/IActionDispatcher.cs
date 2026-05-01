using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Services.Deeper
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
            var line = $"t={ctx.CurrentTimeSeconds:0.00}s  {DescribeAction(action)}";
            lock (_gate)
            {
                _recent.Enqueue(line);
                while (_recent.Count > MaxRecent) _recent.Dequeue();
            }
            App.Logger?.Information("Deeper preview: {Line}", line);
            try { ActionLogged?.Invoke(line); }
            catch (Exception ex) { App.Logger?.Debug("LoggingActionDispatcher subscriber error: {Error}", ex.Message); }
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
            TriggerHapticAction h => $"haptic {(h.PatternName ?? "custom")} @ {h.Intensity:0.00} for {h.DurationMs}ms",
            TriggerEffectAction te => DescribeTriggerEffect(te),
            ScreenShakeAction ss => $"screen_shake {ss.Intensity:0.00} for {ss.DurationMs}ms",
            SetIntensityAction si => $"set_intensity {si.Value:0.00}",
            NoOpEnhancementAction nop => $"<unknown action: {nop.OriginalType}>",
            _ => a.GetType().Name
        };

        private static string DescribeTriggerEffect(TriggerEffectAction te) => te.EffectType switch
        {
            EffectTypes.Haptic     => $"effect haptic {(te.PatternName ?? "custom")} @ {te.Intensity:0.00} for {te.DurationMs}ms",
            EffectTypes.Flash      => $"effect flash {(te.ImagePath ?? "random")} for {te.DurationMs}ms",
            EffectTypes.Bubble     => $"effect bubbles x{te.MaxBubbles} for {te.DurationMs}ms",
            EffectTypes.Subliminal => $"effect subliminal \"{te.Text}\" for {te.DurationMs}ms",
            EffectTypes.Overlay    => $"effect overlay {te.OverlayKind} @ {te.Opacity:0.00} for {te.DurationMs}ms",
            _ => $"effect {te.EffectType}"
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
                catch (Exception ex) { App.Logger?.Debug("RecordingActionDispatcher subscriber error: {Error}", ex.Message); }
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
                        await DispatchPlayAudio(pa);
                        break;

                    case TriggerHapticAction haptic:
                        await DispatchHaptic(haptic);
                        break;

                    case TriggerEffectAction effect:
                        await DispatchTriggerEffect(effect);
                        break;

                    case ScreenShakeAction shake:
                        App.ScreenShake?.Shake(shake.Intensity, shake.DurationMs);
                        break;

                    case SetIntensityAction:
                        // No central session-intensity setting yet; log so creators
                        // see firing without a runtime side-effect.
                        App.Logger?.Debug("Deeper: set_intensity action stubbed in v1");
                        break;

                    case NoOpEnhancementAction:
                        // Round-tripped placeholder — never dispatch.
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Deeper action dispatch error ({Type}): {Error}", action.Type, ex.Message);
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

        private static async Task DispatchPlayAudio(PlayAudioAction pa)
        {
            if (string.IsNullOrEmpty(pa.Path)) return;
            try
            {
                var path = pa.Path;
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(App.EffectiveAssetsPath, path);
                if (!File.Exists(path))
                {
                    App.Logger?.Debug("Deeper play_audio: file not found ({Path})", path);
                    return;
                }
                if (pa.DuckOtherAudio && App.Settings?.Current?.AudioDuckingEnabled == true)
                    App.Audio?.Duck(App.Settings?.Current?.DuckingLevel ?? 80);
                await Task.Run(() => App.Audio?.PlaySound(path, pa.Volume));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Deeper play_audio error: {Error}", ex.Message);
            }
        }

        private static async Task DispatchTriggerEffect(TriggerEffectAction effect)
        {
            try
            {
                switch (effect.EffectType)
                {
                    case EffectTypes.Haptic:
                        // Reuse the existing haptic dispatch path so timeline synthesis
                        // and rule-fired haptics share one code path.
                        await DispatchHaptic(new TriggerHapticAction
                        {
                            PatternName = effect.PatternName,
                            CustomPattern = effect.CustomPattern,
                            Intensity = effect.Intensity,
                            DurationMs = effect.DurationMs
                        });
                        break;

                    case EffectTypes.Flash:
                        // Inherit the user's CCP Flashes settings: image pool,
                        // sound, scale, opacity all come from FlashService's
                        // normal random-image path (passing null path = random).
                        var flashSound = App.Settings?.Current?.FlashAudioEnabled ?? true;
                        App.Flash?.TriggerFlashOnceWithImage(null, effect.DurationMs, flashSound);
                        break;

                    case EffectTypes.Bubble:
                        // maxBubbles is no longer per-effect; derive a sensible
                        // burst from the user's BubblesFrequency × segment width.
                        DispatchBubbleBurst(effect.DurationMs);
                        break;

                    case EffectTypes.Subliminal:
                        if (!string.IsNullOrWhiteSpace(effect.Text))
                            App.Subliminal?.FlashSubliminalCustom(effect.Text!, overrideDurationMs: effect.DurationMs);
                        break;

                    case EffectTypes.Overlay:
                        App.Overlay?.ShowOverlayTimed(effect.OverlayKind ?? OverlayKinds.PinkFilter, effect.DurationMs, effect.Opacity);
                        break;

                    default:
                        App.Logger?.Debug("Deeper trigger_effect: unknown effect_type \"{Type}\"", effect.EffectType);
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Deeper trigger_effect dispatch error: {Error}", ex.Message);
            }
        }

        private static void DispatchBubbleBurst(int durationMs)
        {
            // Bubbles have no per-event helper on the service; a brief Start/Stop
            // window with the dispatcher-owned timer keeps the surface area small.
            // DispatcherTimer (NOT Task.Delay) per CLAUDE.md known issue 6.
            // Density is governed by the user's CCP BubblesFrequency setting —
            // BubbleService.Start() reads that itself, so we just gate the
            // duration here.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            try
            {
                App.Bubbles?.Start();
                var stopTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(Math.Max(50, durationMs))
                };
                stopTimer.Tick += (_, _) =>
                {
                    stopTimer.Stop();
                    try { App.Bubbles?.Stop(); }
                    catch (Exception ex) { App.Logger?.Debug("DispatchBubbleBurst stop: {E}", ex.Message); }
                };
                stopTimer.Start();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DispatchBubbleBurst start: {E}", ex.Message);
            }
        }

        private static async Task DispatchHaptic(TriggerHapticAction haptic)
        {
            try
            {
                IList<double[]>? keyframes = null;
                if (haptic.CustomPattern != null && haptic.CustomPattern.Count > 0)
                    keyframes = haptic.CustomPattern;
                else if (!string.IsNullOrEmpty(haptic.PatternName)
                         && StockHapticPatterns.TryGet(haptic.PatternName, out var named) && named != null)
                    keyframes = named;

                if (keyframes == null) return;
                var samples = StockHapticPatterns.Sample(keyframes, haptic.Intensity, haptic.DurationMs);
                if (App.Haptics != null)
                    await App.Haptics.SetSyncPatternAsync(samples, haptic.DurationMs);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Deeper haptic dispatch error: {Error}", ex.Message);
            }
        }
    }
}
