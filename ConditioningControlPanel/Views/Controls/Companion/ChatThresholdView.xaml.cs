using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z2 — the chat threshold surface. See the XAML header for the visual spec.
    ///
    /// <para>The only behaviour here is Enter-to-send and the "she's thinking" dot pulse. Sending
    /// itself belongs to the viewmodel, which routes through <c>CompanionBrain.SendChatAsync</c>
    /// exactly like the tube box — same moderation, same single-flight, same "still thinking"
    /// phrase on queue overflow.</para>
    /// </summary>
    public partial class ChatThresholdView : UserControl
    {
        private readonly List<Storyboard> _dotClocks = new();

        public ChatThresholdView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Unloaded += (_, _) => StopThinking();
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public IChatThresholdVm? ViewModel
        {
            get => DataContext as IChatThresholdVm;
            set => DataContext = value;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is IChatThresholdVm old) old.PropertyChanged -= OnVmPropertyChanged;
            if (e.NewValue is IChatThresholdVm fresh)
            {
                fresh.PropertyChanged += OnVmPropertyChanged;
                SyncThinking(fresh.IsThinking);
            }
        }

        private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IChatThresholdVm.IsThinking)) return;
            if (sender is not IChatThresholdVm vm) return;

            // Event handlers can outlive the window they were attached from (Known Issues #8).
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() => SyncThinking(vm.IsThinking)), DispatcherPriority.Normal);
        }

        private void SyncThinking(bool thinking)
        {
            if (thinking) StartThinking();
            else StopThinking();
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
