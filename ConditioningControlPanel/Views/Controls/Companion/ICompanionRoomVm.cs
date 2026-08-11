using System.ComponentModel;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// The whole page — "Her Room" — as one contract: the eight zone viewmodels the composed
    /// <see cref="CompanionRoomView"/> hands to its children, plus the navigation seam that lets a
    /// zone reach across to another one.
    ///
    /// <para>Deliberately a bag of the existing zone interfaces and nothing more. Every zone was
    /// built to stand alone against its own mock, and composing them must not become a reason to
    /// widen any of those contracts: the room owns arrangement, the zones own behaviour.</para>
    ///
    /// <para>The wiring pass implements this against the real services (App.Patreon, CompanionBrain,
    /// IMemoryStore, AppSettings) and the view does not change.</para>
    /// </summary>
    public interface ICompanionRoomVm : INotifyPropertyChanged
    {
        /// <summary>Z0 header band + Z1 Companion Card + the constellation along its bottom.</summary>
        ICompanionHeroCardVm Hero { get; }

        /// <summary>Z2 — Talk to her.</summary>
        IChatThresholdVm Chat { get; }

        /// <summary>Z3 — What she knows about you.</summary>
        IMemoryDiaryVm Memory { get; }

        /// <summary>Z4 — Make her yours.</summary>
        IMakeHerYoursVm Personality { get; }

        /// <summary>Z5 — What she can see.</summary>
        IAwarenessPrivacyVm Awareness { get; }

        /// <summary>Z6 — Her attention.</summary>
        IAttentionGaugeVm Attention { get; }

        /// <summary>Z7 — The Engine Room.</summary>
        IEngineRoomDrawerVm Engine { get; }

        /// <summary>Z8 — The Workshop.</summary>
        IWorkshopAccordionVm Workshop { get; }

        /// <summary>
        /// Set by the view on itself when it takes this viewmodel. The design's cross-zone links —
        /// the hero's AI pill opening the Engine Room, the chat card's "open the Engine Room" line,
        /// Z5's "fine-tuning ↓" landing on the Workshop's awareness cell — are page-level moves, so
        /// they cannot live inside a zone. This is how a zone asks the page to make one.
        /// </summary>
        ICompanionRoomNavigator? Navigator { get; set; }
    }

    /// <summary>
    /// What a zone may ask the page to do. Implemented by <see cref="CompanionRoomView"/>.
    ///
    /// <para>Every method is expected to be safe to call before the page has been arranged: the
    /// deep links fire from a click, but the drawers are also opened programmatically by tests and
    /// by the play-test driver, where no message loop has pumped yet.</para>
    /// </summary>
    public interface ICompanionRoomNavigator
    {
        /// <summary>Expands the Engine Room drawer and scrolls it into view.</summary>
        void RevealEngineRoom();

        /// <summary>Scrolls "What she can see" into view and gives it the keyboard focus.</summary>
        void FocusAwareness();

        /// <summary>
        /// Expands the Workshop and, when <paramref name="cellTitle"/> names one of its
        /// pigeonholes, brings that cell into view.
        /// </summary>
        void RevealWorkshop(string? cellTitle = null);
    }
}
