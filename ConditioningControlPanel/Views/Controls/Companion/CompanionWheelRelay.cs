using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Keeps the page scrollable over the three bounded lists inside it.
    ///
    /// <para><b>The problem.</b> "Her Room" is one long <c>PageScroll</c>, and three zones put a
    /// height-capped, internally scrolling list inside it: the fact wall (Z3, 420px), the chat
    /// thread (Z2, 260px) and the live-actions feed (Z7, 140px). WPF's
    /// <c>ScrollViewer.OnMouseWheel</c> sets <c>e.Handled = true</c> unconditionally — even when it
    /// has nothing left to scroll, even when it has no scrollable height at all — so a wheel notch
    /// over any of them never reaches the page. A user reading the diary and spinning down to the
    /// Engine Room simply stops dead, with no visible reason.</para>
    ///
    /// <para><b>The fix.</b> The house pattern elsewhere in this app (LeaderboardTabView,
    /// EnhancementsTabView, AssetsTabView, AvailableSubjectsTabView) is a
    /// <c>PreviewMouseWheel</c> forwarder. This is that pattern, extracted once so all three lists
    /// share it: the inner viewer keeps the notch while it can still move in that direction, and
    /// otherwise the event is re-raised as a bubbling <c>MouseWheelEvent</c> on the host, from
    /// where it bubbles past the inner viewer and into <c>PageScroll</c>.</para>
    ///
    /// <para>The re-raised event is <c>MouseWheelEvent</c>, not the preview, so this handler cannot
    /// re-enter itself.</para>
    /// </summary>
    internal static class CompanionWheelRelay
    {
        /// <summary>
        /// Wires <paramref name="host"/> (an ItemsControl whose template contains the bounded
        /// ScrollViewer) so unusable notches pass through. Idempotent.
        /// </summary>
        public static void Attach(FrameworkElement? host)
        {
            if (host == null) return;
            host.PreviewMouseWheel -= OnPreviewMouseWheel;
            host.PreviewMouseWheel += OnPreviewMouseWheel;
        }

        /// <summary>Removes the forwarder. Safe to call when it was never attached.</summary>
        public static void Detach(FrameworkElement? host)
        {
            if (host == null) return;
            host.PreviewMouseWheel -= OnPreviewMouseWheel;
        }

        /// <summary>
        /// Whether a notch of <paramref name="delta"/> is wasted on a viewer at
        /// <paramref name="verticalOffset"/> with <paramref name="scrollableHeight"/> left to give.
        ///
        /// <para>Its own pure function because it is the whole decision and the only part of this
        /// that a test can see: a mouse wheel over a real ScrollViewer is not something the suite
        /// can synthesise, but "list already at the bottom, user scrolls down" is.</para>
        /// </summary>
        public static bool ShouldForward(double scrollableHeight, double verticalOffset, int delta)
        {
            if (delta == 0) return false;
            if (scrollableHeight <= 0.0) return true;                       // nothing to scroll at all
            if (delta < 0) return verticalOffset >= scrollableHeight - 0.5;  // already at the end
            return verticalOffset <= 0.5;                                    // already at the top
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || sender is not FrameworkElement host) return;

            var inner = FindDescendant<ScrollViewer>(host);
            if (inner == null) return;
            if (!ShouldForward(inner.ScrollableHeight, inner.VerticalOffset, e.Delta)) return;

            // Take the notch away from the inner viewer and hand it to whatever is above the host.
            e.Handled = true;
            host.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = host
            });
        }

        private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) return hit;
                var deeper = FindDescendant<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }
}
