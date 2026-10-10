using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF LockCardService.NotifyCompleted: EMI's lockCardSolved carries {n} = tries (mistakes plus the
/// one that landed) only when there was a mistake, so "1 tries" is never said.</summary>
public sealed class LockCardSolvedCtxTests
{
    [Fact]
    public void ACleanCardCarriesNoNumber() => Assert.Null(LockCardWindow.SolvedCtx(0));

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 5)]
    public void MistakesPlusTheOneThatLanded(int mistakes, int tries)
    {
        var ctx = LockCardWindow.SolvedCtx(mistakes);
        Assert.NotNull(ctx);
        Assert.Equal(tries, (int)ctx!.GetType().GetProperty("n")!.GetValue(ctx)!);
    }
}
