using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// One screen's worth of flat colour, drawn over everything and transparent to the mouse.
    /// The Avalonia twin of the window <c>OverlayService.CreatePinkFilterForScreen</c> builds in
    /// the WPF head (ConditioningControlPanel/Services/Notifications/OverlayService.cs:1363).
    ///
    /// <para><b>Code-only, no .axaml, on purpose.</b> The whole window is a <c>Border</c> whose
    /// brush colour is the only thing that ever changes, so a XAML file would buy one more chance
    /// to hit the <c>x:Name</c>-is-null trap and nothing else.</para>
    ///
    /// <para><b>The property mapping</b>, each one replacing a Win32 style the original ORs in
    /// inside <c>SourceInitialized</c>:</para>
    /// <list type="bullet">
    ///   <item><c>WindowStyle=None</c> + <c>AllowsTransparency</c> -> <c>WindowDecorations.None</c>
    ///         plus <c>TransparencyLevelHint = Transparent</c>. The hint is a REQUEST: X11 with no
    ///         compositor answers <c>None</c>, and the host refuses to show the window in that
    ///         case rather than painting a solid screen-sized block.</item>
    ///   <item><c>WS_EX_TOOLWINDOW</c> -> <c>ShowInTaskbar = false</c>.</item>
    ///   <item><c>WS_EX_NOACTIVATE</c> -> <c>ShowActivated = false</c>.</item>
    ///   <item><c>WS_EX_TOPMOST</c> -> <c>Topmost</c>, which Avalonia maps to
    ///         <c>_NET_WM_STATE_ABOVE</c> (see X11Overlay's header for why that is not wrapped).</item>
    ///   <item><c>WS_EX_TRANSPARENT</c> -> <c>X11Overlay.SetClickThrough</c>, called by the host
    ///         after <c>Show()</c> because the window has no XID before then.</item>
    ///   <item><c>SetWindowPos</c> with physical pixels -> <see cref="PlaceOn"/>.
    ///         <c>Window.Position</c> is ALREADY physical, so the DPI correction the WPF original
    ///         needs has nothing to correct here; only Width/Height are DIPs and get divided.</item>
    /// </list>
    ///
    /// <para>Opacity is linear, <c>percent / 100</c> straight into the alpha byte - the WPF path
    /// says "Linear opacity (no exponential curve)" and this keeps it.</para>
    /// </summary>
    internal sealed class TintOverlayWindow : Window
    {
        private readonly SolidColorBrush _fill = new(Colors.Transparent);

        public TintOverlayWindow()
        {
            SystemDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Content = new Border { Background = _fill };

            // The parameterless ctor is also what --render-all constructs, so it paints the tint
            // the user's settings actually describe rather than an invented one. At the default
            // 10% that PNG is a faint wash, which is the honest picture of the feature.
            Width = 640;
            Height = 400;
            var (r, g, b) = PinkFilterOverlay.EffectiveColor();
            SetTint(r, g, b, CoreSettings.Current.PinkFilterOpacity / 100.0);
        }

        /// <summary>Repaints in place, the way <c>UpdatePinkFilterOpacity</c> mutates the existing
        /// brush rather than rebuilding the window - which is what keeps a slider drag from
        /// creating and destroying a full-screen window on every tick.</summary>
        public void SetTint(byte r, byte g, byte b, double opacity)
            => _fill.Color = Color.FromArgb((byte)(Math.Clamp(opacity, 0, 1) * 255), r, g, b);

        /// <summary>Fills exactly one monitor.</summary>
        public void PlaceOn(Screen screen)
        {
            var b = screen.Bounds;
            var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
            Position = new PixelPoint(b.X, b.Y);
            Width = b.Width / scale;
            Height = b.Height / scale;
        }
    }
}
