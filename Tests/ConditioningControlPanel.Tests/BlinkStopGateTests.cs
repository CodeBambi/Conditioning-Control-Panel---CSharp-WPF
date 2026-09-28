using ConditioningControlPanel.Services.Safety;
using Xunit;
using static ConditioningControlPanel.Services.Safety.BlinkStopGate;

namespace ConditioningControlPanel.Tests;

/// <summary>The 6-blink stop is never more permissive than the panic key (tester ticket 2026-09-27, owner 2026-09-28).</summary>
public class BlinkStopGateTests
{
    [Fact]
    public void FiresWhenNothingHoldsTheUserIn()
        => Assert.Equal(Block.None, Check(false, false, false, panicKeyEnabled: true, strictLockEnabled: false));

    [Fact]
    public void NoEscapeMeansNoBlinkEscape()
        => Assert.Equal(Block.NoEscape, Check(false, false, false, panicKeyEnabled: false, strictLockEnabled: false));

    [Fact]
    public void StrictLockIgnoresTheBlinkRun()
        => Assert.Equal(Block.StrictLock, Check(false, false, false, panicKeyEnabled: true, strictLockEnabled: true));

    [Fact]
    public void LockdownIgnoresTheBlinkRunWhateverElseIsSet()
    {
        Assert.Equal(Block.Lockdown, Check(false, true, false, true, false));
        Assert.Equal(Block.Lockdown, Check(false, true, true, false, true));
    }

    [Fact]
    public void TheBlinkTrainerOwnsItsBlinks()
        => Assert.Equal(Block.BlinkTrainer, Check(true, true, true, false, true));

    [Fact]
    public void ALeashIsCutNotBlinkedAway()
        => Assert.Equal(Block.Leashed, Check(false, false, true, true, false));
}
