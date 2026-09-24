using System;
using ConditioningControlPanel.Services.AIService;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class CompanionQuotaTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 20, 0, 0, TimeSpan.Zero);
    private static string Reset => Now.AddHours(4).ToString("O");

    [Fact]
    public void DailyLimitFailure_ReplacesStalePositiveAllowance()
    {
        var quota = new CompanionQuota();
        quota.Update("a", "a", 8, Reset);
        var error = "{\"companion_protocol\":2,\"request_id\":\"same\",\"error\":\"daily_limit\",\"requests_remaining\":0,\"resets_at\":\"" + Reset + "\"}";
        var response = CompanionProxyContract.ReadQuota(error, "same");
        Assert.NotNull(response);
        quota.Update("a", "a", response.RequestsRemaining, response.ResetsAt);
        Assert.Equal(0, quota.Remaining("a", Now));
    }

    [Fact]
    public void AccountSwitch_DoesNotAttributeOldReplyQuotaToNewAccount()
    {
        var quota = new CompanionQuota();
        quota.Update("a", "a", 20, Reset);
        quota.Update("a", "b", 19, Reset);
        Assert.Equal(-1, quota.Remaining("b", Now));
        Assert.Equal(20, quota.Remaining("a", Now));
        quota.Update("b", "b", 4, Reset);
        Assert.Equal(4, quota.Remaining("b", Now));
        Assert.Equal(-1, quota.Remaining("a", Now));
    }

    [Fact]
    public void UnknownOrExpiredQuota_IsNotInvented()
    {
        var quota = new CompanionQuota();
        Assert.Equal(-1, quota.Remaining("a", Now));
        quota.Update("a", "a", 5, Reset);
        Assert.Equal(-1, quota.Remaining("a", Now.AddHours(4)));
        Assert.Equal(-1, quota.Remaining(null, Now));
    }

    [Theory]
    [InlineData("<html>bad gateway</html>")]
    [InlineData("{\"requests_remaining\":0}")]
    [InlineData("{\"companion_protocol\":2,\"request_id\":\"wrong\",\"requests_remaining\":0}")]
    public void InvalidQuotaEnvelope_IsIgnored(string body) =>
        Assert.Null(CompanionProxyContract.ReadQuota(body, "expected"));
}
