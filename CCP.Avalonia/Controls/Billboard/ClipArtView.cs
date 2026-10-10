using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Billboard.Showcase;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>
    /// The showcase card's art (WPF 7.1.5 Controls/Billboard/ClipArtView): a silent clip looping
    /// over its first-frame poster. WPF used a muted MediaElement; this head has none, so the clip
    /// decodes through <see cref="FlashClipPlayer"/>, which is silent BY CONSTRUCTION
    /// (<c>:no-audio</c> on the media, never Mute or Volume: those are process-wide in the decoder
    /// and would silence a mandatory video). The board itself still reaches for no audio API. The player is made only on the first <see cref="Play"/>
    /// and closed on <see cref="Release"/>. With motion off, or before the first frame lands, the
    /// poster shows. The one transition is a 260 ms fade of the player over the poster.
    ///
    /// <para>Pause closes the decoder and keeps the last frame up (there is no cheap freeze that
    /// keeps a callback surface alive); the next Play starts the clip again.</para>
    /// </summary>
    public sealed class ClipArtView : Grid, IBillboardArtView
    {
        internal const int FadeInMs = 260;

        /// <summary>The decode surface: 16:9, the card's own shape, 30 frames a second.</summary>
        internal const int DecodeWidth = 854, DecodeHeight = 480, FrameGapMs = 33;

        private readonly ShowcaseClipArt? _art;
        private readonly Image _poster;
        private Image? _screen;
        private IDisposable? _player;
        private FxTrack.Run? _fade;
        private bool _playing, _opened, _reportedPlayed, _released;

        public ClipArtView(ShowcaseClipArt? art)
        {
            _art = art;
            ClipToBounds = true;
            Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0d, 0x1a));

            _poster = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Source = LoadPoster(art?.PosterPath),
            };
            RenderOptions.SetBitmapInterpolationMode(_poster, BitmapInterpolationMode.HighQuality);
            Children.Add(_poster);
        }

        /// <summary>Whether a clip may move right now. A seam so the rule is in one place.</summary>
        internal static Func<bool> MotionAllowed { get; set; } = () => BoardMotion.AllowAmbientLoops;

        /// <summary>
        /// Starts the silent decoder for a file and hands each frame to <c>show</c> on the UI thread.
        /// Null = no player (no decoder installed, too many clips): the poster stays. Tests swap it.
        /// </summary>
        internal static Func<string, Action<WriteableBitmap>, IDisposable?> StartPlayer { get; set; } = DefaultStartPlayer;

        private static IDisposable? DefaultStartPlayer(string path, Action<WriteableBitmap> show) =>
            FlashClipPlayer.StartSilent(path, DecodeWidth, DecodeHeight, show, DecodeWidth, FrameGapMs);

        /// <summary>True while a decoder is alive (tests).</summary>
        internal bool HasPlayer => _player != null;

        public void Play()
        {
            if (_released || _art == null) return;
            if (!MotionAllowed())
            {
                Pause();
                return;
            }
            if (!File.Exists(_art.VideoPath)) return;
            if (_playing && _player != null) return;

            _playing = true;
            if (_player == null)
            {
                try { CreatePlayer(); }
                catch (Exception ex) { Fail(ex.Message); return; }
                if (_player == null) { _playing = false; return; }
            }
            if (_opened) ReportPlayed();
        }

        public void Pause()
        {
            _playing = false;
            ClosePlayer();
        }

        public void Release()
        {
            if (_released) return;
            _released = true;
            _playing = false;
            ClosePlayer();
            _fade?.Finish();
            _fade = null;
            if (_screen != null)
            {
                _screen.Source = null;
                Children.Remove(_screen);
                _screen = null;
            }
            (_poster.Source as IDisposable)?.Dispose();
            _poster.Source = null;
        }

        /// <summary>The clip has nothing to touch.</summary>
        public void Touch(Point normalized) { }

        private void CreatePlayer()
        {
            if (_screen == null)
            {
                _screen = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0,
                    IsHitTestVisible = false,
                };
                Children.Add(_screen);
            }
            _player = StartPlayer(_art!.VideoPath, OnFrame);
        }

        private void ClosePlayer()
        {
            var player = _player;
            _player = null;
            if (player == null) return;
            try { player.Dispose(); }
            catch (Exception ex) { Log.Debug("Showcase clip close failed: {E}", ex.Message); }
        }

        /// <summary>A decoded frame, on the UI thread. The first one opens the clip: the fade in.</summary>
        internal void OnFrame(WriteableBitmap frame)
        {
            if (_released || _screen == null || _player == null) return;
            if (!ReferenceEquals(_screen.Source, frame)) _screen.Source = frame;
            _screen.InvalidateVisual();
            if (_opened) return;
            _opened = true;

            var screen = _screen;
            if (BoardMotion.AllowTransitions)
                _fade = FxTrack.Play(FadeInMs, ms => screen.Opacity = Math.Clamp(ms / FadeInMs, 0, 1), () => _fade = null);
            else
                screen.Opacity = 1;
            if (_playing) ReportPlayed();
        }

        /// <summary>Tells the provider, once, that this clip really ran: the rotation moves on.</summary>
        private void ReportPlayed()
        {
            if (_reportedPlayed || _art == null) return;
            _reportedPlayed = true;
            try { _art.Played?.Invoke(_art.ClipId); } catch { }
        }

        /// <summary>A clip that will not play leaves the poster up; the card still reads.</summary>
        private void Fail(string why)
        {
            Log.Warning("Showcase clip {Clip} would not play: {Why}", _art?.ClipId, why);
            ClosePlayer();
            _fade?.Finish();
            _fade = null;
            if (_screen != null) _screen.Opacity = 0;
            _playing = false;
        }

        private static IImage? LoadPoster(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                // Decoded from a stream that closes at once: the cache file is never held open.
                using var fs = File.OpenRead(path);
                return Bitmap.DecodeToWidth(fs, 854, BitmapInterpolationMode.HighQuality);
            }
            catch
            {
                return null;
            }
        }
    }
}
