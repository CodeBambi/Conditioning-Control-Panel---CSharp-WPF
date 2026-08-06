using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Design-time / state-gallery implementation of <see cref="ICompanionHeroCardVm"/>.
    /// <see cref="Default"/> is the artboard; the named factories are the state exhibits.
    /// </summary>
    public sealed class MockCompanionHeroCardVm : CompanionObservable, ICompanionHeroCardVm
    {
        private bool _isMuted;
        private bool _isCompanionShown = true;

        /// <summary>Parameterless ctor for <c>d:DesignInstance IsDesignTimeCreatable=True</c>.</summary>
        public MockCompanionHeroCardVm()
        {
            ChatCommand = CompanionRelayCommand.NoOp("hero.chat");
            SwitchCommand = CompanionRelayCommand.NoOp("hero.switch");
            DetachCommand = CompanionRelayCommand.NoOp("hero.detach");
            ToggleMuteCommand = new CompanionRelayCommand(() => IsMuted = !IsMuted);
            ToggleShownCommand = new CompanionRelayCommand(() => IsCompanionShown = !IsCompanionShown);
            OpenEngineRoomCommand = CompanionRelayCommand.NoOp("hero.engineRoom");
            FocusAwarenessCommand = CompanionRelayCommand.NoOp("hero.awareness");
            WakeCommand = CompanionRelayCommand.NoOp("hero.wake");
            Constellation = MockRelationshipConstellationVm.Dormant();
        }

        public string Name { get; init; } = "Bambi";
        public string ModName { get; init; } = "BAMBI SLEEP";
        public string Flavor { get; init; } = "Gains bonus XP from Pink Filter intensity. Currently plotting something.";
        public ImageSource? Portrait { get; init; }

        public bool IsCompanionEnabled { get; init; } = true;
        public bool IsAiLive { get; init; } = true;
        public bool IsAwarenessOpen { get; init; } = true;
        public string AiPillText { get; init; } = "Cloud — she's listening";
        public string AwarenessPillText { get; init; } = "Eyes open — broad strokes";
        public string AsleepCopy { get; init; } = "she's asleep — wake her?";

        public bool IsMoodLive { get; init; }
        public string MoodGlyph { get; init; } = "✧";
        public string MoodWord { get; init; } = "bratty";
        public string MoodCaption { get; init; } = "she wakes up with a mood of her own soon~";

        public int Level { get; init; } = 41;
        public double XpFraction { get; init; } = 0.62;
        public string XpLabel { get; init; } = "341 / 550 XP";
        public string NextLevelLabel { get; init; } = "next: Lv 42";

        public string ChatShortcutHint { get; init; } = "Ctrl+T";

        public bool IsMuted
        {
            get => _isMuted;
            set => Set(ref _isMuted, value);
        }

        public bool IsCompanionShown
        {
            get => _isCompanionShown;
            set => Set(ref _isCompanionShown, value);
        }

        public ICommand ChatCommand { get; }
        public ICommand SwitchCommand { get; }
        public ICommand DetachCommand { get; }
        public ICommand ToggleMuteCommand { get; }
        public ICommand ToggleShownCommand { get; }
        public ICommand OpenEngineRoomCommand { get; }
        public ICommand FocusAwarenessCommand { get; }
        public ICommand WakeCommand { get; }

        public IRelationshipConstellationVm Constellation { get; init; }

        // ------------------------------- state exhibits -------------------------------

        /// <summary>The artboard: AI live, awareness on, mood token still dormant (pre-Train 4).</summary>
        public static MockCompanionHeroCardVm Default() => new();

        /// <summary>Train 4 landed: the mood token is real and the constellation counts.</summary>
        public static MockCompanionHeroCardVm FullyAlive() => new()
        {
            IsMoodLive = true,
            MoodCaption = "today's mood",
            Constellation = MockRelationshipConstellationVm.Live()
        };

        /// <summary>ChkAvatarEnabled off — the portrait desaturates and the wake affordance appears.</summary>
        public static MockCompanionHeroCardVm Asleep() => new()
        {
            IsCompanionEnabled = false,
            IsAiLive = false,
            IsAwarenessOpen = false,
            AiPillText = "Off — she's asleep",
            AwarenessPillText = "Eyes closed"
        };

        /// <summary>Provider Off but she is awake — barks still work, so the hero stays alive.</summary>
        public static MockCompanionHeroCardVm AiOff() => new()
        {
            IsAiLive = false,
            AiPillText = "Off — she's asleep",
            IsAwarenessOpen = false,
            AwarenessPillText = "Eyes closed"
        };

        /// <summary>A brand-new account: level 1, nothing earned, page still full of life.</summary>
        public static MockCompanionHeroCardVm FreshUser() => new()
        {
            Level = 1,
            XpFraction = 0.04,
            XpLabel = "8 / 200 XP",
            NextLevelLabel = "next: Lv 2",
            Flavor = "She just woke up in your machine. Say hi?",
            Constellation = MockRelationshipConstellationVm.Dormant()
        };
    }
}
