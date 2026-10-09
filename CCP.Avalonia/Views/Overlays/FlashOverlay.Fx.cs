using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// Flash juice, the WPF 7.1.5 <c>FlashService.SpawnFlashWindow</c> / <c>OnFlashClicked</c> half
    /// that is not playback: the lucky roll (x10 XP, gold pulsing glow), the sparkle-boost glow,
    /// rounded corners (<see cref="FlashCorners"/>), per-flash XP, stay-until-popped, and on a pop
    /// the haptic, the hydra multiply and the exit style (<see cref="FlashExit"/>). The rules are
    /// Core (<see cref="FlashFxRules"/>); this file only applies them to overlay windows.
    ///
    /// <para>ponytail: no lucky chime / LuckyProc toast yet (flash audio is the wc-flash-av lane),
    /// no Natasha's favourite (needs a Chaster CanBook seam on this head), no gaze focus service
    /// (the GazeTargets / GazePop / BoostLifetime seam below is what one would call).</para>
    /// </summary>
    internal static partial class FlashOverlay
    {
        /// <summary>The last host a burst came from: hydra children re-read the screens through it.</summary>
        private static Visual? _fxHost;

        /// <summary>Hydra generation of the spawn in progress (0 = an original flash). Set around
        /// <c>Spawn</c> by <see cref="SpawnHydraChildren"/>, read by <see cref="ApplyFx"/>.</summary>
        private static int _pendingGeneration;
        private static int? _pendingLifetimeMs;
        private static FlashExitStyle? _lastExit;

        internal static Func<string, bool> OwnsGrant = id => PrizeOwnership.IsGranted(id);

        /// <summary>WPF OwnsFlashV2: drift-bounce or pendulum grant.</summary>
        internal static bool OwnsFlashV2() =>
            OwnsGrant(PrizeOwnership.FlashDriftBounce) || OwnsGrant("fx.flash.pendulum");

        internal static PerformanceTier Tier(AppSettings s) =>
            s.PerformanceMode ? PerformanceTier.Performance : PerformanceTier.Quality;

        /// <summary>The concurrency cap for this spawn: a stay run may hold 40 (FlashStayRule).</summary>
        internal static int SpawnCap(AppSettings s) =>
            FlashStayRule.Cap(MaxConcurrent, StayApplies(s), sharedHost: true);

        internal static bool StayApplies(AppSettings s) =>
            FlashStayRule.Applies(s.FlashStayUntilPopped, s.FlashClickable, pointFired: false);

        /// <summary>Lifetime this spawn lives: a hydra child's own, the 10-minute stay safety, or
        /// the burst's.</summary>
        internal static TimeSpan ResolveLifetime(TimeSpan burst, AppSettings s)
        {
            if (_pendingLifetimeMs is { } child) return TimeSpan.FromMilliseconds(child);
            return StayApplies(s) ? TimeSpan.FromMilliseconds(FlashStayRule.SafetyLifetimeMs) : burst;
        }

        /// <summary>
        /// Roll and dress one fresh window: lucky, glow, corners, XP. Returns the window rect,
        /// grown by the glow pad (WPF host mode expands the bookkeeping rect the same way).
        /// </summary>
        internal static PixelRect ApplyFx(FlashOverlayWindow w, PixelRect rect, PixelRect screen, TimeSpan lifetime, AppSettings s, Random rng)
        {
            var gen = _pendingGeneration;
            var multiplier = FlashFxRules.RollLucky(SkillTreeRules.HasSkill(s, FlashFxRules.LuckySkillId), gen, false, rng);
            var glow = FlashFxRules.ResolveGlow(s.FlashGlowEnabled, Tier(s), multiplier > 1,
                SkillTreeRules.GetSparkleBoostTier(s), natasha: false);
            var k = w.DesktopScaling > 0 ? w.DesktopScaling : 1.0;
            var radiusPx = FlashCorners.Resolve(s.FlashRoundedCorners, OwnsFlashV2(), glow.HasGlow,
                Math.Min(rect.Width, rect.Height), k);
            w.Fx = new FlashFxState(gen, multiplier > 1, (int)lifetime.TotalMilliseconds, screen);
            w.ExpiresAt = DateTime.Now + lifetime;

            if (multiplier > 1) Log.Information("Flash: lucky flash, {X}x XP", multiplier);
            // WPF pays XP at spawn (FlashService.cs:2164); this head plays no flash sound yet, so 4 base.
            CoreProgression.AddXP(FlashFxRules.Xp(false, s.HydraLinkedTiming, gen, multiplier), "Flash");
            if (gen == 0) _ = CoreHaptics.Service?.FlashDecayVibeAsync();

            var pad = glow.HasGlow ? (int)Math.Ceiling(glow.BlurRadius / 2 * k) : 0;
            w.ApplyLook(glow, radiusPx / k, glow.BlurRadius / 2, s.MotionLevel != MotionLevel.Off);
            return pad == 0 ? rect : new PixelRect(rect.X - pad, rect.Y - pad, rect.Width + 2 * pad, rect.Height + 2 * pad);
        }

        /// <summary>WPF OnFlashClicked after the window chose to pop: tube event, click haptic,
        /// hydra multiply. The window plays its own exit.</summary>
        internal static void OnFlashPopped(FlashOverlayWindow w, bool fromGaze)
        {
            CoreTubeEvents.RaiseFlashClicked();
            _ = CoreHaptics.Service?.FlashClickVibeAsync();
            var s = CoreSettings.Current;
            var fx = w.Fx;
            if (fx == null) return;
            var active = Active.Count(a => a.Window != w);
            var n = FlashFxRules.HydraSpawnCount(s.CorruptionMode, s.HydraLimit, false, fromGaze, fx.HydraGeneration, active);
            if (n <= 0) return;
            var remaining = (w.ExpiresAt - DateTime.Now).TotalMilliseconds;
            var lifeMs = FlashFxRules.HydraChildLifetimeMs(s.HydraLinkedTiming, fx.OriginalLifetimeMs, remaining);
            SpawnHydraChildren(n, fx.HydraGeneration + 1, lifeMs, fx.Screen);
        }

        /// <summary>Exit style for this pop (WPF BuildExit): None = the old quick fade.</summary>
        internal static FlashExitState? PickExit(AppSettings s, Random rng)
        {
            if (FlashExit.Pick(s.FlashExitStyle, _lastExit, rng) is not { } style) return null;
            _lastExit = style;
            return FlashExit.Begin(style, s.MotionLevel, rng.Next());
        }

        /// <summary>WPF TriggerMultiplication: up to two silent children on the parent's screen.</summary>
        private static async void SpawnHydraChildren(int count, int generation, int lifetimeMs, PixelRect parentScreen)
        {
            try
            {
                if (_fxHost is not { } host || _closed) return;
                var gen = _generation;
                var screens = ScreenList.Enumerate(host);
                var idx = screens.ToList().FindIndex(x => x.Bounds == parentScreen);
                var targets = idx >= 0 ? new[] { idx } : Enumerable.Range(0, screens.Count).ToArray();
                var s = CoreSettings.Current;
                var occupied = Active.Select(a => a.Rect).ToList();
                var pics = await Task.Run(() => LoadPictures(count, screens, targets, s, occupied, null));
                var fade = TimeSpan.FromSeconds(s.FadeDuration * FlashPlacement.FadeSecondsPerPercent);
                var alpha = Math.Clamp(s.FlashOpacity / 100.0, 0, 1);
                foreach (var (bmp, rect, path, screen, frames) in pics)
                {
                    if (_closed || gen != _generation || Active.Count >= SpawnCap(s))
                    {
                        bmp.Dispose();
                        if (frames != null) foreach (var f in frames.Value.Frames) f.Dispose();
                        continue;
                    }
                    _pendingGeneration = generation;
                    _pendingLifetimeMs = lifetimeMs;
                    try { if (Spawn(bmp, rect, screen, alpha, fade, TimeSpan.FromMilliseconds(lifetimeMs), frames)) App.MediaHistory?.RecordImages(new[] { path }); }
                    finally { _pendingGeneration = 0; _pendingLifetimeMs = null; }
                }
            }
            catch (Exception ex) { Log.Error(ex, "Flash: hydra spawn failed"); }
        }

        // ---- Gaze seam (WPF FlashService.GetGazeTargets / GazePop; BoostLifetime lives on the window) ----

        /// <summary>Windows a gaze dwell may target; empty unless gaze-pop or stare-linger is on.</summary>
        internal static IReadOnlyList<FlashOverlayWindow> GazeTargets()
        {
            var s = CoreSettings.Current;
            if (!s.FlashGazePopEnabled && !s.FlashGazeLingerEnabled) return Array.Empty<FlashOverlayWindow>();
            return Active.Select(a => a.Window).Where(w => !w.IsLeaving).ToList();
        }

        /// <summary>A gaze dwell completed on <paramref name="w"/>: the same pop as a click, flagged
        /// so hydra takes only the first hop (#784).</summary>
        internal static void GazePop(FlashOverlayWindow w)
        {
            if (!CoreSettings.Current.FlashGazePopEnabled) return;
            w.Pop(fromGaze: true);
        }

        /// <summary>Stare-linger: each dwell tick keeps the flash alive FlashGazeLingerExtensionMs more.</summary>
        internal static void GazeLinger(FlashOverlayWindow w)
        {
            var s = CoreSettings.Current;
            if (s.FlashGazeLingerEnabled) w.BoostLifetime(s.FlashGazeLingerExtensionMs);
        }
    }

    /// <summary>Per-flash juice state (WPF FlashWindow.HydraGeneration / IsLucky / OriginalLifetimeMs / Monitor).</summary>
    internal sealed record FlashFxState(int HydraGeneration, bool IsLucky, int OriginalLifetimeMs, PixelRect Screen);
}
