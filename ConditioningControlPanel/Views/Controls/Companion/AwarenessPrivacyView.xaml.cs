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
        private IAwarenessPrivacyVm? _observed;

        public AwarenessPrivacyView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            DataContextChanged += (_, e) => Observe(e.NewValue as IAwarenessPrivacyVm);
            Unloaded += (_, _) => { StopCursorBlink(); Observe(null); };
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
            Observe(ViewModel);
            // Normal, never Loaded — DispatcherPriority.Loaded is starved in this app.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SyncCursorBlink();
                if (!_introPlayed) { _introPlayed = true; PlayIntro(); }
            }), DispatcherPriority.Normal);
        }

        /// <summary>
        /// Follows <see cref="IAwarenessPrivacyVm.IsWireLive"/>. The dial is a live control: turning
        /// her eyes on after the card has loaded has to start the cursor, and turning them off has
        /// to stop it, or the blink is decided once at load and then lies. Nothing was watching this
        /// before, which is also why a tab re-show could not resume it.
        /// </summary>
        private void Observe(IAwarenessPrivacyVm? vm)
        {
            if (ReferenceEquals(_observed, vm)) return;
            if (_observed != null) _observed.PropertyChanged -= OnVmPropertyChanged;
            _observed = vm;
            if (_observed != null) _observed.PropertyChanged += OnVmPropertyChanged;
        }

        private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IAwarenessPrivacyVm.IsWireLive)) return;

            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) { SyncCursorBlink(); return; }
            dispatcher.BeginInvoke(new Action(SyncCursorBlink), DispatcherPriority.Normal);
        }

        /// <summary>
        /// Starts or stops the blink to match the viewmodel. Public because the room calls it when
        /// the tab becomes visible again — this app hides tabs rather than unloading them.
        /// </summary>
        public void SyncCursorBlink()
        {
            if (ViewModel?.IsWireLive ?? false) StartCursorBlink();
            else StopCursorBlink();
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
