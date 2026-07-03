using System;
using System.Collections.Generic;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media;
using global::Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosPopText: small floating word at a bubble location.
/// </summary>
public partial class ChaosPopText : Window
{
    private readonly ILogger<ChaosPopText> _logger;
    private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService _settings;


    private const int IN_MS = 60;
    private const int HOLD_MS = 230;
    private const int OUT_MS = 200;
    private const double FONT_SIZE = 22;
    private const double PEAK_OPAC = 0.58;
    private const double RISE_DIP = 22;
    private const double WIN_W = 280;
    private const double WIN_H = 88;
    private const int POOL_MAX = 14;

    private static readonly Stack<ChaosPopText> _pool = new();
    private static readonly List<ChaosPopText> _all = new();

    private readonly Grid _root = new();
    private readonly DispatcherTimer _holdTimer = new();
    private OpacityFade? _outFade;
    private bool _closed;

    public ChaosPopText()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<ChaosPopText>>();
        _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
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
        Width = WIN_W;
        Height = WIN_H;
        Opacity = 0;
        Content = _root;
        _root.IsHitTestVisible = false;

        _holdTimer.Interval = TimeSpan.FromMilliseconds(IN_MS + HOLD_MS);
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer.Stop();
            _outFade?.Dispose();
            _outFade = new OpacityFade(this, Opacity, 0, OUT_MS, Retire);
        };

        Opened += (_, _) => ApplyExStyles();
    }

    public static void Show(double anchorXDip, double anchorYDip, string text, Color color)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (App.Services?.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current?.ChaosAnnouncerEnabled != true) return;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    ChaosPopText? w = null;
                    if (_pool.Count > 0) w = _pool.Pop();
                    else if (_all.Count < POOL_MAX) { w = new ChaosPopText(); _all.Add(w); }
                    w?.Play(anchorXDip, anchorYDip, text, color);
                }
                catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosPopText>>().LogInformation("ChaosPopText.Show inner: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosPopText>>().LogInformation("ChaosPopText.Show: {E}", ex.Message); }
    }

    public static void RaiseActive()
    {
        foreach (var w in _all) AvaloniaChaosWindowZ.RaiseTopmost(w);
    }

    public static void ShutdownPool()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var w in _all) { try { w.Close(); } catch { } }
                _all.Clear();
                _pool.Clear();
            });
        }
        catch { }
    }

    private void Play(double anchorXDip, double anchorYDip, string text, Color color)
    {
        if (_closed) return;
        Position = new PixelPoint((int)(anchorXDip - WIN_W / 2), (int)(anchorYDip - WIN_H / 2));

        var (fill, stroke) = Palette(color);
        var rise = new TranslateTransform(0, 6);
        var label = new AvaloniaOutlinedText
        {
            Text = text.ToUpperInvariant(),
            FontSize = FONT_SIZE,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 2.0,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            RenderTransform = rise,
        };
        _root.Children.Clear();
        _root.Children.Add(label);

        _outFade?.Dispose();
        Opacity = 0;
        Show();
        AvaloniaChaosWindowZ.RaiseAboveVideo(this);
        label.Build();

        _outFade = new OpacityFade(this, 0, PEAK_OPAC, IN_MS);
        _holdTimer.Stop();
        _holdTimer.Start();

        // Self-contained rise on a FRESH transform per Play (WPF DoubleAnimation 6 → -RISE_DIP over
        // the whole life). The old code re-subscribed _riseTimer.Tick on every pooled reuse, so
        // stale handlers accumulated and each fired Stop() on the fresh rise — this owns its timer.
        AvaloniaChaosAnim.AnimateDouble(rise, TranslateTransform.YProperty, 6, -RISE_DIP,
            IN_MS + HOLD_MS + OUT_MS, AvaloniaChaosAnim.EasingMode.Linear);
    }

    private void Retire()
    {
        if (_closed) return;
        try
        {
            _holdTimer.Stop();
            _outFade?.Dispose();
            Opacity = 0;
            _root.Children.Clear();
            Hide();
        }
        catch { }
        _pool.Push(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        try { _holdTimer.Stop(); } catch { }
        base.OnClosed(e);
    }

    private static (IBrush fill, IBrush stroke) Palette(Color tint)
    {
        byte Lift(byte c) => (byte)Math.Clamp(c + (255 - c) * 0.28, 0, 255);
        var c = Color.FromRgb(Lift(tint.R), Lift(tint.G), Lift(tint.B));
        var fill = new SolidColorBrush(c);
        var stroke =
new SolidColorBrush(Color.FromRgb(0x0B, 0x08, 0x12));
        return (fill, stroke);
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);
}
