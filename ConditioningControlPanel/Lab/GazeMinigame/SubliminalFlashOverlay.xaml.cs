using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ConditioningControlPanel.Lab.GazeMinigame
{
    /// <summary>
    /// Transparent topmost window used to draw the WRONG / GOOD GIRL flash
    /// over the gameplay screen. Lives in its own window (not a sibling of
    /// the VideoView) because LibVLC's VideoView is a WindowsFormsHost native
    /// HWND that always airspace-violates and paints over WPF siblings — so a
    /// regular WPF TextBlock above the video can't be made visible. A
    /// separate transparent window above the gameplay window draws cleanly
    /// over the native video surface.
    /// </summary>
    public partial class SubliminalFlashOverlay : System.Windows.Window
    {
        public SubliminalFlashOverlay()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Show the overlay over the owner's bounds, animate text in/out,
        /// then close. Returns when the animation has finished.
        /// </summary>
        public static async Task FlashAsync(System.Windows.Window owner, string text, Color color)
        {
            var overlay = new SubliminalFlashOverlay { Owner = owner };
            overlay.Configure(text, color);

            // Pin to the owner's screen rect. Use ActualWidth/Height because
            // owner may be in a fullscreen state where Width/Height are
            // double.NaN. Fall back to RestoreBounds for safety.
            var w = owner.ActualWidth > 0 ? owner.ActualWidth : owner.RestoreBounds.Width;
            var h = owner.ActualHeight > 0 ? owner.ActualHeight : owner.RestoreBounds.Height;
            overlay.Left = owner.Left;
            overlay.Top = owner.Top;
            overlay.Width = w;
            overlay.Height = h;

            overlay.Show();

            try
            {
                var fadeIn  = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(50));
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
                overlay.TxtSubliminal.BeginAnimation(OpacityProperty, fadeIn);
                await Task.Delay(300);   // fade-in (50) + hold (250)
                overlay.TxtSubliminal.BeginAnimation(OpacityProperty, fadeOut);
                await Task.Delay(120);
            }
            finally
            {
                overlay.Close();
            }
        }

        private void Configure(string text, Color color)
        {
            TxtSubliminal.Text = text;
            TxtSubliminal.Foreground = new SolidColorBrush(color);
            SubliminalShadow.Color = color;
        }
    }
}
