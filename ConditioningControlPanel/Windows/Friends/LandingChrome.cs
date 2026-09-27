using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.FriendsWindows;

/// <summary>The few pieces the three landing windows share: colours off the mockup, the
/// avatar disc, the owner's rectangle in DIPs, and the base window every one of them is.</summary>
internal static class LandingChrome
{
    public static readonly Color Ground = Color.FromRgb(0x22, 0x16, 0x41);
    public static readonly Color Line2 = Color.FromRgb(0x4D, 0x3A, 0x78);
    public static readonly Color Text = Color.FromRgb(0xF1, 0xEA, 0xFF);
    public static readonly Color Muted = Color.FromRgb(0xA3, 0x95, 0xC4);
    public static readonly Color Lilac = Color.FromRgb(0xB9, 0x9C, 0xFF);
    public static readonly Color Pink = Color.FromRgb(0xFF, 0x5F, 0xB4);
    public static readonly Color Mint = Color.FromRgb(0x5F, 0xFF, 0xD0);
    public static readonly Color Gold = Color.FromRgb(0xFF, 0xCF, 0x6B);

    public static readonly FontFamily Display = new(new Uri("pack://application:,,,/"), "./Fonts/#Fredoka, Segoe UI");

    private const string ProxyBase = "https://codebambi-proxy.vercel.app";

    public static SolidColorBrush Brush(Color c, byte alpha = 255)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    /// <summary>A round avatar: initials on lilac, the server picture over it when it loads.</summary>
    public static FrameworkElement Avatar(string name, string? avatarPath, double size)
    {
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new System.Windows.Shapes.Ellipse { Width = size, Height = size, Fill = Brush(Lilac) });
        var initial = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();
        grid.Children.Add(new TextBlock
        {
            Text = initial,
            FontFamily = Display,
            FontWeight = FontWeights.SemiBold,
            FontSize = size * 0.42,
            Foreground = Brush(Color.FromRgb(0x1C, 0x12, 0x33)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (!string.IsNullOrEmpty(avatarPath) && avatarPath.StartsWith("/", StringComparison.Ordinal))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(ProxyBase + avatarPath, UriKind.Absolute);
                bmp.DecodePixelWidth = (int)Math.Ceiling(size * 2);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                var face = new System.Windows.Shapes.Ellipse
                {
                    Width = size, Height = size,
                    Fill = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill },
                    Opacity = 0,
                };
                bmp.DownloadCompleted += (_, _) => face.Opacity = 1;
                if (!bmp.IsDownloading) face.Opacity = 1;
                grid.Children.Add(face);
            }
            catch (Exception ex) { App.Logger?.Debug("[Friends] avatar load: {E}", ex.Message); }
        }
        return grid;
    }

    /// <summary>A resource picture under Resources/, or null when it is not there.</summary>
    public static ImageSource? Picture(string resourcePath)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri("pack://application:,,,/Resources/" + resourcePath, UriKind.Absolute);
            bmp.DecodePixelWidth = 480;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[Friends] picture {P}: {E}", resourcePath, ex.Message);
            return null;
        }
    }

    /// <summary>The owner's client rectangle in screen DIPs. Works for a maximised window too,
    /// where Left/Top lie.</summary>
    public static Rect OwnerRect(Window owner)
    {
        try
        {
            var src = PresentationSource.FromVisual(owner);
            if (src?.CompositionTarget != null && owner.ActualWidth > 0)
            {
                var tl = owner.PointToScreen(new Point(0, 0));
                var br = owner.PointToScreen(new Point(owner.ActualWidth, owner.ActualHeight));
                var m = src.CompositionTarget.TransformFromDevice;
                return new Rect(m.Transform(tl), m.Transform(br));
            }
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] owner rect: {E}", ex.Message); }
        return new Rect(owner.Left, owner.Top, Math.Max(owner.Width, 400), Math.Max(owner.Height, 300));
    }

    /// <summary>A borderless, transparent, owned, topmost window that never takes focus.</summary>
    public static void Dress(Window w, Window? owner)
    {
        w.WindowStyle = WindowStyle.None;
        w.AllowsTransparency = true;
        w.Background = Brushes.Transparent;
        w.ShowInTaskbar = false;
        w.ShowActivated = false;
        w.Topmost = true;
        w.ResizeMode = ResizeMode.NoResize;
        w.SizeToContent = SizeToContent.WidthAndHeight;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        if (owner != null && owner.IsLoaded && new WindowInteropHelper(owner).Handle != IntPtr.Zero)
            w.Owner = owner;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    /// <summary>Painted pixels on a layered window take the mouse even with IsHitTestVisible off,
    /// so a window that only decorates asks Win32 to pass input through and never activate.</summary>
    public static void ClickThrough(Window w)
    {
        w.SourceInitialized += (_, _) =>
        {
            try
            {
                const int GWL_EXSTYLE = -20;
                const long WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
                var h = new WindowInteropHelper(w).Handle;
                var ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
                SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            }
            catch (Exception ex) { App.Logger?.Debug("[Friends] click-through: {E}", ex.Message); }
        };
    }
}
