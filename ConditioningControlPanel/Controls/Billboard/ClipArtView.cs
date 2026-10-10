using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Showcase;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// The showcase card's art: a silent clip looping over its first-frame poster. WPF
    /// <see cref="MediaElement"/> on purpose, never LibVLC: LibVLC's mute and volume are
    /// process-wide on Windows and would silence a mandatory video (AGENTS.md). The player is
    /// muted AND at zero volume, made only on the first <see cref="Play"/> and closed on
    /// <see cref="Release"/>. With motion off, or before the clip opens, the poster shows.
    /// </summary>
    public sealed class ClipArtView : Grid, IBillboardArtView
    {
        private const int FadeInMs = 260;

        private readonly ShowcaseClipArt? _art;
        private readonly Image _poster;
        private MediaElement? _player;
        private bool _playing;
        private bool _opened;
        private bool _reportedPlayed;
        private bool _released;

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
            RenderOptions.SetBitmapScalingMode(_poster, BitmapScalingMode.HighQuality);
            Children.Add(_poster);
        }

        /// <summary>Whether a clip may move right now. A seam so the rule is in one place.</summary>
        internal static Func<bool> MotionAllowed { get; set; } = () => MotionFx.AllowAmbientLoops;

        public void Play()
        {
            if (_released || _art == null) return;
            if (!MotionAllowed())
            {
                Pause();
                return;
            }
            if (!File.Exists(_art.VideoPath)) return;

            if (_player == null) CreatePlayer();
            _playing = true;
            try { _player!.Play(); }
            catch (Exception ex) { Fail(ex.Message); return; }
            if (_opened) ReportPlayed();
        }

        public void Pause()
        {
            _playing = false;
            if (_player == null) return;
            try { _player.Pause(); }
            catch (Exception ex) { Fail(ex.Message); }
        }

        public void Release()
        {
            if (_released) return;
            _released = true;
            _playing = false;
            if (_player != null)
            {
                _player.MediaOpened -= OnOpened;
                _player.MediaEnded -= OnEnded;
                _player.MediaFailed -= OnFailed;
                try
                {
                    _player.Stop();
                    _player.Close();
                }
                catch { }
                _player.Source = null;
                Children.Remove(_player);
                _player = null;
            }
            _poster.Source = null;
        }

        /// <summary>The clip has nothing to touch.</summary>
        public void Touch(Point normalized) { }

        private void CreatePlayer()
        {
            _player = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                IsMuted = true,
                Volume = 0,
                ScrubbingEnabled = false,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0,
                IsHitTestVisible = false,
            };
            _player.MediaOpened += OnOpened;
            _player.MediaEnded += OnEnded;
            _player.MediaFailed += OnFailed;
            Children.Add(_player);
            _player.Source = new Uri(_art!.VideoPath, UriKind.Absolute);
        }

        private void OnOpened(object? sender, RoutedEventArgs e)
        {
            if (_player == null || _opened) return;
            _opened = true;
            if (MotionFx.AllowTransitions)
                _player.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(FadeInMs)));
            else
                _player.Opacity = 1;

            if (!_playing)
            {
                try { _player.Pause(); } catch { }
            }
            if (_playing) ReportPlayed();
        }

        /// <summary>Tells the provider, once, that this clip really ran: the rotation moves on.</summary>
        private void ReportPlayed()
        {
            if (_reportedPlayed || _art == null) return;
            _reportedPlayed = true;
            try { _art.Played?.Invoke(_art.ClipId); } catch { }
        }

        private void OnEnded(object? sender, RoutedEventArgs e)
        {
            if (_player == null) return;
            _player.Position = TimeSpan.Zero;
            if (_playing)
            {
                try { _player.Play(); } catch (Exception ex) { Fail(ex.Message); }
            }
        }

        private void OnFailed(object? sender, ExceptionRoutedEventArgs e) => Fail(e.ErrorException?.Message ?? "unknown");

        /// <summary>A clip that will not play leaves the poster up; the card still reads.</summary>
        private void Fail(string why)
        {
            App.Logger?.Warning("Showcase clip {Clip} would not play: {Why}", _art?.ClipId, why);
            if (_player == null) return;
            _player.BeginAnimation(OpacityProperty, null);
            _player.Opacity = 0;
            _playing = false;
        }

        private static ImageSource? LoadPoster(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // never keep the cache file open
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = 854;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }
}
