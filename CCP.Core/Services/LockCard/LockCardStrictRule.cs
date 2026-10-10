namespace ConditioningControlPanel.Services
{
    /// <summary>Who asked for a lock card. Only the player's own machine (schedule, test button,
    /// autonomy, voice, programs) may open a strict one.</summary>
    public enum LockCardOrigin
    {
        /// <summary>The player's own schedule or gesture: the player's own strict setting applies.</summary>
        Local,
        /// <summary>A leash holder's "lines" punishment.</summary>
        Leash,
        /// <summary>A remote controller's trigger_lock_card.</summary>
        Remote,
    }

    /// <summary>Safety rule: a remote or leash participant can never raise restraint, so a card they
    /// cause is never strict, whatever the player's setting says. One place, pinned by tests.</summary>
    public static class LockCardStrictRule
    {
        public static bool Resolve(LockCardOrigin origin, bool settingStrict) =>
            origin == LockCardOrigin.Local && settingStrict;
    }
}
