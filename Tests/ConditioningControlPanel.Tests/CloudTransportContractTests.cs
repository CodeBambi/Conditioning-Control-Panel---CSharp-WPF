using System;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The client half of the cloud transport contract (MASTER-SCOPE §6).
///
/// <para>Everything here is a pure helper or a serialization shape, deliberately: the failures these
/// cover are all silent. A dropped purpose costs money at the wrong tier and makes the client's
/// [AI-METER] line disagree with the server's [AI_USAGE] line about the same request; a PascalCase
/// body is rejected by the server's own validation; an unhandled <c>input_too_large</c> presents to
/// the user as "the companion stopped using AI" with nothing in the log but a 400.</para>
/// </summary>
public class CloudTransportContractTests
{
    // ---------- purpose tiering on the legacy one-shot path ----------

    [Theory]
    [InlineData(AiMeter.PurposeAwareness)]
    [InlineData(AiMeter.PurposeStillOn)]
    [InlineData(AiMeter.PurposeKeyword)]
    [InlineData(AiMeter.PurposeLockScreen)]
    [InlineData(AiMeter.PurposeVideoDone)]
    public void EveryAmbientLegacyCallSite_ResolvesToTheReactionTier(string meterPurpose)
    {
        // The legacy path is NOT dead: a keyword trigger or lock card with a CUSTOM prompt template
        // fails CanRouteAmbient and falls through to it, as do the autonomy nudge and the
        // double-click random thought. Sending no purpose made the server resolve those one-line
        // quips at the chat tier.
        Assert.Equal("reaction", AiService.LegacyPurposeWire(meterPurpose));
    }

    [Fact]
    public void TheLegacyChatCallSite_StillResolvesToTheChatTier()
    {
        Assert.Equal("chat", AiService.LegacyPurposeWire(AiMeter.PurposeChat));
        Assert.Equal("chat", AiService.LegacyPurposeWire("something-unknown"));
    }

    // ---------- wire shape ----------

