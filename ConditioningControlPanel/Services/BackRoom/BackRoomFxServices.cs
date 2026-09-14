using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom.Overlays;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>
/// The real primitive sink: each contract primitive on the app's existing overlay services
/// (CONTRACT section 4). Called on the UI dispatcher by <see cref="DispatcherFxScheduler"/>.
///
/// It stops only what it started. The rain and the glitch wash are shared singletons with a chaos
/// run, and the one-shot flash and subliminal generations are service-wide, so StopAll touches each
/// of those only when the room fired one recently; the brain drain rides the sustained hold, and the
/// Hypno v3 windows (10.13.B, every spiral Loom-woven) belong to the room alone.
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
    private long _drainUntil;
    private DispatcherTimer? _drainTimer;
    private string? _drainKind;

    private static long Now => Environment.TickCount64;
    private static bool Recent(long at) => Now - at < OwnershipMs;

    /// <summary>Settings for one fire. Motion is the EFFECTIVE level (user setting capped by the OS).</summary>
    public static FxEnvironment ReadEnvironment()
    {
        var s = App.Settings?.Current;
        if (s == null)
            return new FxEnvironment(MotionFx.Level, BackRoomFxIntensity.Calm, new FxGates(false, false, false, false, false, false));
        var spiralPath = s.SpiralPath;
        string? Woven(string preset) => BackRoomSpiralSource.Resolve(preset, spiralPath,
            Chaos.DtrhLoomStore.SpiralsFolder, WebRoot, File.Exists);
        double opacity = s.SpiralOpacity > 0 ? Math.Clamp(s.SpiralOpacity / 100.0, 0.05, 1.0) : 0.85;
        // A woven GIF always has a first frame, so the spiral's still exists whenever its weave does.
        return new FxEnvironment(MotionFx.Level, s.BackRoomFxIntensity,
            new FxGates(s.FlashEnabled, s.SubliminalEnabled, s.SpiralEnabled, s.BrainDrainEnabled, s.BrainDrainMeltEnabled,
                Woven(BackRoomSpiralSource.Screen) != null),
            Woven, opacity);
    }

    /// <summary><c>Resources\web</c>, the folder <c>ccp.game</c> maps.</summary>
    internal static string WebRoot => Path.Combine(AppContext.BaseDirectory, "Resources", "web");

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

    // ---- Hypno v3 primitives (CONTRACT 10.13.B), on the overlays in Services/BackRoom/Overlays ----

    /// <summary>The room's WebView2 on screen, set by the host while the room is open (null = no room:
    /// a gif-from grows from the primary screen's centre).</summary>
    internal static Func<RoomViewport?>? Viewport { get; set; }

    private static FxFromTarget Target(FxCssRect? from)
    {
        RoomViewport? vp = null;
        try { vp = Viewport?.Invoke(); } catch (Exception ex) { Diag.Swallowed(ex, "room viewport read"); }
        return BackRoomOverlayMath.MapFrom(from, vp, BackRoomOverlayScreens.All());
    }

    private static string? LocalFile(BackRoomGif? gif)
    {
        if (gif == null) return null;
        var path = TryLocalPath(gif.Url, App.EffectiveAssetsPath, WebRoot);
        if (path != null && File.Exists(path)) return path;
        App.Logger?.Debug("[BackRoom] dealt item {Key} has no local file", gif.Key);
        return null;
    }

    public void Wash(FxRgb color, double peak, BackRoomGif? picture)
        => BackRoomWashOverlay.Show(color, peak, LocalFile(picture), picture == null ? null : Target(null).ScreenPx);

    public void GifFrom(BackRoomGif gif, FxCssRect? from, int durationMs, double scale, double dim, bool still)
    {
        if (LocalFile(gif) is not { } path) return;
        double aspect = gif.W > 0 && gif.H > 0 ? (double)gif.W / gif.H : 4.0 / 3;
        BackRoomGifFromOverlay.Show(path, aspect, Target(from), durationMs, scale, dim, still);
    }

    public void SpiralLoom(string gifPath, int durationMs, double alpha, bool hold, bool still)
        => BackRoomLoomSpiralOverlay.Show(gifPath, durationMs, alpha, still);

    public void ReleaseSpiralLoom() => BackRoomLoomSpiralOverlay.Release();

    public void ReleaseBrainDrain() => StopDrain();

    public void Tunnel(double level, bool still) => BackRoomTunnelOverlay.Set(level, still);

    public void CancelTunnel() => BackRoomTunnelOverlay.Cancel();

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
        if (LocalFile(gif) is { } path) ChaosFlashOverlay.ShowHero(path, durationMs, 0.9, still);
    }

    public void StopAll()
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        if (!disp.CheckAccess()) { disp.BeginInvoke(new Action(StopAll)); return; }
        StopDrain();
        ChaosFlashOverlay.StopHero();
        BackRoomWashOverlay.Stop();
        BackRoomGifFromOverlay.Stop();
        BackRoomLoomSpiralOverlay.Stop();
        BackRoomTunnelOverlay.Cancel();
        if (Recent(_flashAt)) App.Flash?.StopOneShotFlashes();
        if (Recent(_subAt)) App.Subliminal?.StopOneShotSubliminals();
        if (Recent(_rainAt) && App.Chaos?.IsRunning != true) ChaosGifCascadeOverlay.CloseActive();
        if (Recent(_washAt) && App.Chaos?.IsRunning != true) ChaosFlashOverlay.CloseActive();
        _flashAt = _subAt = _rainAt = _washAt = long.MinValue / 2;
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
