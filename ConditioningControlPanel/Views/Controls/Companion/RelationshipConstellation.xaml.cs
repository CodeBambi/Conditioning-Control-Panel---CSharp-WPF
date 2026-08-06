using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z1 bottom band — the relationship constellation. See the XAML header for the visual spec.
    ///
    /// <para>All this code-behind does is fire the two one-shot animations the design allows: the
    /// dormant shimmer sweep and the current node's pop. Neither loops — the tab's single Forever
    /// storyboard belongs to the hero portrait.</para>
    /// </summary>
    public partial class RelationshipConstellation : UserControl
    {
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
            // intro is one-shot: re-entering the tab must not stack a second sweep.
            if (!IsLoaded || _introPlayed) return;
            _introPlayed = true;

            // Normal, never Loaded — DispatcherPriority.Loaded is starved in this app.
            Dispatcher.BeginInvoke(new Action(PlayIntro), System.Windows.Threading.DispatcherPriority.Normal);
        }

        /// <summary>
        /// Plays the dormant shimmer sweep. Safe to call before the visual tree is measured: the
        /// sweep distance falls back to a sane constant when ActualWidth is still zero.
        /// </summary>
        public void PlayIntro()
        {
            try
            {
                if (!IsLoaded) return;
                var vm = ViewModel;
                if (vm != null && vm.IsLive) return; // the sweep is the dormant state's flourish

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
    }
}
