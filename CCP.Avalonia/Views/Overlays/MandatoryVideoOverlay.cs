using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The Avalonia <see cref="IMandatoryVideoHost"/>, after WPF VideoService.StartVideoPlayback /
    /// CreateLibVLCVideoWindow / SetupStrictHandlers / SetupAttention / CloseAll: a borderless, topmost,
    /// full-screen black window per targeted screen (GlobalTargetMonitor/DualMonitorEnabled, secondaries
    /// capped as WPF ShouldFillSecondaryMonitors), the clip letterboxed in each over WPF's blurred fill
    /// (VideoBlurredBackgroundEnabled), with the attention targets on top. One decoder on the shared
    /// LibVLC feeds every screen (WPF ran one muted decoder per secondary), with audio at
    /// master x video volume; other apps are ducked while it plays (AudioDuckingEnabled).
    /// The Esc grace pause (WPF #735) draws its Paused/Resume card inside each video window, not as a
    /// separate topmost window; the clip guards (Core <see cref="MandatoryVideoScheduler.Guard"/>) run
    /// on a 1 s UI timer: a dead (8 s) or lost (5 s without frames) output replays the clip once, as
    /// WPF's vout heal, but on the same shared LibVLC (docs/avalonia-decisions.md). Ambient bubbles
    /// are paused by the Core scheduler.
    /// ponytail: missing against WPF - the off-thread UI-wedge watchdog and the LibVLC retire/quarantine
    /// (a Stop that hangs in native code still hangs the UI thread), the toy-button/gaze/haptics
    /// attention inputs, no-activate z-order, and the remote-media offer after the "no videos" dialog.
    /// </summary>
    internal sealed class MandatoryVideoOverlay : IMandatoryVideoHost
    {
        public static MandatoryVideoOverlay Instance { get; } = new();
        private MandatoryVideoScheduler _scheduler = null!;
        public MandatoryVideoScheduler Scheduler   // tests swap in a fixed library
        {
            get => _scheduler;
            internal set { if (_scheduler != null) _scheduler.NoVideos -= ShowNoVideos; _scheduler = value; value.NoVideos += ShowNoVideos; }
        }

        /// <summary>One screen's window: blurred fill + scrim, the clip, and the attention plane.</summary>
        internal sealed record Surface(Window Window, Image Fill, Border Scrim, Image Video, Canvas Layer, Border Grace, TextBlock Countdown);

        private readonly List<Surface> _surfaces = new();
        private readonly List<Window> _messages = new();
        private readonly List<Target> _targets = new();
        private List<double> _spawnTimes = new();
        private MediaPlayer? _player;
        private Media? _media;
        private VlcFrameSink? _sink;
        private WriteableBitmap? _fill;
        private bool _closing, _didDuck, _attention, _gracePaused, _graceConsumed;
        private long _watchedMs;
        /// <summary>Stopwatch timestamp of the last frame shown (tests age it to fake a lost output).</summary>
        internal long FrameTs;
        private bool _healUsed, _strict;
        private string _path = "";
        private readonly Stopwatch _sinceFrame = new();
        private DateTime _lastGraceUtc, _gracePausedAt;
        /// <summary>The grace pause's wall clock (dedup, countdown, auto-resume); tests step it.</summary>
        internal TimeProvider Time { get; set; } = TimeProvider.System;
        private DateTime Now => Time.GetUtcNow().UtcDateTime;
        private DispatcherTimer? _graceTimer, _guard;
        private readonly Stopwatch _sinceShow = new();

        private MandatoryVideoOverlay() => Scheduler = new MandatoryVideoScheduler(this) { DurationOf = LengthOf };

        /// <summary>WPF VideoService.MetadataCache: created on first use on the shared LibVLC; a miss starts
        /// a background parse and the clip is kept this refill.</summary>
        private static VideoMetadataCache? _lengths;
        private static double? LengthOf(string path)
        {
            if (_lengths == null && LibVlcAudio.Shared is { } vlc) _lengths = new VideoMetadataCache(vlc);
            var d = _lengths?.TryGetDuration(path);
            if (d == null && _lengths != null) _ = _lengths.GetOrComputeDurationAsync(path);
            return d;
        }

        /// <summary>The global panic listener can stop a video right now (LockCardWindow #875's
        /// PanicHookIsInstalled). Tests swap it.</summary>
        internal static Func<bool> PanicListenerLive = () => X11PanicKey.IsListening && X11PanicKey.BoundKeycode != 0;

        internal IReadOnlyList<Window> Windows => _surfaces.Select(s => s.Window).ToList();
        internal IReadOnlyList<Surface> Surfaces => _surfaces;
        internal IReadOnlyList<Window> Messages => _messages;
        internal VlcFrameSink? Sink => _sink;
        internal int LiveTargets => _targets.Count;
        /// <summary>Milliseconds from Show to the first frame on screen; -1 until then.</summary>
        internal long FirstFrameMs { get; private set; } = -1;
        /// <summary>Total ms spent downscaling blur fills, and how many (CCP perf check).</summary>
        internal double FillMs { get; private set; }
        internal int FillCount { get; private set; }

        public void Show(string path, bool strict) => Dispatcher.UIThread.Invoke(() =>
        {
            _healUsed = false;   // WPF PlayVideo: one output heal per clip; the heal's replay keeps it spent
            Play(path, strict);
        });

        private void Play(string path, bool strict)
        {
            if (_surfaces.Count > 0) CloseAll();
            (_path, _strict) = (path, strict);
            _sinceShow.Restart();
            _sinceFrame.Reset();
            FirstFrameMs = -1;
            _watchedMs = 0;
            FillMs = FillCount = 0;
            _graceConsumed = false;   // WPF PlayVideo: one grace pause per clip, replays included

            var s = CoreSettings.Current;
            var host = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            foreach (var screen in Targets(host, s.FillAllMonitorsWithVideo)) _surfaces.Add(Build(screen, strict));

            var vlc = LibVlcAudio.Shared;
            if (vlc == null)
            {
                Log.Warning("VideoService: LibVLC is not available - cannot play {Path}", path);
                Dispatcher.UIThread.Post(Scheduler.End);
                return;
            }
            // WPF StartVideoPlayback prologue: duck other apps (one ref, released in CloseAll), sleep the
            // bouncing text, and arm the attention checks.
            if (s.AudioDuckingEnabled) { CoreAudio.Duck(s.DuckingLevel); _didDuck = true; }
            BouncingTextOverlay.PauseForVideo(true);
            _attention = s.AttentionChecksEnabled;

            _player = new MediaPlayer(vlc) { EnableHardwareDecoding = true };
            _player.Volume = MandatoryVideoScheduler.EffectiveVolume(s.MasterVolume, s.VideoVolume);
            var blur = s.VideoBlurredBackgroundEnabled;
            _sink = new VlcFrameSink(_player, () => _media, bmp =>
            {
                foreach (var x in _surfaces) x.Video.Source = bmp;
                if (!blur) return;
                // WPF BlurVmemSurface: the frame UniformToFill, blurred (radius 48), under a 90/255 black
                // scrim, only where the clip's aspect leaves bars. Here the blur is a 64-px-wide copy
                // the upscale smooths - a fraction of a millisecond per frame, not a full-screen blur.
                var aspect = (double)bmp.PixelSize.Width / bmp.PixelSize.Height;
                _fill?.Dispose();
                _fill = new WriteableBitmap(new PixelSize(FillWidth, Math.Max(1, (int)Math.Round(FillWidth / aspect))), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                foreach (var x in _surfaces)
                {
                    var sb = x.Window.Bounds;
                    var on = MandatoryVideoScheduler.NeedsBlurFill(aspect, sb.Width > 0 && sb.Height > 0 ? sb.Width / sb.Height : 16.0 / 9);
                    x.Fill.Source = on ? _fill : null;
                    x.Fill.IsVisible = x.Scrim.IsVisible = on;
                }
            }, () =>
            {
                FrameTs = Stopwatch.GetTimestamp();
                if (FirstFrameMs < 0)
                {
                    FirstFrameMs = _sinceShow.ElapsedMilliseconds;
                    if (!_gracePaused) _sinceFrame.Start();   // WPF arms the length/max timers once playing
                    Log.Information("VideoService: first frame after {Ms} ms", FirstFrameMs);
                }
                var fill = _surfaces.Any(x => x.Fill.IsVisible);
                if (fill && _sink?.Bitmap is { } src && _fill != null)
                {
                    var t = Stopwatch.GetTimestamp();
                    Downscale(src, _fill);
                    FillMs += Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                    FillCount++;
                }
                foreach (var x in _surfaces)
                {
                    x.Video.InvalidateVisual();
                    if (fill) x.Fill.InvalidateVisual();
                }
            });
            var live = _player;
            // Teardown runs on the UI thread, so checking _player there never touches a disposed player.
            _player.Playing += (_, _) => Dispatcher.UIThread.Post(() => { if (_player == live) LibVlcAudio.ApplyPreferredDevice(live); });
            _player.TimeChanged += (_, e) => Interlocked.Exchange(ref _watchedMs, e.Time);
            _player.EndReached += (_, _) =>
            {
                var len = _player?.Length ?? 0;
                if (len > 0) Interlocked.Exchange(ref _watchedMs, len);
                Dispatcher.UIThread.Post(() =>   // never tear down on LibVLC's own thread
                {
                    // WPF OnEnded: a clip that really played to its end earns Circe's "video" note.
                    if (Scheduler.IsPlaying && FirstFrameMs >= 0) App.ChasterNote("video");
                    Scheduler.Ended();
                });
            };
            _player.EncounteredError += (_, _) => Dispatcher.UIThread.Post(Scheduler.End);
            _media = new Media(vlc, path, FromType.FromPath);
            _player.Play(_media);
            _guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _guard.Tick += (_, _) => GuardTick();
            _guard.Start();
            if (_attention) DispatcherTimer.RunOnce(SetupAttention, TimeSpan.FromSeconds(2));   // WPF: Task.Delay(2000)
        }

        /// <summary>WPF VideoService.UpdateMasterVolume / UpdateVideoVolume: a playing clip follows
        /// Settings · Audio live. UI thread (teardown runs there too).</summary>
        internal void UpdateVolume()
        {
            var s = CoreSettings.Current;
            if (_player is { } p) p.Volume = MandatoryVideoScheduler.EffectiveVolume(s.MasterVolume, s.VideoVolume);
        }

        /// <summary>WPF VideoService.VideoScreens -> App.ResolveScreens (ccp-bugs #1154): the Video
        /// card's own pick (All, or a connected monitor), else the global "Show content on" pick -
        /// Default and an unplugged index both follow it, without rewriting the setting.</summary>
        internal static int VideoTarget(int video, int global, int screenCount) =>
            video == Services.UI.MonitorTarget.All || (video >= 0 && video < screenCount) ? video : global;

        private static List<global::Avalonia.Platform.Screen?> Targets(Window? host, bool fillAll)
        {
            var s = CoreSettings.Current;
            var screens = host is null ? Array.Empty<global::Avalonia.Platform.Screen>() : ScreenList.Enumerate(host);
            var targets = new List<global::Avalonia.Platform.Screen?>();
            if (screens.Count == 0) { targets.Add(null); return targets; }   // headless: one window where the platform puts it
            var primaryIdx = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
            var idx = PinkFilterOverlay.ResolveScreenIndices(VideoTarget(s.VideoTargetMonitor, s.GlobalTargetMonitor, screens.Count),
                s.DualMonitorEnabled, screens.Count, primaryIdx);
            var first = idx.Contains(primaryIdx) ? primaryIdx : idx[0];
            targets.Add(screens[first]);
            if (MandatoryVideoScheduler.ShouldFillSecondaryMonitors(idx.Length, fillAll))
                targets.AddRange(idx.Where(i => i != first).Select(i => screens[i]));
            return targets;
        }

        private Surface Build(global::Avalonia.Platform.Screen? screen, bool strict)
        {
            var fill = new Image { Stretch = Stretch.UniformToFill, IsVisible = false };
            RenderOptions.SetBitmapInterpolationMode(fill, BitmapInterpolationMode.HighQuality);
            var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), IsVisible = false };
            var video = new Image { Stretch = Stretch.Uniform };
            var layer = new Canvas { ClipToBounds = true };
            var (grace, countdown) = GraceCard();
            var w = new Window
            {
                Title = "CCP Video",
                WindowDecorations = WindowDecorations.None,
                Topmost = true,
                ShowInTaskbar = false,
                CanResize = false,
                Background = Brushes.Black,
                Content = new Panel { ClipToBounds = true, Children = { fill, scrim, video, layer, grace } },
            };
            Place(w, screen);
            w.KeyDown += (_, e) =>
            {
                var s = CoreSettings.Current;
                var live = s.PanicKeyEnabled && PanicListenerLive();
                if (MandatoryVideoScheduler.GraceKey(strict, e.Key.ToString(), s.PanicKeyEnabled, s.PanicKey, live) is { } fromPanic
                    && TryGracePause(fromPanic))
                {
                    e.Handled = true;
                    return;
                }
                switch (MandatoryVideoScheduler.KeyAction(strict, e.Key.ToString(), e.KeyModifiers.HasFlag(KeyModifiers.Alt), s.PanicKeyEnabled, s.PanicKey,
                    s.PanicKeyEnabled && PanicListenerLive()))
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
            return new Surface(w, fill, scrim, video, layer, grace, countdown);
        }

        /// <summary>WPF GracePauseOverlayWindow's card: 360x200, centred, #1A1A2E with a 2 px pink edge,
        /// the pause glyph, "Paused", a pink Resume pill (0.85 on hover, 0.7 pressed) and the countdown.</summary>
        private (Border, TextBlock) GraceCard()
        {
            var pink = new SolidColorBrush(Color.Parse("#FF69B4"));
            var countdown = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xA8, 0xC8)), HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            // A real Button like WPF's, so Tab + Enter/Space reach it (P17); the pill is a local template.
            var resume = new Button
            {
                Background = pink,
                Padding = new Thickness(26, 8),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = new TextBlock { Text = "▶  " + Loc.Get("btn_video_grace_resume"), FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
                Template = new global::Avalonia.Controls.Templates.FuncControlTemplate<Button>((b, _) => new Border
                {
                    CornerRadius = new CornerRadius(20),
                    [!Border.BackgroundProperty] = b[!Button.BackgroundProperty],
                    [!Border.PaddingProperty] = b[!Button.PaddingProperty],
                    Child = new global::Avalonia.Controls.Presenters.ContentPresenter
                    {
                        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                        [!global::Avalonia.Controls.Presenters.ContentPresenter.ContentProperty] = b[!Button.ContentProperty],
                    },
                }),
            };
            resume.PropertyChanged += (_, e) =>
            {
                if (e.Property == Button.IsPressedProperty || e.Property == InputElement.IsPointerOverProperty)
                    resume.Opacity = resume.IsPressed ? 0.7 : resume.IsPointerOver ? 0.85 : 1;
            };
            resume.Click += (_, e) => { e.Handled = true; ResumeFromGrace("resume button"); };
            var card = new Border
            {
                Width = 360,
                Height = 200,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.Parse("#1A1A2E")),
                BorderBrush = pink,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24, 18),
                BoxShadow = BoxShadows.Parse("0 0 24 0 #BF000000"),
                IsVisible = false,
                Child = new StackPanel
                {
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "⏸", FontSize = 40, Foreground = pink, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) },
                        new TextBlock { Text = Loc.Get("video_grace_paused_title"), FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) },
                        resume,
                        countdown,
                    },
                },
            };
            return (card, countdown);
        }

        // ---- grace pause (WPF TryGracePauseFromPanic / DoGracePause / ResumeFromGrace, #735) ----

        internal bool GracePaused => _gracePaused;

        /// <summary>The first Esc/panic press of a clip pauses it behind the card for up to 60 s; the
        /// panic key only when panic does not override everything (PanicOverridesAll off).</summary>
        internal bool TryGracePause(bool fromPanicKey)
        {
            if (!ConditioningControlPanel.Services.Safety.PanicPolicy.AllowGracePause(fromPanicKey,
                    ConditioningControlPanel.Services.Safety.PanicPolicy.OverrideEnabled(CoreSettings.Current))) return false;
            var now = Now;
            var since = _lastGraceUtc == default ? double.MaxValue : (now - _lastGraceUtc).TotalMilliseconds;
            var d = MandatoryVideoScheduler.EvaluateGrace(Scheduler.IsPlaying && _player != null, _closing, _gracePaused, _graceConsumed, since);
            if (d == GraceDecision.ConsumedDedup) return true;
            if (d != GraceDecision.Pause) return false;
            _gracePaused = true;
            _lastGraceUtc = _gracePausedAt = now;
            _player?.SetPause(true);
            _sinceShow.Stop();
            _sinceFrame.Stop();   // freezes the attention clock and the clip guards (WPF re-phases both)
            foreach (var x in _surfaces) { x.Layer.IsHitTestVisible = false; x.Grace.IsVisible = true; }
            SetCountdown(MandatoryVideoScheduler.GraceWindowSeconds);
            _graceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _graceTimer.Tick += (_, _) => GraceTick();
            _graceTimer.Start();
            Log.Information("VideoService: grace pause engaged ({Window}s window)", MandatoryVideoScheduler.GraceWindowSeconds);
            return true;
        }

        /// <summary>WPF StartGraceCountdown's 1 s tick: refresh "Resuming in Ns", resume at 0.</summary>
        internal void GraceTick()
        {
            if (!_gracePaused) return;
            var left = MandatoryVideoScheduler.GraceSecondsRemaining((Now - _gracePausedAt).TotalSeconds);
            if (left <= 0) ResumeFromGrace("auto-resume");
            else SetCountdown(left);
        }

        private void SetCountdown(int left)
        {
            foreach (var x in _surfaces) x.Countdown.Text = Loc.GetF("video_grace_auto_resume_in", left);
        }

        internal void ResumeFromGrace(string reason)
        {
            if (!_gracePaused) return;
            _gracePaused = false;
            _graceConsumed = true;   // spent whether it ended by hand or by timeout
            _graceTimer?.Stop();
            _graceTimer = null;
            foreach (var x in _surfaces) { x.Grace.IsVisible = false; x.Layer.IsHitTestVisible = true; }
            _sinceShow.Start();
            if (FirstFrameMs >= 0) _sinceFrame.Start();
            FrameTs = Stopwatch.GetTimestamp();
            _player?.SetPause(false);
            Log.Information("VideoService: grace pause released ({Reason}) after {Sec:F1}s", reason, (Now - _gracePausedAt).TotalSeconds);
        }

        /// <summary>WPF safety/fallback/max-length timers and the vout watchdog, as one tick: a clip
        /// that shows no frame, stalls, overruns or passes the user's max ends like a dismiss.</summary>
        internal void GuardTick()
        {
            if (_gracePaused || _surfaces.Count == 0 || !Scheduler.IsPlaying) return;
            var framed = FirstFrameMs >= 0;
            var why = MandatoryVideoScheduler.Guard(Elapsed, framed ? _sinceFrame.Elapsed.TotalSeconds : Elapsed, framed,
                (_player?.VideoTrackCount ?? -1) != 0, (_player?.Length ?? 0) / 1000.0,
                Stopwatch.GetElapsedTime(FrameTs).TotalSeconds, CoreSettings.Current.VideoMaxDurationSeconds);
            if (why == null) return;
            if (MandatoryVideoScheduler.GuardHeals(why, _healUsed))
            {
                // WPF DispatchVoutHeal(retry): the same clip once more, same strictness.
                Log.Warning("VideoService: clip guard fired ({Why}) after {Sec:F1}s - replaying the clip once", why, Elapsed);
                Scheduler.NoteReplay();
                Play(_path, _strict);
                _healUsed = true;
                return;
            }
            Log.Warning("VideoService: clip guard fired ({Why}) after {Sec:F1}s - ending the clip", why, Elapsed);
            Scheduler.End();
        }

        private static void Place(Window w, global::Avalonia.Platform.Screen? screen)
        {
            if (screen == null) return;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = screen.Bounds.Position;
            w.Width = screen.Bounds.Width / screen.Scaling;
            w.Height = screen.Bounds.Height / screen.Scaling;
        }

        internal const int FillWidth = 64;

        /// <summary>Box-average <paramref name="src"/> into the small <paramref name="dst"/> (4x4 samples
        /// per cell), then one 3x3 box pass; the bilinear upscale to full screen does the rest.</summary>
        internal static void Downscale(WriteableBitmap src, WriteableBitmap dst)
        {
            using var s = src.Lock();
            using var d = dst.Lock();
            int sw = s.Size.Width, sh = s.Size.Height, dw = d.Size.Width, dh = d.Size.Height;
            var tmp = new int[dw * dh * 3];
            for (var y = 0; y < dh; y++)
            for (var x = 0; x < dw; x++)
            {
                int b = 0, g = 0, r = 0;
                for (var j = 0; j < 4; j++)
                {
                    var row = s.Address + Math.Min(sh - 1, (y * 4 + j) * sh / (dh * 4)) * s.RowBytes;
                    for (var i = 0; i < 4; i++)
                    {
                        var p = row + Math.Min(sw - 1, (x * 4 + i) * sw / (dw * 4)) * 4;
                        b += Marshal.ReadByte(p); g += Marshal.ReadByte(p, 1); r += Marshal.ReadByte(p, 2);
                    }
                }
                var o = (y * dw + x) * 3;
                tmp[o] = b >> 4; tmp[o + 1] = g >> 4; tmp[o + 2] = r >> 4;
            }
            for (var y = 0; y < dh; y++)
            for (var x = 0; x < dw; x++)
            {
                int b = 0, g = 0, r = 0, n = 0;
                for (var yy = Math.Max(0, y - 1); yy <= Math.Min(dh - 1, y + 1); yy++)
                for (var xx = Math.Max(0, x - 1); xx <= Math.Min(dw - 1, x + 1); xx++)
                {
                    var o = (yy * dw + xx) * 3;
                    b += tmp[o]; g += tmp[o + 1]; r += tmp[o + 2]; n++;
                }
                Marshal.WriteInt32(d.Address + y * d.RowBytes + x * 4, unchecked((int)0xFF000000u) | (r / n) << 16 | (g / n) << 8 | (b / n));
            }
        }

        // ---- attention checks (WPF SetupAttention / SpawnTarget / ExpireDueSpawns) ----

        private readonly Random _random = new();

        private void SetupAttention()
        {
            if (!_attention || !Scheduler.IsPlaying || _surfaces.Count == 0) return;
            var s = CoreSettings.Current;
            var total = MandatoryVideoScheduler.AttentionTargetCount(s.AttentionDensity, s.RandomizeAttentionTargets, _random);
            _spawnTimes = MandatoryVideoScheduler.AttentionSpawnTimes(total, (_player?.Length ?? 0) / 1000.0, _random);
            _lastFrame = null;
            Log.Information("Attention: {Count} targets over {Duration}s", total, (_player?.Length ?? 0) / 1000);
            NextFrame();
        }

        /// <summary>Seconds on the spawn clock (WPF DateTime.Now - _startTime, the playback start).</summary>
        private double Elapsed => _sinceShow.Elapsed.TotalSeconds;

        private TimeSpan? _lastFrame;

        /// <summary>Spawn, expire and move on every rendered frame of the first window (WPF: a 20 ms
        /// spawn tick plus CompositionTarget.Rendering motion per target).</summary>
        private void NextFrame()
        {
            if (_surfaces.Count == 0) return;
            var w = _surfaces[0].Window;
            w.RequestAnimationFrame(now =>
            {
                if (!_attention || _surfaces.Count == 0 || _surfaces[0].Window != w) return;
                if (_gracePaused) { _lastFrame = null; NextFrame(); return; }   // targets hold still, expiry clock stopped
                Step(Elapsed, _lastFrame is { } l ? Math.Clamp((now - l).TotalSeconds, 0, 0.25) : 1 / 60.0);
                _lastFrame = now;
                NextFrame();
            });
        }

        /// <summary>One frame: spawn what is due, expire what is overdue, move the rest.</summary>
        internal void Step(double elapsed, double dt)
        {
            while (_spawnTimes.Count > 0 && elapsed >= _spawnTimes[0])
            {
                _spawnTimes.RemoveAt(0);
                Spawn(elapsed);
            }
            foreach (var t in _targets.ToList())
            {
                if (elapsed >= t.Due) { t.Remove(); _targets.Remove(t); continue; }
                t.Move(dt);
            }
        }

        /// <summary>WPF SpawnTarget: one target per screen from the enabled pool ("CLICK ME" when
        /// empty); catching any one of the batch scores once and clears the rest.</summary>
        internal void Spawn(double elapsed)
        {
            var s = CoreSettings.Current;
            var pool = s.AttentionPool.Where(p => p.Value).Select(p => p.Key).ToList();
            var text = MandatoryVideoScheduler.FormatTriggerText(pool.Count > 0 ? pool[_random.Next(pool.Count)] : "CLICK ME");
            Scheduler.NoteSpawn();
            var batch = new List<Target>();
            var caught = false;
            foreach (var x in _surfaces)
            {
                var t = new Target(x.Layer, text, Math.Max(40, s.AttentionSize), elapsed + s.AttentionLifespan, _random);
                t.Root.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    if (caught) return;
                    caught = true;
                    PlayPop();
                    Scheduler.NoteHit();
                    foreach (var o in batch) { _targets.Remove(o); if (o != t) o.Remove(); }
                    t.FadeOut();
                    Log.Information("ATTENTION: Hit {Hits}/{Spawned}", Scheduler.AttentionHits, Scheduler.AttentionSpawned);
                };
                batch.Add(t);
                _targets.Add(t);
            }
        }

        private static void PlayPop()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "bubbles", new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" }[Random.Shared.Next(3)]);
            CoreAudio.PlayOneShot(path, 0.6f * (CoreSettings.Current.MasterVolume / 100f), "target-pop");
        }

        /// <summary>WPF InWindowAttentionTarget + AttentionTargetVisual.Build: a gradient pill (or bare
        /// floating text) with black-outlined bold text, bouncing at 187.5 DIP/s inside 8% margins.</summary>
        internal sealed class Target
        {
            internal const double Speed = 187.5, Outline = 7.5;
            public readonly Border Root;
            public readonly double Due;
            private readonly Canvas _layer;
            private double _x, _y, _vx, _vy, _minX, _minY, _maxX, _maxY;
            private readonly double _w, _h;

            public Target(Canvas layer, string text, int size, double due, Random r)
            {
                _layer = layer;
                Due = due;
                var s = CoreSettings.Current;
                Color C(string? hex, Color fb) => Color.TryParse(hex, out var c) ? c : fb;
                var pink = Color.FromRgb(255, 20, 147);
                var floating = s.AttentionFloatingText;
                var border = !floating && s.AttentionShowBorder;
                var label = new OutlinedText(text, size, s.AttentionFont, new SolidColorBrush(C(s.AttentionTextColor, pink)));
                Root = new Border
                {
                    Background = floating ? Brushes.Transparent : new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(C(s.AttentionColor1, pink), 0), new GradientStop(C(s.AttentionColor2, Color.FromRgb(255, 105, 180)), 1) },
                    },
                    CornerRadius = new CornerRadius(floating ? 0 : 20),
                    BorderBrush = border ? new SolidColorBrush(C(s.AttentionBorderColor, pink)) : null,
                    BorderThickness = new Thickness(border ? 3 : 0),
                    BoxShadow = floating ? default : BoxShadows.Parse("5 5 15 0 #99000000"),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = label,
                };
                _w = Math.Max(label.Width + 60, 150);
                _h = Math.Max(label.Height + 40, 60);
                Root.Width = _w;
                Root.Height = _h;
                double aw = layer.Bounds.Width > 0 ? layer.Bounds.Width : _w * 3, ah = layer.Bounds.Height > 0 ? layer.Bounds.Height : _h * 3;
                _minX = Math.Min(150, aw * 0.08);
                _minY = Math.Min(100, ah * 0.08);
                _maxX = Math.Max(_minX + _w, aw - _minX);
                _maxY = Math.Max(_minY + _h, ah - _minY);
                _x = _minX + r.NextDouble() * Math.Max(0, _maxX - _w - _minX);
                _y = _minY + r.NextDouble() * Math.Max(0, _maxY - _h - _minY);
                var a = r.NextDouble() * Math.PI * 2;
                (_vx, _vy) = (Math.Cos(a) * Speed, Math.Sin(a) * Speed);
                Place();
                layer.Children.Add(Root);
            }

            public double X => _x;

            public void Move(double dt)
            {
                _x += _vx * dt;
                _y += _vy * dt;
                if (_x < _minX) { _x = _minX; _vx = Math.Abs(_vx); }
                if (_x + _w > _maxX) { _x = _maxX - _w; _vx = -Math.Abs(_vx); }
                if (_y < _minY) { _y = _minY; _vy = Math.Abs(_vy); }
                if (_y + _h > _maxY) { _y = _maxY - _h; _vy = -Math.Abs(_vy); }
                Place();
            }

            private void Place() { Canvas.SetLeft(Root, _x); Canvas.SetTop(Root, _y); }

            public void Remove() => _layer.Children.Remove(Root);

            /// <summary>WPF: the caught target fades out over 300 ms.</summary>
            public void FadeOut()
            {
                Root.IsHitTestVisible = false;
                Root.Transitions = new global::Avalonia.Animation.Transitions
                {
                    new global::Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(300) },
                };
                Root.Opacity = 0;
                DispatcherTimer.RunOnce(Remove, TimeSpan.FromMilliseconds(320));
            }
        }

        /// <summary>WPF AttentionTargetVisual text: bold, centred lines at 0.95 line height, a 7.5 DIP
        /// black round-joined stroke behind the fill.</summary>
        private sealed class OutlinedText : Control
        {
            private readonly Geometry? _geo;
            private readonly IBrush _fill;

            public OutlinedText(string text, int size, string? font, IBrush fill)
            {
                _fill = fill;
                var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily($"{(string.IsNullOrWhiteSpace(font) ? "Segoe UI" : font)}, Segoe UI, Arial"), FontStyle.Normal, FontWeight.Bold), size, fill)
                { TextAlignment = TextAlignment.Center, LineHeight = size * 0.95 };
                _geo = ft.BuildGeometry(default);
                var b = _geo?.Bounds ?? default;
                if (_geo != null) _geo.Transform = new TranslateTransform(-b.X + Target.Outline, -b.Y + Target.Outline);
                Width = b.Width + Target.Outline * 2;
                Height = b.Height + Target.Outline * 2;
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
                IsHitTestVisible = false;
            }

            public override void Render(DrawingContext dc)
            {
                if (_geo == null) return;
                dc.DrawGeometry(null, new Pen(Brushes.Black, Target.Outline, lineJoin: PenLineJoin.Round), _geo);
                dc.DrawGeometry(_fill, null, _geo);
            }
        }

        // ---- verdict message and teardown ----

        /// <summary>WPF ShowMessage: the verdict in 64 px bold magenta on black, one maximized
        /// non-activating window per screen, for <paramref name="ms"/>; mod lines when a mod sets them.</summary>
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => Dispatcher.UIThread.Invoke(() =>
        {
            var m = App.Mods;
            var text = verdict switch
            {
                AttentionVerdict.Mercy => m?.GetAttentionCheckMercyMessage() ?? "BAMBI GETS MERCY",
                AttentionVerdict.Troll => m?.GetAttentionCheckTrollMessage() ?? "NICE TRY!\nWATCH AGAIN \U0001F61C",
                _ => m?.GetAttentionCheckFailMessage() ?? "MISSED IT!\nTRY AGAIN",
            };
            var host = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            foreach (var screen in Targets(host, fillAll: true))
            {
                var w = new Window
                {
                    WindowDecorations = WindowDecorations.None,
                    Background = Brushes.Black,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Content = new TextBlock
                    {
                        Text = text,
                        Foreground = Brushes.Magenta,
                        FontSize = 64,
                        FontWeight = FontWeight.Bold,
                        FontFamily = new FontFamily("Impact, Arial Black, Segoe UI"),
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    },
                };
                Place(w, screen);
                w.Show();
                w.WindowState = WindowState.Maximized;
                _messages.Add(w);
            }
            DispatcherTimer.RunOnce(() =>
            {
                foreach (var w in _messages) w.Close();
                _messages.Clear();
                then();
            }, TimeSpan.FromMilliseconds(ms));
        });

        /// <summary>WPF's guidance dialog when the library is empty (once per launch).</summary>
        private void ShowNoVideos() => Dispatcher.UIThread.Post(() =>
        {
            var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner == null) return;
            _ = Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("video_no_videos_title"), NoVideosMessage(Scheduler));
        });

        /// <summary>WPF TriggerVideo's guidance text: #1352 names the length filter when it emptied a
        /// library that has files, else the add-files hint.</summary>
        internal static string NoVideosMessage(MandatoryVideoScheduler s)
        {
            var c = CoreSettings.Current;
            if (NoVideosReason.LengthFilterEmptied(s.LastFunnelEnabled, s.LastFunnelDuration))
                return Loc.GetF("video_length_filter_emptied", s.LastFunnelEnabled,
                    NoVideosReason.FormatRange(c.VideoMinDurationSeconds, c.VideoMaxDurationSeconds));
            return Loc.GetF("video_no_videos_found", Path.Combine(CorePaths.EffectiveAssets, "videos")) + "\n\n" + Loc.Get("video_add_files_hint");
        }

        public double CloseAll() => Dispatcher.UIThread.Invoke(() =>
        {
            _closing = true;
            try
            {
                _attention = _gracePaused = false;
                _graceTimer?.Stop();
                _guard?.Stop();
                _graceTimer = _guard = null;
                _spawnTimes.Clear();
                _targets.Clear();
                // Stop joins the decoder thread, so after it no callback touches the frame buffer.
                if (_player != null)
                {
                    try { _player.Stop(); } catch { }
                    try { _player.Dispose(); } catch { }
                }
                _player = null;
                try { _media?.Dispose(); } catch { }
                _media = null;
                foreach (var x in _surfaces) { x.Video.Source = x.Fill.Source = null; x.Window.Close(); }
                _surfaces.Clear();
                _sink?.Free();
                _sink = null;
                _fill?.Dispose();
                _fill = null;
                if (_didDuck) { _didDuck = false; CoreAudio.Unduck(); }
                BouncingTextOverlay.PauseForVideo(false);
                if (FillCount > 0) Log.Information("VideoService: blur fill {Avg:F3} ms/frame over {N} frames", FillMs / FillCount, FillCount);
            }
            finally { _closing = false; }
            return Interlocked.Exchange(ref _watchedMs, 0) / 1000.0;
        });
    }
}
