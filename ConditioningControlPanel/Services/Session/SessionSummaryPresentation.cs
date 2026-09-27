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

        internal static Mode Decide(bool videoUp, bool lockCardUp, bool popQuizUp)
            => videoUp || lockCardUp || popQuizUp ? Mode.Passive : Mode.Modal;
    }
}
