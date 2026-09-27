namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// How the post-session recap may open, given what else is on screen. Pure so the rule is
    /// testable without WPF windows.
    ///
    /// A modal recap (ShowDialog) disables every other window on the UI thread. A lock card and a
    /// pop quiz are ownerless HWND_TOPMOST covers, so a modal recap opened under one leaves the
    /// card DISABLED (no answer, no typing) and the recap BURIED behind it (no Continue): the user
    /// has to Ctrl+Alt+Del to get the input queue back (ccp-bugs #1303). Same trap as a live video
    /// surface (#905), same answer: open the recap passively and let it wait.
    /// </summary>
    internal static class SessionSummaryPresentation
    {
        internal enum Mode
        {
            /// <summary>Nothing blocking is up: modal, pulsed topmost, then dropped back.</summary>
            Modal,
            /// <summary>Something topmost owns the screen: non-modal, not activated, not topmost.</summary>
            Passive,
        }

        /// <param name="bubbleCountUp">The Bubble Count game is the same kind of cover (ownerless,
        /// topmost, waits for typed input) and its clip plays on its own lease, so the video flag
        /// does not see it.</param>
        internal static Mode Decide(bool videoUp, bool lockCardUp, bool popQuizUp, bool bubbleCountUp)
            => videoUp || lockCardUp || popQuizUp || bubbleCountUp ? Mode.Passive : Mode.Modal;
    }
}
