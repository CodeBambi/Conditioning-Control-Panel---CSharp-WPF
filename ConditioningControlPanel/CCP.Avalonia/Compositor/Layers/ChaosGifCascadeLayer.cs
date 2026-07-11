using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Chaos "GifCascade" payload (WS2/WP3 Phase F #7): images/gifs spawn at the top of the
/// stage on a cadence and fall/cascade downward, then despawn off the bottom.
///
/// Behavior contract (WPF Chaos/ChaosGifCascadeOverlay.cs):
/// - Show(spawnRatePerSec, durationSec, gifSize, fallSpeed, opacity, startScale); clamps
///   gifSize 40..600 DIP, fallSpeed 0.5..30, opacity 0.05..1.0, startScale 0.1..1.0;
/// - spawn interval = 1000 / max(0.05, rate) ms, ONE spawn immediately on (re)start; the
///   life window max(1, durationSec) closes the spawner and in-flight clips fall out;
/// - a new Show mid-cascade REPLACES the in-flight clips (WPF Restart → StopAndClear);
/// - caps: 14 clips alive (OOM ceiling), 3 ANIMATED clips alive, 3MB max file size for a
///   clip allowed to animate — over budget falls as a display-size STILL;
/// - motion: speed = fallSpeed * (0.7 + rnd*0.6) DIPs per 16ms frame (vsync delta-scaled,
///   stall clamp 0.1s); clip starts at y = -gifSize, despawns past stage height + gifSize;
/// - growth: scale startScale → 1.0 by 75% of the way down, center-origin;
/// - stage = primary unless dual-monitor (WPF StageBounds — the legacy Avalonia window
///   forced primary AND sized itself in physical px units interpreted as DIPs, spawning
///   clips past the right edge on scaled displays; both drifts die in px space);
/// - clip layout: width = gifSize (Uniform stretch → height by aspect), random center x
///   in [gifSize/2, stageWidth - gifSize/2], top-anchored at the stage top.
///
/// Decode-once discipline (SpiralLayer pattern): each spawned clip decodes OFF-thread via
/// <see cref="SkiaImageDecoder"/> into a <see cref="SkiaFrameSet"/> ONCE — the legacy
/// window re-decoded every GIF frame per tick per clip through AvaloniaAnimatedGif (the
/// exact per-frame streaming the UCE forbids), and dropped animated-webp support (WPF
/// animates webp; SKCodec brings it back). Animated budget: display-size decode + 48
/// frames (the WPF AnimatedWebp.AttachAnimation defaults the cascade rode) + the spiral
/// 96MB safety cap; stills decode at display size (WPF "decode at display size — cheap").
/// The clip falls empty for the few frames the decode takes — invisible in the rain (WPF
/// comment, same behavior).
///
/// Frame-set lifetime (UCE rule 8): Render draws under _sync; every Release happens under
/// _sync (despawn, restart, clear, orphaned decode), so no draw can race disposal.
///
/// Capture-VISIBLE (main surface). Z from CompositorLayers only (UCE rule 9).
/// </summary>
public sealed class ChaosGifCascadeLayer : BaseLayer
{
    private const int MaxConcurrent = 14;              // WPF MAX_CONCURRENT
    private const int MaxAnimated = 3;                 // WPF MAX_ANIMATED
    private const long AnimatedMaxBytes = 3_000_000;   // WPF ANIMATED_MAX_BYTES
    private const int AnimatedMaxFrames = 48;          // WPF AnimatedWebp.AttachAnimation default
    private const double MaxMemoryMb = 96.0;           // spiral safety budget (pathological gifs)
    private const int DefaultFrameDelayMs = 100;       // WPF flash default

    private readonly object _sync = new();
    private readonly List<Faller> _fallers = new();
    private readonly Random _rng = new();
    // Reused paint: only touched inside Render under _sync. Never disposed (layer lives app-long).
    private readonly SKPaint _paint = new() { IsAntialias = true };

    private IReadOnlyList<string> _files = Array.Empty<string>();
    private ConditioningControlPanel.Core.Platform.PixelRect _stage = ConditioningControlPanel.Core.Platform.PixelRect.Empty;
    private double _gifSizePx = 200;
    private double _speedPxPerSec = 4;     // per-clip jitter applied at spawn
    private double _fallSpeed = 4;         // WPF knob (DIPs per 16ms), kept for jitter math
    private double _scale = 1.0;
    private double _opacity = 1.0;
    private double _startScale = 1.0;
    private bool _spawning;
    private double _spawnIntervalMs = 500;
    private double _spawnAccumMs;
    private double _lifeRemainingMs;
    private int _generation;               // orphans in-flight decodes across restart/clear
    private int _animatedAlive;            // guarded by _sync
    private int _lastIndex = -1;           // 1-deep no-repeat guard for SpawnOneLocked picks

    private sealed class Faller
    {
        public SkiaFrameSet? Frames;       // null until the off-thread decode lands
        public bool Animated;              // holds an _animatedAlive reservation
        public double Y;                   // px, from -gifSize
        public double CenterX;             // px, stage-relative
        public double SpeedPxPerSec;
        public int FrameIndex;
        public double FrameTimerSec;

        public void ReleaseFrames()
        {
            var frames = Frames;
            Frames = null;
            frames?.Release();
        }
    }

    public override int ZIndex => CompositorLayers.ChaosGifCascade;

    /// <summary>Raining = spawner open or clips still falling (WPF IsRaining).</summary>
    public override bool IsActive
    {
        get { lock (_sync) { return _spawning || _fallers.Count > 0; } }
    }

    // ConsumeDirty stays the base always-dirty: every live clip falls/grows each frame,
    // and while drained IsActive is false so the engine never ticks or renders this layer.

    /// <summary>(Re)start a cascade — any in-flight clips are replaced (WPF Restart).
    /// <paramref name="stagePx"/> is the stage in PHYSICAL px; <paramref name="screenScale"/>
    /// the primary screen's DPI scale (DIP knobs → px).</summary>
    public void Restart(IReadOnlyList<string> files, double spawnRatePerSec, double durationSec,
        double gifSize, double fallSpeed, double opacity, double startScale,
        ConditioningControlPanel.Core.Platform.PixelRect stagePx, double screenScale)
    {
        if (files.Count == 0 || stagePx.IsEmpty) return;
        lock (_sync)
        {
            StopAndClearLocked();
            _files = files;
            _lastIndex = -1;   // reset no-repeat guard: pool changed
            _stage = stagePx;
            _scale = screenScale > 0 ? screenScale : 1.0;
            _gifSizePx = Math.Clamp(gifSize, 40, 600) * _scale;
            _fallSpeed = Math.Clamp(fallSpeed, 0.5, 30);
            // WPF speed unit: DIPs per 16ms composed frame → px/s.
            _speedPxPerSec = _fallSpeed * _scale / 0.016;
            _opacity = Math.Clamp(opacity, 0.05, 1.0);
            _startScale = Math.Clamp(startScale, 0.1, 1.0);
            _spawnIntervalMs = 1000.0 / Math.Max(0.05, spawnRatePerSec);
            _lifeRemainingMs = Math.Max(1.0, durationSec) * 1000.0;
            _spawnAccumMs = 0;
            _spawning = true;
            SpawnOneLocked();   // WPF Restart spawns one immediately
        }
    }

    /// <summary>Immediate teardown (run end — WPF CloseActive/StopAndClear).</summary>
    public void Clear()
    {
        lock (_sync) { StopAndClearLocked(); }
    }

    private void StopAndClearLocked()
    {
        _generation++;   // orphan every in-flight decode
        foreach (var f in _fallers) f.ReleaseFrames();
        _fallers.Clear();
        _animatedAlive = 0;
        _spawning = false;
    }

    private void SpawnOneLocked()
    {
        if (!_spawning) return;
        if (_fallers.Count >= MaxConcurrent) return;   // never let clips pile up into an OOM
        try
        {
            var idx = _rng.Next(_files.Count);
            if (idx == _lastIndex && _files.Count > 1) idx = (idx + 1) % _files.Count;   // 1-deep no-repeat (mirrors AvaloniaBlinkTrainerService.ShowRandom)
            _lastIndex = idx;
            var path = _files[idx];

            // A gif/animated-webp only animates while the animated budget has room and it
            // isn't huge; otherwise it falls as a display-size still (WPF SpawnOne). The
            // size stat is one cheap call on the spawning thread, keeping the counter
            // race-free under _sync.
            var animatedExt = path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
            var animate = false;
            if (animatedExt && _animatedAlive < MaxAnimated)
            {
                long len = 0;
                try { len = new FileInfo(path).Length; } catch { }
                animate = len > 0 && len <= AnimatedMaxBytes;
            }

            var faller = new Faller
            {
                Animated = animate,
                CenterX = _gifSizePx / 2 + _rng.NextDouble() * Math.Max(1, _stage.Width - _gifSizePx),
                Y = -_gifSizePx,
                SpeedPxPerSec = _speedPxPerSec * (0.7 + _rng.NextDouble() * 0.6),
            };
            if (animate) _animatedAlive++;
            _fallers.Add(faller);

            var gen = _generation;
            var decodeDim = Math.Max(1, (int)_gifSizePx);
            Task.Run(() =>
            {
                try
                {
                    // Decode-once budgets: see class doc.
                    var set = animate
                        ? SkiaImageDecoder.Decode(path, AnimatedMaxFrames, decodeDim, MaxMemoryMb, DefaultFrameDelayMs, 0)
                        : SkiaImageDecoder.Decode(path, 1, decodeDim, 0, DefaultFrameDelayMs, 0);
                    lock (_sync)
                    {
                        // The cascade restarted/cleared, or the clip already fell out.
                        if (set == null || gen != _generation || !_fallers.Contains(faller))
                        {
                            set?.Release();
                            if (set == null && gen == _generation && faller.Animated && _fallers.Contains(faller))
                            {
                                // Decode failed — hand the animated budget back (WPF fallback).
                                faller.Animated = false;
                                _animatedAlive = Math.Max(0, _animatedAlive - 1);
                            }
                            return;
                        }
                        faller.Frames = set;
                        if (faller.Animated && !set.IsAnimated)
                        {
                            // A static file under an animated reservation (e.g. still webp).
                            faller.Animated = false;
                            _animatedAlive = Math.Max(0, _animatedAlive - 1);
                        }
                    }
                }
                catch { /* decode failure = the clip falls empty, WPF logs and moves on */ }
            });
        }
        catch { /* spawn failure is non-fatal (WPF catch) */ }
    }

    public override void Update(TimeSpan deltaTime)
    {
        // WPF OnRender: vsync delta, stall clamp 0.1s.
        var dtSec = Math.Min(deltaTime.TotalSeconds, 0.1);
        if (dtSec <= 0) return;
        lock (_sync)
        {
            if (_spawning)
            {
                _lifeRemainingMs -= deltaTime.TotalMilliseconds;
                if (_lifeRemainingMs <= 0)
                {
                    _spawning = false;   // spawner closes; in-flight clips fall out (WPF _life.Tick)
                }
                else
                {
                    _spawnAccumMs += deltaTime.TotalMilliseconds;
                    while (_spawnAccumMs >= _spawnIntervalMs)
                    {
                        _spawnAccumMs -= _spawnIntervalMs;
                        SpawnOneLocked();
                    }
                }
            }

            for (int i = _fallers.Count - 1; i >= 0; i--)
            {
                var f = _fallers[i];
                f.Y += f.SpeedPxPerSec * dtSec;

                // Advance animated frames at the file's real delays (FlashLayer pattern).
                var frames = f.Frames;
                if (frames is { IsAnimated: true })
                {
                    var delays = frames.FrameDelaysSeconds;
                    f.FrameTimerSec += dtSec;
                    var guard = 0;
                    while (guard++ < 1000)
                    {
                        var delay = delays[f.FrameIndex % delays.Length];
                        if (delay <= 0.0005 || f.FrameTimerSec < delay) break;
                        f.FrameTimerSec -= delay;
                        f.FrameIndex = (f.FrameIndex + 1) % frames.Frames.Length;
                    }
                }

                if (f.Y > _stage.Height + _gifSizePx)
                {
                    f.ReleaseFrames();   // under _sync: no draw can be in flight
                    if (f.Animated) { f.Animated = false; _animatedAlive = Math.Max(0, _animatedAlive - 1); }
                    _fallers.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>Grow factor at vertical position <paramref name="y"/> px: starts at
    /// startScale up top, eases to full by ~75% of the way down (WPF ScaleAt).</summary>
    private double ScaleAt(double y)
    {
        if (_startScale >= 1.0) return 1.0;
        var p = Math.Clamp(y / Math.Max(1.0, _stage.Height * 0.75), 0, 1);
        return _startScale + (1.0 - _startScale) * p;
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        lock (_sync)
        {
            if (_fallers.Count == 0) return;
            _paint.Color = new SKColor(255, 255, 255, (byte)Math.Clamp(_opacity * 255, 0, 255));
            foreach (var f in _fallers)
            {
                var frames = f.Frames;
                if (frames == null) continue;   // decode still in flight — falls empty (WPF)
                var image = frames.Frames[f.FrameIndex % frames.Frames.Length];
                if (image == null || image.Width <= 0 || image.Height <= 0) continue;

                // WPF layout: width = gifSize (Uniform → height by aspect), top at stage top,
                // grow center-origin, translate down by Y.
                var w = _gifSizePx;
                var h = w * image.Height / image.Width;
                var s = ScaleAt(f.Y);
                var cx = _stage.X + f.CenterX;
                var cy = _stage.Y + h / 2 + f.Y;
                var dest = new SKRect(
                    (float)(cx - w * s / 2), (float)(cy - h * s / 2),
                    (float)(cx + w * s / 2), (float)(cy + h * s / 2));
                canvas.DrawImage(image, dest, _paint);
            }
        }
    }
}
