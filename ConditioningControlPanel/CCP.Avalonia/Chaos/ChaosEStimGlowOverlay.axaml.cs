using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Shapes;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosEStimGlow: small violet halo following the cursor while E-Stim is armed.
/// </summary>
public partial class ChaosEStimGlowOverlay : Window
{
    private readonly ILogger<ChaosEStimGlowOverlay> _logger;
    private readonly IPointerState? _pointerState;


    private const double HALO_SIZE = 64;
    private const int WIN_SIZE = 92;
    private const int FOLLOW_MS = 16;

    private static ChaosEStimGlowOverlay? _active;

    private readonly DispatcherTimer _follow;
    private readonly Ellipse _halo;
    private ScalePulse? _tremble;

    public ChaosEStimGlowOverlay()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<ChaosEStimGlowOverlay>>();
        _pointerState = App.Services.GetService<IPointerState>();
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = AvaloniaChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = WIN_SIZE;
        Height = WIN_SIZE;

        var brush = new RadialGradientBrush { GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xBF, 0xEC, 0xFF), 0.10));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0x9C, 0x5C, 0xFF), 0.45));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x9C, 0x5C, 0xFF), 1.0));

        var haloScale = new ScaleTransform(1, 1);
        _halo = new Ellipse
        {
            Width = HALO_SIZE, Height = HALO_SIZE,
            Fill = brush,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = haloScale,
        };
        Content = _halo;

        // A quick electric tremble — charged, not breathing (WPF DoubleAnimation 0.88↔1.10 / 160ms
        // AutoReverse Forever SineEase). ScalePulse's period is the full up+down cycle => 320ms.
        _tremble = new ScalePulse(haloScale, 0.88, 1.10, 320);

        _follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FOLLOW_MS) };
        _follow.Tick += FollowTick;

        Opened += (_, _) => ApplyExStyles();
    }

    public static void EnsureCreated()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null)
                    {
                        _active = new ChaosEStimGlowOverlay();
                        ((global::Avalonia.Controls.Window)_active).Show();
                        _active.Hide();
                    }
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosEStimGlowOverlay>>().LogInformation("ChaosEStimGlow.EnsureCreated: {E}", ex.Message); }
            });
        }
        catch { }
    }

    public static void Arm()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosEStimGlowOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); }
                    else if (!_active.IsVisible) ((global::Avalonia.Controls.Window)_active).Show();
                    AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
                    _active.FollowTick(null, EventArgs.Empty);
                    _active._follow.Start();
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosEStimGlowOverlay>>().LogInformation("ChaosEStimGlow.Arm: {E}", ex.Message); }
            });
        }
        catch { }
    }

    public static void Disarm()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null) return;
                    _active._follow.Stop();
                    _active.Hide();
                }
                catch { }
            });
        }
        catch { }
    }

    public static void RaiseActive() => AvaloniaChaosWindowZ.RaiseTopmost(_active);

    public static void CloseActive()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var w = _active;
                    _active = null;
                    if (w != null) { w._follow.Stop(); w._tremble?.Dispose(); w.Close(); }
                }
                catch { }
            });
        }
        catch { }
    }

    private void FollowTick(object? sender, EventArgs e)
    {
        try
        {
            var cursor = _pointerState?.GetCursorPosition();
            if (cursor.HasValue)
            {
                Position = new PixelPoint(
                    cursor.Value.X - WIN_SIZE / 2,
                    cursor.Value.Y - WIN_SIZE / 2);
                return;
            }

            // Degrade to center of primary screen when global cursor position is unavailable.
            var screens = AvaloniaChaosWindowZ.GetScreens();
            var primary = screens?.Primary;
            if (primary == null) return;
            var b = primary.Bounds;
            var cx = b.X + b.Width / 2;
            var cy = b.Y + b.Height / 2;
            Position = new PixelPoint((int)(cx - WIN_SIZE / 2.0), (int)(cy - WIN_SIZE / 2.0));
        }
        catch (Exception ex) { _logger?.LogInformation("ChaosEStimGlow tick: {E}", ex.Message); }
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);
}
