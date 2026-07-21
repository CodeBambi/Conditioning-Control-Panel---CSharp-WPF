using System.Reflection;
using System.Text.Json;
using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Mechanics tests for the AI operation contract slice (client/docs/ai-operation-contract.md).
/// DEFINE-ONLY: no providers, no network. Tests prove envelope reject-by-default, whole-envelope
/// atomic rejection, per-command results, diagnostic content-freedom (schema proof),
/// generation-invalidation reuse against the real SP-004 registry, and serialization round-trips.
/// </summary>
public class AiOperationContractTests
{
    private static readonly AiEnvelopePolicy Permit = AiEnvelopePolicy.PermitAll;

    // ---- envelope: valid ----

    [Fact]
    public void ValidEnvelope_Validates_AndProducesPlan()
    {
        const string json = """
            { "reply": "hi", "commands": [
              { "command": "bubbles", "data": { "on": true, "frequency": 5 } },
              { "command": "subliminal", "data": { "text": "obey", "opacity": 30 } }
            ] }
            """;

        var result = AiEnvelopeValidator.Validate(json, Permit);

        Assert.True(result.Accepted);
        Assert.Null(result.EnvelopeRejectionCode);
        Assert.Equal("hi", result.Reply);
        Assert.NotNull(result.Plan);
        Assert.Equal(2, result.Plan.Commands.Count);
        Assert.All(result.Verdicts, v => Assert.IsType<AiCommandVerdict.Valid>(v));
        Assert.Equal(AiCommandKind.Bubbles, result.Plan.Commands[0].Kind);
        Assert.Equal(new AiCommandData.Subliminal("obey", 30), result.Plan.Commands[1].Data);
    }

    [Fact]
    public void EnvelopeWithoutCommands_IsAccepted_WithEmptyPlan()
    {
        var result = AiEnvelopeValidator.Validate("""{ "reply": "just text" }""", Permit);

        Assert.True(result.Accepted);
        Assert.NotNull(result.Plan);
        Assert.Empty(result.Plan.Commands);
        Assert.Empty(result.Verdicts);
    }

    [Fact]
    public void EveryCommandKind_RoundTripsThroughValidation()
    {
        const string json = """
            { "commands": [
              { "command": "flash_image", "data": { "amount": 3, "duration": 5, "size": 100, "opacity": 50 } },
              { "command": "bubbles", "data": { "on": true, "frequency": 10 } },
              { "command": "subliminal", "data": { "text": "pink", "opacity": 60 } },
              { "command": "mantra_lockscreen", "data": { "mantra": "good girl", "amount": 2 } },
              { "command": "spiral", "data": { "on": true, "intensity": 30 } },
              { "command": "pink", "data": { "on": false, "intensity": 0 } },
              { "command": "bounce", "data": { "on": true, "words": "empty blank" } },
              { "command": "haptic", "data": { "intensity": 0.5, "duration": 3 } },
              { "command": "video", "data": { "path": "videos/loop.mp4" } },
              { "command": "audio", "data": { "random": true } },
              { "command": "getbacktome", "data": { "token": "t1", "delay": 60, "jsonOnly": true } }
            ] }
            """;

        var result = AiEnvelopeValidator.Validate(json, Permit with { MaxCommandsPerResponse = 20 });

        Assert.True(result.Accepted, result.EnvelopeRejectionCode);
        Assert.All(result.Verdicts, v => Assert.IsType<AiCommandVerdict.Valid>(v));
    }

    // ---- envelope: reject-by-default ----

