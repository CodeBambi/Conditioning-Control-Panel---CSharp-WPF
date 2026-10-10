using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Haptics.Core;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>
    /// Production dispatcher. Delegates to existing CCP services. Every path
    /// is best-effort and silent on failure — a missing device or unsupported
    /// action must never throw out of the engine tick.
    /// </summary>
    /// <remarks>
    /// On this head the effects are the overlay windows under Views/Overlays, reached through the
    /// shell's effect door (portal panic bind first). An action with no twin here is logged once
    /// per bind and does nothing: the voice prompt (speak).
    /// </remarks>
    internal sealed class RealActionDispatcher : IActionDispatcher, IEnhancementRunCleanup
    {
        /// <summary>The visual host: the shell. Tests swap it.</summary>
        internal static Func<Window?> HostProvider = () =>
            (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        /// <summary>Flash / subliminal / drain doors (tests swap them; production goes to the overlays).</summary>
        internal static Action<Window, int> FlashDoor = (host, ms) => FlashOverlay.TriggerOnce(host, durationMs: ms);
        internal static Action<Window, string> SubliminalDoor = (host, text) => SubliminalOverlay.Show(host, text);

        private readonly HashSet<string> _loggedNoTwin = new();

        /// <summary>Actions this bind met that the head cannot draw (one entry per kind).</summary>
        internal IReadOnlyCollection<string> NoTwin { get { lock (_bandGate) return _loggedNoTwin.ToArray(); } }

        private void LogNoTwin(string what)
        {
            bool first;
            lock (_bandGate) first = _loggedNoTwin.Add(what);
            if (first) Log.Information("Deeper: {What} has no surface on this head, skipped", what);
        }

        private static void OnUi(Action a)
        {
            if (Dispatcher.UIThread.CheckAccess()) a(); else Dispatcher.UIThread.Post(a);
        }

        /// <summary>WPF FireVisualEffect's rule on this head: UI thread, through the shell's effect
        /// door, only where click-through overlays exist.</summary>
        private static void Overlay(Action<Window> show) => OnUi(() =>
        {
            if (!X11Overlay.IsAvailable || HostProvider() is null) return;
            ConditioningControlPanel.Avalonia.CompanionEffects.StartEffect(() => { if (HostProvider() is { } h) show(h); });
        });

        // -- IEnhancementRunCleanup (engine Stop) --------------------------------------------

        public void ResetOverlayBands() => OnUi(() =>
        {
            bool any;
            lock (_bandGate) { any = _overlayBandKind.Values.Any(IsDrain); _overlayBandKind.Clear(); }
            if (any) BrainDrainOverlay.EndTimed();
            SyncPinkHold(null);
            SyncSpiralHold(null);
            ScreenShake.Stop();   // engine stop and panic: every window at rest now
        });

        /// <summary>The shake (tests swap it).</summary>
        internal static Action<double, int> ShakeDoor = (intensity, ms) => ScreenShake.Shake(intensity, ms);

        private void SyncHold(string kind, double? opacity)
        {
            if (kind == OverlayKinds.Spiral) SyncSpiralHold(opacity); else SyncPinkHold(opacity);
        }

        /// <summary>The spiral follows the open spiral bands, as the tint does below: up at the band's
        /// opacity while one is tracked (WPF ShowOverlaySustained / ShowOverlayTimed "spiral", #1051: the
        /// authored opacity, not the user's slider), back to the user's own switch when the last closes.</summary>
        private void SyncSpiralHold(double? opacity)
        {
            bool any;
            lock (_bandGate) any = _overlayBandKind.Values.Any(k => k == OverlayKinds.Spiral);
            if (!any)
            {
                if (!SpiralOverlay.IsHeldBy(SpiralOverlay.DeeperOwner)) return;
                SpiralRelease(HostProvider());
                return;
            }
            if (opacity.HasValue) _spiralBandOpacity = Math.Clamp(opacity.Value, 0, 1);
            var held = _spiralBandOpacity;
            Overlay(host =>
            {
                bool still;
                lock (_bandGate) still = _overlayBandKind.Values.Any(k => k == OverlayKinds.Spiral);
                if (still) SpiralHold(host, held);
            });
        }

        private double _spiralBandOpacity = 1;

        /// <summary>The spiral's hold and release (tests swap them to run without a screen).</summary>
        internal static Action<Window, double> SpiralHold = (host, opacity) =>
            SpiralOverlay.Hold(host, SpiralOverlay.DeeperOwner, new SpiralHold(opacity));
        internal static Action<Window?> SpiralRelease = host => SpiralOverlay.Release(SpiralOverlay.DeeperOwner, host);

        /// <summary>The pink tint follows the open pink bands: up at <paramref name="opacity"/> while
        /// one is tracked, handed back to the user's own switch when the last one closes. Stops and
        /// clears go straight through (never behind the effect door: a stop must not wait).</summary>
        private void SyncPinkHold(double? opacity)
        {
            bool anyPink;
            lock (_bandGate) anyPink = _overlayBandKind.Values.Any(k => k == OverlayKinds.PinkFilter);
            if (!anyPink)
            {
                if (!PinkFilterOverlay.BandHold.HasValue) return;
                PinkFilterOverlay.BandHold = null;
                if (HostProvider() is { } h) PinkRefresh(h); else PinkFilterOverlay.CloseAll();
                return;
            }
            if (opacity.HasValue) PinkFilterOverlay.BandHold = Math.Clamp(opacity.Value, 0, 1);
            Overlay(host => { if (PinkFilterOverlay.BandHold.HasValue) PinkRefresh(host); });
        }

        /// <summary>The tint's repaint (tests swap it to count calls without a screen).</summary>
        internal static Action<Window> PinkRefresh = host => PinkFilterOverlay.Refresh(host);

        /// <summary>WPF StopOneShotFlashes. The port's flash layer has one generation, so this takes
        /// down what is on screen; the ambient schedule keeps its rhythm (CloseAll final: false).</summary>
        public void StopOneShotFlashes() => OnUi(() => FlashOverlay.CloseAll(final: false));

        public void StopOneShotSubliminals() => OnUi(SubliminalOverlay.CloseAll);

        // Per-EffectId tracking for band-mode effects. Lets Stop hide the right
        // overlay kind and Restart re-issue a haptic with its previously dispatched
        // samples + intensity but a freshly-computed remaining duration.
        private readonly Dictionary<string, string> _overlayBandKind = new();
        private readonly Dictionary<string, BandHapticState> _hapticBandState = new();
        // Per-EffectId live voice-prompt sessions so band Stop can tear down the right one.
        
        private readonly object _bandGate = new();
        // Config-state haptic skips (no service / disabled / disconnected) repeat once per
        // timeline entry, and the default config - haptics on, Mock provider, nothing
        // connected - hits one on every entry. Log each distinct reason once per bind at
        // Information so a silent drop still leaves a line (#764), then drop to Debug.
        private readonly HashSet<string> _loggedHapticSkips = new();

        private sealed class BandHapticState
        {
            public float[] Samples = System.Array.Empty<float>();
            public double Intensity = 1.0;
            public int OriginalDurationMs;
            public ToyRole? Target;
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
                        await DispatchPlayAudio(pa);
                        break;

                    case TriggerHapticAction haptic:
                        await DispatchHaptic(haptic);
                        break;

                    case TriggerEffectAction effect:
                        await DispatchTriggerEffect(effect, ctx, ct);
                        break;

                    case ScreenShakeAction shake:
                        ShakeDoor(shake.Intensity, shake.DurationMs);   // WPF App.ScreenShake.Shake
                        break;

                    case SetIntensityAction:
                        // No central session-intensity setting yet; log so creators
                        // see firing without a runtime side-effect.
                        Log.Debug("Deeper: set_intensity action stubbed in v1");
                        break;

                    case NoOpEnhancementAction:
                        // Round-tripped placeholder — never dispatch.
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Deeper action dispatch error ({Type}): {Error}", action.Type, ex.Message);
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

        private static Task DispatchPlayAudio(PlayAudioAction pa)
        {
            if (string.IsNullOrEmpty(pa.Path)) return Task.CompletedTask;
            try
            {
                var path = pa.Path;
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(CorePaths.EffectiveAssets, path);
                if (!File.Exists(path))
                {
                    Log.Debug("Deeper play_audio: file not found ({Path})", path);
                    return Task.CompletedTask;
                }
                var st = CoreSettings.Current;
                if (pa.DuckOtherAudio && st?.AudioDuckingEnabled == true)
                    CoreAudio.Duck(st.DuckingLevel);
                // WPF AudioService.PlaySound(path, volume 0..100).
                CoreAudio.PlayOneShot(path, (float)Math.Clamp(pa.Volume / 100.0, 0, 1), "deeper");
            }
            catch (Exception ex)
            {
                Log.Debug("Deeper play_audio error: {Error}", ex.Message);
            }
            return Task.CompletedTask;
        }

        private async Task DispatchTriggerEffect(TriggerEffectAction effect, EnhancementDispatchContext ctx, CancellationToken ct)
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
                        var flashMs = effect.DurationMs;
                        Overlay(host => FlashDoor(host, flashMs));
                        break;

                    case EffectTypes.Bubble:
                        // maxBubbles is no longer per-effect; derive a sensible
                        // burst from the user's BubblesFrequency × segment width.
                        DispatchBubbleBurst(effect.DurationMs, ct);
                        break;

                    case EffectTypes.Subliminal:
                        if (!string.IsNullOrWhiteSpace(effect.Text))
                        {
                            var text = effect.Text!;
                            Overlay(host => SubliminalDoor(host, text));
                        }
                        break;

                    case EffectTypes.Overlay:
                        DispatchOverlayEffect(effect);
                        break;

                    case EffectTypes.Speak:
                        LogNoTwin("speak");
                        break;

                    default:
                        Log.Debug("Deeper trigger_effect: unknown effect_type \"{Type}\"", effect.EffectType);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Deeper trigger_effect dispatch error: {Error}", ex.Message);
            }
        }

        /// <summary>WPF ShowOverlaySustained / HideOverlaySustained / SetSustainedOverlayOpacity /
        /// ShowOverlayTimed for the pink filter, on the tint's band hold.</summary>
        private void DispatchHoldBand(TriggerEffectAction effect, string kind) => OnUi(() =>
        {
            switch (effect.Phase)
            {
                case EffectPhase.Start:
                    lock (_bandGate) _overlayBandKind[effect.EffectId ?? kind] = kind;
                    SyncHold(kind, effect.Opacity);
                    break;

                case EffectPhase.Stop:
                    lock (_bandGate) _overlayBandKind.Remove(effect.EffectId ?? kind);
                    SyncHold(kind, null);
                    break;

                case EffectPhase.Update:
                    SyncHold(kind, effect.Opacity);
                    break;

                case EffectPhase.Restart:
                    break;

                case EffectPhase.OneShot:
                default:
                {
                    var id = "oneshot:" + Guid.NewGuid().ToString("N");
                    lock (_bandGate) _overlayBandKind[id] = kind;
                    SyncHold(kind, effect.Opacity);
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(50, effect.DurationMs)) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        lock (_bandGate) _overlayBandKind.Remove(id);
                        SyncHold(kind, null);
                    };
                    timer.Start();
                    break;
                }
            }
        });

        private static bool IsDrain(string kind) => kind is OverlayKinds.BrainDrain or OverlayKinds.BrainDrainMelt;

        private void DispatchOverlayEffect(TriggerEffectAction effect)
        {
            var kind = effect.OverlayKind ?? OverlayKinds.PinkFilter;
            if (kind == OverlayKinds.PinkFilter)
            {
                DispatchHoldBand(effect, kind);
                return;
            }
            if (kind == OverlayKinds.Spiral)
            {
                // The spiral's hold: up for the band at the authored opacity, the user's switch untouched.
                DispatchHoldBand(effect, kind);
                return;
            }
            if (!IsDrain(kind))
            {
                LogNoTwin("overlay " + kind);
                return;
            }
            // WPF: the opacity handed over IS the drain's strength.
            var strength = (int)Math.Round(Math.Clamp(effect.Opacity, 0, 1) * 100);
            var melt = kind == OverlayKinds.BrainDrainMelt;
            var ms = Math.Max(50, effect.DurationMs);
            switch (effect.Phase)
            {
                case EffectPhase.Stop:
                    if (!string.IsNullOrEmpty(effect.EffectId))
                    {
                        lock (_bandGate) _overlayBandKind.Remove(effect.EffectId!);
                    }
                    OnUi(BrainDrainOverlay.EndTimed);
                    break;

                case EffectPhase.Restart:
                case EffectPhase.Update:
                    // Already up; the timed haze has no live strength dial on this head.
                    break;

                case EffectPhase.Start:
                    if (!string.IsNullOrEmpty(effect.EffectId))
                    {
                        lock (_bandGate) _overlayBandKind[effect.EffectId!] = kind;
                    }
                    // A band's Start carries the time left in the band, so the timed haze ends
                    // with it even if the Stop is lost.
                    Overlay(host => BubbleOverlay.ShowTimedDrain(host, strength, melt, ms));
                    break;

                case EffectPhase.OneShot:
                default:
                    Overlay(host => BubbleOverlay.ShowTimedDrain(host, strength, melt, ms));
                    break;
            }
        }

        // Burst ownership. UI-thread confined. Static because the bubble field is app-global
        // while a dispatcher is per-engine: two engines' bursts must share one claim.
        private static int _bubbleBurstHolds;
        private static bool _bubbleBurstOwnsService;

        private static void DispatchBubbleBurst(int durationMs, CancellationToken ct) => OnUi(() =>
        {
            try
            {
                if (ct.IsCancellationRequested) return;

                // Only tear down what a burst actually started: if the user's ambient Bubbles are
                // already running, this burst rides them and the release leaves them alone.
                if (_bubbleBurstHolds == 0)
                    _bubbleBurstOwnsService = !BubbleOverlay.IsRunning;
                _bubbleBurstHolds++;
                ConditioningControlPanel.Avalonia.CompanionEffects.StartEffect(() => { if (_bubbleBurstHolds > 0) ConditioningControlPanel.Services.CoreBubbles.Start(); });

                bool released = false;
                var stopTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(Math.Max(50, durationMs))
                };
                void Release()
                {
                    if (released) return;
                    released = true;
                    stopTimer.Stop();
                    if (_bubbleBurstHolds > 0) _bubbleBurstHolds--;
                    if (_bubbleBurstHolds == 0 && _bubbleBurstOwnsService)
                    {
                        _bubbleBurstOwnsService = false;
                        ConditioningControlPanel.Services.CoreBubbles.Stop();
                    }
                }
                stopTimer.Tick += (_, _) => Release();
                // Engine Stop cancels the run token: release at once.
                if (ct.CanBeCanceled)
                    ct.Register(() => { try { Dispatcher.UIThread.Post(Release); } catch { } });
                stopTimer.Start();
            }
            catch (Exception ex)
            {
                Log.Debug("DispatchBubbleBurst start: {E}", ex.Message);
            }
        });

        private void LogHapticSkip(string reason)
        {
            bool first;
            lock (_bandGate) first = _loggedHapticSkips.Add(reason);
            if (first)
                Log.Information("Deeper haptic send skipped: {Reason}", reason);
            else
                Log.Debug("Deeper haptic send skipped: {Reason}", reason);
        }

        private async Task DispatchHaptic(TriggerHapticAction haptic)
        {
            var haptics = CoreHaptics.Service;
            try
            {
                // Band-mode Stop is just a cancel — no pattern lookup needed.
                if (haptic.Phase == EffectPhase.Stop)
                {
                    if (!string.IsNullOrEmpty(haptic.EffectId))
                    {
                        lock (_bandGate) _hapticBandState.Remove(haptic.EffectId!);
                    }
                    if (haptics == null)
                    {
                        LogHapticSkip("haptic service unavailable (phase=Stop)");
                        return;
                    }
                    await haptics.StopAsync();
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
                    Log.Information("Deeper haptic send skipped: no pattern (name='{Name}', custom={Count})",
                        haptic.PatternName, haptic.CustomPattern?.Count ?? 0);
                    return;
                }

                var samples = StockHapticPatterns.Sample(keyframes, haptic.Intensity, haptic.DurationMs);
                if (samples.Length == 0)
                {
                    Log.Information("Deeper haptic send skipped: empty samples (name='{Name}', duration={Duration}ms)",
                        haptic.PatternName, haptic.DurationMs);
                    return;
                }

                if (haptics == null)
                {
                    LogHapticSkip("haptic service unavailable");
                    return;
                }

                if (!haptics.Settings.Enabled)
                {
                    LogHapticSkip("haptics disabled in settings");
                    return;
                }

                if (!haptics.IsConnected)
                {
                    LogHapticSkip($"no provider connected ({haptics.Settings.Provider})");
                    return;
                }

                // Restart: send Vibrate:0 first to clear LovenseProvider's 1-second
                // same-level debounce — without this, a same-level re-issue after a
                // backward seek is silently dropped (see lovense_pattern_api_flat memory).
                if (haptic.Phase == EffectPhase.Restart)
                    await haptics.StopAsync();

                if (!string.IsNullOrEmpty(haptic.EffectId))
                {
                    lock (_bandGate)
                    {
                        _hapticBandState[haptic.EffectId!] = new BandHapticState
                        {
                            Samples = samples,
                            Intensity = haptic.Intensity,
                            OriginalDurationMs = haptic.DurationMs,
                            Target = haptic.Target
                        };
                    }
                }

                await haptics.SetSyncPatternAsync(samples, haptic.DurationMs, haptic.Target);
            }
            catch (Exception ex)
            {
                Log.Debug("Deeper haptic dispatch error: {Error}", ex.Message);
            }
        }
    }
}
