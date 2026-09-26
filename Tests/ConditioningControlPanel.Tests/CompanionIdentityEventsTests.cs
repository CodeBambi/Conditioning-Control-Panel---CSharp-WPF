using System;
using Xunit;

namespace ConditioningControlPanel.Tests;

[CollectionDefinition("CompanionIdentityEvents", DisableParallelization = true)]
public sealed class CompanionIdentityEventsCollection { }

[Collection("CompanionIdentityEvents")]
public class CompanionIdentityEventsTests
{
    [Fact]
    public void EveryIdentityChangeNotifiesOnceAndObserverFailureCannotBreakSignIn()
    {
        var previous = App.UnifiedUserId;
        int calls = 0;
        string? seen = null;
        EventHandler broken = (_, _) => throw new InvalidOperationException("synthetic observer");
        EventHandler observer = (_, _) => { calls++; seen = App.UnifiedUserId; };
        App.UnifiedIdentityChanged += broken;
        App.UnifiedIdentityChanged += observer;
        try
        {
            App.UnifiedUserId = "synthetic-account-A";
            App.UnifiedUserId = "synthetic-account-A";
            Assert.Equal(1, calls);
            Assert.Equal("synthetic-account-A", seen);
            App.UnifiedUserId = "synthetic-account-B";
            App.UnifiedUserId = null;
            Assert.Equal(3, calls);
            Assert.Null(seen);
        }
        finally
        {
            App.UnifiedIdentityChanged -= observer;
            App.UnifiedIdentityChanged -= broken;
            App.UnifiedUserId = previous;
        }
    }
}