    [Fact]
    public void MalformedJson_IsRejected()
    {
        var result = AiEnvelopeValidator.Validate("{ not json", Permit);
        Assert.False(result.Accepted);
        Assert.Equal("malformed-json", result.EnvelopeRejectionCode);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void UnknownCommand_IsRejected()
    {
        var result = AiEnvelopeValidator.Validate(
            """{ "commands": [ { "command": "mind_wipe", "data": {} } ] }""", Permit);

        Assert.False(result.Accepted);
        var verdict = Assert.IsType<AiCommandVerdict.UnknownCommand>(Assert.Single(result.Verdicts));
        Assert.Equal("mind_wipe", verdict.Name);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void UnknownField_IsRejected()
    {
        var result = AiEnvelopeValidator.Validate(
            """{ "commands": [ { "command": "bubbles", "data": { "on": true, "frequency": 5, "evil": 1 } } ] }""", Permit);

        Assert.False(result.Accepted);
        var verdict = Assert.IsType<AiCommandVerdict.MalformedData>(Assert.Single(result.Verdicts));
        Assert.Equal("evil", verdict.Field);
        Assert.Equal("unknown-field", verdict.Code);
    }

    [Fact]
    public void OutOfRange_IsRejected_NeverClamped()
    {
        var result = AiEnvelopeValidator.Validate(
            """{ "commands": [ { "command": "flash_image", "data": { "amount": 99, "duration": 5, "size": 100, "opacity": 50 } } ] }""", Permit);

        Assert.False(result.Accepted);
        var verdict = Assert.IsType<AiCommandVerdict.OutOfRange>(Assert.Single(result.Verdicts));
        Assert.Equal("amount", verdict.Field);
        Assert.Equal("0-8", verdict.Limit);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void WrongType_IsRejected()
    {
        var result = AiEnvelopeValidator.Validate(
            """{ "commands": [ { "command": "bubbles", "data": { "on": "yes", "frequency": 5 } } ] }""", Permit);

        Assert.False(result.Accepted);
        var verdict = Assert.IsType<AiCommandVerdict.MalformedData>(Assert.Single(result.Verdicts));
        Assert.Equal("on", verdict.Field);
        Assert.Equal("wrong-type", verdict.Code);
    }

    // ---- envelope: whole-envelope atomic rejection (mixed payload → zero execution) ----

    [Fact]
    public void MixedEnvelope_RejectsAtomically_ValidSiblingIsNotExecuted()
    {
        const string json = """
            { "commands": [
              { "command": "bubbles", "data": { "on": true, "frequency": 5 } },
              { "command": "flash_image", "data": { "amount": 99, "duration": 5, "size": 100, "opacity": 50 } }
            ] }
            """;

        var result = AiEnvelopeValidator.Validate(json, Permit);

        Assert.False(result.Accepted);
        Assert.Null(result.Plan); // zero execution: no executable representation exists
        Assert.Equal(2, result.Verdicts.Count);
        var sibling = Assert.IsType<AiCommandVerdict.NotExecuted>(result.Verdicts[0]);
        Assert.Equal(AiNotExecutedReason.EnvelopeRejected, sibling.Reason);
        Assert.IsType<AiCommandVerdict.OutOfRange>(result.Verdicts[1]);
    }

    [Fact]
    public void InvalidEnvelope_HasNoExecutableRepresentation_ByType()
    {
        // Zero execution semantics as TYPES (contract §8 rule 4): AiExecutionPlan exposes no
        // public constructor — only the validator can create one, and only for a valid envelope.
        Assert.Empty(typeof(AiExecutionPlan).GetConstructors());
    }

    // ---- gating: consent + moderation + cap ----

    [Fact]
    public void MasterToggleOff_GatesEveryCommand_EnvelopeStillAccepted()
    {
        var policy = Permit with { MasterEffectsEnabled = false };
        var result = AiEnvelopeValidator.Validate(
            """{ "commands": [ { "command": "bubbles", "data": { "on": true, "frequency": 5 } } ] }""", policy);

        Assert.True(result.Accepted);
        var verdict = Assert.IsType<AiCommandVerdict.ConsentGated>(Assert.Single(result.Verdicts));
        Assert.Equal("master", verdict.Toggle);
        Assert.Empty(result.Plan!.Commands);
    }

    [Fact]
    public void PerEffectToggleOff_GatesThatCommandOnly()
    {
        var policy = Permit with { IsEffectAllowed = k => k != AiCommandKind.Haptic };
        const string json = """
            { "commands": [
              { "command": "bubbles", "data": { "on": true, "frequency": 5 } },
              { "command": "haptic", "data": { "intensity": 0.5, "duration": 3 } }
            ] }
            """;

        var result = AiEnvelopeValidator.Validate(json, policy);

        Assert.True(result.Accepted);
        Assert.IsType<AiCommandVerdict.Valid>(result.Verdicts[0]);
        Assert.IsType<AiCommandVerdict.ConsentGated>(result.Verdicts[1]);
        Assert.Single(result.Plan!.Commands);
    }

    [Fact]
    public void ModerationBlocks_FreeTextCommandFields()
    {
        var policy = Permit with { ModerateText = t => t.Contains("banned") ? new AiModerationVerdict.Block("test-category") : AiModerationVerdict.Pass.Instance };
        var result = AiEnvelopeValidator.Validate(
            """{ "commands": [ { "command": "subliminal", "data": { "text": "banned text", "opacity": 30 } } ] }""", policy);

        Assert.True(result.Accepted);
        var verdict = Assert.IsType<AiCommandVerdict.ModerationBlocked>(Assert.Single(result.Verdicts));
        Assert.Equal("test-category", verdict.CategoryCode);
        Assert.Empty(result.Plan!.Commands);
    }

    [Fact]
    public void PerResponseCap_ExcessValidCommandsAreNotExecuted()
    {
        var policy = Permit with { MaxCommandsPerResponse = 1 };
        const string json = """
            { "commands": [
              { "command": "bubbles", "data": { "on": true, "frequency": 5 } },
              { "command": "bounce", "data": { "on": true } }
            ] }
            """;

        var result = AiEnvelopeValidator.Validate(json, policy);

        Assert.True(result.Accepted);
        Assert.IsType<AiCommandVerdict.Valid>(result.Verdicts[0]);
        var capped = Assert.IsType<AiCommandVerdict.NotExecuted>(result.Verdicts[1]);
        Assert.Equal(AiNotExecutedReason.CapExceeded, capped.Reason);
        Assert.Single(result.Plan!.Commands);
    }

    // ---- media path rules (contract §8 rule 5) ----

    [Theory]
    [InlineData("""{ "command": "video", "data": { "path": "../escape.mp4" } }""", "traversal")]
    [InlineData("""{ "command": "video", "data": { "path": "\\\\server\\share\\v.mp4" } }""", "unc")]
    [InlineData("""{ "command": "video", "data": { "path": "clip.exe" } }""", "extension")]
    [InlineData("""{ "command": "video", "data": { "path": "v.mp4", "random": true } }""", "path-and-random")]
    public void MediaPathViolations_AreRejected(string commandJson, string code)
    {
        var result = AiEnvelopeValidator.Validate($"{{ \"commands\": [ {commandJson} ] }}", Permit);

        Assert.False(result.Accepted);
        var verdict = Assert.IsType<AiCommandVerdict.MalformedData>(Assert.Single(result.Verdicts));
        Assert.Equal(code, verdict.Code);
    }

    [Fact]
    public void MediaPathOutsideAssetsRoot_IsRejected()
    {
        var policy = Permit with { AssetsRoot = Path.Combine(Path.GetTempPath(), "ccp-assets") };
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere.mp4");
        var json = $$"""{ "commands": [ { "command": "video", "data": { "path": {{JsonSerializer.Serialize(outside)}} } } ] }""";

        var result = AiEnvelopeValidator.Validate(json, policy);

        Assert.False(result.Accepted);
        var verdict = Assert.IsType<AiCommandVerdict.MalformedData>(Assert.Single(result.Verdicts));
        Assert.Equal("escapes-root", verdict.Code);
    }

    // ---- diagnostics: content-freedom is a schema rule (contract §12) ----

    [Fact]
    public void DiagnosticRecord_PropertySetIsExactlyTheContentFreeAllowList()
    {
        // Structural proof, not an instance check: the type's serializable property set IS the schema.
        var expected = new[]
        {
            nameof(AiDiagnosticRecord.OperationClass),
            nameof(AiDiagnosticRecord.EndpointClass),
            nameof(AiDiagnosticRecord.Outcome),
            nameof(AiDiagnosticRecord.StableCode),
            nameof(AiDiagnosticRecord.Generation),
            nameof(AiDiagnosticRecord.DurationMilliseconds),
            nameof(AiDiagnosticRecord.CommandCount),
            nameof(AiDiagnosticRecord.CommandVerdictCodes),
        };

        var actual = typeof(AiDiagnosticRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal), actual);
    }

    [Fact]
    public void DiagnosticRecord_PropertyTypesCarryNoFreeText()
    {
        // Every property is an enum, a stable-token string, a count/duration, or a code list.
        // A free-text payload field would be a new string property outside this allow-list.
        var stringProps = typeof(AiDiagnosticRecord).GetProperties()
            .Where(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(string[]))
            .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            new[] { nameof(AiDiagnosticRecord.CommandVerdictCodes), nameof(AiDiagnosticRecord.StableCode) },
            stringProps);
    }

    [Fact]
    public void DiagnosticRecord_SerializesOnlyContentFreeFields()
    {
        var record = new AiDiagnosticRecord(
            AiOperationClass.Interactive, AiEndpointClass.Loopback, AiDiagnosticOutcome.Faulted,
            "TimeoutException", 2, 1500, 1, ["OutOfRange"]);

        var json = JsonSerializer.Serialize(record);
        var parsed = JsonDocument.Parse(json);

        Assert.Equal(
            new[] { "CommandCount", "CommandVerdictCodes", "DurationMilliseconds", "EndpointClass", "Generation", "OperationClass", "Outcome", "StableCode" },
            parsed.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        // Enums serialize as numbers by default — no surface for text smuggling.
        Assert.Equal(JsonValueKind.Number, parsed.RootElement.GetProperty("Outcome").ValueKind);
    }

    // ---- generation invalidation reuse (contract §2/§3, SP-004 machinery) ----

    [Fact]
    public async Task ProviderSwitch_CancelsInFlightGeneration_AndDiscardsStaleCompletion()
    {
        // A provider switch IS an owner restart (contract §3 rule 2): generation invalidation
        // → token cancellation → stale-application discard, all SP-004 machinery, no new code.
        var registry = new OperationRegistry();
        var owner = registry.OwnerFor("ai-provider");

        owner.Begin(); // generation 0: provider A selected
        var inFlight = owner.RunAsync("ai-reply", async ct =>
        {
            await Task.Delay(TimeSpan.FromMinutes(5), ct); // in-flight network/inference work
            return OperationOutcome.Completed.Instance;
        });

        owner.Begin(); // provider switch: generation 1 — cancels generation 0's token

        var outcome = await inFlight;
        Assert.IsType<OperationOutcome.Cancelled>(outcome); // cancellation is the mechanism
        Assert.Null(owner.LastOutcome); // the stale Cancelled completion is NOT applied over generation 1
        Assert.Equal(1, registry.DiscardedStaleCompletions); // stale-application discard is the backstop
    }

    // ---- endpoint classification (contract §6) ----

    [Theory]
    [InlineData("http://localhost:11434/", AiEndpointClass.Loopback)]
    [InlineData("http://127.0.0.1:11434/", AiEndpointClass.Loopback)]
    [InlineData("http://[::1]:11434/", AiEndpointClass.Loopback)]
    [InlineData("http://192.168.1.50:11434/", AiEndpointClass.RemoteHostOllama)]
    [InlineData("https://ollama.example.com/", AiEndpointClass.RemoteHostOllama)]
    public void OllamaHost_ClassifiesLoopbackVsRemote(string host, AiEndpointClass expected)
    {
        Assert.Equal(expected, AiEndpointClassifier.ClassifyOllamaHost(new Uri(host)));
    }

    [Theory]
    [InlineData("https://codebambi-proxy.vercel.app/v2/ai/chat", AiEndpointClass.FirstPartyCloud)]
    [InlineData("https://api.openai.com/v1/chat", AiEndpointClass.ThirdPartyCloud)]
    [InlineData("http://localhost:8080/v1/chat", AiEndpointClass.Loopback)]
    public void ProviderEndpoint_Classifies(string endpoint, AiEndpointClass expected)
    {
        Assert.Equal(expected, AiEndpointClassifier.ClassifyProviderEndpoint(new Uri(endpoint)));
    }

    // ---- serialization round-trips of every vocabulary type ----

    [Fact]
    public void VocabularyTypes_SerializeRoundTrip()
    {
        RoundTrip<AiReply>(new AiReply.Generated("hello", AiEndpointClass.FirstPartyCloud));
        RoundTrip<AiReply>(new AiReply.Refused(new AiModerationRefusal("cat-1", AiModerationSource.Output)));
        RoundTrip<AiReply>(new AiReply.Unavailable(AiReplyCodes.Offline));
        RoundTrip<AiReply>(new AiReply.Fallback("canned", AiReplyCodes.QuotaExhausted));
        RoundTrip<AiModerationVerdict>(AiModerationVerdict.Pass.Instance);
        RoundTrip<AiModerationVerdict>(new AiModerationVerdict.SoftHit("advice"));
        RoundTrip<AiModerationVerdict>(new AiModerationVerdict.Block("minor"));
        RoundTrip<AiAdmission>(AiAdmission.Admitted.Instance);
        RoundTrip<AiAdmission>(new AiAdmission.Suppressed(AiSuppressionKind.Cooldown));
        RoundTrip(new AiMemoryTurn(AiMemoryRole.User, "hi"));

        foreach (var verdict in VerdictSamples())
            RoundTrip<AiCommandVerdict>(verdict);
    }

    [Fact]
    public void PerCommandResults_RoundTripThroughSerialization()
    {
        // The envelope result is the honest serializable record of why nothing ran (contract §9 rule 2).
        var result = AiEnvelopeValidator.Validate(
            """{ "commands": [ { "command": "nope", "data": {} } ] }""", Permit);

        Assert.False(result.Accepted);
        Assert.IsType<AiCommandVerdict.UnknownCommand>(Assert.Single(result.Verdicts));

        // Round-trip each concrete verdict type directly (closed taxonomy).
        foreach (var verdict in VerdictSamples())
        {
            var serialized = JsonSerializer.Serialize(verdict, verdict.GetType());
            var restored = JsonSerializer.Deserialize(serialized, verdict.GetType());
            Assert.Equal(verdict, restored);
        }
    }

    [Fact]
    public void DiagnosticRecord_RoundTrips()
    {
        var record = new AiDiagnosticRecord(
            AiOperationClass.Awareness, AiEndpointClass.RemoteHostOllama, AiDiagnosticOutcome.Refused,
            "cat-2", 4, 42, 0, []);
        var json = JsonSerializer.Serialize(record);
        var back = JsonSerializer.Deserialize<AiDiagnosticRecord>(json)!;
        Assert.Equal(record.OperationClass, back.OperationClass);
        Assert.Equal(record.EndpointClass, back.EndpointClass);
        Assert.Equal(record.Outcome, back.Outcome);
        Assert.Equal(record.StableCode, back.StableCode);
        Assert.Equal(record.Generation, back.Generation);
        Assert.Equal(record.DurationMilliseconds, back.DurationMilliseconds);
        Assert.Equal(record.CommandCount, back.CommandCount);
        Assert.Equal(record.CommandVerdictCodes, back.CommandVerdictCodes);
    }

    [Fact]
    public void CommandData_RoundTrips()
    {
        AiCommandData[] samples =
        [
            new AiCommandData.FlashImage(1, 2, 3, 4),
            new AiCommandData.Bubbles(true, 5),
            new AiCommandData.Subliminal("text", 30),
            new AiCommandData.MantraLockscreen("mantra", 2),
            new AiCommandData.Overlay(true, 20),
            new AiCommandData.Bounce(false, null),
            new AiCommandData.Haptic(0.5, 3),
            new AiCommandData.Media("videos/a.mp4", false),
            new AiCommandData.GetBackToMe("tok", 60, true, "later"),
        ];
        foreach (var data in samples)
            RoundTrip<AiCommandData>(data);
    }

    private static IEnumerable<AiCommandVerdict> VerdictSamples()
    {
        yield return AiCommandVerdict.Valid.Instance;
        yield return new AiCommandVerdict.UnknownCommand("nope");
        yield return new AiCommandVerdict.MalformedData("amount", "wrong-type");
        yield return new AiCommandVerdict.OutOfRange("frequency", "0-10");
        yield return new AiCommandVerdict.ModerationBlocked("cat-1");
        yield return new AiCommandVerdict.ConsentGated("master");
        yield return new AiCommandVerdict.NotExecuted(AiNotExecutedReason.EnvelopeRejected);
        yield return new AiCommandVerdict.NotExecuted(AiNotExecutedReason.CapExceeded);
        yield return new AiCommandVerdict.NotExecuted(AiNotExecutedReason.SupersededGeneration);
    }

    private static void RoundTrip<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, value!.GetType());
        var restored = (T?)JsonSerializer.Deserialize(json, value.GetType());
        Assert.Equal(value, restored);
    }
}
