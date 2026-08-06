using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z5 — What she can see. See the XAML header for the visual spec.
    ///
    /// <para>The dial itself is entirely declarative. This code-behind only runs the two decorative
    /// clocks: the wire view's cursor blink (alive only while the frame is live) and the dormant
    /// block's one-shot shimmer. Both stop on unload so a hidden tab is not still animating.</para>
    /// </summary>
    public partial class AwarenessPrivacyView : UserControl
    {
        private Storyboard? _blink;
        private bool _introPlayed;

        public AwarenessPrivacyView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += (_, _) => StopCursorBlink();
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public IAwarenessPrivacyVm? ViewModel
        {
            get => DataContext as IAwarenessPrivacyVm;
            set => DataContext = value;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            // Normal, never Loaded — DispatcherPriority.Loaded is starved in this app.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ViewModel?.IsWireLive ?? false) StartCursorBlink();
                if (!_introPlayed) { _introPlayed = true; PlayIntro(); }
            }), DispatcherPriority.Normal);
        }

        /// <summary>Starts the wire cursor blink. Idempotent; a no-op when the frame is not live.</summary>
        public void StartCursorBlink()
        {
            try
            {
                if (!IsLoaded || _blink != null) return;
                if (TryFindResource("CmpCursorBlinkStoryboard") is not Storyboard proto) return;

                var sb = proto.Clone();
                foreach (var tl in sb.Children) Storyboard.SetTarget(tl, WireCursor);
                sb.Begin(this, isControllable: true);
                _blink = sb;
            }
            catch (InvalidOperationException)
            {
                _blink = null;
            }
        }

        /// <summary>Stops the cursor blink and leaves the cursor visible.</summary>
        public void StopCursorBlink()
        {
            try { _blink?.Stop(this); }
            catch (InvalidOperationException) { /* already torn down */ }
            finally { _blink = null; }
        }

        /// <summary>Sweeps the dormant block's shimmer once. No-op when Train 2 is live.</summary>
        public void PlayIntro()
        {
            try
            {
                if (!IsLoaded) return;
                if (!(ViewModel?.IsDormant ?? false)) return;
                if (TryFindResource("CmpShimmerSweepStoryboard") is not Storyboard proto) return;

                var sb = proto.Clone();
                // One-time ActualWidth read at Loaded — a value, not a binding.
                double travel = DormantHost.ActualWidth > 1 ? DormantHost.ActualWidth + 90 : 420;
                foreach (var tl in sb.Children)
                {
                    if (tl is not DoubleAnimation da) continue;
                    da.From = -90;
                    da.To = travel;
                    Storyboard.SetTarget(da, DormantShimmerShift);
                }

                DormantShimmer.Opacity = 1;
                sb.Begin(this);
            }
            catch (InvalidOperationException)
            {
                // Decorative only.
            }
        }
    }
}
