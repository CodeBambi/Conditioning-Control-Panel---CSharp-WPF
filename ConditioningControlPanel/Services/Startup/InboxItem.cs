using System;

namespace ConditioningControlPanel.Services.Startup
{
    /// <summary>
    /// One row in the startup Inbox: a surface that wanted to interrupt, parked until the user
    /// asks for it.
    ///
    /// <para>The item carries the surface itself in <see cref="Open"/>, not a description of it.
    /// Opening a row runs exactly the code that would have run had the popup fired at its usual
    /// moment - the same window, the same guards, the same one-shot bookkeeping - so nothing about
    /// a surface's behaviour depends on which route it took here.</para>
    ///
    /// <para>Per launch, not persisted. An Inbox row is an interruption deferred, not a task list;
    /// carrying rows across launches would rebuild the pile-up this whole redesign exists to
    /// delete.</para>
    /// </summary>
    public sealed class InboxItem
    {
        /// <summary>
        /// Dedupe key, e.g. <c>announcement:ann_spiral_launch_690</c>. Two posts with the same key
        /// produce one row: the announcement check and the marquee refresh can both land in a
        /// launch, and the user should see the news once.
        /// </summary>
        public string Key { get; init; } = "";

        /// <summary>Row heading. One short line - this is a list, not a card.</summary>
        public string Title { get; init; } = "";

        /// <summary>One line of context under the title. May be empty.</summary>
        public string Summary { get; init; } = "";

        /// <summary>A single emoji or character for the row's left edge.</summary>
        public string Glyph { get; init; } = "";

        /// <summary>
        /// Shows the original surface. Runs on the UI thread, either immediately (nothing is
        /// quiet) or when the user opens the row.
        /// </summary>
        public Action Open { get; init; } = static () => { };

        /// <summary>
        /// The surface's own dismissal bookkeeping - recording <c>DismissedAnnouncementId</c>,
        /// stamping the nudge's week - for a row the user waved away without opening. Null when
        /// the surface has nothing to remember.
        /// </summary>
        public Action? Dismiss { get; init; }

        /// <summary>When the row was posted. Rows render newest first.</summary>
        public DateTime PostedUtc { get; init; } = DateTime.UtcNow;
    }
}
