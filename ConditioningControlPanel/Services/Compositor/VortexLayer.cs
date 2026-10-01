using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Super Vortex: a second, small spiral that follows the cursor ON TOP of the spiral overlay (the
/// base <see cref="SpiralLayer"/> keeps running underneath, opacity quirk and all). All the maths
/// lives in <see cref="VortexSim"/>; this layer samples the cursor, counts global clicks from its
/// own <see cref="GlobalMouseHook"/> (never swallowed), and draws. OverlayService starts it with
/// the spiral and stops it in StopSpiral, which every panic and emergency exit path runs.
/// World-space px so the vortex can cross monitors.
/// </summary>
public sealed class VortexLayer : BaseLayer
{
    private const int Segments = 48, Arms = 3;
    private const int DeckSize = 3, DeckMaxDim = 512;

    private static readonly SKColor[] ArmColors = BuildArmColors();
    private static readonly SKColor Mote = new(255, 170, 225);

    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _glow = new() { IsAntialias = true };
    private readonly SKPaint _image = new() { FilterQuality = SKFilterQuality.Low };
    private readonly SKShader _flashShader = UnitGlow(new SKColor(255, 200, 240));
    private readonly SKShader _bloomShader = UnitGlow(new SKColor(255, 215, 245));
    private readonly SKColorFilter _red = SKColorFilter.CreateBlendMode(new SKColor(255, 0, 80), SKBlendMode.Modulate);
    private readonly SKColorFilter _cyan = SKColorFilter.CreateBlendMode(new SKColor(0, 200, 255), SKBlendMode.Modulate);

    private VortexSim _sim = new();
    private GlobalMouseHook? _hook;
    private int _pendingClicks;
    private bool _dirty = true;
    private double _master = 1;
    private MotionLevel _level = MotionLevel.Full;
    private bool _flicker;                 // gif cycles frames (Full motion and not photosafe)
    private System.Drawing.Rectangle _screenPx;
    private SKImage[]? _deck;              // the next gif's frames, loaded off-thread
    private SKImage[]? _showing;           // the gif on screen now
    private volatile bool _deckLoading;
    private double _deckTriedAt = double.NegativeInfinity;
    private const double DeckRetrySec = 10;   // an empty or interrupted load tries again, not forever
    private PerformanceTier _tier = PerformanceTier.Quality;
    private double _sincePaint;
    private bool _changed;
    private int _generation;

    public VortexLayer(CompositorEngine engine) : base(engine) { }

    /// <summary>Just above the spiral it rides.</summary>
    public override int ZIndex => CompositorLayers.Spiral + 1;
    public override bool WorldSpacePx => true;

    public override bool ShouldRenderOnScreen(System.Drawing.Rectangle screenBoundsPx)
        => App.ShouldRenderTargetOnScreen(
               App.Settings?.Current?.SpiralTargetMonitor ?? App.MonitorTargetFollowGlobal,
               screenBoundsPx);

    public override bool Dirty => _dirty;
    public override void ClearDirty() { if (_dirty) _sincePaint = 0; _dirty = false; }

    /// <summary>Start (or keep) the vortex. UI thread.</summary>
    public void Start()
    {
        if (IsActive) return;
        _sim = new VortexSim();
        _screenPx = default;
        _deckTriedAt = double.NegativeInfinity;
        _tier = PerformanceProfile.CurrentTier;   // read at start, never per frame
        Interlocked.Exchange(ref _pendingClicks, 0);
        try
        {
            _hook ??= new GlobalMouseHook
            {
                LeftDown = _ => { Interlocked.Increment(ref _pendingClicks); return false; },
                RightDown = _ => { Interlocked.Increment(ref _pendingClicks); return false; },
            };
            _hook.Start();
        }
        catch (Exception ex) { App.Logger?.Debug("VortexLayer: mouse hook unavailable: {E}", ex.Message); }
        LoadDeck();
        _dirty = true;
        SetActive(true);
    }

    /// <summary>Stop at once (panic, spiral off, Super switched off). UI thread.</summary>
    public void Stop()
    {
        if (!IsActive && _hook == null) return;   // the 500 ms reconciler calls this while off
        Interlocked.Increment(ref _generation);
        try { _hook?.Dispose(); } catch { }
        _hook = null;
        SetActive(false);
    }

    public override void OnDeactivated()
    {
        Dispose(ref _deck);
        Dispose(ref _showing);
    }

    public override void Update(TimeSpan delta)
    {
        double dt = Math.Min(delta.TotalSeconds, 0.1);
        if (!GetCursorPos(out var cur)) return;
        if (!_screenPx.Contains(cur.X, cur.Y))
        {
            try
            {
                _screenPx = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cur.X, cur.Y)).Bounds;
                _sim.R0 = 0.64 * Math.Max(200, Math.Min(_screenPx.Width, _screenPx.Height));
            }
            catch { }
        }

        var s = App.Settings?.Current;
        _master = 0.3 + 0.7 * Math.Clamp((s?.SpiralOpacity ?? 10) / 100.0, 0, 1);
        _level = MotionFx.Level;
        _flicker = _level == MotionLevel.Full && s?.LockdownPhotosafe != true;

        int clicks = Interlocked.Exchange(ref _pendingClicks, 0);
        for (int i = 0; i < clicks; i++)
        {
            if (_sim.Click(_level, Random.Shared.NextDouble()) && _deck != null)
            {
                Dispose(ref _showing);
                _showing = _deck;
                _deck = null;
                LoadDeck();
            }
        }

        double x = _sim.X, y = _sim.Y, size = _sim.Size, rot = _sim.Rot;
        _sim.Step(dt, cur.X, cur.Y, _level);
        bool gifUp = _showing != null && VortexMath.GifFrame(_sim.Time - _sim.GifBorn, _flicker, out _, out _, out _);
        if (_showing != null && !gifUp) Dispose(ref _showing);
        if (_deck == null && !_deckLoading && _sim.Time - _deckTriedAt >= DeckRetrySec) LoadDeck();

        // Remember a change even on a frame the repaint gate skips, so the last move of a burst
        // is never left unpainted.
        if (x != _sim.X || y != _sim.Y || size != _sim.Size || rot != _sim.Rot || gifUp || clicks > 0
            || Array.Exists(_sim.Motes, m => m.Alive))
            _changed = true;
        _sincePaint += dt;
        if (_changed && VortexMath.ShouldRepaint(_sincePaint, _tier)) { _dirty = true; _changed = false; }
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        var sim = _sim;
        float cx = (float)sim.X, cy = (float)sim.Y, r0 = (float)sim.R0, r = (float)sim.Radius;
        float reach = Math.Max(r0 * 1.3f, r * 1.3f);
        if (cx + reach < boundsPx.Left || cx - reach > boundsPx.Right
            || cy + reach < boundsPx.Top || cy - reach > boundsPx.Bottom) return;
        double m = _master;
        double t = sim.Time;

        foreach (var e in sim.Echoes)
            if (e.Alive) DrawArms(canvas, cx, cy, (float)(r0 * e.Size), e.Turns, e.Rot, VortexMath.EchoFade(t - e.Born) * m);
        DrawArms(canvas, cx, cy, r, sim.Turns, sim.Rot, sim.ArmsAlpha * m);

        if (sim.FlashAlpha > 0) DrawGlow(canvas, _flashShader, cx, cy, r * 0.9f, sim.FlashAlpha * m);

        // Dust motes spiral inward, additive.
        _fill.BlendMode = SKBlendMode.Plus;
        float dot = (float)Math.Clamp(1.6 * r0 / VortexMath.MockupR0, 1.6, 4.0);
        float alphaRef = Math.Max(r, 40);
        foreach (var d in sim.Motes)
        {
            if (!d.Alive) continue;
            double a = VortexMath.Clamp01(d.Radius / alphaRef) * 0.9 * m;
            _fill.Color = Mote.WithAlpha(ToByte(a));
            canvas.DrawCircle(cx + (float)(Math.Cos(d.Angle) * d.Radius), cy + (float)(Math.Sin(d.Angle) * d.Radius), dot, _fill);
        }
        _fill.BlendMode = SKBlendMode.SrcOver;

        // Implosion rings.
        for (int i = 0; i < sim.RingBorn.Length; i++)
        {
            if (!VortexMath.Ring(t - sim.RingBorn[i], out var rs, out var ra)) continue;
            _stroke.StrokeWidth = 2f * (float)Math.Max(1, dpiScale);
            _stroke.Color = SKColors.White.WithAlpha(ToByte(ra * m));
            canvas.DrawCircle(cx, cy, (float)(sim.RingRadius[i] * rs), _stroke);
        }

        if (sim.BloomAlpha > 0) DrawGlow(canvas, _bloomShader, cx, cy, r * 0.5f, sim.BloomAlpha * m);

        var deck = _showing;
        if (deck != null && deck.Length > 0
            && VortexMath.GifFrame(t - sim.GifBorn, _flicker, out var fr, out var ga, out var glitch))
            DrawGif(canvas, deck[fr % deck.Length], (float)sim.GifX, (float)sim.GifY, r0 * 0.9f, ga, glitch);
    }

    private void DrawArms(SKCanvas canvas, float cx, float cy, float radius, double turns, double rot, double alpha)
    {
        if (alpha <= 0.003 || radius < 1) return;
        const double tau = Math.PI * 2;
        for (int a = 0; a < Arms; a++)
        {
            double baseAngle = rot + a * tau / Arms;
            for (int i = 0; i < Segments; i++)
            {
                double u0 = i / (double)Segments, u1 = (i + 1) / (double)Segments;
                double a0 = baseAngle + u0 * turns * tau, a1 = baseAngle + u1 * turns * tau;
                _stroke.StrokeWidth = (float)((1 - u0 * 0.7) * radius * 0.06 + 1);
                _stroke.Color = ArmColors[i].WithAlpha(ToByte(alpha * (1 - u0 * 0.6)));
                canvas.DrawLine(cx + (float)(Math.Cos(a0) * u0 * radius), cy + (float)(Math.Sin(a0) * u0 * radius),
                                cx + (float)(Math.Cos(a1) * u1 * radius), cy + (float)(Math.Sin(a1) * u1 * radius), _stroke);
            }
        }
    }

    private void DrawGlow(SKCanvas canvas, SKShader shader, float cx, float cy, float radius, double alpha)
    {
        if (alpha <= 0.003 || radius < 1) return;
        _glow.Shader = shader;
        _glow.Color = SKColors.White.WithAlpha(ToByte(alpha));
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.Scale(radius);
        canvas.DrawCircle(0, 0, 1, _glow);
        canvas.Restore();
    }

    private void DrawGif(SKCanvas canvas, SKImage img, float cx, float cy, float box, double alpha, bool glitch)
    {
        if (alpha <= 0.003) return;
        float scale = Math.Min(box / img.Width, box / img.Height);
        float w = img.Width * scale, h = img.Height * scale;
        var rect = SKRect.Create(cx - w / 2, cy - h / 2, w, h);
        if (glitch)
        {
            _image.BlendMode = SKBlendMode.Plus;
            _image.Color = SKColors.White.WithAlpha(ToByte(alpha * 0.5));
            _image.ColorFilter = _red;
            canvas.DrawImage(img, SKRect.Create(rect.Left - 6, rect.Top, w, h), _image);
            _image.ColorFilter = _cyan;
            canvas.DrawImage(img, SKRect.Create(rect.Left + 6, rect.Top, w, h), _image);
            _image.ColorFilter = null;
            _image.BlendMode = SKBlendMode.SrcOver;
        }
        _image.Color = SKColors.White.WithAlpha(ToByte(alpha * 0.95));
        canvas.DrawImage(img, rect, _image);
    }

    /// <summary>
    /// The next gif's frames, from the player's own picture pool through the flash loader: the
    /// first frames of a gif, or three stills. Remote picks come from the pool's warm, consented
    /// stills, so there is no new fetch and no new consent question. An empty pool leaves the
    /// deck null, the click simply skips the flicker, and Update tries again after a while.
    /// </summary>
    private void LoadDeck()
    {
        if (_deckLoading || _deck != null) return;
        var flash = App.Flash;
        if (flash == null) return;
        _deckLoading = true;
        _deckTriedAt = _sim.Time;
        int gen = Volatile.Read(ref _generation);
        Task.Run(async () =>
        {
            SKImage[]? frames = null;
            try
            {
                var picks = flash.GetChaosImagePaths(DeckSize);
                var paths = picks.Where(p => !FlashService.IsRemotePath(p) && File.Exists(p)).ToList();
                // A real .gif or an animated .webp; SKCodec decodes both. A one-frame file
                // falls through to the stills below.
                var gif = paths.FirstOrDefault(p => p.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                                                    || AnimatedWebp.IsAnimated(p));
                if (gif != null && AnimatedWebp.DecodeFrames(gif, DeckMaxDim, DeckSize, 12.0) is { } d && d.Frames.Count > 0)
                    frames = d.Frames.Select(SkiaWpfInterop.ToSKImage).ToArray();
                else
                {
                    var stills = new List<SKImage>();
                    foreach (var p in picks)
                    {
                        // The pool only hands out a remote URL when the player's source and consent
                        // allow it, and its bytes are already warm; drawing it here (rather than
                        // dropping it) is what keeps an online-only player from losing pictures.
                        SKImage? img = FlashService.IsRemotePath(p)
                            ? await LoadRemoteStill(p).ConfigureAwait(false)
                            : File.Exists(p) ? DecodeStill(p) : null;
                        if (img != null) stills.Add(img);
                    }
                    frames = stills.ToArray();
                }
                if (frames.Length == 0) frames = null;
            }
            catch (Exception ex) { App.Logger?.Debug("VortexLayer: deck load failed: {E}", ex.Message); }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) { Dispose(ref frames); _deckLoading = false; return; }
            dispatcher.BeginInvoke(() =>
            {
                _deckLoading = false;
                if (gen != Volatile.Read(ref _generation) || !IsActive || _deck != null) { Dispose(ref frames); return; }
                _deck = frames;
            });
        });
    }

    private static async Task<SKImage?> LoadRemoteStill(string url)
    {
        try
        {
            var bmp = await FlashService.LoadRemoteStillForOverlayAsync(url, DeckMaxDim).ConfigureAwait(false);
            return bmp == null ? null : SkiaWpfInterop.ToSKImage(bmp);
        }
        catch { return null; }
    }

    private static SKImage? DecodeStill(string path)
    {
        try
        {
            using var bmp = SKBitmap.Decode(path);
            if (bmp == null) return null;
            float scale = Math.Min(1f, DeckMaxDim / (float)Math.Max(bmp.Width, bmp.Height));
            if (scale >= 1f) return SKImage.FromBitmap(bmp);
            using var small = bmp.Resize(new SKImageInfo(Math.Max(1, (int)(bmp.Width * scale)), Math.Max(1, (int)(bmp.Height * scale))), SKFilterQuality.Medium);
            return small == null ? null : SKImage.FromBitmap(small);
        }
        catch { return null; }
    }

    private static void Dispose(ref SKImage[]? images)
    {
        var old = images;
        images = null;
        if (old == null) return;
        foreach (var i in old) { try { i.Dispose(); } catch { } }
    }

    private static byte ToByte(double a) => (byte)Math.Clamp(a * 255, 0, 255);

    private static SKColor[] BuildArmColors()
    {
        var c = new SKColor[Segments];
        for (int i = 0; i < Segments; i++)
        {
            double u = i / (double)Segments;
            c[i] = SKColor.FromHsl((float)(320 - u * 70), 90f, (float)(60 + u * 10));
        }
        return c;
    }

    /// <summary>A unit radial glow (centre colour to clear), drawn under a scale so it is built once.</summary>
    private static SKShader UnitGlow(SKColor c)
        => SKShader.CreateRadialGradient(new SKPoint(0, 0), 1f, new[] { c, c.WithAlpha(0) }, null, SKShaderTileMode.Clamp);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);
}
