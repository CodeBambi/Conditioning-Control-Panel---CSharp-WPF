using System;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The friends honesty pass (2026-09-28): a list change answers what the server said, and each
/// refusal has words of its own, so no surface cheers before the call came back.
/// </summary>
public class FriendsHonestyTests
{
    [Theory]
    [InlineData("not_found", ActResult.NotFound)]
    [InlineData("full", ActResult.Full)]
    [InlineData("too_fast", ActResult.TooFast)]
    [InlineData("bad_input", ActResult.Refused)]
    [InlineData("blocked", ActResult.Refused)]
    [InlineData(null, ActResult.TryLater)]
    [InlineData("something_new", ActResult.TryLater)]
    public void A_worded_refusal_keeps_its_reason(string? reason, ActResult expected)
        => Assert.Equal(expected, FriendsApi.ActFromWire(reason));

    [Fact]
    public void Every_refusal_has_words_and_none_reads_as_a_success()
    {
        foreach (var r in Enum.GetValues<ActResult>())
        {
            var key = FriendsDrawerRules.ActResultKey(r);
            Assert.StartsWith("friends_", key);
            Assert.NotEqual("friends_result_sent", key);
        }
        Assert.Equal("friends_act_not_found", FriendsDrawerRules.ActResultKey(ActResult.NotFound));
    }

    [Fact]
    public void A_result_line_stays_long_enough_to_read()
        => Assert.True(FriendsDrawerRules.ResultHoldSeconds >= 4);
}
