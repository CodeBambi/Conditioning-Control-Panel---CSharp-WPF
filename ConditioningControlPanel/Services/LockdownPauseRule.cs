namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The session Pause/Resume button under Lockdown. Lockdown forbids PAUSING a session; it must
    /// never forbid RESUMING one. A session can end up paused under Lockdown without the button
    /// (the rapid-blink stop gesture, a panic press, a pause taken just before the lock), and the
    /// old gate refused the button in both directions, so the user was stranded in a paused
    /// session they could neither resume nor end (Discord ticket, Sep 2026). PURE.
    /// </summary>
    public static class LockdownPauseRule
    {
        /// <summary>True when a press of the pause button must be refused.</summary>
        public static bool RefusesPauseButton(bool lockdownActive, bool sessionPaused)
            => lockdownActive && !sessionPaused;
    }
}
