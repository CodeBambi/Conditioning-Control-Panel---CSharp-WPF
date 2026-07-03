using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosWaveTimerOverlay: a small click-through pill pinned to the
/// top-right corner showing the current wave and time left.
/// </summary>
public partial class ChaosWaveTimerOverlay : Window
{
    private readonly ILogger<ChaosWaveTimerOverlay> _logger;


    private static ChaosWaveTimerOverlay? _active;

    private readonly Border _pill;
    private readonly TextBlock _wave;
    private readonly TextBlock _clock;
    private readonly TextBlock _score;
    private OpacityPulse? _blink;

    private bool _urgent;
    private bool _finalRush;

    public ChaosWaveTimerOverlay()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<ChaosWaveTimerOverlay>>();
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
        SizeToContent = SizeToContent.WidthAndHeight;

        _wave = new TextBlock
        {
            Foreground = AppBrush("PinkButtonHoveredBrush", new SolidColorBrush(Color.FromRgb(0xE8, 0x9B, 0xC8))),
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        _clock = new TextBlock
        {
            Foreground = AppBrush("TextLightBrush", Brushes.White),
            FontSize = 17,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_wave);
        row.Children.Add(_clock);

        _score = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(_score);

        _pill = new Border
        {
            Background = AppBrush("PanelBgTransparentBrush", new SolidColorBrush(Color.FromArgb(170, 0x12, 0x0E, 0x1E))),
            BorderBrush = AppBrush("TransparentPink50Brush", new SolidColorBrush(Color.FromArgb(160, 0xE8, 0x43, 0x93))),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 5, 14, 5),
            IsVisible = false,
            IsHitTestVisible = false,
            Child = stack,
        };
        Content = _pill;

        var wa = GetPrimaryWorkArea();   // physical px
        double scale0 = AvaloniaChaosWindowZ.PrimaryScaling();
        Position = new PixelPoint((int)(wa.Right - 180 * scale0), (int)(wa.Y + 10 * scale0));
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ClientSizeProperty)
            {
                try
                {
                    var area = GetPrimaryWorkArea();   // physical px
                    double scale = RenderScaling <= 0 ? AvaloniaChaosWindowZ.PrimaryScaling() : RenderScaling;
                    // area.* are physical px; ClientSize + the 14/10 gaps are DIPs — convert to px
                    // before assigning the physical-px Position (WPF did this all in DIP space).
                    Position = new PixelPoint(
                        (int)(area.Right - ClientSize.Width * scale - 14 * scale),
                        (int)(area.Y + 10 * scale));
                }
                catch { }
            }
        };

        Opened += (_, _) => ApplyExStyles();
    }

    public static void EnsureCreated()
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess()) TryCreate();
            else Dispatcher.UIThread.Post(TryCreate);
        }
        catch { }
    }

    private static void TryCreate()
    {
        try
        {
            if (_active == null) { _active = new ChaosWaveTimerOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); }
        }
        catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosWaveTimerOverlay>>().LogInformation("ChaosWaveTimer.EnsureCreated: {E}", ex.Message); }
    }

    public static void Update(int wave, int waveCount, double secLeftInWave, double score)
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosWaveTimerOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); }
                    _active.SetText(wave, waveCount, secLeftInWave, score);
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosWaveTimerOverlay>>().LogInformation("ChaosWaveTimer.Update: {E}", ex.Message); }
            });
        }
        catch { }
    }

    public static void Clear()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try { if (_active != null) _active._pill.IsVisible = false; }
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
                try { _active?.CloseNow(); }
                catch { }
            });
        }
        catch { }
    }

    private void SetText(int wave, int waveCount, double secLeftInWave, double score)
    {
        _pill.IsVisible = true;
        bool last = wave >= waveCount;
        _wave.Text = last ? "LAST WAVE" : $"WAVE {wave}/{waveCount}";
        int s = (int)Math.Max(0, Math.Ceiling(secLeftInWave));
        _clock.Text = $"{s / 60}:{s % 60:00}";
        _score.Text = $"{(int)score:N0}";

        bool urgent = secLeftInWave <= 10;
        if (urgent != _urgent)
        {
            _urgent = urgent;
            _clock.Foreground = urgent
                ? AppBrush("DangerBrush", new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A)))
                : Brushes.White;
        }

        bool finalRush = urgent && last;
        if (finalRush != _finalRush)
        {
            _finalRush = finalRush;
            if (finalRush)
            {
                // Smooth pulse (WPF DoubleAnimation 1.0↔0.25 / 420ms AutoReverse Forever), not a
                // hard 420ms opacity toggle. The pulse period is the full up+down cycle => 840ms.
                _blink?.Dispose();
                _blink = new OpacityPulse(_clock, 0.25, 1.0, 840);
            }
            else
            {
                _blink?.Dispose();
                _blink = null;
                _clock.Opacity = 1.0;
            }
        }
    }

    private void CloseNow()
    {
        if (ReferenceEquals(_active, this)) _active = null;
        try { _blink?.Dispose(); } catch { }
        try { Close(); } catch { }
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);

    private static Rect GetPrimaryWorkArea()
    {
        var screens = AvaloniaChaosWindowZ.GetScreens();
        var primary = screens?.Primary;
        if (primary == null) return new Rect(0, 0, 1920, 1080);
        var wa =
primary.WorkingArea;
        return new Rect(wa.X, wa.Y, wa.Width, wa.Height);
    }

    private static IBrush AppBrush(string key, IBrush fallback)
    {
        if (global::Avalonia.Application.Current?.TryGetResource(key, global::Avalonia.Styling.ThemeVariant.Default, out var v) == true && v is IBrush b)
            return b;
        return fallback;
    }
}
