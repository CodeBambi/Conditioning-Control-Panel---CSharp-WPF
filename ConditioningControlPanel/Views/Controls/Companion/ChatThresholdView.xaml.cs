using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z2 — the chat threshold surface. See the XAML header for the visual spec.
    ///
    /// <para>The behaviour here is deliberately thin — Enter-to-send, the "she's thinking" dot
    /// pulse, keeping the thread pinned to its newest line, and the dormant state's one-shot
    /// shimmer. Sending itself belongs to the viewmodel, which routes through
    /// <c>CompanionBrain.SendChatAsync</c> exactly like the tube box — same moderation, same
    /// single-flight, same "still thinking" phrase on queue overflow.</para>
    ///
    /// <para>Animation budget (FX plan): nothing here loops except the thinking dots, and those
    /// live only while a send is in flight. The tab's single ambient loop is the hero portrait.</para>
    /// </summary>
    public partial class ChatThresholdView : UserControl
    {
        private readonly List<Storyboard> _dotClocks = new();
        private INotifyCollectionChanged? _watchedTurns;
        private bool _shimmerPlayed;

        public ChatThresholdView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public IChatThresholdVm? ViewModel
        {
            get => DataContext as IChatThresholdVm;
            set => DataContext = value;
        }

        // =====================================================================================
        //  lifetime
        // =====================================================================================

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SyncThinking(ViewModel?.IsThinking ?? false);
            ScrollThreadToEnd();
            PlayDormantShimmer();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            StopThinking();
            WatchTurns(null);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is IChatThresholdVm old) old.PropertyChanged -= OnVmPropertyChanged;
            if (e.NewValue is IChatThresholdVm fresh)
            {
                fresh.PropertyChanged += OnVmPropertyChanged;
                WatchTurns(fresh.Turns as INotifyCollectionChanged);
                SyncThinking(fresh.IsThinking);
                ScrollThreadToEnd();
            }
            else
            {
                WatchTurns(null);
            }
        }

        private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not IChatThresholdVm vm) return;

            // Event handlers can outlive the window they were attached from (Known Issues #8).
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && dispatcher.HasShutdownStarted) return;

            switch (e.PropertyName)
            {
                case nameof(IChatThresholdVm.IsThinking):
                    Post(() => SyncThinking(vm.IsThinking));
                    break;
                case nameof(IChatThresholdVm.Turns):
                    Post(() =>
                    {
                        WatchTurns(vm.Turns as INotifyCollectionChanged);
                        ScrollThreadToEnd();
                    });
                    break;
                case nameof(IChatThresholdVm.State):
                    Post(PlayDormantShimmer);
                    break;
            }
        }

        /// <summary>
        /// Queues work at Normal priority. Loaded priority is starved in this app (see the memory
        /// on DispatcherPriority), so every deferred UI touch on this page uses Normal.
        /// </summary>
        private void Post(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) { action(); return; }
            dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
        }

        // =====================================================================================
        //  the thread stays pinned to her newest line
        // =====================================================================================

        /// <summary>Follows a live thread whose collection mutates in place (the wired-up case).</summary>
        private void WatchTurns(INotifyCollectionChanged? turns)
        {
            if (ReferenceEquals(_watchedTurns, turns)) return;
            if (_watchedTurns != null) _watchedTurns.CollectionChanged -= OnTurnsCollectionChanged;
            _watchedTurns = turns;
            if (_watchedTurns != null) _watchedTurns.CollectionChanged += OnTurnsCollectionChanged;
        }

        private void OnTurnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => Post(ScrollThreadToEnd);

        /// <summary>
        /// Keeps the newest bubble visible. The thread lives in a templated ItemsControl, so the
        /// ScrollViewer only exists once the template is applied — hence the deferred, tolerant
        /// lookup rather than an x:Name reference.
        /// </summary>
        public void ScrollThreadToEnd()
        {
            Post(() =>
            {
                try
                {
                    if (ThreadList == null) return;
                    ThreadList.ApplyTemplate();
                    ThreadList.UpdateLayout();
                    FindDescendant<ScrollViewer>(ThreadList)?.ScrollToEnd();
                }
                catch (InvalidOperationException)
                {
                    // Layout torn down under us — the thread simply stays where it is.
                }
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

        // =====================================================================================
        //  motion
        // =====================================================================================

        /// <summary>
        /// The pre-Train-1 promise card's shimmer: one sweep, on load, and only in the dormant
        /// state. Never a loop — the FX plan spends this tab's only ambient budget on the hero.
        /// </summary>
        public void PlayDormantShimmer()
        {
            try
            {
                if (!IsLoaded || _shimmerPlayed) return;
                if (ViewModel is not { State: CompanionZoneState.Dormant }) return;
                if (TryFindResource("CmpShimmerSweepStoryboard") is not Storyboard proto) return;

                var sb = proto.Clone();
                // One-time ActualWidth read at Loaded — a value, not a binding, so nothing thrashes.
                double travel = DormantHost.ActualWidth > 1 ? DormantHost.ActualWidth + 90 : 480;
                foreach (var tl in sb.Children)
                {
                    if (tl is not DoubleAnimation da) continue;
                    da.From = -90;
                    da.To = travel;
                    Storyboard.SetTarget(da, DormantShimmerShift);
                }

                DormantShimmer.Opacity = 1;
                sb.Begin(this);
                _shimmerPlayed = true;
            }
            catch (InvalidOperationException)
            {
                // Decorative only — never worth a crash.
            }
        }

        /// <summary>Starts the three-dot pulse. Triggered, not ambient: it stops the moment the
        /// reply lands, so the page is back to exactly one running loop (the hero portrait).</summary>
        public void StartThinking()
        {
            try
            {
                if (!IsLoaded || _dotClocks.Count > 0) return;
                if (TryFindResource("CmpThinkingDotsStoryboard") is not Storyboard proto) return;

                var dots = new UIElement[] { Dot1, Dot2, Dot3 };
                for (int i = 0; i < dots.Length; i++)
                {
                    var sb = proto.Clone();
                    foreach (var tl in sb.Children)
                    {
                        if (tl is not DoubleAnimation da) continue;
                        da.BeginTime = TimeSpan.FromMilliseconds(150 * i);
                        Storyboard.SetTarget(da, dots[i]);
                    }
                    sb.Begin(this, isControllable: true);
                    _dotClocks.Add(sb);
                }
            }
            catch (InvalidOperationException)
            {
                StopThinking();
            }
        }

        /// <summary>Stops the pulse and restores full opacity.</summary>
        public void StopThinking()
        {
            foreach (var sb in _dotClocks)
            {
                try { sb.Stop(this); }
                catch (InvalidOperationException) { /* already torn down */ }
            }
            _dotClocks.Clear();
            Dot1.Opacity = Dot2.Opacity = Dot3.Opacity = 1.0;
        }

        private void SyncThinking(bool thinking)
        {
            if (thinking)
            {
                StartThinking();
                ScrollThreadToEnd();
            }
            else
            {
                StopThinking();
            }
        }

        // =====================================================================================
        //  input
        // =====================================================================================

        /// <summary>Enter sends; Shift+Enter is left alone so a future multi-line box still works.</summary>
        private void DraftBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;

            var vm = ViewModel;
            if (vm == null || !vm.CanSend) return;
            if (string.IsNullOrWhiteSpace(vm.Draft)) return;
            if (vm.SendCommand.CanExecute(null)) vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
