using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Train 1 — the <c>UseCompanionBrain</c> kill switch as it is actually loaded from settings.json.
///
/// It defaults ON (the brain is the point of the release) but must be honoured when explicitly set
/// false, and — critically — an upgrader's settings.json that predates the key must land on the
/// default rather than on <c>default(bool)</c>. That failure mode is silent: the app would just keep
/// using the legacy stateless path and nobody would file a bug.
/// </summary>
public class CompanionBrainKillSwitchTests
{
    private static readonly JsonSerializerSettings LoaderSettings = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Error = (_, args) => { args.ErrorContext.Handled = true; }
    };

    private static AppSettings Load(string json)
        => JsonConvert.DeserializeObject<AppSettings>(json, LoaderSettings)!;

    [Fact]
    public void FreshInstall_BrainOn() => Assert.True(new AppSettings().UseCompanionBrain);

    [Fact]
    public void EmptyDocument_BrainOn() => Assert.True(Load("{}").UseCompanionBrain);

    [Fact]
    public void UpgraderWithoutTheKey_BrainOn()
    {
        const string json = """
        {
          "Welcomed": true,
          "LastSeenVersion": "6.7.0",
          "AiChatEnabled": true,
          "IdleGiggleIntervalSeconds": 120
        }
        """;
        Assert.True(Load(json).UseCompanionBrain);
    }

    [Fact]
    public void ExplicitFalse_IsHonoured()
        => Assert.False(Load("""{"UseCompanionBrain": false}""").UseCompanionBrain);

    [Fact]
    public void RoundTrips()
    {
        var settings = new AppSettings { UseCompanionBrain = false };
        var json = JsonConvert.SerializeObject(settings);
        Assert.Contains("\"UseCompanionBrain\":false", json);
        Assert.False(Load(json).UseCompanionBrain);
    }

    /// <summary>
    /// The purpose vocabulary is a contract with the server (MASTER-SCOPE §6.1) and with the
    /// [AI-METER] stream. Typos here are invisible client-side and land as "unknown purpose ->
    /// treat as chat" on the server, i.e. silently billed at the wrong tier.
    /// </summary>
    [Theory]
    [InlineData(AiPurpose.Chat, "chat")]
    [InlineData(AiPurpose.Reaction, "reaction")]
    [InlineData(AiPurpose.Memory, "memory")]
    [InlineData(AiPurpose.Summary, "summary")]
    public void PurposeWireNames_MatchTheServerContract(AiPurpose purpose, string expected)
    {
        var options = new AiCallOptions { Purpose = purpose };
        Assert.Equal(expected, options.PurposeWire);
        // The meter uses the same vocabulary so a log line and a request body can be correlated.
        Assert.Equal(expected, options.MeterPurpose);
    }

    [Fact]
    public void PresetOptions_MatchTheTierTable()
    {
        // Doc 01 §5.1: chat is tier A and interactive; reactions are the cheap tier and are never
        // allowed to escalate the user-facing Content Policy Notice.
        Assert.Equal(AiPurpose.Chat, AiCallOptions.Chat.Purpose);
        Assert.True(AiCallOptions.Chat.Interactive);

        Assert.Equal(AiPurpose.Reaction, AiCallOptions.Reaction.Purpose);
        Assert.False(AiCallOptions.Reaction.Interactive);
        Assert.True(AiCallOptions.Reaction.MaxTokens < AiCallOptions.Chat.MaxTokens);
    }
}
