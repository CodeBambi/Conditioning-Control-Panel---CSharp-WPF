using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Board;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>
    /// The pixel board as raised 3D tiles (Tonight Board, WPF 7.1.5 Controls/Billboard/BoardTileView).
    /// Every frame is drawn in software by Core's <see cref="BoardRaster"/> into one
    /// <see cref="WriteableBitmap"/> sized to the art area at a whole tile pitch, on a 30 fps
    /// <see cref="FrameClock"/> that runs only while the deck has Play()ed it (or a touch ripple is
    /// still spreading). No effect, no visual per tile: one bitmap drawn nearest-neighbour. A board
    /// with nothing moving left (no effects, one frame, no ripple, landed) stops drawing.
    /// </summary>
    public sealed class BoardTileView : Control, IBillboardArtView
    {
        private const int Fps = 30;
        private static readonly IImmutableSolidColorBrush Grout = new ImmutableSolidColorBrush(Color.FromRgb(0x04, 0x03, 0x0A));

        private readonly BoardPicture? _picture;
        private readonly Action<int>? _onShown;
        private readonly BoardFxSet _fx;
        private readonly FrameClock _clock;
        private readonly Stopwatch _watch = new();
        private readonly List<BoardRipple> _ripples = new();
        private readonly int[] _colour = new int[BoardPicture.Tiles];
        private readonly float[] _lift = new float[BoardPicture.Tiles];
        private readonly BoardTileRole[] _role = new BoardTileRole[BoardPicture.Tiles];

        private BoardRaster? _raster;
        private WriteableBitmap? _bitmap;
        private readonly Stopwatch _wall = Stopwatch.StartNew(); // ripple clock: always runs
        private double _fxBank;           // effect seconds banked across pauses
        private double? _arrivalStart;    // effect time the arrival began; null = never played (a flat, still board)
        private bool _playing;            // the deck wants motion
        private bool _ambient;            // effects and strip frames may loop (Full motion, a tier that allows it)
        private bool _released;
        private bool _shownReported;
        private bool _dirty = true;
        private bool _loaded;

        /// <summary>Tests pin the effect clock here to draw a chosen moment (seconds since arrival).</summary>
        internal double? FxSecondsForTests { get; set; }

        static BoardTileView() => BoardPng.Install();

        public BoardTileView(object? data)
        {
            switch (data)
            {
                case BoardArtData d: _picture = d.Picture; _onShown = d.OnShown; break;
                case BoardPicture p: _picture = p; break;
            }
            _fx = BoardFxSet.From(_picture?.Post.Fx);
            ClipToBounds = true;
            _clock = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(1000.0 / Fps) };
            _clock.Tick += (_, _) => OnTick();
            AttachedToVisualTree += (_, _) => OnLoaded();
            DetachedFromVisualTree += (_, _) => { _loaded = false; _clock.Stop(); };
        }

        /// <summary>The picture this view draws.</summary>
        public BoardPicture? Picture => _picture;

        /// <summary>The bitmap's pitch (tests).</summary>
        internal int Pitch => _raster?.Layout.Pitch ?? 0;

        /// <summary>True while the frame clock runs (tests).</summary>
        internal bool ClockRunning => _clock.IsEnabled;

        /// <summary>Effect seconds: they run only while the deck has the card playing.</summary>
        private double FxNow => FxSecondsForTests ?? _fxBank + _watch.Elapsed.TotalSeconds;

        private double WallNow => _wall.Elapsed.TotalSeconds;

        private void OnLoaded()
        {
            _loaded = true;
            if (!_shownReported && _picture != null)
            {
                _shownReported = true;
                try { _onShown?.Invoke(_picture.Post.Version); } catch (Exception) { }
            }
            EnsureSurface();
            UpdateClock();
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            EnsureSurface();
        }

        // ---- IBillboardArtView -----------------------------------------------------------------

        public void Play()
        {
            if (_released || _picture == null) return;
            if (!BoardMotion.AllowTransitions) { Pause(); return; }
            _playing = true;
            _ambient = BoardMotion.AllowAmbientLoops;
            if (!_watch.IsRunning) _watch.Start();
            // The first Play is the arrival: tiles pop up and land one by one, then the effects roll.
            _arrivalStart ??= FxSecondsForTests.HasValue ? 0 : FxNow;
            _dirty = true;
            UpdateClock();
        }

        public void Pause()
        {
            _playing = false;
            if (_watch.IsRunning)
            {
                _fxBank += _watch.Elapsed.TotalSeconds;
                _watch.Reset();
            }
            // A board paused half built would look broken: let every tile land at once.
            if (_arrivalStart is { } a && FxNow - a < BoardFxMath.BuildDoneSeconds)
                _arrivalStart = FxNow - BoardFxMath.BuildDoneSeconds;
            _dirty = true;
            Render();
            UpdateClock();
        }

        public void Release()
        {
            _released = true;
            _playing = false;
            _clock.Stop();
            _watch.Reset();
            _ripples.Clear();
            _bitmap?.Dispose();
            _bitmap = null;
            _raster = null;
            InvalidateVisual();
        }

        public void Touch(Point normalized)
        {
            if (_released || _picture == null || !BoardMotion.AllowTransitions) return;
            if (!BoardTouch.TileAt(normalized.X, normalized.Y, Bounds.Width, Bounds.Height, out var tx, out var ty)) return;
            // A touch on a board that never played lands it first, so the ripple has tiles to lift.
            _arrivalStart ??= FxNow - BoardFxMath.BuildDoneSeconds;
            _ripples.Add(new BoardRipple(tx, ty, WallNow));
            if (_ripples.Count > 6) _ripples.RemoveAt(0);
            _dirty = true;
            UpdateClock(); // a ripple spreads even while the deck holds the card paused under the pointer
        }

        // ---- clock ---------------------------------------------------------------------------

        /// <summary>The clock runs only while something moves: an effect, a strip, the arrival or a ripple.</summary>
        private void UpdateClock()
        {
            bool run = !_released && _loaded && (Animating || _ripples.Count > 0);
            if (run) _clock.Start(); else _clock.Stop();
        }

        private void OnTick()
        {
            if (_released) { _clock.Stop(); return; }
            double wall = WallNow;
            bool hadRipples = _ripples.Count > 0;
            for (int k = _ripples.Count - 1; k >= 0; k--)
                if (wall - _ripples[k].StartSeconds > BoardFxMath.RippleDropAfter) _ripples.RemoveAt(k);
            if (hadRipples && _ripples.Count == 0) _dirty = true; // draw the settled frame once
            Render();
            UpdateClock();
        }

        // ---- drawing -------------------------------------------------------------------------

        private void EnsureSurface()
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (_released || w <= 0 || h <= 0) return;
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            // Fit by whichever side is tighter so the 16:9 grid never overflows the art area.
            double px = Math.Min(w * scale, h * scale * BoardPicture.GridW / BoardPicture.GridH);
            int pitch = BoardTileLayout.PitchFor(px);
            if (_raster == null || _raster.Layout.Pitch != pitch)
            {
                _raster = new BoardRaster(pitch);
                // The message glow is baked once here, per picture and pitch; frames only scale it.
                _raster.SetPicture(_picture);
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(new PixelSize(_raster.Width, _raster.Height), new Vector(96 * scale, 96 * scale),
                    PixelFormat.Bgra8888, AlphaFormat.Premul);
                _dirty = true;
            }
            Render();
        }

        private bool Animating =>
            _playing && ((_ambient && (_fx.Animates || (_picture?.FrameCount ?? 1) > 1)) ||
                         (_arrivalStart is { } a && FxNow - a < BoardFxMath.BuildDoneSeconds));

        /// <summary>Computes and blits one frame. Internal so tests can force a frame.</summary>
        internal void Render()
        {
            if (_raster == null || _bitmap == null || _picture == null) return;
            if (!Animating && _ripples.Count == 0 && !_dirty) return;
            _dirty = false;

            double t = FxNow;
            bool still = _arrivalStart == null;
            var fx = _ambient ? _fx : default;
            int frame = 0;
            if (_picture.FrameCount > 1 && _ambient && !still)
                frame = (int)(Math.Floor(t * _picture.Post.Fps) % _picture.FrameCount);
            double build = _arrivalStart is { } a ? t - a : BoardFxMath.BuildDoneSeconds;
            BoardScene.Compute(_picture, frame, fx, t, build, _ripples, WallNow, still, _colour, _lift, _role);
            // The glow breathes only on a clock that already runs for the effects (Full motion);
            // under Reduced or Off it holds still. It rises with the arrival, never ahead of it.
            bool breathe = _ambient && _playing && _fx.Animates;
            double glow = BoardFxMath.GlowStrength(t, build, still, breathe);
            // CRT is a look, not motion: it stays on a still board, only its flicker stops.
            _raster.Draw(_colour, _lift, _fx.Crt, _ambient && _playing ? BoardFxMath.CrtFlicker(t) : 0.012, _role, glow);
            using (var fb = _bitmap.Lock())
            {
                int w = _raster.Width;
                for (int y = 0; y < _raster.Height; y++)
                    Marshal.Copy(_raster.Pixels, y * w, fb.Address + y * fb.RowBytes, w);
            }
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var b = Bounds;
            context.FillRectangle(Grout, new Rect(0, 0, b.Width, b.Height));
            if (_bitmap == null || _raster == null) return;
            // Uniform, centred: the grid is 16:9; the letterbox shows the grout.
            double s = Math.Min(b.Width / _raster.Width, b.Height / _raster.Height);
            double dw = _raster.Width * s, dh = _raster.Height * s;
            var dest = new Rect((b.Width - dw) / 2, (b.Height - dh) / 2, dw, dh);
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = global::Avalonia.Media.Imaging.BitmapInterpolationMode.None }))
                context.DrawImage(_bitmap, new Rect(0, 0, _raster.Width, _raster.Height), dest);
        }
    }

    /// <summary>
    /// The head's PNG codec for Core's board (<see cref="BoardPicture.PngDecoder"/>): Skia decodes
    /// to straight (unpremultiplied) BGRA, read back as 0xAARRGGBB ints. WPF 7.1.5 decoded with its
    /// own imaging inside BoardPicture.Decode.
    /// </summary>
    public static class BoardPng
    {
        public static void Install() => BoardPicture.PngDecoder ??= Decode;

        public static (int[] Pixels, int Width, int Height)? Decode(byte[] png)
        {
            try
            {
                using var stream = new SKMemoryStream(png);
                using var codec = SKCodec.Create(stream);
                if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0) return null;
                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                using var bgra = SKBitmap.Decode(codec, info);
                if (bgra == null) return null;
                var px = new int[bgra.Width * bgra.Height];
                Marshal.Copy(bgra.GetPixels(), px, 0, px.Length);
                return (px, bgra.Width, bgra.Height);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
