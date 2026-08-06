namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// The names zones use to point at each other across the page.
    ///
    /// <para>A deep link that misses is silent — the drawer opens, nothing scrolls, and it looks
    /// like the affordance simply does not work. These constants exist so a caller cannot mistype
    /// an anchor, and so the wiring pass has one place to keep in step when a Workshop cell is
    /// renamed or localized.</para>
    ///
    /// <para>They are <see cref="IWorkshopCellVm.Key"/> values, NOT headings. The wiring pass split
    /// the two: <see cref="WorkshopAccordion"/> reveals by key and renders <c>Title</c>, so a cell's
    /// heading can be translated (and re-worded) without any of these links following it into the
    /// wrong language. The keys are ASCII, uppercase and deliberately not user-visible.</para>
    /// </summary>
    public static class CompanionRoomAnchors
    {
        /// <summary>The roster pigeonhole — where the hero's Switch chip lands.</summary>
        public const string WorkshopRosterCell = "ROSTER";

        /// <summary>The awareness cooldown pigeonhole — where Z5's "fine-tuning ↓" lands.</summary>
        public const string WorkshopAwarenessCell = "AWARENESS FINE-TUNING";

        /// <summary>Behaviour sliders + the two shortcut editors.</summary>
        public const string WorkshopBehaviorCell = "BEHAVIOR";

        /// <summary>Trigger mode, its interval, and the phrase shelf.</summary>
        public const string WorkshopTriggersCell = "TRIGGERS & PHRASES";

        /// <summary>The per-mod Hypnotube link pool.</summary>
        public const string WorkshopLibraryCell = "HER LIBRARY";

        /// <summary>Community prompt browse / import / export / installed list.</summary>
        public const string WorkshopCommunityCell = "COMMUNITY";
    }
}
