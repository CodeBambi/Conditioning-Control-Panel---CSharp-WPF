using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// Z2 — the chat threshold surface, ported from the WPF head. See the XAML header for the spec.
    ///
    /// <para>The behaviour is deliberately thin: Enter-to-send, keeping the thread pinned to its
    /// newest line, and the dormant state's one-shot shimmer. The "she's thinking" dot pulse is a
    /// class-gated Style animation in the XAML, so the two Storyboards the WPF code-behind cloned
    /// (<c>CmpThinkingDotsStoryboard</c>, <c>CmpShimmerSweepStoryboard</c>) have no counterpart
    /// here; the shimmer is a <see cref="DoubleTransition"/> exactly as <see cref="MakeHerYoursView"/>
    /// does it.</para>
    ///
    /// <para>Not done, and not stubbed: <c>CompanionWheelRelay.Attach(ThreadList)</c> - a WPF helper
    /// on the routed MouseWheel event that has not been ported, so a wheel notch over the capped
    /// thread may be eaten by it instead of reaching the page.</para>
    ///
    /// <para>Sending is the viewmodel's. The WPF <c>IChatThresholdVm</c> is routed through
    /// <c>CompanionBrain.SendChatAsync</c>; here <see cref="ChatThresholdViewModel"/> is the mock's
    /// artboard (append your line, raise IsThinking) with no transport behind it.</para>
    /// </summary>
    public partial class ChatThresholdView : UserControl
    {
        private INotifyCollectionChanged? _watchedTurns;
        private bool _shimmerPlayed;

        public ChatThresholdView()
        {
            InitializeComponent();
            DataContext = new ChatThresholdViewModel();
            DataContextChanged += (_, _) => WatchTurns((DataContext as ChatThresholdViewModel)?.Turns);
            Loaded += OnLoaded;
            Unloaded += (_, _) => WatchTurns(null);
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public ChatThresholdViewModel? ViewModel
        {
            get => DataContext as ChatThresholdViewModel;
            set => DataContext = value;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            WatchTurns(ViewModel?.Turns);
            ScrollThreadToEnd();
            PlayDormantShimmer();
        }

        /// <summary>Follows a live thread whose collection mutates in place.</summary>
        private void WatchTurns(INotifyCollectionChanged? turns)
        {
            if (ReferenceEquals(_watchedTurns, turns)) return;
            if (_watchedTurns != null) _watchedTurns.CollectionChanged -= OnTurnsChanged;
            _watchedTurns = turns;
            if (_watchedTurns != null) _watchedTurns.CollectionChanged += OnTurnsChanged;
        }

        private void OnTurnsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollThreadToEnd();

        /// <summary>
        /// Keeps the newest bubble visible. The ScrollViewer only exists once the ItemsControl's
        /// template is applied - hence the deferred, tolerant lookup. Normal priority, never
        /// Loaded: Loaded-priority work is starved in this app.
        /// </summary>
        public void ScrollThreadToEnd()
        {
            Dispatcher.UIThread.Post(() =>
            {
                try { ThreadList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToEnd(); }
                catch (InvalidOperationException) { /* layout torn down under us */ }
            }, DispatcherPriority.Normal);
        }

        /// <summary>
        /// The pre-Train-1 promise card's shimmer: one sweep, on load, and only in the dormant
        /// state. Never a loop - the FX plan spends this tab's only ambient budget on the hero.
        /// </summary>
        public void PlayDormantShimmer()
        {
            if (_shimmerPlayed || !IsLoaded) return;
            if (ViewModel is not { State: ChatThresholdViewModel.ZoneState.Dormant }) return;
            if (DormantShimmer.RenderTransform is not TransformGroup group) return;
            var shift = group.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (shift is null) return;

            // One-time Bounds read at Loaded - a value, not a binding, so nothing thrashes.
            double travel = DormantHost.Bounds.Width > 1 ? DormantHost.Bounds.Width + 90 : 480;
            shift.Transitions = null;
            shift.X = -90;
            shift.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = TranslateTransform.XProperty,
                    Duration = TimeSpan.FromSeconds(1.4),
                    Easing = new CubicEaseInOut()
                }
            };
            DormantShimmer.Opacity = 1;
            shift.X = travel;
            _shimmerPlayed = true;
        }

        /// <summary>Enter sends; Shift+Enter is left alone so a future multi-line box still works.</summary>
        private void DraftBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;

            var vm = ViewModel;
            if (vm == null || !vm.CanSend) return;
            if (string.IsNullOrWhiteSpace(vm.Draft)) return;
            if (vm.SendCommand.CanExecute(null)) vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// The view's viewmodel: the WPF <c>IChatThresholdVm</c> contract with <c>MockChatThresholdVm</c>'s
    /// Live artboard as its default data, so the view renders a real her / you / echo thread with
    /// an AI badge. <b>Not a port of the interface</b> - that, the mock and the zone-state enum live
    /// in the WPF head beside the other zones' contracts, and a shared copy here would collide with
    /// whichever sibling port lands its zone first. The enums are nested for the same reason.
    /// </summary>
    public sealed class ChatThresholdViewModel : INotifyPropertyChanged
    {
        public enum ZoneState { Live, Dormant, Locked, Empty, Disabled }

        private readonly Relay _send;
        private string _draft = string.Empty;
        private bool _isThinking;

        public ChatThresholdViewModel()
        {
            // WPF's CommandManager.RequerySuggested re-polled CanExecute for free; Avalonia only
            // re-polls on CanExecuteChanged, so Draft and IsThinking raise it by hand.
            SendCommand = _send = new Relay(Send, () => CanSend && !IsThinking && !string.IsNullOrWhiteSpace(Draft));
            OpenFullChatCommand = new Relay(() => { });     // ponytail: needs the tube chat, wired when it is ported
            HistoryCommand = new Relay(() => { });          // ponytail: needs the transcript viewer, wired when it is ported
            UnlockCommand = new Relay(() => { });           // ponytail: needs the Patreon tab, wired when it is ported
            OpenEngineRoomCommand = new Relay(() => { });   // ponytail: needs the room's RevealEngineRoom, wired when the page is composed
        }

        public ZoneState State { get; init; } = ZoneState.Live;

        /// <summary>The last ~3 real turns, oldest first. Observable so the view follows a growing thread.</summary>
        public ObservableCollection<ChatBubble> Turns { get; } = new(LiveThread());

        /// <summary>Static fake bubbles rendered under the veil. Never live content.</summary>
        public IReadOnlyList<ChatBubble> TeaserTurns { get; init; } = StagedTeaser();

        public string Draft
        {
            get => _draft;
            set { if (_draft != value) { _draft = value; Raise(); _send.RaiseCanExecuteChanged(); } }
        }

        public bool IsThinking
        {
            get => _isThinking;
            set { if (_isThinking != value) { _isThinking = value; Raise(); _send.RaiseCanExecuteChanged(); } }
        }

        public bool CanSend { get; init; } = true;

        public string LastHeardCopy { get; init; } = Loc.GetF("companion_chat_last_heard_fmt", "2h ago");
        public string FooterCopy { get; init; } = Loc.Get("companion_chat_footer_remembers");
        public string StateCopy { get; init; } = string.Empty;
        public string LockCopy { get; init; } = Loc.Get("companion_chat_lock_copy");
        public string LockCtaLabel { get; init; } = Loc.Get("companion_chat_lock_cta");
        public string InputPlaceholder { get; init; } = Loc.Get("companion_chat_input_placeholder");

        public ICommand SendCommand { get; }
        public ICommand OpenFullChatCommand { get; }
        public ICommand HistoryCommand { get; }
        public ICommand UnlockCommand { get; }
        public ICommand OpenEngineRoomCommand { get; }

        /// <summary>Appends your line and puts her into "thinking". Nothing is sent anywhere.</summary>
        // ponytail: needs CompanionBrain.SendChatAsync, wired when it moves to Core
        public void Send()
        {
            if (!SendCommand.CanExecute(null)) return;
            Turns.Add(new ChatBubble(ChatBubble.BubbleKind.You, Draft.Trim(), timestamp: "just now"));
            Draft = string.Empty;
            IsThinking = true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // The WPF mock's artboard, verbatim: AI badges on her model turns only, never on the echo.
        private static IEnumerable<ChatBubble> LiveThread() => new[]
        {
            new ChatBubble(ChatBubble.BubbleKind.Echo, "said aloud: “the rabbit hole. every bubble's a little gift…”", timestamp: "3h ago"),
            new ChatBubble(ChatBubble.BubbleKind.Her, "level 41 already?? remember when the spiral scared you, princess~", isAi: true, timestamp: "2h ago"),
            new ChatBubble(ChatBubble.BubbleKind.You, "it still does a little", timestamp: "2h ago"),
            new ChatBubble(ChatBubble.BubbleKind.Her, "good. it should~ 💕", isAi: true, timestamp: "2h ago")
        };

        private static IReadOnlyList<ChatBubble> StagedTeaser() => new[]
        {
            new ChatBubble(ChatBubble.BubbleKind.Her, "mmm I was just thinking about you~"),
            new ChatBubble(ChatBubble.BubbleKind.You, "you were?"),
            new ChatBubble(ChatBubble.BubbleKind.Her, "always, princess. now about that streak…")
        };

        private sealed class Relay : ICommand
        {
            private readonly Action _run;
            private readonly Func<bool>? _can;
            public Relay(Action run, Func<bool>? can = null) { _run = run; _can = can; }
            public bool CanExecute(object? p) => _can?.Invoke() ?? true;
            public void Execute(object? p) => _run();
            public event EventHandler? CanExecuteChanged;
            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>One bubble in the thread - the WPF <c>IChatBubbleVm</c> shape, plus the three
    /// kind flags the template's classes bind to.</summary>
    public sealed class ChatBubble
    {
        public enum BubbleKind { Her, You, Echo }

        public ChatBubble(BubbleKind kind, string text, bool isAi = false, string? timestamp = null,
            string? linkTitle = null, ICommand? openLink = null)
        {
            Kind = kind; Text = text; IsAiGenerated = isAi; Timestamp = timestamp;
            LinkTitle = linkTitle; OpenLinkCommand = openLink;
        }

        public BubbleKind Kind { get; }
        public string Text { get; }
        /// <summary>INVARIANT: true only for a genuine model completion. Never a bark, never an echo.</summary>
        public bool IsAiGenerated { get; }
        public string? Timestamp { get; }
        public string? LinkTitle { get; }
        public ICommand? OpenLinkCommand { get; }

        public bool IsHer => Kind == BubbleKind.Her;
        public bool IsYou => Kind == BubbleKind.You;
        public bool IsEcho => Kind == BubbleKind.Echo;
    }
}
