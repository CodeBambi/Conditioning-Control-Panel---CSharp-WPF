using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z1 bottom band — the relationship constellation. See the XAML header for the visual spec.
    ///
    /// <para>All this code-behind does is fire the band's one-shot intro: a staggered twinkle across
    /// the earned nodes when the strip is live, or the shimmer sweep across the promise block when
    /// it is dormant. Neither loops — the tab's single Forever storyboard belongs to the hero
    /// portrait, and a second one here would break the FX budget for the whole page.</para>
    ///
    /// <para><see cref="PlayIntro"/> is public and re-runnable on purpose: the host replays it on a
    /// stage-up so the newly-current node pops, which is the moment the whole ratchet exists for.</para>
    /// </summary>
    public partial class RelationshipConstellation : UserControl
    {
        /// <summary>Gap between consecutive node pops, in seconds. Reads as a twinkle, not a wave.</summary>
        private const double TwinkleStaggerSeconds = 0.09;

        private bool _introPlayed;

        public RelationshipConstellation()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public IRelationshipConstellationVm? ViewModel
        {
            get => DataContext as IRelationshipConstellationVm;
            set => DataContext = value;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Animations only ever start from a loaded, templated tree (Known Issues #2), and the
            // auto-intro is one-shot: re-entering the tab must not stack a second sweep.
            if (!IsLoaded || _introPlayed) return;
            _introPlayed = true;

            // Normal, never Loaded — DispatcherPriority.Loaded is starved in this app.
            Dispatcher.BeginInvoke(new Action(PlayIntro), System.Windows.Threading.DispatcherPriority.Normal);
        }

        /// <summary>
        /// Plays the band's intro: the staggered node twinkle when live, the dormant shimmer sweep
        /// when not. Safe to call before the visual tree is measured — the sweep distance falls back
        /// to a sane constant when ActualWidth is still zero, and a strip whose containers have not
        /// been generated simply skips its pops rather than throwing.
        /// </summary>
        public void PlayIntro()
        {
            if (!IsLoaded) return;

            if (ViewModel?.IsLive == true) PlayNodeTwinkle();
            else PlayDormantShimmer();
        }

        /// <summary>The dormant flourish: one skewed highlight band walked across the promise block.</summary>
        private void PlayDormantShimmer()
        {
            try
            {
                if (TryFindResource("CmpShimmerSweepStoryboard") is not Storyboard proto) return;

                var sb = proto.Clone();
                // One-time ActualWidth read at Loaded — a value, not a binding, so nothing thrashes.
                double travel = DormantHost.ActualWidth > 1 ? DormantHost.ActualWidth + 90 : 620;
                foreach (var tl in sb.Children)
                {
                    if (tl is not DoubleAnimation da) continue;
                    da.From = -90;
                    da.To = travel;
                    // Storyboard.Begin only accepts a FrameworkElement as its scope, so the
                    // transform is addressed by object rather than by name.
                    Storyboard.SetTarget(da, DormantShimmerShift);
                }

                DormantShimmer.Opacity = 1;
                sb.Begin(this);
            }
            catch (InvalidOperationException)
            {
                // A storyboard against a not-yet-namescoped target is never worth a crash here.
            }
        }

        /// <summary>
        /// The live flourish: every node pops once, left to right, a beat apart. Future nodes twinkle
        /// too — the ladder should read as one object, not as two groups.
        /// </summary>
        private void PlayNodeTwinkle()
        {
            try
            {
                if (TryFindResource("CmpNodePulseStoryboard") is not Storyboard proto) return;

                // Containers only exist after a layout pass; without one there is nothing to animate
                // and nothing to fail on either.
                NodeStrip.UpdateLayout();

                int count = NodeStrip.Items.Count;
                for (int i = 0; i < count; i++)
                {
                    if (NodeStrip.ItemContainerGenerator.ContainerFromIndex(i) is not DependencyObject container)
                        continue;
                    if (FindNodeScale(container) is not ScaleTransform scale) continue;

                    var sb = proto.Clone();
                    foreach (var tl in sb.Children)
                    {
                        if (tl is not DoubleAnimation da) continue;
                        da.BeginTime = TimeSpan.FromSeconds(i * TwinkleStaggerSeconds);
                        Storyboard.SetTarget(da, scale);
                    }
                    sb.Begin(this);
                }
            }
            catch (InvalidOperationException)
            {
                // Same rule as the sweep: decoration never takes the tab down.
            }
        }

        /// <summary>
        /// Finds the disc's own <see cref="ScaleTransform"/> inside a generated node container.
        /// The node lives in a ControlTemplate, so its name is not reachable from this namescope —
        /// a shallow visual walk for the element named "Node" is the cheap, safe way in.
        /// </summary>
        private static ScaleTransform? FindNodeScale(DependencyObject root)
        {
            if (root is FrameworkElement fe &&
                string.Equals(fe.Name, "Node", StringComparison.Ordinal))
            {
                return fe.RenderTransform as ScaleTransform;
            }

            int children = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < children; i++)
            {
                var found = FindNodeScale(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }
    }
}
