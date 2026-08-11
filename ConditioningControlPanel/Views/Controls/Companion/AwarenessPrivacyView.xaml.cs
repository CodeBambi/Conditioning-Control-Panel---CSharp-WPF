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
        /// <summary>
        /// How often the wire view re-reads while the card is on screen. Matches the observer's own
        /// poll: a trust surface that lags the thing it is describing is a trust surface that is wrong,
        /// and anything faster would be redrawing between the observer's own ticks.
        /// </summary>
        public static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(1500);

        private Storyboard? _blink;
        private bool _introPlayed;
        private IAwarenessPrivacyVm? _observed;
        private DispatcherTimer? _refresh;

        public AwarenessPrivacyView()
        {
            InitializeComponent();
            WipeConfirm = new MemoryForgetConfirm();
            Loaded += OnLoaded;
            DataContextChanged += (_, e) =>
            {
                Observe(e.NewValue as IAwarenessPrivacyVm);
                WipeConfirm.Bind((e.NewValue as IAwarenessPrivacyVm)?.WipeCommand);
            };
            Unloaded += (_, _) =>
            {
                StopCursorBlink();
                StopRefresh();
                WipeConfirm.Disarm();
                Observe(null);
            };
        }

        /// <summary>
        /// The wipe's two-step, in the same inline shape the memory diary uses: the destructive command
        /// runs only from <c>ConfirmCommand</c>, only while armed, and re-binding always disarms. This
        /// erases everything she has noticed, so it may never be one click.
        /// </summary>
        public MemoryForgetConfirm WipeConfirm { get; }

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
            WipeConfirm.Bind(ViewModel?.WipeCommand);
            StartRefresh();
            // Normal, never Loaded — DispatcherPriority.Loaded is starved in this app.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SyncCursorBlink();
                if (!_introPlayed) { _introPlayed = true; PlayIntro(); }
            }), DispatcherPriority.Normal);
        }

        /// <summary>
        /// Arms the wire view's refresh while the card is on screen. Idempotent, and deliberately
        /// visible-only: this is a readout, not a scheduler, and nothing about awareness's own
        /// lifecycle — recording, pruning, reacting — depends on it ticking.
        /// </summary>
        public void StartRefresh()
        {
            if (_refresh != null) return;

            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            _refresh = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = RefreshInterval };
            _refresh.Tick += OnRefreshTick;
            _refresh.Start();
        }

        /// <summary>Disarms the refresh. Safe to call when it was never started.</summary>
        public void StopRefresh()
        {
            if (_refresh == null) return;
            _refresh.Stop();
            _refresh.Tick -= OnRefreshTick;
            _refresh = null;
        }

        private void OnRefreshTick(object? sender, EventArgs e)
        {
            try
            {
                if (Application.Current?.Dispatcher?.HasShutdownStarted == true) { StopRefresh(); return; }
                if (!IsLoaded) { StopRefresh(); return; }

                // The runtime viewmodel is the only one with anything to re-read; the mocks are static
                // exhibits and asking them to sync would be a no-op with an interface change attached.
                (DataContext as Runtime.AwarenessPrivacyRuntimeVm)?.Sync();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Awareness panel refresh failed: {E}", ex.Message);
            }
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
