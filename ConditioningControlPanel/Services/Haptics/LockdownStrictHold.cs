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

    /// <summary><see cref="Refuses"/> against the live Lockdown and settings.</summary>
    public static bool RefusesNow(bool turningOn)
        => Refuses(App.Lockdown?.IsActive == true,
                   App.Settings?.Current?.LockdownForceStrictLock == true,
                   turningOn);
}
