using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE POST-ZERO RETRY's policy (0825 hunt, F1). The director asks for a sync once a minute after
/// the zero show ended without a ceremony; every reason to stop is a parameter here, so the whole
/// loop's termination is pinned without a dispatcher.
/// </summary>
public class DescentPostZeroRetryTests
{
    private static bool Go(int attempts = 0, bool armed = true, bool open = false, bool offer = false,
        bool done = false, bool pending = false) =>
        DescentPostZeroRetry.ShouldContinue(attempts, armed, open, offer, done, pending);

    [Fact]
    public void FreshAfterATimeout_Continues() => Assert.True(Go());

    [Fact]
    public void Budget_IsBounded()
    {
        Assert.True(Go(attempts: DescentPostZeroRetry.MaxAttempts - 1));
        Assert.False(Go(attempts: DescentPostZeroRetry.MaxAttempts));
        Assert.False(Go(attempts: DescentPostZeroRetry.MaxAttempts + 100));
    }

    /// <summary>Fifteen minutes is the intent: past the herd, short of a nag.</summary>
    [Fact]
    public void Budget_IsAboutFifteenMinutes()
    {
        var total = DescentPostZeroRetry.Every * DescentPostZeroRetry.MaxAttempts;
        Assert.InRange(total.TotalMinutes, 10, 20);
        // Every attempt must land outside ProfileSyncService's 30s cooldown or it is wasted.
        Assert.True(DescentPostZeroRetry.Every.TotalSeconds > 30);
    }

    [Fact]
    public void CeremonyOpen_Stops() => Assert.False(Go(open: true));

    [Fact]
    public void OfferInHand_Stops() => Assert.False(Go(offer: true));

    [Fact]
    public void Migrated_Stops() => Assert.False(Go(done: true));

    [Fact]
    public void ChoicePending_Stops() => Assert.False(Go(pending: true));

    /// <summary>The kill switch ends it too: no fuse, no ceremony to chase.</summary>
    [Fact]
    public void FuseDark_Stops() => Assert.False(Go(armed: false));
}
