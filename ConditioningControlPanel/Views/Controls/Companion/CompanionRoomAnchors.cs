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
    /// <para>They are cell <i>titles</i> rather than ids because <see cref="WorkshopAccordion"/>
    /// reveals by title (its cells are data, not named elements). When the Workshop titles become
    /// loc keys, this is the file that resolves them.</para>
    /// </summary>
    public static class CompanionRoomAnchors
    {
        /// <summary>The roster pigeonhole — where the hero's Switch chip lands.</summary>
        public const string WorkshopRosterCell = "ROSTER";

        /// <summary>The awareness cooldown pigeonhole — where Z5's "fine-tuning ↓" lands.</summary>
        public const string WorkshopAwarenessCell = "AWARENESS FINE-TUNING";
    }
}
