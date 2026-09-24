using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;

namespace ConditioningControlPanel.Controls;

/// <summary>Native image: routed clicks and the parent pass-flip transforms stay in WPF.</summary>
public sealed class AnimatedLogoImage : Image
{
    internal const string ArtworkUri = "pack://application:,,,/Resources/branding/ccp-logo.jpg";
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
    private readonly Stopwatch _clock = new();
    private DashboardLogoRenderer? _renderer;
    private SKSurface? _surface;
    private WriteableBitmap? _frame;
    private double _last, _phase, _energy;
    private bool _ambientAllowed, _failed;
    public bool IsAnimatedArtwork { get; private set; }
    internal bool IsAnimating => _timer.IsEnabled;

    public AnimatedLogoImage()
    {
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => { Stop(); Release(); };
        IsVisibleChanged += (_, _) => Refresh();
    }

    public bool AmbientAllowed
    {
        get => _ambientAllowed;
        set { if (_ambientAllowed == value) return; _ambientAllowed = value; Refresh(); }
    }

    internal static bool IsBundledNeutral(ImageSource? source) =>
        source is BitmapImage bitmap && string.Equals(bitmap.UriSource?.OriginalString,
            "pack://application:,,,/Resources/logo2.png", StringComparison.OrdinalIgnoreCase);

    public void SetArtwork(ImageSource? original)
    {
        Stop(); Release(); _failed = false; _phase = 0;
        Source = original;
        IsAnimatedArtwork = false;
        if (IsBundledNeutral(original))
        {
            try
            {
                var artwork = new BitmapImage(new Uri(ArtworkUri));
                artwork.Freeze(); Source = artwork; IsAnimatedArtwork = true;
            }
            catch (Exception ex) { Diag.Swallowed(ex, "logo artwork fallback"); }
        }
        Refresh();
    }

    private void Refresh()
    {
        if (!IsLoaded || !IsVisible || !AmbientAllowed || !IsAnimatedArtwork || _failed) { Stop(); return; }
        if (_timer.IsEnabled) return;
        _last = 0; _clock.Restart(); _timer.Start();
    }

    private void Stop()
    {
        _timer.Stop(); _clock.Reset(); _energy = 0; _frame = null; InvalidateVisual();
    }

    private void Release()
    {
        _surface?.Dispose(); _surface = null;
        _renderer?.Dispose(); _renderer = null;
    }

    private void Tick()
    {
        try
        {
            if (_renderer == null)
            {
                using var stream = Application.GetResourceStream(new Uri(ArtworkUri)).Stream;
                _renderer = new DashboardLogoRenderer(stream);
            }
            int size = Math.Clamp((int)Math.Ceiling(Math.Min(ActualWidth, ActualHeight)
                * VisualTreeHelper.GetDpi(this).DpiScaleX), 128, 512);
            if (_frame == null || _frame.PixelWidth != size)
            {
                _surface?.Dispose();
                _surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
                _frame = new WriteableBitmap(size, size, 96, 96, PixelFormats.Pbgra32, null);
            }
            double now = _clock.Elapsed.TotalSeconds, dt = Math.Clamp(now - _last, 0, .1);
            _last = now;
            double target = IsMouseOver ? 1 : 0;
            _energy += (target - _energy) * (1 - Math.Exp(-dt / (target > _energy ? .18 : .48)));
            _phase = (_phase + dt * Math.Tau / 12 * (1 + 2 * _energy)) % Math.Tau;
            _renderer.Draw(_surface!.Canvas, size, size, _phase, _energy);
            _surface.Canvas.Flush();
            using var pixels = _surface.PeekPixels();
            _frame.WritePixels(new Int32Rect(0, 0, size, size), pixels.GetPixels(), pixels.RowBytes * size, pixels.RowBytes);
            InvalidateVisual();
        }
        catch (Exception ex) { _failed = true; Stop(); Release(); Diag.Swallowed(ex, "logo animation fallback"); }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!IsAnimatedArtwork) { base.OnRender(dc); return; }
        var rect = new Rect(RenderSize);
        dc.PushClip(new RectangleGeometry(rect, rect.Width * 83 / 1024, rect.Height * 83 / 1024));
        if (_frame != null) dc.DrawImage(_frame, rect);
        else base.OnRender(dc);
        dc.Pop();
    }
}
