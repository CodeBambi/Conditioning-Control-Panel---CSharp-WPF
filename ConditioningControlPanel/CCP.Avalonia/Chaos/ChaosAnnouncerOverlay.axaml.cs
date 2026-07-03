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
/// Avalonia port of ChaosAnnouncerOverlay: full-screen subtitle announcement in the upper third.
/// One window kept alive; content swapped between announcements.
/// </summary>
public partial class ChaosAnnouncerOverlay : Window
{
private const int IN_MS = 110;
    private const int HOLD_MS = 650;
    public const int TEACH_HOLD_MS = 3000;
    private const int OUT_MS = 240;
    private const double FONT_SIZE = 60;
    private const double SUB_FONT_SIZE = 26;
    private const double ART_HEIGHT_DIP = 120;
    private const double TOP_OFFSET_DIP = 92;

    private static ChaosAnnouncerOverlay? _active;
    private static readonly List<(string text, ChaosAnnounceKind kind, string? artKey, string? subText, int holdMs, int priority)> _queue = new();
    private static bool _showing;

    private readonly Grid _host;
    private readonly DispatcherTimer _life = new();
    private (string text, ChaosAnnounceKind kind, string? artKey, string? subText, int holdMs)? _pending;
    private OpacityFade? _fade;

    public ChaosAnnouncerOverlay()
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

