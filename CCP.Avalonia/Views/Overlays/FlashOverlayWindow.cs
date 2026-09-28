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
    internal sealed class FlashOverlayWindow : Window
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
            Closed += (_, _) => picture.Dispose();
        }

        /// <summary>
        /// Fade in to <paramref name="alpha"/> over <paramref name="fade"/>, hold until
        /// <paramref name="lifetime"/> after spawn, fade out over <paramref name="fade"/>, close.
        /// Same envelope as WPF's heartbeat: ExpiresAt = spawn + lifetime, ramps inside it.
        /// </summary>
        public void Run(double alpha, TimeSpan fade, TimeSpan lifetime)
        {
            Fade(0, alpha, fade);
            DispatcherTimer.RunOnce(() => Fade(alpha, 0, fade, Close), lifetime);
        }

        /// <summary>WPF's heartbeat ramp - linear, alpha written in 1/32 steps (FADE_ALPHA_EPSILON) -
        /// applied by the compositor through <see cref="X11Overlay.SetOpacity"/>.</summary>
        private void Fade(double from, double to, TimeSpan fade, Action? done = null)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(to - from) * 32));
            for (var i = 1; i <= steps; i++)
            {
                var v = from + (to - from) * i / steps;
                var last = i == steps;
                DispatcherTimer.RunOnce(() => { X11Overlay.SetOpacity(this, v); if (last) done?.Invoke(); }, fade * i / steps + TimeSpan.FromMilliseconds(1));
            }
        }
    }
}
