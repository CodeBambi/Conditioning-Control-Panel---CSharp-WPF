using System;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Velvet Kit 2 · round 3. Collapses the side-art column of a feature page when the page is
    /// too narrow to afford it.
    ///
    /// <para><b>Why this exists.</b> Every feature page is authored once and hosted twice: in the
    /// Studio detail pane, which is ~1000-1100px wide, and in
    /// <c>Features/FeaturePopupWindow.xaml</c>, which is 520px. Round 3 caps the settings column
    /// (<c>3*</c>, <c>MaxWidth="780"</c>) so a slider stops being a 1200px runway in the wide pane
    /// and fills the width it gives back with one big art card (<c>2*</c>). At 520px there is no
    /// width to give back: the art would squeeze the settings into a gutter. So the last column
    /// goes to zero and the art card hides, and the settings retake the full width.</para>
    ///
    /// <para><b>Usage.</b> Put <c>feat:AdaptiveSideArt.CollapseBelow="700"</c> on the wrapping
    /// Grid. The art column is the Grid's LAST <see cref="ColumnDefinition"/>, and the art card is
    /// whatever child sits in it - no names, no per-page wiring.</para>
    ///
    /// <para><b>Deliberately dumb.</b> No storyboards, no animation, no attached behaviour object:
    /// the quiet-surface rule says a layout that reacts to a resize must not also perform. It sets
    /// a GridLength and a Visibility, and it is idempotent.</para>
    /// </summary>
    public static class AdaptiveSideArt
    {
        /// <summary>
        /// Width (in DIPs, measured on the Grid itself) below which the last column collapses.
        /// <see cref="double.NaN"/> - the default - means "never collapse".
        /// </summary>
        public static readonly DependencyProperty CollapseBelowProperty =
            DependencyProperty.RegisterAttached(
                "CollapseBelow", typeof(double), typeof(AdaptiveSideArt),
                new PropertyMetadata(double.NaN, OnCollapseBelowChanged));

        public static void SetCollapseBelow(DependencyObject element, double value)
            => element?.SetValue(CollapseBelowProperty, value);

        public static double GetCollapseBelow(DependencyObject element)
            => element == null ? double.NaN : (double)element.GetValue(CollapseBelowProperty);

        /// <summary>
        /// The column's authored width, stashed on first collapse so the restore is exact even if
        /// a page ever uses something other than <c>2*</c>.
        /// </summary>
        private static readonly DependencyProperty OriginalWidthProperty =
            DependencyProperty.RegisterAttached(
                "OriginalWidth", typeof(object), typeof(AdaptiveSideArt),
                new PropertyMetadata(null));

        private static void OnCollapseBelowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Grid grid) return;

            // Idempotent: -= before += so a re-set of the property cannot double-subscribe.
            grid.SizeChanged -= OnGridSizeChanged;
            grid.Loaded -= OnGridLoaded;

            if (e.NewValue is not double threshold || double.IsNaN(threshold)) return;

            grid.SizeChanged += OnGridSizeChanged;
            grid.Loaded += OnGridLoaded;

            // Apply immediately for the case where the Grid is already measured (a page that gets
            // re-shown, or a designer/harness load).
            Apply(grid);
        }

        private static void OnGridLoaded(object sender, RoutedEventArgs e) => Apply(sender as Grid);

        private static void OnGridSizeChanged(object sender, SizeChangedEventArgs e) => Apply(sender as Grid);

        private static void Apply(Grid? grid)
        {
            try
            {
                if (grid == null) return;

                double threshold = GetCollapseBelow(grid);
                if (double.IsNaN(threshold)) return;

                var cols = grid.ColumnDefinitions;
                if (cols == null || cols.Count < 2) return;   // one-column page: nothing to collapse

                double width = grid.ActualWidth;
                if (width <= 0) return;                       // not measured yet - leave as authored

                var col = cols[cols.Count - 1];
                if (col == null) return;

                if (col.GetValue(OriginalWidthProperty) is not GridLength)
                    col.SetValue(OriginalWidthProperty, col.Width);

                bool collapse = width < threshold;
                var target = collapse
                    ? new GridLength(0)
                    : (col.GetValue(OriginalWidthProperty) is GridLength stashed ? stashed : new GridLength(2, GridUnitType.Star));

                if (col.Width != target) col.Width = target;

                int index = cols.Count - 1;
                foreach (var child in grid.Children)
                {
                    if (child is not UIElement ui) continue;
                    if (Grid.GetColumn(ui) != index) continue;

                    var want = collapse ? Visibility.Collapsed : Visibility.Visible;
                    if (ui.Visibility != want) ui.Visibility = want;
                }
            }
            catch (Exception ex)
            {
                // A layout helper must never take the page down with it.
                App.Logger?.Debug("AdaptiveSideArt apply failed: {E}", ex.Message);
            }
        }
    }
}
