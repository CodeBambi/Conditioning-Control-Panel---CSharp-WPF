using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using Serilog;
using CoreIntensity = ConditioningControlPanel.Services.BackRoom.BackRoomFxIntensity;

namespace ConditioningControlPanel.Avalonia.Views.Games.BackRoom;

/// <summary>
/// The real primitive sink on this head (WPF BackRoomFxServices): each contract primitive on the
/// overlay the port already has. A primitive with no port overlay is not in <see cref="Supported"/>,
/// so the dispatcher acks it skipped <c>unknown</c> instead of claiming it played.
///
/// <para>Ported: flash-burst (FlashOverlay), the three sub primitives (SubliminalOverlay), the Brain
/// Drain melt and haze (BrainDrainOverlay's timed drain) and the colour wash (a TintOverlayWindow
/// under the WPF wash envelope, colour only). Not ported: gif-rain, glitch wash, gif-full, gif-from,
/// the Loom spiral and tunnel vision (no overlay on this head yet).</para>
///
/// <para>No CCP feature toggle gates any of it (the Back Room is an authored show): only the
/// effective motion level (OS reduced motion) and Calm shape a fire. It stops only what it started:
/// the wash is the room's alone; the flash, subliminal and drain surfaces are shared, so StopAll
/// touches each only when the room fired one recently.</para>
/// </summary>
internal sealed class BackRoomFxHead : IBackRoomFxSink
{
    private static BackRoomFx? _shared;
    private static readonly object SharedLock = new();
    private static readonly BackRoomFxHead Sink = new();

    /// <summary>The one dispatcher every room and Breakout window shares, so they share one hero gate.</summary>
    public static BackRoomFx Shared
    {
        get
        {
            lock (SharedLock)
                return _shared ??= new BackRoomFx(Sink, new UiFxScheduler(), ReadEnvironment, supports: p => Supported.Contains(p));
        }
    }

    /// <summary>The primitives this head can really show.</summary>
    internal static readonly HashSet<FxPrim> Supported = new()
    {
        FxPrim.FlashBurst, FxPrim.SubSingle, FxPrim.SubSeq, FxPrim.SubBurst9,
        FxPrim.BrainDrainMelt, FxPrim.Haze, FxPrim.Wash,
    };

    /// <summary>Any attached visual of the open room (it only reaches <c>Screens</c>). Set by the host
    /// while a room is open; null = no room, nothing shows.</summary>
    internal static Visual? Host { get; set; }

    /// <summary>How long after a fire StopAll still considers a shared one-shot channel the room's.</summary>
    private const int OwnershipMs = 12_000;
    private long _flashAt = long.MinValue / 2, _subAt = long.MinValue / 2;
    private bool _drain;

    private static long Now => Environment.TickCount64;
    private static bool Recent(long at) => Now - at < OwnershipMs;

    /// <summary>Settings for one fire: the EFFECTIVE motion level (the user's setting capped by the OS
    /// animation flag) and the room's intensity. No feature toggle.</summary>
    public static FxEnvironment ReadEnvironment()
    {
        var s = CoreSettings.Current;
        var motion = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level;
        var intensity = s.BackRoomFxIntensity switch
        {
            CoreIntensity.Calm => BackRoomFxIntensity.Calm,
            CoreIntensity.Full => BackRoomFxIntensity.Full,
            _ => BackRoomFxIntensity.Normal,
        };
        // No spiral source: the Loom spiral overlay is not on this head, so a spiral step resolves to nothing.
        return new FxEnvironment(motion, intensity, null);
    }

    public void FlashBurst(int amount, double opacity, int gapMs)
    {
        if (Host is not { } host) return;
        var s = CoreSettings.Current;
        // FlashDuration is SECONDS; TriggerOnce's override is MILLISECONDS.
        int? duration = s.FlashDuration > 0 ? s.FlashDuration * 1000 : null;
        _flashAt = Now;
        // ponytail: the authored opacity and image gap (WPF FlashBurstLook) have no override on this head's
        // flash overlay yet; the burst uses the player's own.
        FlashOverlay.TriggerOnce(host, amount, duration, BackRoomFxPlan.FlashSize);
    }

    public void Subliminal(string text, double opacity)
    {
        if (Host is not { } host) return;
        _subAt = Now;
        SubliminalOverlay.Show(host, text, (int)Math.Round(Math.Clamp(opacity, 0, 1) * 100));
        try { CoreProgression.AddXP(10, "Subliminal"); } catch { }   // WPF FlashSubliminalCustom pays inside the service
    }

    public void BrainDrain(int durationMs, double level, bool melt)
    {
        if (Host is not { } host) return;
        var s = CoreSettings.Current;
        // The melt peaks at its authored level; the haze is the user's own blur at the recipe's fraction.
        double strength = melt ? Math.Clamp(level, 0.01, 1.0) : Math.Clamp(s.BrainDrainIntensity / 100.0 * level, 0.01, 1.0);
        _drain = BrainDrainOverlay.ShowTimed(host, Math.Max(1, (int)Math.Round(strength * 100)), melt, durationMs) || _drain;
    }

    public void ReleaseBrainDrain() => StopDrain();

