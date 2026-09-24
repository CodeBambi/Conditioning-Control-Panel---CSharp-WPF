using System;
using System.Text.Json;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class CompanionProxyContractTests
{
    [Theory]
    [InlineData(401, "{}", AiFailureKind.SignInRequired, false)]
    [InlineData(503, "<html>gateway</html>", AiFailureKind.Unavailable, true)]
    [InlineData(429, "{\"error\":\"weekly_budget_exhausted\",\"retryable\":false}", AiFailureKind.BudgetLimit, false)]
    [InlineData(429, "{\"error\":\"daily_limit\"}", AiFailureKind.DailyLimit, false)]
    [InlineData(409, "{\"error\":\"request_in_progress\",\"retryable\":true}", AiFailureKind.Busy, true)]
    public void ErrorContract_ProducesStatusNotReply(int status, string body, AiFailureKind failure, bool retryable)
    {
        var result = CompanionProxyContract.ReadFailure(status, body);
        Assert.Equal(failure, result.Failure);
        Assert.Equal(retryable, result.Retryable);
        Assert.Empty(result.Text);
        Assert.False(result.IsAiGenerated);
    }

    [Theory]
    [InlineData("I was going to expl", "length")]
    [InlineData("{\"text\":\"unfinished", "stop")]
    [InlineData("<think>reasoning only</think>", "stop")]
    [InlineData("broken\uFFFDtext", "stop")]
    public void BrokenGeneration_IsNotDisplayed(string raw, string finish) =>
        Assert.Empty(CompanionProxyContract.CleanReply(raw, finish));

    [Fact]
    public void ValidShortReply_IsPreserved() =>
        Assert.Equal("sounds good", CompanionProxyContract.CleanReply("sounds good", "stop"));

    [Fact]
    public void LegacyRequest_OmitsPreviewFields_AndPreviewCarriesIdentifier()
    {
        var request = new V2ChatRequest();
        var legacy = JsonSerializer.Serialize(request);
        Assert.DoesNotContain("companion_protocol", legacy);
        Assert.DoesNotContain("request_id", legacy);
        request.CompanionProtocol = 2;
        request.RequestId = Guid.NewGuid().ToString("D");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(request));
        Assert.Equal(2, document.RootElement.GetProperty("companion_protocol").GetInt32());
        Assert.Equal(request.RequestId, document.RootElement.GetProperty("request_id").GetString());
    }

    [Theory]
    [InlineData(AiPurpose.Chat, 240)]
    [InlineData(AiPurpose.Reaction, 80)]
    [InlineData(AiPurpose.Memory, 300)]
    [InlineData(AiPurpose.Summary, 300)]
    public void PreviewCaps_ArePurposeSized(AiPurpose purpose, int cap) =>
        Assert.Equal(cap, AiCallOptions.PreviewTokenLimit(purpose));
}
