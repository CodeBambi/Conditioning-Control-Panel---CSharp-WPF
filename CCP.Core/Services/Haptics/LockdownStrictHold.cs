namespace ConditioningControlPanel.Services;

/// <summary>
/// The one rule for the panel's Strict Lock toggles during a Lockdown: while a Lockdown runs with
/// its "force Strict Lock" safety on, no strict toggle can be switched OFF. Switching one ON is
/// always fine. The main panel greys the toggles on this rule when Lockdown starts; the toggles'
/// own handlers check it again so a click that slips past the greying still changes nothing
/// (ccp-bugs #1282, Bubble Count's strict toggle stayed live through a Lockdown).
///
/// <para>Panic is not this rule's business: it never reads or writes the panic key.</para>
/// </summary>
public static class LockdownStrictHold
{
    /// <summary>True while Lockdown holds the strict toggles.</summary>
    public static bool Holds(bool lockdownActive, bool forceStrictLock) => lockdownActive && forceStrictLock;

    /// <summary>True when a toggle change must be refused: only a switch-off, only while held.</summary>
    public static bool Refuses(bool lockdownActive, bool forceStrictLock, bool turningOn)
        => !turningOn && Holds(lockdownActive, forceStrictLock);

    /// <summary><see cref="Holds"/> against the live Lockdown and settings.</summary>
    public static bool HoldsNow
        => Holds(LockdownService.Current?.IsActive == true, CoreSettings.Current.LockdownForceStrictLock);

    /// <summary><see cref="Refuses"/> against the live Lockdown and settings.</summary>
    public static bool RefusesNow(bool turningOn) => !turningOn && HoldsNow;

    /// <summary>
    /// What a strict flag becomes when something writes it wholesale (a preset, a recalled
    /// config): while held, a flag that was on stays on; otherwise the incoming value wins.
    /// </summary>
    public static bool Keep(bool holds, bool before, bool incoming) => holds ? before || incoming : incoming;

    /// <summary>
    /// Put the strict flags back after a wholesale write (Preset.ApplyTo), so loading a preset
    /// mid-Lockdown cannot switch Strict Lock off. Pass the values read before the write.
    /// </summary>
    public static void RestoreAfterApply(Models.AppSettings s, bool strictBefore, bool bubbleStrictBefore)
    {
        var holds = HoldsNow;
        s.StrictLockEnabled = Keep(holds, strictBefore, s.StrictLockEnabled);
        s.BubbleCountStrictLock = Keep(holds, bubbleStrictBefore, s.BubbleCountStrictLock);
    }
}
