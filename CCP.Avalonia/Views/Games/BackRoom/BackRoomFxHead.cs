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
/// under the WPF wash envelope), and on <see cref="BackRoomOverlays"/> the glitch wash, gif-full, gif-from, the
/// picture inside a wash, tunnel vision and the Loom spiral, and gif-rain on <see cref="GifCascadeOverlay"/>.</para>
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
                return _shared ??= new BackRoomFx(Sink, new UiFxScheduler(), ReadEnvironment, xp: PayPictureXp, supports: p => Supported.Contains(p));
        }
    }

    /// <summary>The primitives this head can really show.</summary>
    internal static readonly HashSet<FxPrim> Supported = new()
    {
        FxPrim.FlashBurst, FxPrim.SubSingle, FxPrim.SubSeq, FxPrim.SubBurst9,
        FxPrim.BrainDrainMelt, FxPrim.Haze, FxPrim.Wash,
        FxPrim.GlitchBubbles, FxPrim.GifFull, FxPrim.GifFrom, FxPrim.SpiralFull, FxPrim.SpiralLoom,
        FxPrim.GifRain,
    };

    /// <summary>A Back Room picture pays as a flash image does (WPF BackRoomFxServices.PayPictureXp, CONTRACT 10.14):
    /// the base through AddXP as Flash. The lucky flash roll is not on this head (see MainShellWindow.Enhancements).</summary>
    internal static Action<int> PayPictureXp = baseXp =>
    {
        try { CoreProgression.AddXP(baseXp, "Flash"); } catch (Exception ex) { Log.Debug("[BackRoom] picture xp: {E}", ex.Message); }
    };

    /// <summary><c>Resources/web</c>, the folder <c>ccp.game</c> maps.</summary>
    internal static string WebRoot => Path.Combine(AppContext.BaseDirectory, "Resources", "web");

    /// <summary>
    /// A dealt media url back to the local file behind it (WPF TryLocalPath). On this head a dealt url is
    /// usually on the loopback listener, so it is first read back as the virtual url it stands for. Only
    /// the two origins the room maps (<c>ccp.assets</c>, <c>ccp.game</c>) are accepted, and the file comes
    /// from the asset server's own resolver, so a hostile url in a deal cannot name a path outside them.
    /// </summary>
    internal static string? TryLocalPath(string? url, WebAssetServer server)
    {
        try
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            if (server.IsLoopback(uri)) uri = server.ToVirtual(uri);
            if (!server.IsVirtual(uri) || uri.Host is not ("ccp.assets" or WebAssetServer.GameHost)) return null;
            var path = server.ResolveVirtual(uri, out _, out _);
            return path != null && File.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch (Exception ex) { Log.Debug("[BackRoom] bad media url: {E}", ex.Message); return null; }
    }

    private static readonly string[] DrawableExtensions =
        { ".gif", ".webp", ".png", ".jpg", ".jpeg", ".jfif", ".bmp" };

    /// <summary>True when a dealt url names something the picture overlays can decode (a clip is not).</summary>
    internal static bool IsDrawablePicture(string? url)
    {
        var bare = url ?? string.Empty;
        int cut = bare.IndexOfAny(new[] { '?', '#' });
        var ext = Path.GetExtension(cut >= 0 ? bare[..cut] : bare);
        return Array.FindIndex(DrawableExtensions, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)) >= 0;
    }

    private const int StandInProbes = 8;

    /// <summary>WPF StandInFor: a picture the overlays CAN open, standing in for a dealt clip they cannot. One of the
    /// player's own animated files (at most 8 header reads), else the bundled loop. The same key gives the same
    /// stand-in for the whole sit-down. Null only when there is nothing at all.</summary>
    internal static string? StandInFor(string? key, IReadOnlyList<string>? library, string? webRoot)
    {
        int n = 0;
        foreach (var ch in key ?? string.Empty)
            if (char.IsDigit(ch)) n = n * 10 + (ch - '0');
        try
        {
            var own = (library ?? Array.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f) && !f.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                .Where(f => Path.GetExtension(f).Equals(".gif", StringComparison.OrdinalIgnoreCase)
                         || Path.GetExtension(f).Equals(".webp", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var rng = new Random(unchecked(n * 31 + 7));
            for (int i = 0, probes = 0; i < own.Count && probes < StandInProbes; i++, probes++)
            {
                int j = rng.Next(i, own.Count);
                (own[i], own[j]) = (own[j], own[i]);
                if (BackRoomMedia.ProbeAnimated(own[i]).Ok && File.Exists(own[i])) return own[i];
            }
        }
        catch (Exception ex) { Log.Debug("[BackRoom] stand-in library: {E}", ex.Message); }
        try
        {
            if (string.IsNullOrEmpty(webRoot)) return null;
            var loop = Path.Combine(webRoot, "backroom", "stations", "slot", "fallback", $"gif{n % BackRoomMedia.Slots}.webp");
            return File.Exists(loop) ? loop : null;
        }
        catch (Exception ex) { Log.Debug("[BackRoom] stand-in loop: {E}", ex.Message); return null; }
    }

    /// <summary>The local file a dealt item shows as (tests swap it). A clip (every online pick) takes the stand-in.</summary>
    internal static Func<BackRoomGif?, string?> LocalFile = gif =>
    {
        if (gif == null) return null;
        if (!IsDrawablePicture(gif.Url)) return StandInFor(gif.Key, BackRoomMedia.LocalImagePaths(), WebRoot);
        return TryLocalPath(gif.Url, WebAssetServer.Shared);
    };

    /// <summary>The glitch wash's picture: one from the flash pool, as WPF ChaosFlashOverlay.PickImage (tests swap it).</summary>
    internal static Func<string?> GlitchPick = () => FlashOverlay.GetChaosImagePaths(1).FirstOrDefault(f => !f.StartsWith("http", StringComparison.OrdinalIgnoreCase));

    // The woven spiral's file probes, kept per SpiralPath for a few seconds (WPF WovenFor).
    private static readonly Dictionary<string, string?> WovenHits = new(StringComparer.Ordinal);
    private static string? _wovenFor;
    private static long _wovenAt = long.MinValue / 2;

    private static string? WovenFor(string preset, string? spiralPath)
    {
        lock (WovenHits)
        {
            if (_wovenFor != spiralPath || Now - _wovenAt > 5000) { WovenHits.Clear(); _wovenFor = spiralPath; _wovenAt = Now; }
            preset = BackRoomSpiralSource.Preset(preset);
            if (!WovenHits.TryGetValue(preset, out var hit))
                WovenHits[preset] = hit = BackRoomSpiralSource.Resolve(preset, spiralPath,
                    ConditioningControlPanel.Services.Chaos.DtrhLoomStore.SpiralsFolder, WebRoot, File.Exists);
            return hit;
        }
    }

    // IB6: the room and a Breakout window can be open together. Each window attaches itself; the
    // newest one still open is the host, so closing either never blanks the survivor's effects.
    private static readonly List<Visual> Hosts = new();
    private static Visual? _hostOverride;

    /// <summary>Any attached visual of an open room (it only reaches <c>Screens</c>): the newest window
    /// still attached; null = no room, nothing shows. The setter is the tests' override.</summary>
    internal static Visual? Host
    {
        get { lock (Hosts) return _hostOverride ?? (Hosts.Count > 0 ? Hosts[^1] : null); }
        set { lock (Hosts) _hostOverride = value; }
    }

    /// <summary>A room or Breakout window opened: it hosts the effects, and gets its own fx door.</summary>
    internal static IBackRoomFx Attach(Visual window, IBackRoomFx? inner = null)
    {
        lock (Hosts) { Hosts.Remove(window); Hosts.Add(window); }
        return new OwnedFx(window, inner ?? Shared);
    }

    /// <summary>That window closed. The next newest window (if any) hosts from here.</summary>
    internal static void Detach(Visual window) { lock (Hosts) Hosts.Remove(window); }

    private static bool AnotherIsOpen(Visual window) { lock (Hosts) return Hosts.Exists(h => !ReferenceEquals(h, window)); }

    /// <summary>
    /// One window's door onto the shared dispatcher (one hero gate for every window, as before). Its
    /// "stop everything" is owner-scoped: while ANOTHER room window is open it releases only the holds
    /// and the tunnel of the stations THIS window fired for, and leaves the other window's effects
    /// alone; a one-shot it fired ends on its own clock. The last window to stop (and so a panic, which
    /// closes every game window) still cancels everything.
    /// </summary>
    private sealed class OwnedFx : IBackRoomFx
    {
        private readonly Visual _owner;
        private readonly IBackRoomFx _inner;
        private readonly HashSet<string> _stations = new(StringComparer.Ordinal);

        public OwnedFx(Visual owner, IBackRoomFx inner) { _owner = owner; _inner = inner; }

        private void Note(string? station) { lock (_stations) _stations.Add(station ?? string.Empty); }

        public BackRoomFxAck Fire(string fxId, string station, IReadOnlyList<string> symbolKeys, BackRoomMediaDeal deal)
        { Note(station); return _inner.Fire(fxId, station, symbolKeys, deal); }

        public BackRoomFxAck Fire(string fxId, string station, IReadOnlyList<string> symbolKeys, BackRoomMediaDeal deal, BackRoomFxArgs? args, string? token)
        { Note(station); return _inner.Fire(fxId, station, symbolKeys, deal, args, token); }

        public void Release(string token, string station) => _inner.Release(token, station);
        public void Tunnel(string station, double level) { Note(station); _inner.Tunnel(station, level); }
        public void ReleaseStation(string station) => _inner.ReleaseStation(station);

        public void CancelAll()
        {
            string[] mine;
            lock (_stations) { mine = _stations.ToArray(); _stations.Clear(); }
            if (!AnotherIsOpen(_owner)) { _inner.CancelAll(); return; }
            foreach (var station in mine)
            {
                try { _inner.ReleaseStation(station); }
                catch (Exception ex) { Log.Debug("[BackRoom] owned release: {E}", ex.Message); }
            }
        }
    }

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
        // Every Back Room spiral is Loom-woven: the player's own weave, else the bundled one for the preset.
        var spiralPath = s.SpiralPath;
        return new FxEnvironment(motion, intensity, preset => WovenFor(preset, spiralPath));
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
        if (Host is not { } host || _washRefused || !X11Overlay.IsAvailable) return;
        StopWash();
        BackRoomOverlays.StopSlot(BackRoomOverlays.WashPicture);
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
        // The picture in the middle rides the same envelope; `shown` (and its XP) only once it is really on.
        if (picture != null && LocalFile(picture) is { } file)
            BackRoomOverlays.WashWithPicture(host, file, _washPeak, WashEnvelope, shown);
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

    // ---- gif-rain: the cascade (WPF BackRoomFxServices.GifRain, same numbers as the Chaos payload) ----

    /// <summary>WPF GifCascadePayload.GIF_SIZE / FALL_SPEED / START_SCALE.</summary>
    internal const double RainGifSize = 400, RainFallSpeed = 3.6, RainStartScale = 0.45;

    public void GifRain(int count, int durationMs, double opacity)
    {
        if (Host is not { } host) return;
        // A rain already on screen keeps its own; a second cascade on top is noise.
        if (GifCascadeOverlay.IsRaining) return;
        double seconds = Math.Max(1.0, durationMs / 1000.0);
        GifCascadeOverlay.Show(host, BackRoomOverlays.RainOwner, Math.Max(1, count) / seconds, seconds,
            RainGifSize, RainFallSpeed, opacity, RainStartScale);
    }

    /// <summary>The room's web view on screen for a gif-from's rect (WPF BackRoomFxServices.Viewport). Tests swap it.</summary>
    internal static Func<RoomViewport?> Viewport = () => BackRoomFromMap.Read(Host);

    // ---- the room's own overlay windows (BackRoomOverlays) ----

    public void GlitchWash(int durationMs, double opacity)
    {
        if (Host is not { } host) return;
        string? pick;
        try { pick = GlitchPick(); } catch (Exception ex) { Log.Debug("[BackRoom] glitch pick: {E}", ex.Message); pick = null; }
        if (pick != null) BackRoomOverlays.Full(BackRoomOverlays.Glitch, host, pick, durationMs, opacity, null);
    }

    public void GifFull(BackRoomGif gif, int durationMs, double opacity, Action shown)
    {
        if (Host is { } host && LocalFile(gif) is { } path) BackRoomOverlays.Full(BackRoomOverlays.Hero, host, path, durationMs, opacity, shown);
    }

    public bool GifFrom(BackRoomGif gif, FxCssRect? from, int durationMs, double scale, double dim, Action shown)
    {
        if (Host is not { } host || LocalFile(gif) is not { } path) return false;
        double aspect = gif.W > 0 && gif.H > 0 ? (double)gif.W / gif.H : 4.0 / 3;
        RoomViewport? vp = null;
        try { vp = Viewport(); } catch (Exception ex) { Log.Debug("[BackRoom] room viewport read: {E}", ex.Message); }
        BackRoomOverlays.GifFrom(host, path, aspect, durationMs, scale, dim, shown, from, vp);
        return true;
    }

    public void SpiralLoom(string gifPath, int durationMs, double alpha, bool hold, bool slow)
    {
        if (Host is { } host) BackRoomOverlays.Spiral(host, gifPath, durationMs, alpha, slow);
    }

    public void ReleaseSpiralLoom() => BackRoomOverlays.ReleaseSpiral();

    public void Tunnel(double level)
    {
        if (Host is { } host) BackRoomOverlays.Tunnel(host, level); else BackRoomOverlays.CancelTunnel();
    }

    public void CancelTunnel() => BackRoomOverlays.CancelTunnel();

    /// <summary>Panic, suspend, close: everything the room started goes now.</summary>
    public void StopAll()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(StopAll); return; }
        StopDrain();
        StopWash();
        BackRoomOverlays.StopAll();
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
