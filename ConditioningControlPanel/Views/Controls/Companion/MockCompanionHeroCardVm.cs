using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Localization;

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
            DetachCommand = CompanionRelayCommand.NoOp("hero.detach");
            ToggleMuteCommand = new CompanionRelayCommand(() => IsMuted = !IsMuted);
            ToggleShownCommand = new CompanionRelayCommand(() => IsCompanionShown = !IsCompanionShown);
            // The three cross-zone links. They still record their tag like every other mock
            // command; what they add is the page-level move, which only the room can make.
            SwitchCommand = new CompanionRelayCommand(() =>
            {
                CompanionRelayCommand.Note("hero.switch");
                Navigator?.RevealWorkshop(CompanionRoomAnchors.WorkshopRosterCell);
            });
            OpenEngineRoomCommand = new CompanionRelayCommand(() =>
            {
                CompanionRelayCommand.Note("hero.engineRoom");
                Navigator?.RevealEngineRoom();
            });
            FocusAwarenessCommand = new CompanionRelayCommand(() =>
            {
                CompanionRelayCommand.Note("hero.awareness");
                Navigator?.FocusAwareness();
            });
            WakeCommand = CompanionRelayCommand.NoOp("hero.wake");
            Constellation = MockRelationshipConstellationVm.Dormant();
            Header = MockCompanionHeaderVm.Entitled();
        }

        public string Name { get; init; } = "Bambi";
        public string ModName { get; init; } = "BAMBI SLEEP";
        public string Flavor { get; init; } = "Gains bonus XP from Pink Filter intensity. Currently plotting something.";
        public ImageSource? Portrait { get; init; }

        public bool IsCompanionEnabled { get; init; } = true;
        public bool IsAiLive { get; init; } = true;

        /// <summary>Behind the Lab, as opposed to merely off. See the interface for why it matters.</summary>
        public bool IsAiLocked { get; init; }

        public bool IsAwarenessOpen { get; init; } = true;
        public string AiPillText { get; init; } =
            Loc.Get("companion_hero_pill_ai_cloud");
        public string AwarenessPillText { get; init; } =
            Loc.Get("companion_hero_pill_eyes_broad");
        public string AsleepCopy { get; init; } =
            Loc.Get("companion_hero_asleep_copy");

        public bool IsMoodLive { get; init; }
        /// <summary>The live mood glyph. Pre-Train-4 the view swaps in the sleeping moon itself.</summary>
        public string MoodGlyph { get; init; } = "✧";
        /// <summary>Dormant on purpose: claiming a mood she does not have yet would be a lie.</summary>
        public string MoodWord { get; init; } =
            Loc.Get("companion_hero_mood_asleep");
        public string MoodCaption { get; init; } =
            Loc.Get("companion_hero_mood_caption_dormant");

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

        /// <summary>Z0 band. Set to null to prove the hero renders with the header collapsed.</summary>
        public ICompanionHeaderVm? Header { get; init; }

        /// <summary>
        /// Set by <see cref="MockCompanionRoomVm"/> when this card is composed into the page, so
        /// the AI pill, the awareness pill and the Switch chip actually go somewhere. Left null the
        /// hero still works standalone — the commands just record their tag and stop.
        /// </summary>
        public ICompanionRoomNavigator? Navigator { get; set; }

        // ------------------------------- state exhibits -------------------------------

        /// <summary>The artboard: AI live, awareness on, mood token still dormant (pre-Train 4).</summary>
        public static MockCompanionHeroCardVm Default() => new();

        /// <summary>Train 4 landed: the mood token is real and the constellation counts.</summary>
        public static MockCompanionHeroCardVm FullyAlive() => new()
        {
            IsMoodLive = true,
            MoodWord = "bratty",
            MoodCaption = Loc.Get("companion_hero_mood_caption_live"),
            Constellation = MockRelationshipConstellationVm.Live()
        };

        /// <summary>
        /// No AI entitlement. The design's rule made visible: the hero is untouched (barks are free)
        /// and the only difference is the header plate dimming behind a Vault teaser ribbon.
        /// </summary>
        public static MockCompanionHeroCardVm FreeTier() => new()
        {
            Header = MockCompanionHeaderVm.FreeTier(),
            IsAiLive = false,
            // NOT the disabled state's copy. Three different situations used to share
            // "Off — she's asleep": companion disabled, provider Off, and no entitlement. Only
            // the last one is a thing the user can buy, and telling a paying-curious reader she
            // is asleep contradicts the teaser ribbon two inches to the left.
            AiPillText = Loc.Get("companion_hero_pill_ai_locked"),
            IsAiLocked = true
        };

        /// <summary>Hosted by a page that draws its own header: the Z0 band collapses.</summary>
        public static MockCompanionHeroCardVm NoHeader() => new() { Header = null };

        /// <summary>ChkAvatarEnabled off — the portrait desaturates and the wake affordance appears.</summary>
        public static MockCompanionHeroCardVm Asleep() => new()
        {
            IsCompanionEnabled = false,
            IsAiLive = false,
            IsAwarenessOpen = false,
            AiPillText = Loc.Get("companion_hero_pill_ai_off"),
            AwarenessPillText = Loc.Get("companion_hero_pill_eyes_closed")
        };

        /// <summary>Provider Off but she is awake — barks still work, so the hero stays alive.</summary>
        public static MockCompanionHeroCardVm AiOff() => new()
        {
            IsAiLive = false,
            AiPillText = Loc.Get("companion_hero_pill_ai_off"),
            IsAwarenessOpen = false,
            AwarenessPillText = Loc.Get("companion_hero_pill_eyes_closed")
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
