using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z4 — Make her yours. See the XAML header for the visual spec.
    ///
    /// <para>The only code here fires the interview spotlight's shimmer sweep — a ONE-SHOT on tab
    /// load, not a loop. The mockup's CSS animates it forever; the FX plan does not allow a second
    /// ambient loop on this tab, so it plays once when the card appears and once again whenever
    /// <see cref="PlayIntro"/> is called (e.g. after an interview completes).</para>
    /// </summary>
    public partial class MakeHerYoursView : UserControl
    {
        private bool _introPlayed;

        public MakeHerYoursView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public IMakeHerYoursVm? ViewModel
        {
            get => DataContext as IMakeHerYoursVm;
            set => DataContext = value;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _introPlayed) return;
            _introPlayed = true;
            // Normal, never Loaded — DispatcherPriority.Loaded is starved in this app.
            Dispatcher.BeginInvoke(new Action(PlayIntro), DispatcherPriority.Normal);
        }

        /// <summary>Sweeps the spotlight highlight across the interview card exactly once.</summary>
        public void PlayIntro()
        {
            try
            {
                if (!IsLoaded) return;
                if (TryFindResource("CmpShimmerSweepStoryboard") is not Storyboard proto) return;

                var sb = proto.Clone();
                // One-time ActualWidth read at Loaded — a value, not a binding.
                double travel = InterviewCard.ActualWidth > 1 ? InterviewCard.ActualWidth + 90 : 480;
                foreach (var tl in sb.Children)
                {
                    if (tl is not DoubleAnimation da) continue;
                    da.From = -90;
                    da.To = travel;
                    Storyboard.SetTarget(da, InterviewShimmerShift);
                }

                InterviewShimmer.Opacity = 1;
                sb.Begin(this);
            }
            catch (InvalidOperationException)
            {
                // Decorative only — never worth a crash.
            }
        }
    }
}
