using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx);
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

        public Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx)
        {
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
            ScreenShakeAction ss => $"screen_shake {ss.Intensity:0.00} for {ss.DurationMs}ms",
            SetIntensityAction si => $"set_intensity {si.Value:0.00}",
            NoOpEnhancementAction nop => $"<unknown action: {nop.OriginalType}>",
            _ => a.GetType().Name
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

        public async Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx)
        {
            var line = $"t={ctx.CurrentTimeSeconds:0.00}s  {LoggingActionDispatcher.DescribeAction(action)}";
            try { await _inner.DispatchAsync(action, ctx); }
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
        public async Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx)
        {
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

                    case ScreenShakeAction:
                        // No screen-shake primitive exists yet; logged so missing
                        // capability is visible without crashing creator content.
                        App.Logger?.Debug("Deeper: screen_shake action unsupported in v1");
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
            double? target = seek.Target switch
            {
                SeekTargets.Time => seek.Time,
                SeekTargets.RegionStart => ResolveRegion(ctx, seek.RegionId)?.Start,
                SeekTargets.RegionEnd => ResolveRegion(ctx, seek.RegionId)?.End,
                _ => null
            };
            if (target.HasValue) ctx.Source.Seek(target.Value);
        }

        private static void DispatchLoopRegion(LoopRegionAction loop, EnhancementDispatchContext ctx)
        {
            var id = loop.RegionId ?? ctx.CurrentRegionId;
            if (id == null) return;
            var region = ResolveRegion(ctx, id);
            if (region == null) return;
            // Loop is implemented as a seek-back to region start; the engine
            // (not the dispatcher) is responsible for re-firing on the next
            // tick if it crosses the end again.
            ctx.Source.Seek(region.Start);
        }

        private static Region? ResolveRegion(EnhancementDispatchContext ctx, string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return ctx.Enhancement.Regions.FirstOrDefault(r => r.Id == id);
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