    [Fact]
    public void TheLegacyRequestBodySerializesInSnakeCase()
    {
        // The body goes out through JsonContent.Create — System.Text.Json with default options —
        // which ignores the Newtonsoft attributes entirely. Without JsonPropertyName this serialized
        // as PascalCase: the server's `!messages || !Array.isArray(messages)` guard rejects that
        // outright, and "Purpose" was silently discarded by its destructure.
        var json = JsonSerializer.Serialize(new ProxyChatRequest
        {
            Messages = new[] { new ProxyChatMessage { Role = "user", Content = "hi" } },
            MaxTokens = 60,
            Temperature = 0.8,
            Purpose = "reaction"
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("messages", out _));
        Assert.True(root.TryGetProperty("max_tokens", out _));
        Assert.True(root.TryGetProperty("temperature", out _));
        Assert.Equal("reaction", root.GetProperty("purpose").GetString());
        Assert.False(root.TryGetProperty("Purpose", out _));
    }

    [Fact]
    public void ANullPurposeIsOmittedEntirely_SoAPreTierServerSeesTodaysBody()
    {
        var json = JsonSerializer.Serialize(new ProxyChatRequest
        {
            Messages = Array.Empty<ProxyChatMessage>(),
            Purpose = null
        });

        Assert.DoesNotContain("purpose", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheResponseCarriesTheServersTokenAccounting_WhenItReportsIt()
    {
        // cached_in is the ONLY client-visible signal that the stable prefix is being
        // cache-discounted: [AI-METER]'s in_tok is chars/4 of what we SENT and reads identically
        // whether the provider cache hit or missed.
        const string body = """
        {"content":"hi~","requests_remaining":42,"purpose":"chat",
         "tokens_used":{"in":2300,"cached_in":1900,"out":58},"tokens_remaining_today":37700}
        """;

        var parsed = JsonSerializer.Deserialize<ProxyChatResponse>(body);

        Assert.NotNull(parsed);
        Assert.Equal("chat", parsed!.Purpose);
        Assert.Equal(1900, parsed.TokensUsed?.CachedIn);
        Assert.Equal(2300, parsed.TokensUsed?.In);
        Assert.Equal(37700, parsed.TokensRemainingToday);
        Assert.Equal(42, parsed.RequestsRemaining);
    }

    [Fact]
    public void TodaysProductionResponseStillParses_WithTheNewFieldsNull()
    {
        var parsed = JsonSerializer.Deserialize<ProxyChatResponse>("""{"content":"hi~","requests_remaining":9}""");

        Assert.Equal("hi~", parsed!.Content);
        Assert.Null(parsed.TokensUsed);
        Assert.Null(parsed.TokensRemainingToday);
    }

    // ---------- input_too_large ----------

    [Fact]
    public void TheProxysOversizeRejectionIsRecognised()
    {
        Assert.True(AiService.IsInputTooLarge(
            """{"error":"input_too_large","message":"Message content too long (max 10000 chars)"}"""));
        Assert.False(AiService.IsInputTooLarge("""{"error":"rate_limited"}"""));
        Assert.False(AiService.IsInputTooLarge(""));
        Assert.False(AiService.IsInputTooLarge(null));
    }

    [Fact]
    public void CompactForRetry_KeepsTheSystemPromptAndTheNewestTurns()
    {
        var messages = new[] { "system", "user", "assistant", "user", "assistant", "user" }
            .Select((role, i) => new ProxyChatMessage
            {
                Role = i == 0 ? ChatMessage.RoleSystem : role,
                Content = i == 0 ? "SYSTEM" : $"m{i}"
            })
            .ToArray();

        var compacted = AiService.CompactForRetry(messages, keepTail: 2);

        Assert.NotNull(compacted);
        Assert.Equal(3, compacted!.Length);
        Assert.Equal(ChatMessage.RoleSystem, compacted[0].Role);
        Assert.Equal("m4", compacted[1].Content);
        Assert.Equal("m5", compacted[2].Content);   // the turn she is actually answering
    }

    [Fact]
    public void CompactForRetry_GivesUpWhenTheSystemMessageAloneIsTheProblem()
    {
        // Nothing left to shed means the prompt itself is over the cap — retrying the same body
        // forever would just burn the daily request budget.
        var messages = new[]
        {
            new ProxyChatMessage { Role = ChatMessage.RoleSystem, Content = new string('s', 20000) },
            new ProxyChatMessage { Role = ChatMessage.RoleUser, Content = "hi" }
        };

        Assert.Null(AiService.CompactForRetry(messages, keepTail: 4));
        Assert.Null(AiService.CompactForRetry(Array.Empty<ProxyChatMessage>(), keepTail: 4));
    }

    // ---------- local provider sampling knobs ----------

    [Fact]
    public void TheLocalChatPayloadCarriesTheCallersTokenAndTemperatureBudget()
    {
        // AiCallOptions.Reaction asks for 60 tokens at 0.8 precisely so an ambient quip stays a
        // one-liner. Dropping that silently lets Ollama use the model's default num_predict — a
        // multi-paragraph "reaction" that then inflates every later window.
        var json = LocalAiService.BuildChatPayload("qwen3",
            new[] { ("system", (string?)"SYSTEM"), ("user", (string?)"hi") },
            maxTokens: 60, temperature: 0.8);

        using var doc = JsonDocument.Parse(json);
        var options = doc.RootElement.GetProperty("options");
        Assert.Equal(60, options.GetProperty("num_predict").GetInt32());
        Assert.Equal(0.8, options.GetProperty("temperature").GetDouble(), 3);
        Assert.False(doc.RootElement.GetProperty("think").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public void EveryLocalChatPayloadCarriesAContextWindow()
    {
        // #856: with no num_ctx Ollama applies its own default (2048 on most builds) and drops the
        // OLDEST tokens - the persona and the rules - with no error anywhere. The window must cover
        // everything PromptAssembler is allowed to send plus the reply budget.
        foreach (var json in new[]
                 {
                     LocalAiService.BuildChatPayload("qwen3",
                         new[] { ("user", (string?)"hi") }, maxTokens: null, temperature: null),
                     LocalAiService.BuildChatPayload("qwen3",
                         new[] { ("user", (string?)"hi") }, maxTokens: 60, temperature: 0.8)
                 })
        {
            using var doc = JsonDocument.Parse(json);
            var numCtx = doc.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32();
            Assert.True(numCtx >= PromptAssembler.ContextFitTokenBudget + 60,
                $"num_ctx {numCtx} does not cover the assembler's budget");
            Assert.True(numCtx >= 8192, $"num_ctx {numCtx} is below the 8k floor");
        }
    }

    [Fact]
    public void TheLegacyLocalPayloadStillCarriesNoSamplingKnobs_WhenNoBudgetIsGiven()
    {
        var json = LocalAiService.BuildChatPayload("qwen3",
            new[] { ("user", (string?)"hi") }, maxTokens: null, temperature: null);

        using var doc = JsonDocument.Parse(json);
        var options = doc.RootElement.GetProperty("options");
        Assert.False(options.TryGetProperty("num_predict", out _));
        Assert.False(options.TryGetProperty("temperature", out _));
    }
}
