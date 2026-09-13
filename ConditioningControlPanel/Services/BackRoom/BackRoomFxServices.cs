using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>
/// The real primitive sink: each contract primitive on the app's existing overlay services
/// (CONTRACT section 4). Called on the UI dispatcher by <see cref="DispatcherFxScheduler"/>.
///
/// It stops only what it started. The rain and the glitch wash are shared singletons with a chaos
/// run, and the one-shot flash and subliminal generations are service-wide, so StopAll touches each
/// of those only when the room fired one recently; the spiral and brain drain ride the sustained
/// holds, which already release without tearing down another owner.
/// </summary>
public sealed class BackRoomFxServices : IBackRoomFxSink
{
    private static BackRoomFx? _shared;
    private static readonly object SharedLock = new();

    /// <summary>The one dispatcher the room host and the dev rig share, so they share one hero gate.</summary>
    public static BackRoomFx Shared
    {
        get
        {
            lock (SharedLock)
                return _shared ??= new BackRoomFx(new BackRoomFxServices(), new DispatcherFxScheduler(), ReadEnvironment);
        }
    }

    /// <summary>How long after a fire StopAll still considers a shared one-shot channel the room's.</summary>
    private const int OwnershipMs = 12_000;

    private long _flashAt = long.MinValue / 2, _subAt = long.MinValue / 2, _rainAt = long.MinValue / 2, _washAt = long.MinValue / 2;
    private long _spiralUntil, _drainUntil;
    private DispatcherTimer? _spiralTimer, _drainTimer;
    private string? _drainKind;

    private static long Now => Environment.TickCount64;
    private static bool Recent(long at) => Now - at < OwnershipMs;

    /// <summary>Settings for one fire. Motion is the EFFECTIVE level (user setting capped by the OS).</summary>
    public static FxEnvironment ReadEnvironment()
    {
        var s = App.Settings?.Current;
        if (s == null)
            return new FxEnvironment(MotionFx.Level, BackRoomFxIntensity.Calm, new FxGates(false, false, false, false, false, false));
        return new FxEnvironment(MotionFx.Level, s.BackRoomFxIntensity,
            new FxGates(s.FlashEnabled, s.SubliminalEnabled, s.SpiralEnabled, s.BrainDrainEnabled, s.BrainDrainMeltEnabled,
                SpiralStillPath(s) != null));
    }

