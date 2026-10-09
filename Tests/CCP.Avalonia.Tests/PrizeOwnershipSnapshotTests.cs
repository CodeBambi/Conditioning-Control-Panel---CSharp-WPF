using System;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>progression#17 (WPF OwnershipService.ApplySnapshot + PrizeFeed.Apply): a prize the server
/// says the signed-in account owns reads owned; another account's block and stale revisions never show.</summary>
public sealed class PrizeOwnershipSnapshotTests
{
    [Fact]
    public void ValidateBlockMakesBoughtPrizesOwned()
    {
        var oldAccount = PrizeOwnership.CurrentAccount;
        var oldSink = ProviderSubscription.PrizesSink;
        string? me = "u_me";
        PrizeOwnership.CurrentAccount = () => me;
        try
        {
            PrizeOwnership.Clear();
            PrizeOwnership.Seed();
            Assert.False(PrizeOwnership.IsGranted(PrizeOwnership.FlashDriftBounce));

            ProviderSubscription.PrizesSink!("u_me", "u_me",
                new PrizesBlock { Revision = 5, Grants = new List<string> { " fx.flash.drift_bounce ", "" } }, "test");
            Assert.True(PrizeOwnership.IsGranted(PrizeOwnership.FlashDriftBounce));

            // An older revision never takes a prize away; a block answered for another record is ignored.
            PrizeOwnership.Apply("u_me", "u_me", new PrizesBlock { Revision = 4, Grants = new List<string>() }, "test");
            PrizeOwnership.Apply("u_me", "u_other", new PrizesBlock { Revision = 9, Grants = new List<string>() }, "test");
            PrizeOwnership.Apply("u_me", "u_me", null, "test");
            Assert.True(PrizeOwnership.IsGranted(PrizeOwnership.FlashDriftBounce));

            // Another account signed in: the held grants are not theirs.
            me = "u_other";
            Assert.False(PrizeOwnership.IsGranted(PrizeOwnership.FlashDriftBounce));
            PrizeOwnership.Apply("u_me", "u_me", new PrizesBlock { Revision = 6, Grants = new List<string> { "x" } }, "test");
            Assert.False(PrizeOwnership.IsGranted("x"));
        }
        finally
        {
            PrizeOwnership.Clear();
            PrizeOwnership.CurrentAccount = oldAccount;
            ProviderSubscription.PrizesSink = oldSink;
        }
    }
}
