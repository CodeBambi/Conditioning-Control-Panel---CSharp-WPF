// PORTED from ConditioningControlPanel/Services/Possession/Effects/ToastEffect.cs (7.1.5, wave B).
// R1: a toast that is not a real toast slides into a corner of the window, says something wrong in the
// mod's voice (Core ToastLines) and slides out after four seconds, or at once when clicked.
//
// It takes no input (the click that dismisses it is only watched and carries on to whatever is under
// it) and it never covers anything the user presses: the corner is chosen so the toast's rectangle
// touches no button, toggle, text box or slider; with no such corner free the toast does not appear.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Services.Possession.Effects;

internal sealed class ToastEffect : PossessionEffectBase
{
    internal const double SlideMs = 280, Edge = 18, ToastWidth = 300, ToastHeight = 56;
    private static readonly IBrush Ember = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x5C)).ToImmutable();
    private static readonly IBrush DeepRed = new SolidColorBrush(Color.FromRgb(0x1A, 0x0A, 0x0A)).ToImmutable();
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0xFF, 0xE3, 0xDA)).ToImmutable();
    private static string? _lastLine;

    /// <summary>The window to haunt (the live shell; tests hand their own).</summary>
    internal Func<TopLevel?> Window = () => global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.Current;

    private Border? _toast;
    private TranslateTransform? _slide;
    private TopLevel? _top;
    private Rect _rect;
    private EventHandler<PointerPressedEventArgs>? _press;

    public override string Id => "toast";
    public override PossessionRung MinRung => PossessionRung.Drift;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => false;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(4);

    internal Border? Toast => _toast;
    internal Rect ToastRect => _rect;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
        Window() is { } top && OverlayLayer.GetOverlayLayer(top) != null && FreeCorner(top) != null;

    /// <summary>The first corner (bottom right first, as WPF) whose rectangle touches nothing pressable.</summary>
    internal static Rect? FreeCorner(TopLevel top)
    {
        double w = top.Bounds.Width, h = top.Bounds.Height;
        if (w < ToastWidth + 2 * Edge || h < ToastHeight + 2 * Edge) return null;
        var corners = new[]
        {
            new Rect(w - ToastWidth - Edge, h - ToastHeight - Edge, ToastWidth, ToastHeight),
            new Rect(Edge, h - ToastHeight - Edge, ToastWidth, ToastHeight),
            new Rect(w - ToastWidth - Edge, Edge, ToastWidth, ToastHeight),
            new Rect(Edge, Edge, ToastWidth, ToastHeight),
        };
        foreach (var corner in corners)
        {
            bool clear = true;
            foreach (var v in top.GetVisualDescendants())
            {
                if (v is not Control c || !c.IsEffectivelyVisible) continue;
                if (!PossessionTree.IsInteractive(c) || c is ScrollViewer || c is SelectingItemsControl and not ComboBox) continue;   // containers span the window; their pressable items are met one by one
                if (c.TransformToVisual(top) is not { } m) continue;
                if (new Rect(c.Bounds.Size).TransformToAABB(m).Intersects(corner)) { clear = false; break; }
            }
            if (clear) return corner;
        }
        return null;
    }

    protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (Window() is not { } top || OverlayLayer.GetOverlayLayer(top) is not { } layer) return;
        if (FreeCorner(top) is not { } rect) return;
        var line = ToastLines.Pick(ctx.Rng, _lastLine);
        _lastLine = line;

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        row.Children.Add(new TextBlock
        {
            Text = "◆", Foreground = Ember, FontSize = 15, VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI"),
        });
        var text = new TextBlock
        {
            Text = line, Foreground = Ink, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0),
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        bool fromRight = rect.X > Edge;
        _slide = new TranslateTransform(fromRight ? ToastWidth + Edge : -(ToastWidth + Edge), 0);
        _toast = new Border
        {
            Width = ToastWidth, MinHeight = ToastHeight, Background = DeepRed,
            BorderBrush = Ember,      // the ember edge: this is Possession, not a real toast
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 12, 8), IsHitTestVisible = false, Focusable = false,
            Opacity = 0, RenderTransform = _slide, Child = row,
        };
        Canvas.SetLeft(_toast, rect.X);
        Canvas.SetTop(_toast, rect.Y);
        layer.Children.Add(_toast);
        _rect = rect;
        _top = top;

        // IN: slide and fade together (photosafe: the same move, there is no blink in it).
        Tween(_slide, SlideMs, false, (0, TranslateTransform.XProperty, _slide.X), (1, TranslateTransform.XProperty, 0));
        Tween(_toast, SlideMs, false, (0, Visual.OpacityProperty, 0), (1, Visual.OpacityProperty, 1));

        // A click on the toast sends it away. Watched only: the press carries on to what is under it.
        _press = (_, e) =>
        {
            if (_toast != null && _rect.Contains(e.GetPosition(top))) _ = UndoAsync(TimeSpan.FromMilliseconds(160));
        };
        top.AddHandler(InputElement.PointerPressedEvent, _press, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override bool SettleCore(double ms)
    {
        if (_toast == null || _slide == null) return false;
        double off = _rect.X > Edge ? ToastWidth + Edge : -(ToastWidth + Edge);
        Tween(_slide, ms, false, (0, TranslateTransform.XProperty, _slide.X), (1, TranslateTransform.XProperty, off));
        Tween(_toast, ms, false, (0, Visual.OpacityProperty, Math.Clamp(_toast.Opacity, 0, 1)), (1, Visual.OpacityProperty, 0));
        return true;
    }

    protected override void RestoreCore()
    {
        if (_top != null && _press != null) _top.RemoveHandler(InputElement.PointerPressedEvent, _press);
        (_toast?.Parent as Panel)?.Children.Remove(_toast!);
        _toast = null;
        _slide = null;
        _top = null;
        _press = null;
    }
}
