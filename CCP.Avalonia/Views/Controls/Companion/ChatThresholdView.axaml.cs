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
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using ConditioningControlPanel.Views.Controls.Companion;

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
        private ChatThresholdViewModel? _vm;

        public ChatThresholdView()
        {
            InitializeComponent();
            DataContext = _vm = new ChatThresholdViewModel();
            DataContextChanged += (_, _) =>
            {
                // A replaced viewmodel stops listening to the brain's turn log (WPF Detach on teardown).
                if (!ReferenceEquals(_vm, DataContext)) _vm?.Detach();
                _vm = DataContext as ChatThresholdViewModel;
                WatchTurns(_vm?.Turns);
            };
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

            // The provider-Off state's one link jumps to the Engine Room, which only the page that
            // hosts this zone can do. Found on Loaded rather than in the ctor because the parent
            // chain does not exist yet there; left null when the zone is rendered alone, which
            // leaves the link disabled rather than silently dead.
            if (ViewModel is { OpenEngineRoom: null } vm &&
                this.FindAncestorOfType<CompanionRoomView>() is ICompanionRoomNavigator nav)
            {
                vm.OpenEngineRoom = nav.RevealEngineRoom;
            }
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
            if (ViewModel is not { State: CompanionZoneState.Dormant }) return;
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
    /// whichever sibling port lands its zone first. <see cref="CreateLive"/> is the WPF
    /// <c>ChatThresholdRuntimeVm</c> over <c>App.Brain</c>; the pure half is Core's
    /// <see cref="CompanionRoomLogic"/>, shared with WPF.
    /// </summary>
    public sealed class ChatThresholdViewModel : INotifyPropertyChanged
    {
        private readonly Relay _send;
        private readonly Relay _engineRoom;
        private readonly bool _live;
        private string _draft = string.Empty;
        private bool _isThinking;
        private CompanionZoneState _state = CompanionZoneState.Live;
        private string _lastHeardCopy = Loc.GetF("companion_chat_last_heard_fmt", "2h ago");
        private string _threadSignature = string.Empty;
        private ChatSession? _session;

        public ChatThresholdViewModel() : this(live: false) { }

        /// <summary>The live zone (WPF ChatThresholdRuntimeVm): reads App.Brain, sends through brain.ChatAsync.</summary>
        internal static ChatThresholdViewModel CreateLive()
        {
            var vm = new ChatThresholdViewModel(live: true);
            vm.Sync();
            return vm;
        }

        private ChatThresholdViewModel(bool live)
        {
            _live = live;
            Turns = live ? new() : new(LiveThread());
            TeaserTurns = live ? LiveTeaser() : StagedTeaser();
            if (live) _lastHeardCopy = string.Empty;
            // WPF's CommandManager.RequerySuggested re-polled CanExecute for free; Avalonia only
            // re-polls on CanExecuteChanged, so Draft and IsThinking raise it by hand.
            SendCommand = _send = new Relay(Send, () => CanSend && !IsThinking && !string.IsNullOrWhiteSpace(Draft));
            // Staged (not live): the three commands are disarmed Relays, so the buttons do not
            // pretend to be armed.
            // Live: Open full chat is the tube's input box (WPF App.AvatarWindow.OpenChatInput),
            // History the stored transcript (WPF CompanionTranscriptWindow.ShowFor) and Unlock the Patreon tab.
            OpenFullChatCommand = live
                ? new Relay(() => Views.AvatarTube.AvatarTubeWindow.Live?.OpenChatInput())
                : new Relay(() => { }, () => false);
            HistoryCommand = live
                ? new Relay(() => CompanionTranscriptWindow.ShowFor(ShellWindow()))
                : new Relay(() => { }, () => false);
            UnlockCommand = live
                ? new Relay(() => ShellWindow()?.ShowTab("patreon"))
                : new Relay(() => { }, () => false);
            // The page IS composed now, so this one has a real target - see OpenEngineRoom.
            OpenEngineRoomCommand = _engineRoom = new Relay(() => _openEngineRoom?.Invoke(),
                                                           () => _openEngineRoom != null);
        }

        private static Views.Windows.MainShellWindow? ShellWindow()
            => (global::Avalonia.Application.Current?.ApplicationLifetime
                as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
               ?.Windows.OfType<Views.Windows.MainShellWindow>().FirstOrDefault();

        private Action? _openEngineRoom;

        /// <summary>
        /// What "open the Engine Room" does. Set by the view once it can see the page that hosts it
        /// (<see cref="ICompanionRoomNavigator"/>); null when the zone is rendered alone, which
        /// leaves the link correctly disabled instead of dead. Avalonia never re-polls CanExecute,
        /// so the setter raises it.
        /// </summary>
        public Action? OpenEngineRoom
        {
            get => _openEngineRoom;
            set { _openEngineRoom = value; _engineRoom.RaiseCanExecuteChanged(); }
        }

        public CompanionZoneState State
        {
            get => _state;
            private set
            {
                if (_state == value) return;
                _state = value;
                Raise(); Raise(nameof(CanSend)); Raise(nameof(StateCopy)); Raise(nameof(FooterCopy));
                _send.RaiseCanExecuteChanged();
            }
        }

        /// <summary>The last ~3 real turns, oldest first. Observable so the view follows a growing thread.</summary>
        public ObservableCollection<ChatBubble> Turns { get; }

        /// <summary>Static fake bubbles rendered under the veil. Never live content.</summary>
        public IReadOnlyList<ChatBubble> TeaserTurns { get; }

        public string Draft
        {
            get => _draft;
            set { if (_draft != value) { _draft = value; Raise(); _send.RaiseCanExecuteChanged(); } }
        }

        public bool IsThinking
        {
            get => _isThinking;
            set
            {
                if (_isThinking == value) return;
                _isThinking = value;
                Raise(); Raise(nameof(CanSend)); Raise(nameof(FooterCopy));
                _send.RaiseCanExecuteChanged();
            }
        }

        public bool CanSend => State == CompanionZoneState.Live && !IsThinking;

        public string LastHeardCopy
        {
            get => _lastHeardCopy;
            private set { if (_lastHeardCopy != value) { _lastHeardCopy = value; Raise(); } }
        }

        public string FooterCopy => IsThinking
            ? Loc.Get("companion_chat_footer_picking")
            : Turns.Count == 0
                ? Loc.Get("companion_chat_footer_first")
                : Loc.Get("companion_chat_footer_remembers");

        public string StateCopy => State switch
        {
            CompanionZoneState.Dormant => Loc.Get("companion_chat_dormant_copy"),
            CompanionZoneState.Disabled => Loc.Get("companion_chat_disabled_copy"),
            _ => string.Empty
        };
        public string LockCopy { get; init; } = Loc.Get("companion_chat_lock_copy");
        public string LockCtaLabel { get; init; } = Loc.Get("companion_chat_lock_cta");
        public string InputPlaceholder { get; init; } = Loc.Get("companion_chat_input_placeholder");

        public ICommand SendCommand { get; }
        public ICommand OpenFullChatCommand { get; }
        public ICommand HistoryCommand { get; }
        public ICommand UnlockCommand { get; }
        public ICommand OpenEngineRoomCommand { get; }

        /// <summary>Live: through brain.ChatAsync (WPF ChatThresholdRuntimeVm.Send). Artboard: appends your line only.</summary>
        public void Send()
        {
            if (!SendCommand.CanExecute(null)) return;
            if (_live) { SendLive(); return; }
            Turns.Add(new ChatBubble(ChatBubble.BubbleKind.You, Draft.Trim(), timestamp: "just now"));
            Draft = string.Empty;
            IsThinking = true;
        }

        // ===================== live (WPF ChatThresholdRuntimeVm) =====================

        /// <summary>Re-reads the provider, the entitlement and the thread. The room calls it on resume and after every send.</summary>
        public void Sync()
        {
            if (!_live) return;
            try
            {
                var brain = App.Brain;
                AttachSession(brain?.Session);
                var settings = CoreSettings.Current;
                State = CompanionRoomLogic.ResolveState(
                    brainRouting: CompanionBrain.ShouldRoute(brain),
                    aiEnabled: settings.AiChatEnabled,
                    cloudProvider: (settings.CompanionPrompt?.AiProvider ?? AiProviderType.Cloud) == AiProviderType.Cloud,
                    // WPF: App.Patreon?.HasAiAccess == true || App.HasCloudIdentity (as CompanionHeroCard.Sync).
                    entitled: CoreAccount.HasPremiumAccess || !string.IsNullOrEmpty(CoreAccount.UnifiedUserId));
                RebuildThread(brain);
                RefreshLastHeard(brain);
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Companion room: chat sync failed"); }
        }

        /// <summary>Stops listening to the brain's turn log, so a replaced viewmodel is not pinned by a live session.</summary>
        public void Detach() => AttachSession(null);

        /// <summary>True while subscribed to a session's TurnsChanged (for tests).</summary>
        internal bool IsAttached => _session != null;

        private void AttachSession(ChatSession? session)
        {
            if (ReferenceEquals(_session, session)) return;
            if (_session != null) _session.TurnsChanged -= OnTurnsChanged;
            _session = session;
            if (_session != null) _session.TurnsChanged += OnTurnsChanged;
        }

        // A turn landed from the tube box or a bark echo: marshal, Normal priority (Loaded is starved).
        private void OnTurnsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
        {
            try { var brain = App.Brain; RebuildThread(brain); RefreshLastHeard(brain); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Companion room: chat turn refresh failed"); }
        }, DispatcherPriority.Normal);

        private void RebuildThread(CompanionBrain? brain)
        {
            // She names titles; the app owns links. Only her own chat lines get a watch chip
            // (WPF ChatThresholdRuntimeVm): never the user's message, never a bark echo.
            var projected = brain != null && CompanionBrain.ShouldRoute(brain)
                ? CompanionRoomLogic.PickThread(brain.Session.Turns).Select(t => new ChatBubble(
                    t.Kind switch
                    {
                        TurnKind.UserChat => ChatBubble.BubbleKind.You,
                        TurnKind.BarkEcho => ChatBubble.BubbleKind.Echo,
                        _ => ChatBubble.BubbleKind.Her
                    },
                    CompanionRoomLogic.BubbleText(t), CompanionRoomLogic.IsAiBubble(t),
                    CompanionRoomLogic.RelativeTime(t.Utc),
                    WatchLink(t)?.Title,
                    WatchLink(t) is { } hit ? Runtime.CompanionLinkLauncher.CommandFor(hit.Url) : null)).ToList()
                : new List<ChatBubble>();

            var signature = string.Concat(projected.Select(b => $"{b.Kind}\u001F{b.IsAiGenerated}\u001F{b.Text}\u001F{b.Timestamp}\u001F{b.LinkTitle}\u001F"));
            if (Turns.Count == projected.Count && signature == _threadSignature) return;
            _threadSignature = signature;
            Turns.Clear();
            foreach (var b in projected) Turns.Add(b);
            Raise(nameof(FooterCopy));
        }

        internal static ConditioningControlPanel.Services.Companion.CompanionLinkIndex.Entry? WatchLink(CompanionTurn t) =>
            t.Kind == TurnKind.AssistantChat
                ? ConditioningControlPanel.Services.Companion.CompanionLinkIndex.FindMentionedTitle(t.Text)
                : null;

        private void RefreshLastHeard(CompanionBrain? brain)
        {
            var last = brain?.Session.Turns.LastOrDefault(t => t.Kind == TurnKind.UserChat);
            LastHeardCopy = last == null
                ? string.Empty
                : Loc.GetF("companion_chat_last_heard_fmt", CompanionRoomLogic.RelativeTime(last.Utc));
        }

        private void SendLive()
        {
            var text = Draft.Trim();
            var brain = App.Brain;
            if (brain == null || !CompanionBrain.ShouldRoute(brain)) { Sync(); return; }
            Draft = string.Empty;
            IsThinking = true;
            PendingSend = SendAsync(brain, text);
        }

        /// <summary>The send in flight, for tests.</summary>
        internal System.Threading.Tasks.Task? PendingSend { get; private set; }

        private async System.Threading.Tasks.Task SendAsync(CompanionBrain brain, string text)
        {
            AiReplyResult? result = null;
            // Same entry point as the tube box: same moderation spine, same single-flight.
            try { result = await brain.ChatAsync(text); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Companion room: chat send failed"); }
            Dispatcher.UIThread.Post(() =>
            {
                IsThinking = false;
                Sync();
                // A refusal or canned fallback never lands in the turn log: it goes to the tube bubble.
                if (result == null || result.IsAiGenerated || result.IsApplicationReply) return;
                var tube = Views.AvatarTube.AvatarTubeWindow.Live;
                if (tube == null) return;
                if (result.Refusal != null) tube.ShowModerationRefusalBubble(result.Refusal.Source);
                else if (!string.IsNullOrWhiteSpace(result.Text)) tube.GigglePriority(result.Text, aiGenerated: false);
            }, DispatcherPriority.Normal);
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

        // WPF ChatThresholdRuntimeVm.BuildTeaser.
        private static IReadOnlyList<ChatBubble> LiveTeaser() => new[]
        {
            new ChatBubble(ChatBubble.BubbleKind.You, Loc.Get("companion_chat_teaser_you")),
            new ChatBubble(ChatBubble.BubbleKind.Her, Loc.Get("companion_chat_teaser_her"), isAi: true)
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
