using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>SAFETY (hunt3 IC1): a leash holder or a remote controller can never raise restraint, so a
/// lock card they cause is never strict, whatever the player's own setting says.</summary>
public sealed class LockCardStrictRuleTests
{
    [Theory]
    [InlineData(LockCardOrigin.Local, true, true)]
    [InlineData(LockCardOrigin.Local, false, false)]
    [InlineData(LockCardOrigin.Leash, true, false)]
    [InlineData(LockCardOrigin.Leash, false, false)]
    [InlineData(LockCardOrigin.Remote, true, false)]
    [InlineData(LockCardOrigin.Remote, false, false)]
    public void OnlyThePlayersOwnMachineMayOpenAStrictCard(LockCardOrigin origin, bool setting, bool expected) =>
        Assert.Equal(expected, LockCardStrictRule.Resolve(origin, setting));

    [Fact]
    public void EveryOriginButLocalIsNeverStrict()
    {
        foreach (LockCardOrigin o in System.Enum.GetValues(typeof(LockCardOrigin)))
            if (o != LockCardOrigin.Local) Assert.False(LockCardStrictRule.Resolve(o, true));
    }
}
