using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models.Deeper;
using Serilog;

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
            // Per-tick opacity-ramp updates would flood the preview log; dry-run has
            // no real overlay to update anyway, so drop them silently.
            if (action is TriggerEffectAction { Phase: EffectPhase.Update }) return Task.CompletedTask;
            var line = $"t={ctx.CurrentTimeSeconds:0.00}s  {DescribeAction(action)}";
            lock (_gate)
            {
                _recent.Enqueue(line);
                while (_recent.Count > MaxRecent) _recent.Dequeue();
            }
            Log.Information("Deeper preview: {Line}", line);
            try { ActionLogged?.Invoke(line); }
            catch (Exception ex) { Log.Debug("LoggingActionDispatcher subscriber error: {Error}", ex.Message); }
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
                EffectTypes.Speak      => $"effect speak \"{te.SpeakTarget}\" x{te.SpeakRequiredReps}{phaseSuffix}",
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
                catch (Exception ex) { Log.Debug("RecordingActionDispatcher subscriber error: {Error}", ex.Message); }
            }
        }

        public void Clear()
        {
            lock (_gate) _recent.Clear();
        }
    }

}
