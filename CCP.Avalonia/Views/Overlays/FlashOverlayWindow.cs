using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// One flash image on the desktop: the Avalonia twin of WPF's per-flash <c>FlashWindow</c>
    /// (ConditioningControlPanel/Services/Flash/FlashService.cs, SpawnFlashWindow). Exactly the
    /// image's size, fades in to the Opacity slider, holds for its lifetime, fades out, closes.
    /// <see cref="FlashOverlay"/> places it and makes it an override-redirect click-through window.
    ///
    /// <para><b>No geometry in the constructor, on purpose.</b> Width/Height/Position set before
    /// <c>X11Overlay.SetOverrideRedirect</c> are replayed by the WM later and can land after the
    /// real size (see that method's notes); the host sets geometry through it.</para>
    ///
    /// <para><b>The fade is the compositor's, not ours</b> (<see cref="X11Overlay.SetOpacity"/>):
    /// animating the Image's Opacity re-renders the window every frame, and on the default GLX
    /// renderer under XWayland that stalled the UI dispatcher ~1.7 s per burst (staggers and
    /// timers late). With the property the app renders each flash exactly once.</para>
    /// </summary>
    internal sealed partial class FlashOverlayWindow : Window
    {
        private readonly Image _image;

        /// <summary>--render-all only: the flash card art as a sample picture.</summary>
        internal FlashOverlayWindow()
            : this(new Bitmap(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png"))))
        {
            Width = 400;
            Height = 300;
        }

        public FlashOverlayWindow(Bitmap picture)
        {
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            _image = new Image { Source = picture, Stretch = Stretch.Uniform };
            Content = _image;
            Closed += (_, _) => { _closed = true; picture.Dispose(); };
        }

        /// <summary>An animated flash (GIF / animated WebP): WPF's heartbeat frame-stepper, one
        /// frame every <paramref name="frameDelay"/> (already scaled by the GIF speed setting),
        /// looping for the flash's whole life. The window owns the frames and frees them on close.</summary>
        public FlashOverlayWindow(System.Collections.Generic.List<Bitmap> frames, TimeSpan frameDelay)
            : this(frames[0])
        {
            _frames = frames;
            _frameTimer = new DispatcherTimer { Interval = frameDelay };
            _frameTimer.Tick += (_, _) => StepFrame();
            Opened += (_, _) => _frameTimer?.Start();
            Closed += (_, _) =>
            {
                _frameTimer?.Stop();
                _frameTimer = null;
                for (var i = 1; i < frames.Count; i++) frames[i].Dispose();   // [0] goes with the base close
            };
        }

        private readonly System.Collections.Generic.List<Bitmap>? _frames;
        private DispatcherTimer? _frameTimer;
        internal int FrameIndex { get; private set; }

        /// <summary>One heartbeat step: next frame, wrapping to the first.</summary>
        internal void StepFrame()
        {
            if (_frames == null || _frames.Count < 2) return;
            FrameIndex = (FrameIndex + 1) % _frames.Count;
            _image.Source = _frames[FrameIndex];
        }

        /// <summary>
        /// Fade in to <paramref name="alpha"/> over <paramref name="fade"/>, hold until
        /// <paramref name="lifetime"/> after spawn, fade out over <paramref name="fade"/>, close.
        /// Same envelope as WPF's heartbeat: ExpiresAt = spawn + lifetime, ramps inside it.
        /// </summary>
        public void Run(double alpha, TimeSpan fade, TimeSpan lifetime)
        {
            _alpha = alpha;
            Fade(0, alpha, fade);
            DispatcherTimer.RunOnce(() => { if (!_popped) Fade(alpha, 0, fade, Close); }, lifetime);
        }

        private double _alpha;
        private bool _closed;
        private bool _popped;

        /// <summary>Raised once when a clickable flash is clicked (WPF FlashService.OnFlashClicked).</summary>
        internal event Action? Popped;

        /// <summary>WPF FlashClickable: the picture takes the click and pops; off = click-through.</summary>
        internal void MakeClickable()
        {
            IsHitTestVisible = true;
            _image.IsHitTestVisible = true;
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));   // a hit surface over transparent letterbox
            PointerPressed += (_, e) => { e.Handled = true; Pop(); };
        }

        /// <summary>Cut this flash's own lifetime short: a quick fade, then close. Other flashes live on.</summary>
        internal void Pop()
        {
            if (_popped) return;
            _popped = true;
            Popped?.Invoke();
            Fade(_alpha, 0, TimeSpan.FromMilliseconds(180), Close);
        }

        /// <summary>WPF's heartbeat ramp - linear, alpha written in 1/32 steps (FADE_ALPHA_EPSILON) -
        /// applied by the compositor through <see cref="X11Overlay.SetOpacity"/>.</summary>
        private void Fade(double from, double to, TimeSpan fade, Action? done = null)
        {
            // WPF moves alpha by dt/fadeSeconds per frame, so a ramp takes |to-from| x fade:
            // at Opacity 50% the fade is half as long, not a slower full-length one.
            var span = fade * Math.Abs(to - from);
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(to - from) * 32));
            for (var i = 1; i <= steps; i++)
            {
                var v = from + (to - from) * i / steps;
                var last = i == steps;
                DispatcherTimer.RunOnce(() => { if (_closed) return; X11Overlay.SetOpacity(this, v); if (last) done?.Invoke(); }, span * i / steps + TimeSpan.FromMilliseconds(1));
            }
        }
    }
}
