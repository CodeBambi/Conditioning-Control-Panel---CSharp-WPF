using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1171: EMI is put back in the topmost band only when she lost the flag.</summary>
public class EmiTopmostRuleTests
{
    private const int Toolwindow = 0x00000080;

    [Fact]
    public void LostFlag_WhileVisible_IsRepaired()
        => Assert.True(EmiTopmostRule.NeedsRepair(visible: true, wantsTopmost: true, exStyle: Toolwindow));

    [Fact]
    public void StillTopmost_IsLeftAlone_EvenIfBuriedUnderASibling()
        => Assert.False(EmiTopmostRule.NeedsRepair(true, true, Toolwindow | EmiTopmostRule.WsExTopmost));

    [Fact]
    public void Hidden_IsLeftAlone()
        => Assert.False(EmiTopmostRule.NeedsRepair(visible: false, wantsTopmost: true, exStyle: 0));

    [Fact]
    public void NotAskedToBeTopmost_IsLeftAlone()
        => Assert.False(EmiTopmostRule.NeedsRepair(visible: true, wantsTopmost: false, exStyle: 0));
}
