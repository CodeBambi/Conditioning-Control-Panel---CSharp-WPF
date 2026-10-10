using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>PORTED from the WPF PossessionOffLimitsTests (7.1.5), the name half: the rooms the user
/// must always be able to leave are never possessable. The visual-tree half needs a WPF tree.</summary>
public sealed class PossessionOffLimitsTests
{
    [Theory]
    [InlineData("LockdownCardBorder", true)]
    [InlineData("BtnEmergencyExit", true)]
    [InlineData("TxtSecretExit", true)]
    [InlineData("txtlockdownexit", true)]
    [InlineData("BtnStart", false)]
    [InlineData("TxtPossessionRung", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReservedNamesAreOffLimits(string? name, bool reserved) =>
        Assert.Equal(reserved, PossessionOffLimits.IsReservedName(name));
}
