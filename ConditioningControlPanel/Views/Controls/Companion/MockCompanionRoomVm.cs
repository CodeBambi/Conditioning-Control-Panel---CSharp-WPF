using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// The whole page as one design-time viewmodel: eight zone mocks in a bag, plus the fan-out
    /// that gives each of them the page's <see cref="ICompanionRoomNavigator"/>.
    ///
    /// <para><b>What this type is for.</b> Every zone was built to stand alone against its own mock,
    /// and the state gallery indexes those one at a time. A page, though, has states of its own —
    /// "free tier", "nothing has happened yet", "she's asleep" are combinations, not zone settings,
    /// and the design's rules only become checkable when the zones are seen together (the hero must
    /// stay fully alive while Z2 locks; the memory diary must stay unpaywalled while everything
    /// else sells). Each factory below is one of those page states, so
    /// <see cref="CompanionRoomView"/> renders every one of them without a service, a login, or a
    /// train landing.</para>
    ///
    /// <para><b>The navigator fan-out.</b> Three zones carry cross-page links — the hero's AI pill,
    /// awareness pill and Switch chip; Z2's "open the Engine Room" line; Z5's "fine-tuning ↓". A
    /// zone cannot make those moves itself, so each mock takes an optional navigator and the room
    /// hands its own down here, in one place, the moment the view claims this viewmodel. Standalone
    /// (gallery, per-zone tests) the navigator stays null and those commands just record their tag,
    /// which is exactly the behaviour those zones shipped with.</para>
    /// </summary>
    public sealed class MockCompanionRoomVm : CompanionObservable, ICompanionRoomVm
    {
        private ICompanionRoomNavigator? _navigator;

        /// <summary>Parameterless ctor for <c>d:DesignInstance IsDesignTimeCreatable=True</c>.</summary>
        public MockCompanionRoomVm()
        {
            HeroMock = MockCompanionHeroCardVm.Default();
            ChatMock = MockChatThresholdVm.Live();
            MemoryMock = MockMemoryDiaryVm.Populated();
            PersonalityMock = MockMakeHerYoursVm.Live();
            AwarenessMock = MockAwarenessPrivacyVm.Live();
            AttentionMock = MockAttentionGaugeVm.Plenty();
            EngineMock = MockEngineRoomDrawerVm.Collapsed();
            WorkshopMock = MockWorkshopAccordionVm.Collapsed();
        }

        // The concrete mocks are exposed as well as the interfaces: the harness and the tests drive
        // them (send a line, flip a dial), which the narrow zone contracts deliberately do not allow.
        public MockCompanionHeroCardVm HeroMock { get; private init; }
        public MockChatThresholdVm ChatMock { get; private init; }
        public MockMemoryDiaryVm MemoryMock { get; private init; }
        public MockMakeHerYoursVm PersonalityMock { get; private init; }
        public MockAwarenessPrivacyVm AwarenessMock { get; private init; }
        public MockAttentionGaugeVm AttentionMock { get; private init; }
        public MockEngineRoomDrawerVm EngineMock { get; private init; }
        public MockWorkshopAccordionVm WorkshopMock { get; private init; }

        public ICompanionHeroCardVm Hero => HeroMock;
        public IChatThresholdVm Chat => ChatMock;
        public IMemoryDiaryVm Memory => MemoryMock;
        public IMakeHerYoursVm Personality => PersonalityMock;
        public IAwarenessPrivacyVm Awareness => AwarenessMock;
        public IAttentionGaugeVm Attention => AttentionMock;
        public IEngineRoomDrawerVm Engine => EngineMock;
        public IWorkshopAccordionVm Workshop => WorkshopMock;

        /// <summary>
        /// The page seam. Setting it pushes the same navigator into every zone mock that has one,
        /// which is the whole reason this property is not just an auto-property: a zone handed a
        /// stale navigator would deep-link into a page that is no longer on screen.
        /// </summary>
        public ICompanionRoomNavigator? Navigator
        {
            get => _navigator;
            set
            {
                if (!Set(ref _navigator, value)) return;
                HeroMock.Navigator = value;
                ChatMock.Navigator = value;
                AwarenessMock.Navigator = value;
            }
        }

        // =================================================================================
        //  page states — the mockup's state gallery, read as whole-page combinations
        // =================================================================================

        /// <summary>
        /// The zone gallery's Engine Room exhibits open the drawer so their interior is visible;
        /// on the page it has to rest closed, because the whole point of Z7 is that the plumbing
        /// stopped being the front door. This keeps an exhibit's copy and shuts it.
        /// </summary>
        private static MockEngineRoomDrawerVm Closed(MockEngineRoomDrawerVm engine)
        {
            engine.IsExpanded = false;
            return engine;
        }

        /// <summary>
        /// The artboard: she is awake, the provider is live, awareness is open, memory has facts,
        /// and both drawers rest closed. This is the page a subscribed user opens.
        /// </summary>
        public static MockCompanionRoomVm Default() => new();

        /// <summary>
        /// No AI entitlement — the design's flagship combination, and the one worth looking at
        /// hardest: the header plate dims behind the Vault ribbon and Z2 goes to the teaser veil,
        /// but the hero is untouched (barks are free), Z3 stays fully readable (local, deterministic,
        /// never paywalled — the trust surface), and Z4 stays wide open because the interview is the
        /// conversion surface. Nothing on this page is allowed to look broken.
        /// </summary>
        public static MockCompanionRoomVm FreeTier() => new()
        {
            HeroMock = MockCompanionHeroCardVm.FreeTier(),
            ChatMock = MockChatThresholdVm.Locked(),
            AttentionMock = MockAttentionGaugeVm.Saving(),
            EngineMock = Closed(MockEngineRoomDrawerVm.LoggedOut())
        };

        /// <summary>
        /// The reskin shipping ahead of the trains: every LLM-backed surface is present and
        /// <i>sleeping</i>. Designed content in all four — shimmer, in-character promise, muted
        /// train microtag — and not one gray box or the word TODO anywhere on the page.
        /// </summary>
        public static MockCompanionRoomVm Dormant() => new()
        {
            ChatMock = MockChatThresholdVm.Dormant(),
            MemoryMock = MockMemoryDiaryVm.Dormant(),
            PersonalityMock = MockMakeHerYoursVm.Dormant(),
            AwarenessMock = MockAwarenessPrivacyVm.Dormant()
        };

        /// <summary>
        /// A brand-new account on a fully-landed build: level 1, an empty thread, an empty fact wall
        /// that still shows the deterministic profile strip ("60% of the feeling from minute one"),
        /// and a constellation that has nothing to count yet.
        /// </summary>
        public static MockCompanionRoomVm Empty() => new()
        {
            HeroMock = MockCompanionHeroCardVm.FreshUser(),
            ChatMock = MockChatThresholdVm.Fresh(),
            MemoryMock = MockMemoryDiaryVm.Empty(),
            PersonalityMock = MockMakeHerYoursVm.Dormant()
        };

        /// <summary>
        /// The day's attention is spent. The meter keeps its sliver, the copy promises tomorrow, and
        /// the rest of the page carries on — because her voice never runs out, only the thinking does.
        /// </summary>
        public static MockCompanionRoomVm Drained() => new()
        {
            AttentionMock = MockAttentionGaugeVm.Drained()
        };

        /// <summary>
        /// The companion is switched off (ChkAvatarEnabled): the portrait desaturates behind the
        /// wake affordance, the chat input stands down with the Engine Room jump link, her eyes are
        /// closed, and the provider segment sits on Off. Everything stays readable — asleep is a
        /// state, not a disabled page.
        /// </summary>
        public static MockCompanionRoomVm Disabled() => new()
        {
            HeroMock = MockCompanionHeroCardVm.Asleep(),
            ChatMock = MockChatThresholdVm.AiOff(),
            AwarenessMock = MockAwarenessPrivacyVm.EyesClosed(),
            EngineMock = MockEngineRoomDrawerVm.Off()
        };

        // =================================================================================
        //  the harness index
        // =================================================================================

        /// <summary>
        /// Page states by key, in the order the preview harness lays its buttons out. The keys are
        /// stable: the harness, the tests and the play-test driver all index into this, so adding a
        /// page state means adding it here — that is the point of the type.
        /// </summary>
        public static IReadOnlyDictionary<string, Func<MockCompanionRoomVm>> Variants { get; } =
            new Dictionary<string, Func<MockCompanionRoomVm>>(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = Default,
                ["freeTier"] = FreeTier,
                ["dormant"] = Dormant,
                ["empty"] = Empty,
                ["drained"] = Drained,
                ["disabled"] = Disabled
            };

        /// <summary>Builds a page state by key, or null when there is no such key.</summary>
        public static MockCompanionRoomVm? Get(string? key)
            => key != null && Variants.TryGetValue(key, out var factory) ? factory() : null;
    }
}