        var screens = AvaloniaChaosWindowZ.GetScreens();
        var all = screens?.All;
        if (all == null || all.Count == 0)
        {
            Position = new PixelPoint(0, 0);
            Width = 1920;
            Height = 1080;
        }
        else
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var s in all)
            {
                var b = s.Bounds;
                minX = Math.Min(minX, b.X);
                minY = Math.Min(minY, b.Y);
                maxX = Math.Max(maxX, b.Right);
                maxY = Math.Max(maxY, b.Bottom);
            }
            Position = new PixelPoint((int)minX, (int)minY);
            Width = maxX - minX;
            Height = maxY - minY;
        }
        Opacity = 0;

        _host = new Grid { IsHitTestVisible = false };
        Content = _host;

        Opened += (_, _) => ApplyExStyles();
        Loaded += (_, _) =>
        {
            if (_pending is { } p) { _pending = null; DisplayCore(p.text, p.kind, p.artKey, p.subText, p.holdMs); }
        };

        _life.Tick += (_, _) =>
        {
            _life.Stop();
            _fade?.Dispose();
            _fade = new OpacityFade(this, Opacity, 0, OUT_MS, () =>
            {
                try { _host.Children.Clear(); } catch { }
                if (_queue.Count == 0) { try { Hide(); } catch { } }
                ShowNext();
            });
        };
    }

    public static void Announce(string text, ChaosAnnounceKind kind,
                                string? artKey = null, string? subText = null, int? holdMs = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (App.Services?.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current?.ChaosAnnouncerEnabled != true) return;
            Dispatcher.UIThread.Invoke(() =>
            {
                _queue.Add((text, kind, artKey, subText, holdMs ?? HOLD_MS, 0));
                if (!_showing) ShowNext();
            });
        }
        catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosAnnouncerOverlay>>().LogInformation("ChaosAnnouncer.Announce: {E}", ex.Message); }
    }

    public static void AnnounceNarrator(string text, int bandPriority, bool interrupt, int holdMs)
    {
        var logger = App.Services.GetRequiredService<ILogger<ChaosAnnouncerOverlay>>();
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!AvaloniaChaosMode.NarrativeActive) return;
            Dispatcher.UIThread.Invoke(() =>
            {
                _queue.Add((text, ChaosAnnounceKind.Narrator, null, null, holdMs, 100 + bandPriority));
                if (!_showing) ShowNext();
                else if (interrupt) _active?.CutShort();   // STORY: end the current line so she shows next
            });
        }
        catch (Exception ex) { App.Services?.GetRequiredService<ILogger<ChaosAnnouncerOverlay>>().LogInformation("ChaosAnnouncer.AnnounceNarrator: {E}", ex.Message); }
    }

    public static void RaiseActive() => AvaloniaChaosWindowZ.RaiseTopmost(_active);

    public static void CloseActive()
    {
        try { _queue.Clear(); _showing = false; _active?.CloseNow(); } catch { }
    }

    private static void ShowNext()
    {
        var logger = App.Services.GetRequiredService<ILogger<ChaosAnnouncerOverlay>>();
        if (!TryDequeue(out var dq)) { _showing = false; return; }
        _showing = true;
        var (text, kind, artKey, subText, holdMs, _) = dq;
        try
        {
            if (_active == null) { _active = new ChaosAnnouncerOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); }
            else if (!_active.IsVisible) { try { ((global::Avalonia.Controls.Window)_active).Show(); } catch { } }
            AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
            _active.Display(text, kind, artKey, subText, holdMs);
        }
        catch (Exception ex)
        {
            App.Services?.GetRequiredService<ILogger<ChaosAnnouncerOverlay>>().LogInformation("ChaosAnnouncer.ShowNext: {E}", ex.Message);
            _showing = false;
        }
    }

    private static bool TryDequeue(out (string text, ChaosAnnounceKind kind, string? artKey, string? subText, int holdMs, int priority) item)
    {
        item = default;
        if (_queue.Count == 0) return false;
        int best = 0;
        for (int i = 1; i < _queue.Count; i++)
            if (_queue[i].priority > _queue[best].priority) best = i;
        item = _queue[best];
        _queue.RemoveAt(best);
        return true;
    }

    private void Display(string text, ChaosAnnounceKind kind, string? artKey, string? subText, int holdMs)
    {
        if (!IsLoaded) { _pending = (text, kind, artKey, subText, holdMs); return; }
        DisplayCore(text, kind, artKey, subText, holdMs);
    }

    private void DisplayCore(string text, ChaosAnnounceKind kind, string? artKey, string? subText, int holdMs)
    {
        _life.Stop();
        _life.Interval = TimeSpan.FromMilliseconds(IN_MS + Math.Max(0, holdMs));
        var (fill, stroke) = Palette(kind);
        var scale = new ScaleTransform(0.85, 0.85);
        var art = artKey != null ? AvaloniaChaosArt.Resolve("announce", artKey) : null;

        Control content;
        if (art != null)
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical };
            panel.Children.Add(new Image
            {
                Source = art,
                Height = ART_HEIGHT_DIP,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            if (!string.IsNullOrWhiteSpace(subText))
            {
                var outlined = new AvaloniaOutlinedText
                {
                    Text = subText!.ToUpperInvariant(),
                    FontSize = SUB_FONT_SIZE,
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = 2.2,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0),
                };
                outlined.Build();
                panel.Children.Add(outlined);
            }
            content = panel;
        }
        else
        {
            var label = new AvaloniaOutlinedText
            {
                Text = text.ToUpperInvariant(),
                FontSize = FONT_SIZE,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 3.2,
            };
            label.Build();
            content = label;
        }

        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Top;
        content.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        content.RenderTransform = scale;

        var wa = GetPrimaryWorkArea();
        var anchor = new Grid
        {
            Width = wa.Width,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(
                Math.Max(0, wa.X - VirtualLeft()),
                Math.Max(0, wa.Y - VirtualTop()) + TOP_OFFSET_DIP, 0, 0),
        };
        anchor.Children.Add(content);
        _host.Children.Clear();
        _host.Children.Add(anchor);

        _fade?.Dispose();
        _fade = new OpacityFade(this, 0, 1, IN_MS);
        // Scale-pop the content in (WPF BackEase EaseOut 0.85→1.0 over IN_MS+70, amplitude 0.6) —
        // not a static 0.85.
        AvaloniaChaosAnim.ScalePop(scale, 0.85, 1.0, IN_MS + 70);
        _life.Start();
    }

    private void CutShort()
    {
        try { _life.Stop(); _life.Interval = TimeSpan.FromMilliseconds(1); _life.Start(); } catch { }
    }

    private void CloseNow()
    {
        try { _life.Stop(); } catch { }
        try { _host.Children.Clear(); } catch { }
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    private static (IBrush fill, IBrush stroke) Palette(ChaosAnnounceKind kind)
    {
        IBrush fill = kind switch
        {
            ChaosAnnounceKind.Mantra => AppBrush("PinkButtonHoveredBrush", new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x7A))),
            ChaosAnnounceKind.Temptation => AppBrush("DangerBrush", new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B))),
            ChaosAnnounceKind.Willpower => new SolidColorBrush(Color.FromRgb(0x7A, 0xE0, 0xFF)),
            ChaosAnnounceKind.Depth => AppBrush("TextLightBrush", Brushes.White),
            ChaosAnnounceKind.Streak => new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3C)),
            ChaosAnnounceKind.Item => new SolidColorBrush(Color.FromRgb(0x7A, 0xFF, 0xD2)),
            ChaosAnnounceKind.PowerUp => new SolidColorBrush(Color.FromRgb(0x9C, 0xE8, 0xA0)),
            ChaosAnnounceKind.Narrator => new SolidColorBrush(Color.FromRgb(0xE6, 0x9A, 0xFF)),
            _ => AppBrush("TextLightBrush", Brushes.White),
        };
        return (fill, new SolidColorBrush(Color.FromRgb(0x0B, 0x08, 0x12)));
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);

    private static Rect GetPrimaryWorkArea()
    {
        var screens = AvaloniaChaosWindowZ.GetScreens();
        var primary = screens?.Primary;
        if (primary == null) return new Rect(0, 0, 1920, 1080);
        var wa = primary.WorkingArea;
        return new Rect(wa.X, wa.Y, wa.Width, wa.Height);
    }

    private static double VirtualLeft()
    {
        var screens = AvaloniaChaosWindowZ.GetScreens();
        if (screens?.All.Count == 0) return 0;
        double minX = double.MaxValue;
        foreach (var s in screens!.All) minX = Math.Min(minX, s.Bounds.X);
        return minX;
    }

    private static IBrush AppBrush(string key, IBrush fallback)
    {
        if (global::Avalonia.Application.Current?.TryGetResource(key, global::Avalonia.Styling.ThemeVariant.Default, out var v) == true && v is IBrush b)
            return b;
        return fallback;
    }

    private static double VirtualTop()
    {
        var screens = AvaloniaChaosWindowZ.GetScreens();
        if (screens?.All.Count == 0) return 0;
        double minY = double.MaxValue;
        foreach (var s in screens!.All) minY = Math.Min(minY, s.Bounds.Y);
        return minY;
}
}
