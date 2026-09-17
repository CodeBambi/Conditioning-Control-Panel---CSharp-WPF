using System;
using System.Collections.Generic;
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
                return _shared ??= new BackRoomFx(new BackRoomFxServices(), new DispatcherFxScheduler(), ReadEnvironment, xp: PayPictureXp);
        }
    }

    /// <summary>A Back Room picture pays as a flash image does in FlashService: the base times the lucky flash roll,
    /// <c>XPSource.Flash</c>, through AddXP (idle suppression, skill multiplier, login gate). CONTRACT 10.14.</summary>
    internal static void PayPictureXp(int baseXp)
    {
        int lucky = App.SkillTree?.RollLuckyFlash() ?? 1;
        App.Progression?.AddXP(baseXp * lucky, XPSource.Flash);
    }

    /// <summary>How long after a fire StopAll still considers a shared one-shot channel the room's.</summary>
    private const int OwnershipMs = 12_000;

    private long _flashAt = long.MinValue / 2, _subAt = long.MinValue / 2, _rainAt = long.MinValue / 2, _washAt = long.MinValue / 2;
    private long _drainUntil;
    private DispatcherTimer? _drainTimer;
    private string? _drainKind;

    /// <summary>The melt's alpha ramp (0 to its peak over the step) is written this often.</summary>
    private const int MeltRampStepMs = 100;
    private DispatcherTimer? _meltRamp;
    private long _meltStart;
    private int _meltMs;
    private double _meltPeak;

    private static long Now => Environment.TickCount64;
    private static bool Recent(long at) => Now - at < OwnershipMs;

    /// <summary>Settings for one fire: the EFFECTIVE motion level (the user's setting capped by the OS animation flag,
    /// safety line a) and the room's intensity (safety line b). No feature toggle: the Back Room is an authored show.
    /// No settings at all (a desk run before they load) reads as Calm.</summary>
    public static FxEnvironment ReadEnvironment()
    {
        var s = App.Settings?.Current;
        var spiralPath = s?.SpiralPath;
        string? Woven(string preset) => WovenFor(preset, spiralPath);
        return new FxEnvironment(MotionFx.Level, s?.BackRoomFxIntensity ?? BackRoomFxIntensity.Calm, Woven);
    }

    // The woven spiral's file probes, kept per SpiralPath for a few seconds: every fire and every tunnel
    // update (up to 10 a second) reads the environment on the UI thread.
    private const int WovenCacheMs = 5000;
    private static readonly object WovenLock = new();
    private static readonly Dictionary<string, string?> WovenHits = new(StringComparer.Ordinal);
    private static string? _wovenFor;
    private static long _wovenAt = long.MinValue / 2;

    private static string? WovenFor(string preset, string? spiralPath)
    {
        lock (WovenLock)
        {
            if (_wovenFor != spiralPath || Now - _wovenAt > WovenCacheMs) { WovenHits.Clear(); _wovenFor = spiralPath; _wovenAt = Now; }
            preset = BackRoomSpiralSource.Preset(preset);
            if (!WovenHits.TryGetValue(preset, out var hit))
                WovenHits[preset] = hit = BackRoomSpiralSource.Resolve(preset, spiralPath, Chaos.DtrhLoomStore.SpiralsFolder, WebRoot, File.Exists);
            return hit;
        }
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

    public void FlashBurst(int amount, double opacity, int gapMs)
    {
        var s = App.Settings?.Current;
        // FlashDuration is SECONDS; TriggerFlashOnce's customDuration is MILLISECONDS.
        int? duration = s != null && s.FlashDuration > 0 ? s.FlashDuration * 1000 : null;
        _flashAt = Now;
        // Medium images at the authored opacity, staggered at the authored gap (334 ms under reduced motion: the
        // 3 Hz cap), whatever the user's own Flash sliders say.
        App.Flash?.TriggerFlashOnce(amount, duration, BackRoomFxPlan.FlashSize, true, new FlashBurstLook(opacity, gapMs, Peripheral: true, PreviewV2: true));
    }

    public void GifRain(int count, int durationMs, double opacity)
    {
        // A chaos run's rain already on screen keeps its own; a second cascade on top is noise.
        if (ChaosGifCascadeOverlay.IsRaining) return;
        _rainAt = Now;
        double seconds = Math.Max(1.0, durationMs / 1000.0);
        ChaosGifCascadeOverlay.Show(
            spawnRatePerSec: Math.Max(1, count) / seconds,
            durationSec: seconds,
            gifSize: GifCascadePayload.GIF_SIZE,
            fallSpeed: GifCascadePayload.FALL_SPEED,
            opacity: opacity,
            startScale: GifCascadePayload.START_SCALE);
    }

    public void GlitchWash(int durationMs, double opacity)
    {
        _washAt = Now;
        ChaosFlashOverlay.Show(durationMs, opacity);
    }

    public void Subliminal(string text, double opacity)
    {
        _subAt = Now;
        // The authored word envelope: visible, then fades (in 80 ms, hold 400 ms, out 350 ms), never a blink.
        App.Subliminal?.FlashSubliminalCustom(text, (int)Math.Round(Math.Clamp(opacity, 0, 1) * 100), BackRoomFxPlan.WordHoldMs, true,
            BackRoomFxPlan.WordFadeInMs, BackRoomFxPlan.WordFadeOutMs);
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

    public void Wash(FxRgb color, double peak, BackRoomGif? picture, Action shown)
        => BackRoomWashOverlay.Show(color, peak, LocalFile(picture), picture == null ? null : Target(null).ScreenPx, shown);

    public bool GifFrom(BackRoomGif gif, FxCssRect? from, int durationMs, double scale, double dim, Action shown)
    {
        if (LocalFile(gif) is not { } path) return false;
        double aspect = gif.W > 0 && gif.H > 0 ? (double)gif.W / gif.H : 4.0 / 3;
        BackRoomGifFromOverlay.Show(path, aspect, Target(from), durationMs, scale, dim, shown);
        return true;
    }

    public void SpiralLoom(string gifPath, int durationMs, double alpha, bool hold, bool slow)
        => BackRoomLoomSpiralOverlay.Show(gifPath, durationMs, alpha, slow);

    public void ReleaseSpiralLoom() => BackRoomLoomSpiralOverlay.Release();

    public void ReleaseBrainDrain() => StopDrain();

    public void Tunnel(double level) => BackRoomTunnelOverlay.Set(level);

    public void CancelTunnel() => BackRoomTunnelOverlay.Cancel();

    public void BrainDrain(int durationMs, double level, bool melt)
    {
        var kind = melt ? "braindrain_melt" : "braindrain";
        if (_drainKind != null && _drainKind != kind) App.Overlay?.HideOverlaySustained(_drainKind);
        _drainKind = kind;
        StopMeltRamp();
        if (melt)
        {
            // Authored: the melt's alpha ramps from 0 to `level` over the step, then lets go. The blur's
            // strength IS its alpha on the app's path (AlphaFor(intensity)), so the ramp drives that.
            _meltPeak = Math.Clamp(level, 0.01, 1.0);
            _meltStart = Now;
            _meltMs = Math.Max(1, durationMs);
            App.Overlay?.ShowOverlaySustained(kind, 0.01);
            _meltRamp = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(MeltRampStepMs) };
            _meltRamp.Tick += (_, _) => MeltTick();
            _meltRamp.Start();
        }
        else
        {
            // The haze: the user's own Brain Drain blur at the recipe's fraction, held flat.
            var s = App.Settings?.Current;
            double strength = Math.Clamp((s?.BrainDrainIntensity ?? 20) / 100.0 * level, 0.01, 1.0);
            App.Overlay?.ShowOverlaySustained(kind, strength);
        }
        _drainUntil = Math.Max(_drainUntil, Now + durationMs);
        _drainTimer = Rearm(_drainTimer, _drainUntil, StopDrain);
    }

    private void MeltTick()
    {
        if (_drainKind == null) { StopMeltRamp(); return; }
        double t = Math.Clamp((Now - _meltStart) / (double)_meltMs, 0, 1);
        try { App.Overlay?.SetSustainedOverlayOpacity(_drainKind, Math.Max(0.01, t * _meltPeak)); }
        catch (Exception ex) { App.Logger?.Warning(ex, "[BackRoom] melt ramp failed"); }
        if (t >= 1) StopMeltRamp();
    }

    private void StopMeltRamp()
    {
        _meltRamp?.Stop();
        _meltRamp = null;
    }

    public void GifFull(BackRoomGif gif, int durationMs, double opacity, Action shown)
    {
        if (LocalFile(gif) is { } path) ChaosFlashOverlay.ShowHero(path, durationMs, opacity, false, shown);
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
        StopMeltRamp();
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
