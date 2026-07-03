using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class EnhancementsTabView : UserControl
    {
        public EnhancementsTabView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Redirects vertical mouse wheel scrolling to horizontal scrolling for the skill tree —
        /// EXCEPT when the wheel is over a nested vertically-scrollable region (the tree-header
        /// panel with its stats/analytics expanders). There we yield so WPF's default wheel
        /// handling scrolls that inner viewer vertically; otherwise its content below the
        /// canvas fold would be unreachable.
        /// </summary>
        private void SkillTreeScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                var el = e.OriginalSource as DependencyObject;
                while (el != null && el != scrollViewer)
                {
                    if (el is ScrollViewer inner && inner.ScrollableHeight > 0)
                        return; // let the inner viewer take the wheel (vertical scroll)
                    el = el is Visual || el is System.Windows.Media.Media3D.Visual3D
                        ? VisualTreeHelper.GetParent(el)
                        : LogicalTreeHelper.GetParent(el);
                }

                // Scroll horizontally instead of vertically
                double offset = scrollViewer.HorizontalOffset - (e.Delta * 0.5);
                scrollViewer.ScrollToHorizontalOffset(offset);
                e.Handled = true;
            }
        }
    }
}
