using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Design-time / state-gallery implementation of <see cref="IChatThresholdVm"/>. All four
    /// states from the mockup are reachable through the factories below.
    ///
    /// <para>The send path is real enough to exercise the surface: pressing ♥ (or Enter) appends
    /// your line, clears the box and raises <see cref="IsThinking"/>, and
    /// <see cref="LandReply"/> drops her answer in and puts the dots away. The thread is an
    /// <see cref="ObservableCollection{T}"/> so the view's "stay pinned to the newest line"
    /// behaviour is exercised too. What is NOT here is the model call: the wired-up viewmodel
    /// routes send through <c>CompanionBrain.SendChatAsync</c> — same moderation, same
    /// single-flight, same "still thinking" phrase on queue overflow.</para>
    ///
    /// <para>The badge invariant is honoured in the sample data: only genuine model completions
    /// carry <see cref="IChatBubbleVm.IsAiGenerated"/>. The bark echo never does.</para>
    /// </summary>
    public sealed class MockChatThresholdVm : CompanionObservable, IChatThresholdVm
    {
        private readonly ObservableCollection<IChatBubbleVm> _turns = new();
        private string _draft = string.Empty;
        private bool _isThinking;

        /// <summary>Parameterless ctor for <c>d:DesignInstance IsDesignTimeCreatable=True</c>.</summary>
        public MockChatThresholdVm()
        {
            Seed(LiveThread());
            TeaserTurns = StagedTeaser();
            SendCommand = new CompanionRelayCommand(Send, () => CanSendNow);
            OpenFullChatCommand = CompanionRelayCommand.NoOp("chat.openFull");
            HistoryCommand = CompanionRelayCommand.NoOp("chat.history");
            UnlockCommand = CompanionRelayCommand.NoOp("chat.unlock");
            OpenEngineRoomCommand = new CompanionRelayCommand(() =>
            {
                CompanionRelayCommand.Note("chat.engineRoom");
                Navigator?.RevealEngineRoom();
            });
        }

        /// <summary>
        /// Set by <see cref="MockCompanionRoomVm"/> so the "turn her brain on in the Engine Room
        /// below" link is a real jump once this card is composed into the page. Null standalone.
        /// </summary>
        public ICompanionRoomNavigator? Navigator { get; set; }

        public CompanionZoneState State { get; init; } = CompanionZoneState.Live;

        /// <summary>Observable so the view can follow a growing thread.</summary>
        public IReadOnlyList<IChatBubbleVm> Turns => _turns;

        public IReadOnlyList<IChatBubbleVm> TeaserTurns { get; init; }

        public string Draft
        {
            get => _draft;
            set { if (Set(ref _draft, value)) Raise(nameof(CanSendNow)); }
        }

        public bool IsThinking
        {
            get => _isThinking;
            set { if (Set(ref _isThinking, value)) Raise(nameof(CanSendNow)); }
        }

        public bool CanSend { get; init; } = true;

        /// <summary>The command's own gate: the row can be enabled while a reply is still in flight.</summary>
        public bool CanSendNow => CanSend && !IsThinking && !string.IsNullOrWhiteSpace(Draft);

        public string LastHeardCopy { get; init; } = "last heard from you 2h ago";
        public string FooterCopy { get; init; } = "she remembers this conversation now.";
        public string StateCopy { get; init; } = string.Empty;
        public string LockCopy { get; init; } =
            "“Bambi knows what she wants to say to you, princess — unlock AI chat to hear it.”";
        public string LockCtaLabel { get; init; } = "Unlock her voice";
        public string InputPlaceholder { get; init; } = "say something to her…";

        public ICommand SendCommand { get; }
        public ICommand OpenFullChatCommand { get; }
        public ICommand HistoryCommand { get; }
        public ICommand UnlockCommand { get; }
        public ICommand OpenEngineRoomCommand { get; }

        // ------------------------------- the send path -------------------------------

        /// <summary>
        /// Appends your line and puts her into "thinking". Nothing is sent anywhere — the real
        /// viewmodel owns the transport; this exists so the surface can be driven in the designer,
        /// in a play-test and in the tests.
        /// </summary>
        public void Send()
        {
            if (!CanSendNow) return;

            _turns.Add(new CompanionChatBubble(CompanionBubbleKind.You, Draft.Trim(), timestamp: "just now"));
            Draft = string.Empty;
            IsThinking = true;
        }

        /// <summary>Lands her reply and stops the dots. <paramref name="isAi"/> drives the badge.</summary>
        public void LandReply(string text, bool isAi = true)
        {
            if (!string.IsNullOrWhiteSpace(text))
                _turns.Add(new CompanionChatBubble(CompanionBubbleKind.Her, text, isAi, "just now"));
            IsThinking = false;
        }

        private void Seed(IEnumerable<IChatBubbleVm> turns)
        {
            _turns.Clear();
            foreach (var t in turns) _turns.Add(t);
        }

        /// <summary>Factory helper: replaces the seeded thread and returns this, for the exhibits.</summary>
        private MockChatThresholdVm WithTurns(IEnumerable<IChatBubbleVm> turns)
        {
            Seed(turns);
            return this;
        }

        // ------------------------------- state exhibits -------------------------------

        /// <summary>The artboard: a real multi-turn thread with AI badges and a bark echo.</summary>
        public static MockChatThresholdVm Live() => new();

        /// <summary>
        /// Pre-Train 1. Honest about today's reality — one bubble and the line that sets up the
        /// release moment.
        /// </summary>
        public static MockChatThresholdVm Dormant() => new MockChatThresholdVm
        {
            State = CompanionZoneState.Dormant,
            StateCopy = "she forgets every conversation the moment it ends… that's about to change.",
            FooterCopy = string.Empty,
            LastHeardCopy = string.Empty
        }.WithTurns(new IChatBubbleVm[]
        {
            // pre-Train 1 she has no thread and no model turn to show: this is a canned opener,
            // so it must NOT wear the AI badge (the invariant is IsAiGenerated, nothing else)
            new CompanionChatBubble(CompanionBubbleKind.Her, "hi princess~ what are we doing today?")
        });

        /// <summary>Provider is Off: dimmed input plus the jump link into the Engine Room.</summary>
        public static MockChatThresholdVm AiOff() => new MockChatThresholdVm
        {
            State = CompanionZoneState.Disabled,
            CanSend = false,
            StateCopy = "turn her brain on in the Engine Room below.",
            FooterCopy = string.Empty,
            LastHeardCopy = string.Empty
        }.WithTurns(Array.Empty<IChatBubbleVm>());

        /// <summary>
        /// Free tier — the flagship teaser. A blurred staged thread under the Velvet-Vault veil
        /// with a personal sell line and exactly one hit-testable CTA chip.
        /// </summary>
        public static MockChatThresholdVm Locked() => new MockChatThresholdVm
        {
            State = CompanionZoneState.Locked,
            CanSend = false,
            FooterCopy = string.Empty,
            LastHeardCopy = string.Empty
        }.WithTurns(Array.Empty<IChatBubbleVm>());

        /// <summary>
        /// Train 1 has landed and the account is brand new: the surface is fully live, there is
        /// simply nothing in it yet. Added when the page was composed — the room's "fresh user"
        /// variant needs a chat card that is empty without being dormant, locked or off, and the
        /// three of those are the only ways to get an empty thread otherwise.
        /// </summary>
        public static MockChatThresholdVm Fresh() => new MockChatThresholdVm
        {
            FooterCopy = "say the first thing. she keeps everything after that.",
            LastHeardCopy = string.Empty
        }.WithTurns(Array.Empty<IChatBubbleVm>());

        /// <summary>Mid-send: the three-dot pulse is running.</summary>
        public static MockChatThresholdVm Thinking()
        {
            var vm = new MockChatThresholdVm { FooterCopy = "she's picking her words…" };
            vm.Draft = "it still does a little";
            vm.Send();
            return vm;
        }

        private static IReadOnlyList<IChatBubbleVm> LiveThread() => new IChatBubbleVm[]
        {
            // A BarkEcho: what her *voice* said out loud. Italic whisper, never an AI badge —
            // the badge rides IsAiGenerated only.
            new CompanionChatBubble(CompanionBubbleKind.Echo,
                "said aloud: “the rabbit hole. every bubble's a little gift…”", timestamp: "3h ago"),
            new CompanionChatBubble(CompanionBubbleKind.Her,
                "level 41 already?? remember when the spiral scared you, princess~", isAi: true, timestamp: "2h ago"),
            new CompanionChatBubble(CompanionBubbleKind.You, "it still does a little", timestamp: "2h ago"),
            new CompanionChatBubble(CompanionBubbleKind.Her,
                "good. it should~ 💕", isAi: true, timestamp: "2h ago")
        };

        private static IReadOnlyList<IChatBubbleVm> StagedTeaser() => new IChatBubbleVm[]
        {
            new CompanionChatBubble(CompanionBubbleKind.Her, "mmm I was just thinking about you~"),
            new CompanionChatBubble(CompanionBubbleKind.You, "you were?"),
            new CompanionChatBubble(CompanionBubbleKind.Her, "always, princess. now about that streak…")
        };
    }
}