    /// <summary>A spiral the gif-full window can hold as a still frame: the user's own spiral file, if
    /// it is an image. A video spiral has no still here, so at Off it is skipped as motion.</summary>
    internal static string? SpiralStillPath(AppSettings s)
    {
        try
        {
            var p = s.SpiralPath;
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) return null;
            var ext = Path.GetExtension(p).ToLowerInvariant();
            return ext is ".gif" or ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" ? p : null;
        }
        catch (Exception ex) { Diag.Swallowed(ex, "spiral path probe"); return null; }
    }

    /// <summary>
    /// Map a dealt media url back to the local file behind it. Only the two origins the room maps
    /// (<c>ccp.assets</c> = the assets folder, <c>ccp.game</c> = Resources\web) are accepted, and
    /// the result must stay inside that root, so even a hostile url in a deal cannot name a path.
    /// </summary>
    internal static string? TryLocalPath(string? url, string? assetsRoot, string? webRoot)
    {
        try
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttps) return null;
            string? root = uri.Host switch
            {
                "ccp.assets" => assetsRoot,
                "ccp.game" => webRoot,
                _ => null,
            };
            if (string.IsNullOrEmpty(root)) return null;

            var rel = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            if (rel.Length == 0) return null;
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(rootFull, rel));
            return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception ex) { Diag.Swallowed(ex, "bad media url"); return null; }
    }

    public void FlashBurst(int amount)
    {
        var s = App.Settings?.Current;
        // FlashDuration is SECONDS; TriggerFlashOnce's customDuration is MILLISECONDS.
        int? duration = s != null && s.FlashDuration > 0 ? s.FlashDuration * 1000 : null;
        _flashAt = Now;
        App.Flash?.TriggerFlashOnce(amount, duration, null, true);
    }

    public void GifRain(int durationMs)
    {
        // A chaos run's rain already on screen keeps its own; a second cascade on top is noise.
        if (ChaosGifCascadeOverlay.IsRaining) return;
        _rainAt = Now;
        ChaosGifCascadeOverlay.Show(
            spawnRatePerSec: GifCascadePayload.SPAWN_RATE_PER_SEC,
            durationSec: Math.Max(1.0, durationMs / 1000.0),
            gifSize: GifCascadePayload.GIF_SIZE,
            fallSpeed: GifCascadePayload.FALL_SPEED,
            opacity: GifCascadePayload.OPACITY,
            startScale: GifCascadePayload.START_SCALE);
    }

    public void GlitchWash(int durationMs, double opacity)
    {
        _washAt = Now;
        ChaosFlashOverlay.Show(durationMs, opacity);
    }

    public void Subliminal(string text)
    {
        _subAt = Now;
        App.Subliminal?.FlashSubliminalCustom(text, null, null, true);
    }

    public void Spiral(int durationMs, double level, bool still)
    {
        var s = App.Settings?.Current;
        double opacity = 0.85;
        if (s != null && s.SpiralOpacity > 0) opacity = Math.Clamp(s.SpiralOpacity / 100.0, 0.05, 1.0);
        opacity = Math.Clamp(opacity * level, 0.02, 1.0);

        if (still)
        {
            var path = s != null ? SpiralStillPath(s) : null;
            if (path != null) ChaosFlashOverlay.ShowHero(path, durationMs, opacity, still: true);
            return;
        }

        // Sustained plus our own timer instead of ShowOverlayTimed: a timed overlay cannot be taken
        // down early, and suspend/close must end the spiral now, not when its timer runs out.
        App.Overlay?.ShowOverlaySustained("spiral", opacity);
        _spiralUntil = Math.Max(_spiralUntil, Now + durationMs);
        _spiralTimer = Rearm(_spiralTimer, _spiralUntil, StopSpiral);
    }

    public void BrainDrain(int durationMs, double level, bool melt)
    {
        var s = App.Settings?.Current;
        double strength = Math.Clamp((s?.BrainDrainIntensity ?? 20) / 100.0 * level, 0.01, 1.0);
        var kind = melt ? "braindrain_melt" : "braindrain";
        if (_drainKind != null && _drainKind != kind) App.Overlay?.HideOverlaySustained(_drainKind);
        _drainKind = kind;
        App.Overlay?.ShowOverlaySustained(kind, strength);
        _drainUntil = Math.Max(_drainUntil, Now + durationMs);
        _drainTimer = Rearm(_drainTimer, _drainUntil, StopDrain);
    }

    public void GifFull(BackRoomGif gif, int durationMs, bool still)
    {
        var path = TryLocalPath(gif.Url, App.EffectiveAssetsPath, Path.Combine(AppContext.BaseDirectory, "Resources", "web"));
        if (path == null || !File.Exists(path))
        {
            App.Logger?.Debug("[BackRoom] gif-full: dealt item {Key} has no local file", gif.Key);
            return;
        }
        ChaosFlashOverlay.ShowHero(path, durationMs, 0.9, still);
    }

    public void StopAll()
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        if (!disp.CheckAccess()) { disp.BeginInvoke(new Action(StopAll)); return; }
        StopSpiral();
        StopDrain();
        ChaosFlashOverlay.StopHero();
        if (Recent(_flashAt)) App.Flash?.StopOneShotFlashes();
        if (Recent(_subAt)) App.Subliminal?.StopOneShotSubliminals();
        if (Recent(_rainAt) && App.Chaos?.IsRunning != true) ChaosGifCascadeOverlay.CloseActive();
        if (Recent(_washAt) && App.Chaos?.IsRunning != true) ChaosFlashOverlay.CloseActive();
        _flashAt = _subAt = _rainAt = _washAt = long.MinValue / 2;
    }

    private void StopSpiral()
    {
        _spiralTimer?.Stop();
        _spiralTimer = null;
        if (_spiralUntil == 0) return;
        _spiralUntil = 0;
        App.Overlay?.HideOverlaySustained("spiral");
    }

    private void StopDrain()
    {
        _drainTimer?.Stop();
        _drainTimer = null;
        if (_drainKind == null) return;
        var kind = _drainKind;
        _drainKind = null;
        _drainUntil = 0;
        App.Overlay?.HideOverlaySustained(kind);
    }

    private static DispatcherTimer Rearm(DispatcherTimer? timer, long untilMs, Action onEnd)
    {
        timer?.Stop();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(50, untilMs - Now)) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            try { onEnd(); } catch (Exception ex) { App.Logger?.Warning(ex, "[BackRoom] fx overlay release failed"); }
        };
        t.Start();
        return t;
    }
}

/// <summary>The app's fx scheduler: one-shot DispatcherTimers on the UI dispatcher (never Task.Delay,
/// CLAUDE.md known issue 7).</summary>
public sealed class DispatcherFxScheduler : IFxScheduler
{
    public long NowMs => Environment.TickCount64;

    public IDisposable After(int delayMs, Action action)
    {
        var handle = new Handle();
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return handle;
        disp.BeginInvoke(new Action(() =>
        {
            if (handle.Cancelled || disp.HasShutdownStarted) return;
            var t = new DispatcherTimer(DispatcherPriority.Normal, disp) { Interval = TimeSpan.FromMilliseconds(Math.Max(0, delayMs)) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (!handle.Cancelled) action();
            };
            handle.Timer = t;
            t.Start();
        }));
        return handle;
    }

    private sealed class Handle : IDisposable
    {
        public volatile bool Cancelled;
        public DispatcherTimer? Timer;

        public void Dispose()
        {
            Cancelled = true;
            var t = Timer;
            if (t == null) return;
            try { t.Dispatcher.BeginInvoke(new Action(t.Stop)); }
            catch (Exception ex) { Diag.Swallowed(ex, "dispatcher gone"); }
        }
    }
}