    private void StopDrain()
    {
        if (!_drain) return;
        _drain = false;
        try { BrainDrainOverlay.EndTimed(); } catch (Exception ex) { Log.Debug("[BackRoom] drain release: {E}", ex.Message); }
    }

    // ---- the wash (10.13.B): the WPF envelope on tint windows, colour only ----

    /// <summary>WPF BackRoomOverlayMath.WashRiseMs / WashDecayPerSec.</summary>
    internal const int WashRiseMs = 80;
    internal const double WashDecayPerSec = 4.5;

    /// <summary>WPF BackRoomOverlayMath.WashEnvelope: up in 80 ms, then an exponential fall, gone at WashMs.</summary>
    internal static double WashEnvelope(double ageMs)
    {
        if (ageMs < 0 || ageMs >= BackRoomFxPlan.WashMs) return 0;
        if (ageMs < WashRiseMs) return ageMs / WashRiseMs;
        return Math.Exp(-(ageMs - WashRiseMs) / 1000.0 * WashDecayPerSec);
    }

    private readonly List<TintOverlayWindow> _wash = new();
    private DispatcherTimer? _washTimer;
    private long _washStart;
    private double _washPeak;
    private FxRgb _washColor;
    private static bool _washRefused;

    public void Wash(FxRgb color, double peak, BackRoomGif? picture, Action shown)
    {
        // The picture inside a wash is not ported (no decoder window here), so `shown` never runs and no XP is paid.
        if (Host is not { } host || _washRefused || !X11Overlay.IsAvailable) return;
        StopWash();
        _washColor = color;
        _washPeak = Math.Clamp(peak, 0, BackRoomFxPlan.WashPeak);
        _washStart = Now;
        try
        {
            foreach (var screen in ScreenList.Enumerate(host))
            {
                var w = new TintOverlayWindow();
                w.PlaceOn(screen);
                w.SetTint(color.R, color.G, color.B, 0);
                w.Show();
                if (!X11Overlay.SetClickThrough(w, true) || w.ActualTransparencyLevel == WindowTransparencyLevel.None)
                {
                    // A full-screen topmost window that swallows clicks would lock the desktop: never show it.
                    _washRefused = true;
                    try { w.Close(); } catch { }
                    StopWash();
                    return;
                }
                _wash.Add(w);
            }
        }
        catch (Exception ex) { Log.Debug("[BackRoom] wash: {E}", ex.Message); StopWash(); return; }
        if (_wash.Count == 0) return;
        _washTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _washTimer.Tick += (_, _) => WashTick();
        _washTimer.Start();
    }

    private void WashTick()
    {
        double age = Now - _washStart;
        if (age >= BackRoomFxPlan.WashMs) { StopWash(); return; }
        double a = Math.Clamp(WashEnvelope(age) * _washPeak, 0, 1);
        foreach (var w in _wash) w.SetTint(_washColor.R, _washColor.G, _washColor.B, a);
    }

    private void StopWash()
    {
        try { _washTimer?.Stop(); } catch { }
        _washTimer = null;
        foreach (var w in _wash.ToList()) { try { w.Close(); } catch { } }
        _wash.Clear();
    }

    // ---- no overlay on this head: never reached, the dispatcher skips these as unknown ----

    public void GifRain(int count, int durationMs, double opacity) { }
    public void GlitchWash(int durationMs, double opacity) { }
    public void GifFull(BackRoomGif gif, int durationMs, double opacity, Action shown) { }
    public bool GifFrom(BackRoomGif gif, FxCssRect? from, int durationMs, double scale, double dim, Action shown) => false;
    public void SpiralLoom(string gifPath, int durationMs, double alpha, bool hold, bool slow) { }
    public void ReleaseSpiralLoom() { }
    public void Tunnel(double level) { }
    public void CancelTunnel() { }

    /// <summary>Panic, suspend, close: everything the room started goes now.</summary>
    public void StopAll()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(StopAll); return; }
        StopDrain();
        StopWash();
        // final: false, so the player's own flashes can show again after the room is gone.
        if (Recent(_flashAt)) { try { FlashOverlay.CloseAll(false); } catch (Exception ex) { Log.Debug("[BackRoom] flash stop: {E}", ex.Message); } }
        if (Recent(_subAt)) { try { SubliminalOverlay.CloseAll(); } catch (Exception ex) { Log.Debug("[BackRoom] sub stop: {E}", ex.Message); } }
        _flashAt = _subAt = long.MinValue / 2;
    }
}

/// <summary>The app's fx scheduler: one-shot DispatcherTimers on the UI thread (WPF DispatcherFxScheduler).</summary>
internal sealed class UiFxScheduler : IFxScheduler
{
    public long NowMs => Environment.TickCount64;

    public IDisposable After(int delayMs, Action action)
    {
        var handle = new Handle();
        Dispatcher.UIThread.Post(() =>
        {
            if (handle.Cancelled) return;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(0, delayMs)) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (!handle.Cancelled) action();
            };
            handle.Timer = t;
            t.Start();
        });
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
            try { Dispatcher.UIThread.Post(t.Stop); } catch { }
        }
    }
}
