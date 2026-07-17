using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Models;
using XamlAnimatedGif;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Displays standalone, always-on corner-GIF overlays that live outside any session.
    /// Reads <see cref="AppSettings.CornerGifOverlays"/> and shows one transparent, click-through,
    /// topmost window per enabled slot. This mirrors SessionEngine's session-scoped corner GIF but
    /// is app-wide and can drive several corners at once (the Spiral card exposes two).
    /// </summary>
    public class CornerGifService
    {
        private readonly List<Window> _windows = new();

        /// <summary>
        /// Tears down every overlay and re-shows the ones currently enabled in settings.
        /// Safe to call from any thread; marshals to the UI thread. Call after any config change,
        /// and once on startup to restore persisted overlays.
        /// </summary>
        public void RefreshOverlays()
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null) return;
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(RefreshOverlays));
                return;
            }

            StopAll();

            var overlays = App.Settings?.Current?.CornerGifOverlays;
            if (overlays == null) return;

            foreach (var o in overlays)
            {
                if (o != null && o.Enabled)
                {
                    try { ShowOne(o); }
                    catch (Exception ex) { App.Logger?.Error(ex, "CornerGifService: ShowOne failed"); }
                }
            }
        }

        /// <summary>Closes every active corner-GIF overlay window.</summary>
        public void StopAll()
        {
            var disp = Application.Current?.Dispatcher;
            if (disp != null && !disp.CheckAccess())
            {
                disp.Invoke(StopAll);
                return;
            }

            foreach (var w in _windows)
            {
                try { w.Close(); }
                catch { /* already gone */ }
            }
            _windows.Clear();
        }

        private void ShowOne(CornerGifOverlaySetting setting)
        {
            Uri? gifUri = null;
            System.Drawing.Image? img = null;

            var gifPath = setting.GifPath;
            if (!string.IsNullOrEmpty(gifPath) && System.IO.File.Exists(gifPath))
            {
                try
                {
                    gifUri = new Uri(gifPath);
                    img = System.Drawing.Image.FromFile(gifPath);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("CornerGifService: failed to load GIF from file: {Error}", ex.Message);
                    gifUri = null;
                    img = null;
                }
            }

            // Fallback to the built-in spiral so an enabled-but-unpicked slot still shows something.
            if (img == null)
            {
                try
                {
                    gifUri = new Uri(ModResourceResolver.ResolveUri("spirals/spiral.gif"), UriKind.Absolute);
                    var resourceInfo = Application.GetResourceStream(gifUri);
                    if (resourceInfo?.Stream != null)
                    {
                        using (resourceInfo.Stream)
                        {
                            img = System.Drawing.Image.FromStream(resourceInfo.Stream);
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("CornerGifService: failed to load default spiral resource: {Error}", ex.Message);
                }
            }

            if (img == null || gifUri == null)
            {
                App.Logger?.Warning("CornerGifService: could not load any corner GIF image - skipping");
                return;
            }

            double gifWidth = img.Width;
            double gifHeight = img.Height;
            img.Dispose();

            // Scale to the user's longest-edge size (default 300).
            var targetSize = setting.Size > 0 ? setting.Size : 300;
            double scale = targetSize / Math.Max(gifWidth, gifHeight);
            double windowWidth = gifWidth * scale;
            double windowHeight = gifHeight * scale;

            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            if (screen == null) return;

            double dpiScale;
            using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
            {
                dpiScale = g.DpiX / 96.0;
            }

            double screenWidth = screen.Bounds.Width / dpiScale;
            double screenHeight = screen.Bounds.Height / dpiScale;

            double left = 0, top = 0;
            switch (setting.Position)
            {
                case CornerPosition.TopLeft:
                    left = 0; top = 0;
                    break;
                case CornerPosition.TopRight:
                    left = screenWidth - windowWidth; top = 0;
                    break;
                case CornerPosition.BottomLeft:
                    left = 0; top = screenHeight - windowHeight;
                    break;
                case CornerPosition.BottomRight:
                    left = screenWidth - windowWidth; top = screenHeight - windowHeight;
                    break;
            }

            var opacity = Math.Clamp(setting.Opacity, 1, 100) / 100.0;

            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Width = windowWidth,
                Height = windowHeight,
                Left = left,
                Top = top,
                Opacity = opacity,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            var imageElement = new Image
            {
                Stretch = System.Windows.Media.Stretch.Uniform
            };

            AnimationBehavior.SetSourceUri(imageElement, gifUri);
            AnimationBehavior.SetRepeatBehavior(imageElement, System.Windows.Media.Animation.RepeatBehavior.Forever);
            AnimationBehavior.AddErrorHandler(imageElement, (s, e) =>
            {
                App.Logger?.Warning("CornerGifService: GIF animation error ({Kind}): {Error}",
                    e.Kind, e.Exception?.Message);
            });

            window.Content = imageElement;
            window.SourceInitialized += (s, e) => MakeWindowClickThrough(window);
            window.Show();
            _windows.Add(window);

            App.Logger?.Information("CornerGifService: overlay shown at {Position} ({Path}, {W}x{H}px, {Opacity}%)",
                setting.Position, gifUri, (int)windowWidth, (int)windowHeight, setting.Opacity);
        }

        private static void MakeWindowClickThrough(Window window)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    }
}
