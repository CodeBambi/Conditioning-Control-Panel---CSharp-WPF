using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Services;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The Avalonia <see cref="IMandatoryVideoHost"/>, after WPF VideoService.StartVideoPlayback /
    /// CreateLibVLCVideoWindow / SetupStrictHandlers / CloseAll: a borderless, topmost, full-screen
    /// black window per targeted screen (GlobalTargetMonitor/DualMonitorEnabled, secondaries capped
    /// as WPF ShouldFillSecondaryMonitors), the clip letterboxed in each. One decoder on the shared
    /// LibVLC feeds every screen (WPF ran one muted decoder per secondary), with audio at
    /// master x video volume.
    /// ponytail: missing against WPF - the blurred-background fill (VideoBlurredBackgroundEnabled,
    /// default on), attention-check targets and their XP, the Esc grace pause and its overlays,
    /// the safety/max-length/vout/wedge watchdogs, audio ducking, pausing bubbles/bouncing text,
    /// no-activate z-order, and the "no videos" guidance dialog.
    /// </summary>
    internal sealed class MandatoryVideoOverlay : IMandatoryVideoHost
    {
        public static MandatoryVideoOverlay Instance { get; } = new();
        public MandatoryVideoScheduler Scheduler { get; internal set; }   // tests swap in a fixed library

        private readonly List<Window> _windows = new();
        private MediaPlayer? _player;
        private Media? _media;
        private VlcFrameSink? _sink;
        private bool _closing;
        private long _watchedMs;
        private readonly Stopwatch _sinceShow = new();

        private MandatoryVideoOverlay() => Scheduler = new MandatoryVideoScheduler(this);

        internal IReadOnlyList<Window> Windows => _windows;
        internal VlcFrameSink? Sink => _sink;
        /// <summary>Milliseconds from Show to the first frame on screen; -1 until then.</summary>
        internal long FirstFrameMs { get; private set; } = -1;

        public void Show(string path, bool strict) => Dispatcher.UIThread.Invoke(() =>
        {
            if (_windows.Count > 0) CloseAll();
            _sinceShow.Restart();
            FirstFrameMs = -1;
            _watchedMs = 0;

            var s = CoreSettings.Current;
            var host = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var screens = host is null ? Array.Empty<global::Avalonia.Platform.Screen>() : ScreenList.Enumerate(host);
            var targets = new List<global::Avalonia.Platform.Screen?>();
            if (screens.Count > 0)
            {
                var primaryIdx = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
                var idx = PinkFilterOverlay.ResolveScreenIndices(s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primaryIdx);
                var first = idx.Contains(primaryIdx) ? primaryIdx : idx[0];
                targets.Add(screens[first]);
                if (MandatoryVideoScheduler.ShouldFillSecondaryMonitors(idx.Length, s.FillAllMonitorsWithVideo))
                    targets.AddRange(idx.Where(i => i != first).Select(i => screens[i]));
            }
            else targets.Add(null);   // no screen list (headless): one window where the platform puts it

            foreach (var screen in targets) _windows.Add(Build(screen, strict));

            var vlc = LibVlcAudio.Shared;
            if (vlc == null)
            {
                Log.Warning("VideoService: LibVLC is not available - cannot play {Path}", path);
                Dispatcher.UIThread.Post(Scheduler.End);
                return;
            }
            _player = new MediaPlayer(vlc) { EnableHardwareDecoding = true };
            _player.Volume = MandatoryVideoScheduler.EffectiveVolume(s.MasterVolume, s.VideoVolume);
            _sink = new VlcFrameSink(_player, () => _media, bmp =>
            {
                foreach (var w in _windows) ((Image)w.Content!).Source = bmp;
            }, () =>
            {
                if (FirstFrameMs < 0)
                {
                    FirstFrameMs = _sinceShow.ElapsedMilliseconds;
                    Log.Information("VideoService: first frame after {Ms} ms", FirstFrameMs);
                }
                foreach (var w in _windows) ((Image)w.Content!).InvalidateVisual();
            });
            _player.TimeChanged += (_, e) => Interlocked.Exchange(ref _watchedMs, e.Time);
            _player.EndReached += (_, _) =>
            {
                var len = _player?.Length ?? 0;
                if (len > 0) Interlocked.Exchange(ref _watchedMs, len);
                Dispatcher.UIThread.Post(Scheduler.End);   // never tear down on LibVLC's own thread
            };
            _player.EncounteredError += (_, _) => Dispatcher.UIThread.Post(Scheduler.End);
            _media = new Media(vlc, path, FromType.FromPath);
            _player.Play(_media);
        });

        private Window Build(global::Avalonia.Platform.Screen? screen, bool strict)
        {
            var w = new Window
            {
                Title = "CCP Video",
                WindowDecorations = WindowDecorations.None,
                Topmost = true,
                ShowInTaskbar = false,
                CanResize = false,
                Background = Brushes.Black,
                Content = new Image { Stretch = Stretch.Uniform },
            };
            if (screen != null)
            {
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Position = screen.Bounds.Position;
                w.Width = screen.Bounds.Width / screen.Scaling;
                w.Height = screen.Bounds.Height / screen.Scaling;
            }
            w.KeyDown += (_, e) =>
            {
                var s = CoreSettings.Current;
                switch (MandatoryVideoScheduler.KeyAction(strict, e.Key.ToString(), e.KeyModifiers.HasFlag(KeyModifiers.Alt), s.PanicKeyEnabled, s.PanicKey))
                {
                    case VideoKeyAction.Dismiss: e.Handled = true; Scheduler.End(); break;
                    case VideoKeyAction.ForceStop: e.Handled = true; Scheduler.ForceCleanup(); break;
                    case VideoKeyAction.Swallow: e.Handled = true; break;
                }
            };
            // WPF: strict vetoes a user close while playing; a non-strict close is a dismiss.
            w.Closing += (_, e) => { if (!_closing && strict && Scheduler.IsPlaying) e.Cancel = true; };
            w.Closed += (_, _) => { if (!_closing) Scheduler.End(); };
            w.Show();
            w.WindowState = WindowState.FullScreen;
            w.Activate();
            return w;
        }

        public double CloseAll() => Dispatcher.UIThread.Invoke(() =>
        {
            _closing = true;
            try
            {
                // Stop joins the decoder thread, so after it no callback touches the frame buffer.
                if (_player != null)
                {
                    try { _player.Stop(); } catch { }
                    try { _player.Dispose(); } catch { }
                }
                _player = null;
                try { _media?.Dispose(); } catch { }
                _media = null;
                foreach (var w in _windows) { ((Image)w.Content!).Source = null; w.Close(); }
                _windows.Clear();
                _sink?.Free();
                _sink = null;
            }
            finally { _closing = false; }
            return Interlocked.Exchange(ref _watchedMs, 0) / 1000.0;
        });
    }
}
