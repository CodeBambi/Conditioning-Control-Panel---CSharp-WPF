using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Design-time / state-gallery implementation of <see cref="IChatThresholdVm"/>. All four
    /// states from the mockup are reachable through the factories below.
    /// </summary>
    public sealed class MockChatThresholdVm : CompanionObservable, IChatThresholdVm
    {
        private string _draft = string.Empty;
        private bool _isThinking;

        /// <summary>Parameterless ctor for <c>d:DesignInstance IsDesignTimeCreatable=True</c>.</summary>
        public MockChatThresholdVm()
        {
            Turns = LiveThread();
            TeaserTurns = StagedTeaser();
            SendCommand = new CompanionRelayCommand(() => Draft = string.Empty);
            OpenFullChatCommand = CompanionRelayCommand.NoOp("chat.openFull");
            HistoryCommand = CompanionRelayCommand.NoOp("chat.history");
            UnlockCommand = CompanionRelayCommand.NoOp("chat.unlock");
            OpenEngineRoomCommand = CompanionRelayCommand.NoOp("chat.engineRoom");
        }

        public CompanionZoneState State { get; init; } = CompanionZoneState.Live;
        public IReadOnlyList<IChatBubbleVm> Turns { get; init; }
        public IReadOnlyList<IChatBubbleVm> TeaserTurns { get; init; }

        public string Draft
        {
            get => _draft;
            set => Set(ref _draft, value);
        }

        public bool IsThinking
        {
            get => _isThinking;
            set => Set(ref _isThinking, value);
        }

        public bool CanSend { get; init; } = true;
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

        // ------------------------------- state exhibits -------------------------------

        /// <summary>The artboard: a real multi-turn thread with AI badges and a bark echo.</summary>
        public static MockChatThresholdVm Live() => new();

        /// <summary>
        /// Pre-Train 1. Honest about today's reality — one bubble and the line that sets up the
        /// release moment.
        /// </summary>
        public static MockChatThresholdVm Dormant() => new()
        {
            State = CompanionZoneState.Dormant,
            Turns = new IChatBubbleVm[]
            {
                new CompanionChatBubble(CompanionBubbleKind.Her, "hi princess~ what are we doing today?", isAi: true)
            },
            StateCopy = "she forgets every conversation the moment it ends… that's about to change.",
            FooterCopy = string.Empty,
            LastHeardCopy = string.Empty
        };

        /// <summary>Provider is Off: dimmed input plus the jump link into the Engine Room.</summary>
        public static MockChatThresholdVm AiOff() => new()
        {
            State = CompanionZoneState.Disabled,
            CanSend = false,
            Turns = Array.Empty<IChatBubbleVm>(),
            StateCopy = "turn her brain on in the Engine Room below.",
            FooterCopy = string.Empty,
            LastHeardCopy = string.Empty
        };

        /// <summary>
        /// Free tier — the flagship teaser. A blurred staged thread under the Velvet-Vault veil
        /// with a personal sell line and exactly one hit-testable CTA chip.
        /// </summary>
        public static MockChatThresholdVm Locked() => new()
        {
            State = CompanionZoneState.Locked,
            CanSend = false,
            Turns = Array.Empty<IChatBubbleVm>(),
            FooterCopy = string.Empty,
            LastHeardCopy = string.Empty
        };

        /// <summary>Mid-send: the three-dot pulse is running.</summary>
        public static MockChatThresholdVm Thinking()
        {
            var vm = new MockChatThresholdVm { Draft = "it still does a little" };
            vm.IsThinking = true;
            return vm;
        }

        private static IReadOnlyList<IChatBubbleVm> LiveThread() => new IChatBubbleVm[]
        {
            // A BarkEcho: what her *voice* said out loud. Italic whisper, never an AI badge —
            // the badge rides IsAiGenerated only.
            new CompanionChatBubble(CompanionBubbleKind.Echo,
                "said aloud: “the rabbit hole. every bubble's a little gift…”"),
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
