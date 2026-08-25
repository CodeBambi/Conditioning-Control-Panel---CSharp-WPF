using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R1 "toast" - a notification from the house. A small crimson card slides in at the bottom right with
/// the companion's face on it and one line that is not an app message ("setting reverted by: her"),
/// sits for four seconds and slides out. It names itself: the line IS the attribution, which is why
/// this is the one big-feeling effect that is not <see cref="PossessionEffectBase.IsBig"/> - the warden
/// would only be repeating what the toast already said.
///
/// <para>It is theatre, not chrome: it lives in the GhostLayer, it never queues behind the real toast
/// system, and it needs no click. The GhostLayer is deliberately not hit-testable (a ghost may never
/// eat a click), so "clicking dismisses" is served by a preview handler on the window that checks the
/// point against the toast's rectangle - the click still reaches whatever was underneath.</para>
/// </summary>
public sealed class ToastEffect : PossessionEffectBase
{
    private const double SlideMs = 280;
    private const double Margin = 18;
    private const double ToastWidth = 300;

    private static readonly Color Crimson = Color.FromRgb(0xDC, 0x14, 0x3C);
    private static readonly Color DeepRed = Color.FromRgb(0x1A, 0x0A, 0x0A);

    /// <summary>Last line shown, so two toasts in a row are never the same words.</summary>
    private static string? _lastLine;

    private Border? _toast;
    private TranslateTransform? _slide;
    private Rect _rect;
    private Window? _window;
    private MouseButtonEventHandler? _click;

    public override string Id => "toast";
    public override PossessionRung MinRung => PossessionRung.Drift;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => false;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(4);

    /// <summary>Targetless and window-wide: the ember charge belongs on the toast, which does not exist
    /// yet when the base would fire it. The toast's own ember edge carries the attribution instead.</summary>
    protected override bool ChargeOnApply => false;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
        => _toast == null && ctx.Host.GhostLayer != null;

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var layer = ctx.Host.GhostLayer;
        if (layer == null) return;

        var line = ToastLines.Pick(Rng, _lastLine);
        _lastLine = line;

        _toast = BuildToast(line);
        if (_toast == null) return;

        // Measure before parking it: the card's height depends on how the line wraps.
        _toast.Measure(new Size(ToastWidth, double.PositiveInfinity));
        double w = ToastWidth;
        double h = Math.Max(44, _toast.DesiredSize.Height);

        double layerW = layer.ActualWidth > 0 ? layer.ActualWidth : (ctx.Host.Window?.ActualWidth ?? 0);
        double layerH = layer.ActualHeight > 0 ? layer.ActualHeight : (ctx.Host.Window?.ActualHeight ?? 0);
        if (layerW <= 0 || layerH <= 0) { _toast = null; return; }

        double left = Math.Max(Margin, layerW - w - Margin);
        double top = Math.Max(Margin, layerH - h - Margin);
        _rect = new Rect(left, top, w, h);

        Canvas.SetLeft(_toast, left);
        Canvas.SetTop(_toast, top);
        layer.Children.Add(_toast);

        // In from the right edge.
        _slide = new TranslateTransform(w + Margin, 0);
        _toast.RenderTransform = _slide;
        _toast.Opacity = 0;

        HookDismiss(ctx);

        PossAnim.To(_slide, TranslateTransform.XProperty, 0, SlideMs, PossAnim.EaseOut);
        PossAnim.To(_toast, UIElement.OpacityProperty, 1, SlideMs, PossAnim.EaseOut);
        await PossAnim.DelayAsync(SlideMs + 20, ct).ConfigureAwait(true);
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        UnhookDismiss();

        double ms = UndoMs(duration, 160, SlideMs);
        var toast = _toast;

        try
        {
            if (toast != null && ms > 0 && _slide != null)
            {
                PossAnim.To(_slide, TranslateTransform.XProperty, _rect.Width + Margin, ms, PossAnim.EaseIn);
                PossAnim.To(toast, UIElement.OpacityProperty, 0, ms, PossAnim.EaseIn);
                await PossAnim.DelayAsync(ms + 20, CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession toast undo failed: {Error}", ex.Message); }

        try { if (toast != null) Ctx?.Host.GhostLayer?.Children.Remove(toast); }
        catch { }

        _toast = null;
        _slide = null;
        _window = null;
    }

    // ---- the card ------------------------------------------------------------------------------

    private Border? BuildToast(string line)
    {
        try
        {
            var crimson = new SolidColorBrush(Crimson); crimson.Freeze();
            var deep = new SolidColorBrush(DeepRed); deep.Freeze();
            var ink = new SolidColorBrush(Color.FromRgb(0xFF, 0xE3, 0xDA)); ink.Freeze();

            var shell = new Border
            {
                Width = ToastWidth,
                Background = deep,
                BorderBrush = EmberBrush,      // the ember edge: this is Possession, not a real toast
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 12, 8),
                IsHitTestVisible = false,
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var face = BuildFace(crimson);
            if (face != null)
            {
                Grid.SetColumn(face, 0);
                row.Children.Add(face);
            }

            var text = new TextBlock
            {
                Text = line,
                Foreground = ink,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(face != null ? 9 : 0, 0, 0, 0),
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            shell.Child = row;
            return shell;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession toast build failed: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>Her live portrait when the tube can hand one over, otherwise an ember glyph. Read-only
    /// either way: the tube's own Image keeps its source, we just point a second Image at it.</summary>
    private static FrameworkElement? BuildFace(Brush plate)
    {
        try
        {
            ImageSource? source = null;
            try { source = App.AvatarWindow?.ImgAvatar?.Source; } catch { }

            if (source != null)
            {
                return new Border
                {
                    Width = 30,
                    Height = 38,
                    CornerRadius = new CornerRadius(4),
                    Background = plate,
                    ClipToBounds = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new Image
                    {
                        Source = source,
                        Stretch = Stretch.UniformToFill,
                        IsHitTestVisible = false,
                    },
                };
            }
        }
        catch { }

        try
        {
            return new TextBlock
            {
                Text = "◆",
                Foreground = EmberBrush,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        catch { return null; }
    }

    // ---- dismissal -----------------------------------------------------------------------------

    private void HookDismiss(PossessionContext ctx)
    {
        try
        {
            _window = ctx.Host.Window;
            if (_window == null) return;
            _click = (_, e) =>
            {
                try
                {
                    var layer = Ctx?.Host.GhostLayer;
                    if (layer == null || _toast == null) return;
                    var p = e.GetPosition(layer);
                    if (!_rect.Contains(p)) return;      // not on the toast: the click carries on
                    // Right-click dismisses too (suggestion): handled only on that path, so a right
                    // press on the toast can't also open a context menu behind it.
                    if (e.ChangedButton == MouseButton.Right) e.Handled = true;
                    _ = UndoAsync(TimeSpan.FromMilliseconds(160));
                }
                catch { }
            };
            _window.PreviewMouseLeftButtonDown += _click;
            _window.PreviewMouseRightButtonDown += _click;
        }
        catch { _click = null; }
    }

    private void UnhookDismiss()
    {
        try { if (_window != null && _click != null) _window.PreviewMouseLeftButtonDown -= _click; }
        catch { }
        try { if (_window != null && _click != null) _window.PreviewMouseRightButtonDown -= _click; }
        catch { }
        _click = null;
    }
}
