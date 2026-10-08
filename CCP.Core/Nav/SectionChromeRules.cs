namespace ConditioningControlPanel.Nav
{
    /// <summary>
    /// The pure numbers of WPF 7.1.5 MainWindow.SectionChrome.cs: the page wash every section
    /// paints behind its pages, the "Moved" note budget and which old keys land with or without it.
    /// </summary>
    public static class SectionChromeRules
    {
        /// <summary>The page wash's top-left alpha (about 14%) and its hue line's (about 35%).</summary>
        public const byte SectionWashAlpha = 0x24, SectionWashLineAlpha = 0x59;

        /// <summary>The wash's colour change, 250 ms (Reduced halves it, Off is instant).</summary>
        public const int SectionWashMs = 250;

        /// <summary>How many times the "Moved" note shows across the app's life, then never.</summary>
        public const int NavMovedNoteLimit = 3;

        /// <summary>Old keys that land on a new home with a "Moved" note.</summary>
        public static readonly string[] MovedRedirectKeys = { "together" };

        /// <summary>Old keys that land on their new home WITHOUT the note ("exclusives" is Premium again).</summary>
        public static readonly string[] SilentRedirectKeys = { "exclusives" };
    }
}
