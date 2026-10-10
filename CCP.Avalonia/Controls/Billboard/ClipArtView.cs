// PORTED from ConditioningControlPanel/Controls/Billboard/ClipArtView.cs (the showcase card's art).
// WPF plays the clip with a muted MediaElement; this head's only video path is LibVLC into a
// VlcFrameSink bitmap, made muted with no audio track at all, so the board stays silent.

using System;
using System.IO;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Billboard.Showcase;
using LibVLCSharp.Shared;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>WPF ClipArtView: a silent clip looping over its first-frame poster. The player is
    /// made on the first <see cref="Run"/> and freed on <see cref="Release"/>; with ambient loops
    /// off, before the first frame, or when the clip will not decode, the poster shows.</summary>
    public sealed class ClipArtView : Grid, IBillboardArtView
    {
        private const int FadeInMs = 260;

        private readonly ShowcaseClipArt? _art;
        private readonly Image _poster;
        private Image? _video;
        private MediaPlayer? _player;
        private Media? _media;
        private VlcFrameSink? _sink;
        private bool _running, _opened, _reportedPlayed, _released, _failed;

        /// <summary>Whether a clip may move right now (WPF MotionFx.AllowAmbientLoops). A seam.</summary>
        internal static Func<bool> MotionAllowed { get; set; } = () => Env.AllowAmbientLoops;

        public ClipArtView(ShowcaseClipArt? art)
        {
            _art = art;
            ClipToBounds = true;
            Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0d, 0x1a));
            _poster = new Image
            {
                Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Source = LoadPoster(art?.PosterPath),
            };
            RenderOptions.SetBitmapInterpolationMode(_poster, BitmapInterpolationMode.HighQuality);
            Children.Add(_poster);
        }

        /// <summary>The player, once made (test seam).</summary>
        internal bool HasPlayer => _player != null;

        internal Image Poster => _poster;

        public void Run()
        {
            if (_released || _failed || _art == null) return;
            if (!MotionAllowed()) { Hold(); return; }
            if (!File.Exists(_art.VideoPath)) return;
            if (_player == null && !CreatePlayer()) return;
            _running = true;
            try
            {
                if (_player!.State == VLCState.Paused) _player.SetPause(false);
                else if (!_player.IsPlaying) _player.Play(_media!);
            }
            catch (Exception ex) { Fail(ex.Message); return; }
            if (_opened) ReportPlayed();
        }

        public void Hold()
        {
            _running = false;
            try { if (_player?.IsPlaying == true) _player.SetPause(true); }
            catch (Exception ex) { Fail(ex.Message); }
        }

        public void Release()
        {
            if (_released) return;
            _released = true;
            _running = false;
            FreePlayer();
            if (_video != null) Children.Remove(_video);
            _video = null;
            _poster.Source = null;
        }

        private bool CreatePlayer()
        {
            var vlc = LibVlcAudio.Shared;
            if (vlc == null) return false;   // no LibVLC on this machine: the poster stands
            _video = new Image
            {
                Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0, IsHitTestVisible = false,
            };
            Children.Add(_video);
            _player = new MediaPlayer(vlc) { EnableHardwareDecoding = true, Mute = true };
            _sink = new VlcFrameSink(_player, () => _media, OnFirstBitmap, () => _video?.InvalidateVisual());
            _media = new Media(vlc, _art!.VideoPath, FromType.FromPath);
            _media.AddOption(":no-audio");
            _media.AddOption(":input-repeat=65535");   // WPF MediaEnded -> Position 0 -> Play
            _player.EncounteredError += (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Fail("decoder error"));
            return true;
        }

        /// <summary>WPF OnOpened: the first frame is in, the clip fades over its poster.</summary>
        private void OnFirstBitmap(WriteableBitmap bmp)
        {
            if (_video == null || _released) return;
            _video.Source = bmp;
            if (_opened) return;
            _opened = true;
            if (Env.AllowTransitions)
                _video.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(FadeInMs) } };
            _video.Opacity = 1;
            if (!_running) Hold();
            else ReportPlayed();
        }

        /// <summary>Tells the provider, once, that this clip really ran: the rotation moves on.</summary>
        private void ReportPlayed()
        {
            if (_reportedPlayed || _art == null) return;
            _reportedPlayed = true;
            try { _art.Played?.Invoke(_art.ClipId); } catch (Exception ex) { Log.Debug("Showcase played hook failed: {E}", ex.Message); }
        }

        /// <summary>A clip that will not play leaves the poster up; the card still reads.</summary>
        private void Fail(string why)
        {
            Log.Warning("Showcase clip {Clip} would not play: {Why}", _art?.ClipId, why);
            _failed = true;
            _running = false;
            FreePlayer();
            if (_video != null) Children.Remove(_video);
            _video = null;
        }

        private void FreePlayer()
        {
            if (_player != null)
            {
                try { _player.Stop(); } catch (Exception ex) { Log.Debug("Showcase stop: {E}", ex.Message); }   // joins the decoder thread
                try { _player.Dispose(); } catch (Exception ex) { Log.Debug("Showcase dispose: {E}", ex.Message); }
            }
            _player = null;
            try { _media?.Dispose(); } catch (Exception ex) { Log.Debug("Showcase media dispose: {E}", ex.Message); }
            _media = null;
            _sink?.Free();
            _sink = null;
        }

        private static Bitmap? LoadPoster(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using var fs = File.OpenRead(path);   // never keep the cache file open
                return Bitmap.DecodeToWidth(fs, 854, BitmapInterpolationMode.HighQuality);
            }
            catch (Exception ex) { Log.Debug("Showcase poster did not load: {E}", ex.Message); return null; }
        }
    }
}
