using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A Lockdown with its Strict Lock safety on holds every strict toggle: none switches off,
/// any may switch on (#1282).
/// </summary>
public class LockdownStrictHoldTests
{
    [Fact]
    public void A_lockdown_forcing_strict_refuses_a_switch_off()
        => Assert.True(LockdownStrictHold.Refuses(lockdownActive: true, forceStrictLock: true, turningOn: false));

    [Fact]
    public void A_lockdown_forcing_strict_still_lets_a_toggle_switch_on()
        => Assert.False(LockdownStrictHold.Refuses(lockdownActive: true, forceStrictLock: true, turningOn: true));

    [Fact]
    public void A_lockdown_with_the_strict_safety_off_holds_nothing()
    {
        Assert.False(LockdownStrictHold.Holds(lockdownActive: true, forceStrictLock: false));
        Assert.False(LockdownStrictHold.Refuses(lockdownActive: true, forceStrictLock: false, turningOn: false));
    }

    [Fact]
    public void No_lockdown_holds_nothing()
    {
        Assert.False(LockdownStrictHold.Holds(lockdownActive: false, forceStrictLock: true));
        Assert.False(LockdownStrictHold.Refuses(lockdownActive: false, forceStrictLock: true, turningOn: false));
    }
}
