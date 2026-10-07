using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Board;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// The pixel board as raised 3D tiles (Tonight Board, 2026-10-07). Every frame is drawn in
    /// software into one <see cref="WriteableBitmap"/> sized to the art area at a whole tile pitch,
    /// on a 30 fps <see cref="FrameClock"/> that runs only while the deck has Play()ed it (or a
    /// touch ripple is still spreading). No animated Effect, no visual per tile: one Image.
    /// A board that has nothing moving left (no effects, one frame, no ripple, landed) stops drawing.
    /// </summary>
    public sealed class BoardTileView : Grid, IBillboardArtView
    {
        private const int Fps = 30;

        private readonly BoardPicture? _picture;
        private readonly Action<int>? _onShown;
        private readonly BoardFxSet _fx;
        private readonly Image _image;
        private readonly FrameClock _clock = new() { Interval = TimeSpan.FromMilliseconds(1000.0 / Fps) };
        private readonly Stopwatch _watch = new();
        private readonly List<BoardRipple> _ripples = new();
        private readonly int[] _colour = new int[BoardPicture.Tiles];
        private readonly float[] _lift = new float[BoardPicture.Tiles];

        private BoardRaster? _raster;
        private WriteableBitmap? _bitmap;
        private double _timeBank;         // effect seconds banked across pauses
        private double? _arrivalStart;    // effect time the arrival began, null = never (still board)
        private bool _playing;            // the deck wants motion
        private bool _ambient;            // effects and strip frames may loop (Full motion, a tier that allows it)
        private bool _released;
        private bool _shownReported;
        private bool _dirty = true;

        public BoardTileView(object? data)
        {
            switch (data)
            {
                case BoardArtData d: _picture = d.Picture; _onShown = d.OnShown; break;
                case BoardPicture p: _picture = p; break;
            }
            _fx = BoardFxSet.From(_picture?.Post.Fx);

            Background = new SolidColorBrush(Color.FromRgb(0x04, 0x03, 0x0A));
            ClipToBounds = true;
            _image = new Image { Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
            Children.Add(_image);

            _clock.Tick += (_, _) => OnTick();
            SizeChanged += (_, _) => EnsureSurface();
            Loaded += OnLoaded;
            Unloaded += (_, _) => _clock.Stop();
        }

        /// <summary>Effect seconds right now (frozen while paused).</summary>
        private double Now => _timeBank + _watch.Elapsed.TotalSeconds;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_shownReported && _picture != null)
            {
                _shownReported = true;
                try { _onShown?.Invoke(_picture.Post.Version); } catch (Exception) { }
            }
            EnsureSurface();
            if (_playing) StartClock();
        }

        // ---- IBillboardArtView -----------------------------------------------------------------

        public void Play()
        {
            if (_released || _picture == null) return;
            if (!MotionFx.AllowTransitions) { Pause(); return; }
            _playing = true;
            _ambient = MotionFx.AllowAmbientLoops;
            // The first Play is the arrival: tiles pop up and land one by one, then the effects roll.
            _arrivalStart ??= Now;
            StartClock();
        }

        public void Pause()
        {
            _playing = false;
            // A board paused half built would look broken: let every tile land at once.
            if (_arrivalStart is { } a && Now - a < BoardFxMath.BuildDoneSeconds)
                _arrivalStart = Now - BoardFxMath.BuildDoneSeconds;
            if (_ripples.Count == 0) StopClock();
            _dirty = true;
            Render();
        }

        public void Release()
        {
            _released = true;
            _playing = false;
            StopClock();
            _ripples.Clear();
            _image.Source = null;
            _bitmap = null;
            _raster = null;
        }

        public void Touch(Point normalized)
        {
            if (_released || _picture == null || !MotionFx.AllowTransitions) return;
            if (!TryTileAt(normalized, out var tx, out var ty)) return;
            _arrivalStart ??= Now - BoardFxMath.BuildDoneSeconds;
            _ripples.Add(new BoardRipple(tx, ty, Now));
            if (_ripples.Count > 6) _ripples.RemoveAt(0);
            StartClock(); // runs even while the deck holds the card paused under the pointer
        }

        // ---- clock ---------------------------------------------------------------------------

        private void StartClock()
        {
            if (_released) return;
            if (!_watch.IsRunning) _watch.Start();
            if (IsLoaded) _clock.Start();
            _dirty = true;
        }

        private void StopClock()
        {
            _clock.Stop();
            if (_watch.IsRunning)
            {
                _timeBank += _watch.Elapsed.TotalSeconds;
                _watch.Reset();
            }
        }

        private void OnTick()
        {
            if (_released) { _clock.Stop(); return; }
            double now = Now;
            for (int k = _ripples.Count - 1; k >= 0; k--)
                if (now - _ripples[k].StartSeconds > BoardFxMath.RippleDropAfter) _ripples.RemoveAt(k);

            if (!_playing && _ripples.Count == 0)
            {
                StopClock();
                _dirty = true;
                Render();
                return;
            }
            Render();
            // Landed, nothing looping, no ripple: the picture is final, so the clock rests until a touch.
            if (!Animating && _ripples.Count == 0) StopClock();
        }

        // ---- drawing -------------------------------------------------------------------------

        private void EnsureSurface()
        {
            if (_released || ActualWidth <= 0 || ActualHeight <= 0) return;
            double scale = 1;
            try { scale = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch (Exception) { }
            // Fit by whichever side is tighter so the 16:9 grid never overflows the art area.
            double px = Math.Min(ActualWidth * scale, ActualHeight * scale * BoardPicture.GridW / BoardPicture.GridH);
            int pitch = BoardTileLayout.PitchFor(px);
            if (_raster == null || _raster.Layout.Pitch != pitch)
            {
                _raster = new BoardRaster(pitch);
                _bitmap = new WriteableBitmap(_raster.Width, _raster.Height, 96 * scale, 96 * scale, PixelFormats.Bgra32, null);
                _image.Source = _bitmap;
                _dirty = true;
            }
            Render();
        }

        private bool Animating =>
            _playing && ((_ambient && (_fx.Animates || (_picture?.FrameCount ?? 1) > 1)) ||
                         (_arrivalStart is { } a && Now - a < BoardFxMath.BuildDoneSeconds));

        private void Render()
        {
            if (_raster == null || _bitmap == null || _picture == null) return;
            bool moving = Animating || _ripples.Count > 0;
            if (!moving && !_dirty) return;
            _dirty = false;

            double t = Now;
            bool still = !_playing && _ripples.Count == 0;
            int frame = 0;
            var fx = _ambient ? _fx : default;
            if (_picture.FrameCount > 1 && !still && _ambient)
                frame = (int)(Math.Floor(t * _picture.Post.Fps) % _picture.FrameCount);
            double build = _arrivalStart is { } a ? t - a : 10;
            BoardScene.Compute(_picture, frame, fx, t, build, _ripples, still, _colour, _lift);
            // CRT is a look, not motion: it stays on a still board, only its flicker stops.
            _raster.Draw(_colour, _lift, _fx.Crt, still || !_ambient ? 0.012 : BoardFxMath.CrtFlicker(t));
            _bitmap.WritePixels(new Int32Rect(0, 0, _raster.Width, _raster.Height), _raster.Pixels, _raster.Width * 4, 0);
        }

        /// <summary>A point in 0..1 view coordinates to a tile, through the Uniform letterbox.</summary>
        private bool TryTileAt(Point n, out double tx, out double ty)
        {
            tx = ty = 0;
            double vw = ActualWidth, vh = ActualHeight;
            if (vw <= 0 || vh <= 0) return false;
            return BoardTouch.TileAt(n, vw, vh, out tx, out ty);
        }
    }

    /// <summary>Touch mapping, pure: the grid is drawn Uniform (16:9) and centred in the view.</summary>
    public static class BoardTouch
    {
        public static bool TileAt(Point normalized, double viewW, double viewH, out double tx, out double ty)
        {
            tx = ty = 0;
            const double gw = BoardPicture.GridW, gh = BoardPicture.GridH;
            double s = Math.Min(viewW / gw, viewH / gh);
            if (s <= 0) return false;
            double ox = (viewW - gw * s) / 2, oy = (viewH - gh * s) / 2;
            double x = (normalized.X * viewW - ox) / s, y = (normalized.Y * viewH - oy) / s;
            if (x < 0 || y < 0 || x >= gw || y >= gh) return false;
            tx = Math.Floor(x);
            ty = Math.Floor(y);
            return true;
        }
    }
}
