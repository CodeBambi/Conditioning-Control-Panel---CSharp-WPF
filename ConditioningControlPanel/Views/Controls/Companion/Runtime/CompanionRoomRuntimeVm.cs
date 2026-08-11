using System;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// "Her Room" against the live app: the eight zone viewmodels, the navigator fan-out, and one
    /// <see cref="Sync"/> the MainWindow partials call where they used to poke controls by name.
    ///
    /// <para><b>Why one Sync rather than eight.</b> The old tab had ~110 named elements and roughly
    /// as many write sites spread across seven partials. Replacing each with a targeted viewmodel
    /// write would have reproduced that sprawl with more indirection. Everything on this page is
    /// derived from settings and services that the same partials have already written by the time
    /// they would have touched the UI, so the page re-reads instead of being told. It is cheap
    /// (no I/O, no allocation beyond the fact wall) and it cannot drift.</para>
    ///
    /// <para>The finer-grained syncs are still exposed for the hot paths — companion XP lands
    /// several times a minute during a session, and rebuilding the diary for it would be silly.</para>
    /// </summary>
    internal sealed class CompanionRoomRuntimeVm : CompanionObservable, ICompanionRoomVm
    {
        private readonly CompanionRuntimeContext _ctx;
        private ICompanionRoomNavigator? _navigator;

        public CompanionRoomRuntimeVm(Func<MainWindow?> window)
        {
            _ctx = new CompanionRuntimeContext(window);
            Shelf = new WorkshopShelfParts();

            HeroVm = new CompanionHeroRuntimeVm(_ctx);
            ChatVm = new ChatThresholdRuntimeVm(_ctx);
            MemoryVm = new MemoryDiaryRuntimeVm(_ctx);
            PersonalityVm = new MakeHerYoursRuntimeVm(_ctx);
            AwarenessVm = new AwarenessPrivacyRuntimeVm(_ctx);
            AttentionVm = new AttentionGaugeRuntimeVm(_ctx);
            EngineVm = new EngineRoomRuntimeVm(_ctx);
            WorkshopVm = new WorkshopRuntimeVm(_ctx, Shelf);
        }

        /// <summary>The re-parented legacy controls, for the tab's compat passthroughs.</summary>
        public WorkshopShelfParts Shelf { get; }

        public CompanionHeroRuntimeVm HeroVm { get; }
        public ChatThresholdRuntimeVm ChatVm { get; }
        public MemoryDiaryRuntimeVm MemoryVm { get; }
        public MakeHerYoursRuntimeVm PersonalityVm { get; }
        public AwarenessPrivacyRuntimeVm AwarenessVm { get; }
        public AttentionGaugeRuntimeVm AttentionVm { get; }
        public EngineRoomRuntimeVm EngineVm { get; }
        public WorkshopRuntimeVm WorkshopVm { get; }

        public ICompanionHeroCardVm Hero => HeroVm;
        public IChatThresholdVm Chat => ChatVm;
        public IMemoryDiaryVm Memory => MemoryVm;
        public IMakeHerYoursVm Personality => PersonalityVm;
        public IAwarenessPrivacyVm Awareness => AwarenessVm;
        public IAttentionGaugeVm Attention => AttentionVm;
        public IEngineRoomDrawerVm Engine => EngineVm;
        public IWorkshopAccordionVm Workshop => WorkshopVm;

        /// <summary>
        /// The page seam. The zones reach the navigator through the shared context rather than
        /// holding one each, so there is exactly one place a stale navigator could survive a
        /// teardown — and setting this to null clears it for all eight at once.
        /// </summary>
        public ICompanionRoomNavigator? Navigator
        {
            get => _navigator;
            set
            {
                if (!Set(ref _navigator, value)) return;
                _ctx.Navigator = value;

                // The page letting go of its navigator is this room's only teardown signal (the view
                // does it from Unloaded and from a DataContext swap), and Z2 is the one zone that
                // holds a subscription on a service — the brain's turn log — rather than only being
                // pushed at. Nothing re-attaches it here: the next Sync does, which is also what
                // picks the session up when the brain finishes constructing after startup.
                if (value == null) ChatVm.Detach();
            }
        }

        // =====================================================================================

        /// <summary>
        /// Re-reads the whole page. Called where <c>SyncCompanionTabUI</c> / <c>SyncAiBrainUI</c>
        /// used to write to elements by name.
        /// </summary>
        public void Sync()
        {
            HeroVm.Sync();
            ChatVm.Sync();
            MemoryVm.Sync();
            PersonalityVm.Sync();
            AwarenessVm.Sync();
            AttentionVm.Sync();
            EngineVm.Sync();
        }

        /// <summary>
        /// The hot path: companion XP / level-up / drain, which fire several times a minute during a
        /// session. Only the hero moves, so only the hero re-reads.
        /// </summary>
        public void SyncHero() => HeroVm.Sync();

        /// <summary>Provider, entitlement and budget changed — the old <c>SyncAiBrainUI</c> surface.</summary>
        public void SyncBrain()
        {
            HeroVm.Sync();
            ChatVm.Sync();
            AttentionVm.Sync();
            EngineVm.Sync();
        }
    }
}
