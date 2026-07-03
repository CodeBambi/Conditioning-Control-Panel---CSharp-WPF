using System;
using System.Collections.Generic;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosEffectBannerOverlay: top-of-screen strip naming active Chaos bonuses.
/// Kept alive while empty; closed only at teardown.
/// </summary>
public partial class ChaosEffectBannerOverlay : Window
{
    private const double FONT_SIZE = 34;
    private const double ART_HEIGHT_DIP = 56;
    private const int FADE_IN_MS = 200;
    private const int FADE_OUT_MS = 380;

    private static ChaosEffectBannerOverlay? _active;

    private readonly StackPanel _row;
    private readonly Dictionary<string, Control> _entries = new();
    private readonly Dictionary<string, ScalePulse> _pulses = new();
    private OpacityFade? _fade;

    public ChaosEffectBannerOverlay()
    {
        InitializeComponent();

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

        var wa = GetWorkArea();
        Position = new PixelPoint((int)wa.X, (int)(wa.Y + 6));
        Width = wa.Width;
        Height = 80;

        _row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        Content = _row;

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
                    if (_active == null) { _active = new ChaosEffectBannerOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); }
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosEffectBannerOverlay>>().LogInformation("ChaosEffectBanner.EnsureCreated: {E}", ex.Message); }
            });
        }
        catch { }
    }

    public static void Show(string id, string text, Color accent, string? artKey = null)
    {
        var logger = App.Services.GetRequiredService<ILogger<ChaosEffectBannerOverlay>>();
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosEffectBannerOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); }
                    accent = ChaosBoonColors.ForOrDefault(id, accent);   // payload-based color language (WPF parity)
                    _active.AddEntry(id, text, accent, artKey);
                    AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosEffectBannerOverlay>>().LogInformation("ChaosEffectBanner.Show: {E}", ex.Message); }
            });
        }
        catch { }
    }

    public static void End(string id)
    {
        try
        {
            Dispatcher.UIThread.Post(() => _active?.FadeEntry(id));
        }
        catch { }
    }

    public static void RaiseActive() => AvaloniaChaosWindowZ.RaiseTopmost(_active);

    public static void CloseActive()
    {
        try { _active?.CloseNow(); } catch { }
    }

    private void AddEntry(string id, string text, Color accent, string? artKey = null)
    {
        if (_entries.ContainsKey(id)) return;

        var scale = new ScaleTransform(1, 1);
        Control label;
        var art = AvaloniaChaosArt.Resolve("announce", artKey ?? id);
        if (art != null)
        {
            label = new Image
            {
                Source = art,
                Height = ART_HEIGHT_DIP,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(18, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RenderTransform = scale,
                Opacity = 0,
            };
        }
        else
        {
            var outlined = new AvaloniaOutlinedText
            {
                Text = text.ToUpperInvariant(),
                FontSize = FONT_SIZE,
                Fill = FrozenBrush(accent),
                Stroke = FrozenBrush(Color.FromRgb(0x0B, 0x08, 0x12)),
                StrokeThickness = 2.6,
                Margin = new Thickness(18, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RenderTransform = scale,
                Opacity = 0,
            };
            outlined.Build();
            label = outlined;
        }
        _entries[id] = label;
        _row.Children.Add(label);

        _fade?.Dispose();
        _fade = new OpacityFade(label, 0, 1, FADE_IN_MS);
        _pulses[id]?.Dispose();
        // A barely-there heartbeat (WPF DoubleAnimation 1.0↔1.03 / 850ms AutoReverse Forever
        // SineEase). ScalePulse's period is the full up+down cycle => 1700ms.
        _pulses[id] = new ScalePulse(scale, 1.0, 1.03, 1700);
    }

    private void FadeEntry(string id)
    {
        if (!_entries.TryGetValue(id, out var el)) return;
        _entries.Remove(id);
        if (_pulses.TryGetValue(id, out var pulse))
        {
            _pulses.Remove(id);
            pulse.Dispose();
        }
        _fade?.Dispose();
        _fade = new OpacityFade(el, el.Opacity, 0, FADE_OUT_MS, () =>
        {
            try { _row.Children.Remove(el); } catch { }
        });
    }

    private void CloseNow()
    {
        if (ReferenceEquals(_active, this)) _active = null;
        _entries.Clear();
        foreach (var p in _pulses.Values) p.Dispose();
        _pulses.Clear();
        try { Close(); } catch { }
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);

    private static Rect GetWorkArea()
    {
        var screens = AvaloniaChaosWindowZ.GetScreens();
        var primary = screens?.Primary;
        if (primary == null) return new Rect(0, 0, 1920, 1080);
        var wa =
primary.WorkingArea;
        return new Rect(wa.X, wa.Y, wa.Width, wa.Height);
    }

    private static IBrush FrozenBrush(Color c) => new SolidColorBrush(c);
}
