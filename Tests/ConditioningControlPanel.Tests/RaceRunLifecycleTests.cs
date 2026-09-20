using ConditioningControlPanel.Services.Race;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class RaceRunLifecycleTests
{
    [Fact]
    public void DuplicateStartAndEndPayOnlyOnceButTheNextRunCanPay()
    {
        var run = new RaceRunLifecycle();
        int rewards = 0;
        void End() { if (run.TryEnd()) rewards++; }
        End();
        Assert.True(run.TryStart());
        Assert.False(run.TryStart());
        End();
        End();
        Assert.Equal(1, rewards);
        Assert.True(run.TryStart());
        End();
        Assert.Equal(2, rewards);
    }

    [Fact]
    public void TeardownCannotLeaveAnUnclaimedPayoutForTheNextPage()
    {
        var run = new RaceRunLifecycle();
        run.TryStart();
        run.Reset();
        Assert.False(run.TryEnd());
        Assert.True(run.TryStart());
        Assert.True(run.TryEnd());
        Assert.False(run.TryEnd());
    }
}
