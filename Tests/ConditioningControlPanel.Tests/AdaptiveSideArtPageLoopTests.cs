using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1321 / #1323 on the real pages. The side-art decision reads the adaptive Grid's own
/// width, so that width must follow the window, not the page's content. Both pages wrapped the
/// adaptive Grid in a HorizontalAlignment="Center" Grid, which sizes to its content: collapsing
/// the art (and lifting the settings column's MaxWidth) changed the content width by hundreds of
/// DIPs, far past the 48 DIP hysteresis, so the decision could feed itself forever. Measured in
/// this harness: a 1400 DIP wide Awareness page laid out its adaptive Grid at 606 DIP and stayed
/// compact at every width.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class AdaptiveSideArtPageLoopTests
{
    public static IEnumerable<object[]> Pages() => new[]
    {
        new object[] { "awareness" },
        new object[] { "listening" },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public void TheAdaptiveGridFollowsTheWindowAndSettles(string page)
    {
        var failures = new List<string>();
        WpfRenderHarness.OnStaThread(() =>
        {
            FrameworkElement view = page == "awareness" ? new AwarenessTabView() : new SheListeningTabView();
            var host = new Grid { Width = 1400, Height = 800 };
            host.Children.Add(view);

            Grid? grid = null;
            ColumnDefinition? col = null;
            int flips = 0;
            var dpd = DependencyPropertyDescriptor.FromProperty(ColumnDefinition.WidthProperty, typeof(ColumnDefinition));
            EventHandler onChange = (_, _) => flips++;

            // Narrow, then widen again, like a window being dragged.
            var widths = new List<double>();
            for (double w = 1400; w >= 900; w -= 20) widths.Add(w);
            for (double w = 900; w <= 1400; w += 20) widths.Add(w);

            try
            {
                foreach (var width in widths)
                {
                    host.Width = width;
                    host.Measure(new Size(width, 800));
                    host.Arrange(new Rect(0, 0, width, 800));
                    host.UpdateLayout();

                    if (grid == null)
                    {
                        grid = Descendants(view).OfType<Grid>()
                            .First(g => !double.IsNaN(AdaptiveSideArt.GetCollapseBelow(g)));
                        col = grid.ColumnDefinitions[grid.ColumnDefinitions.Count - 1];
                        dpd.AddValueChanged(col, onChange);
                    }

                    // A settled page does not change its mind on another layout pass.
                    flips = 0;
                    view.InvalidateMeasure();
                    host.UpdateLayout();
                    if (flips > 0) failures.Add($"{width}: flipped on a re-layout");

                    // The Grid is as wide as the window allows (minus page margins and a scrollbar, up to the page cap), never
                    // shrink-wrapped to whatever the current layout happens to contain.
                    if (grid.ActualWidth < Math.Min(width - 120, 1100))
                        failures.Add($"{width}: adaptive grid only {grid.ActualWidth:0.#} wide");
                }
            }
            finally { if (col != null) dpd.RemoveValueChanged(col, onChange); }

            // Wide enough for the art at the widest width.
            if (col!.Width.Value == 0) failures.Add("1400: art column collapsed at full width");
        }, timeoutSeconds: 120);

        Assert.True(failures.Count == 0, page + ": " + string.Join("; ", failures.Take(12)));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
}
