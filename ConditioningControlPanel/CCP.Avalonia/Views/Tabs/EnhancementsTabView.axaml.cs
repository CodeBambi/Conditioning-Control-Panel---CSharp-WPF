using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class EnhancementsTabView : UserControl
{
    // The skill-tree content is authored at a fixed design size (3 rows -> 640px tall); we scale it
    // by the available viewport height so cards + images grow/shrink with the window while width
    // still scrolls horizontally (WPF parity). Clamped so it never gets unreadably small or huge.
    private const double SkillTreeDesignHeight = 640.0;
    private LayoutTransformControl? _skillTreeScaler;

    public EnhancementsTabView()
    {
        AvaloniaXamlLoader.Load(this);

        _skillTreeScaler = this.FindControl<LayoutTransformControl>("SkillTreeScaler");
        var scroller = this.FindControl<ScrollViewer>("SkillTreeScroller");
        if (scroller != null)
        {
            scroller.PointerWheelChanged += SkillTreeScroller_PointerWheelChanged;
            scroller.EffectiveViewportChanged += (_, _) => UpdateSkillTreeScale(scroller);
        }
    }

    private void UpdateSkillTreeScale(ScrollViewer scroller)
    {
        if (_skillTreeScaler == null)
            return;

        var viewportHeight = scroller.Bounds.Height;
        if (viewportHeight <= 0)
            return;

        var scale = Math.Clamp(viewportHeight / SkillTreeDesignHeight, 0.5, 1.25);
        if (_skillTreeScaler.LayoutTransform is ScaleTransform existing &&
            Math.Abs(existing.ScaleX - scale) < 0.001)
            return;

        _skillTreeScaler.LayoutTransform = new ScaleTransform(scale, scale);
    }

    /// <summary>
    /// Redirects vertical mouse wheel scrolling to horizontal scrolling for the skill tree.
    /// </summary>
    private void SkillTreeScroller_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        var delta = e.Delta.Y;
        if (delta == 0)
            return;

        scrollViewer.Offset = scrollViewer.Offset.WithX(scrollViewer.Offset.X - delta * 40);
        e.Handled = true;
    }
}
