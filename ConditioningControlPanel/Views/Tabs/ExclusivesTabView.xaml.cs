using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// The Exclusives "Velvet Vault" tab shell. All roster/card logic lives in
    /// MainWindow.Exclusives.cs (house pattern: views delegate to MainWindow);
    /// this code-behind only owns the backdrop and geometry chores.
    /// </summary>
    public partial class ExclusivesTabView : UserControl
    {
        // The room: a neutral black/grey/neon-blue cyber-vault plate, so every
        // mod's FxTheme accent washes over it without clashing. A plain
        // gradient (the XAML background) is the fallback if the file is gone.
        private static readonly string BackdropDiskPath =
            Path.Combine(AppContext.BaseDirectory, "assets", "exclusives", "vault_backdrop.png");

        public ExclusivesTabView()
        {
            InitializeComponent();
            LoadBackdrop();

            // WPF's ClipToBounds is rectangular; rounded corners need explicit
            // clip geometry that tracks the element's size.
            RoundClipOnResize(SpotArtHost, 14);
        }

        private void LoadBackdrop()
        {
            try
            {
                if (!File.Exists(BackdropDiskPath)) return;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(BackdropDiskPath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                // The plate is 1376x768 painted art behind a heavy scrim - a
                // half-size decode reads identically and saves ~4 MB.
                bmp.DecodePixelWidth = 900;
                bmp.EndInit();
                bmp.Freeze();
                VaultBackdrop.Source = bmp;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Exclusives backdrop load failed: {E}", ex.Message);
            }
        }

        /// <summary>Keeps a rounded-rect Clip on <paramref name="host"/> matched to its size.</summary>
        internal static void RoundClipOnResize(FrameworkElement host, double radius)
        {
            host.SizeChanged += (_, e) =>
            {
                try
                {
                    host.Clip = new RectangleGeometry(
                        new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), radius, radius);
                }
                catch { }
            };
        }

        private void Spotlight_Click(object sender, RoutedEventArgs e) => OpenSpotlight();

        private void Spotlight_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenSpotlight();

        private void OpenSpotlight()
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OpenExclusiveSpotlight();
        }
    }
}
