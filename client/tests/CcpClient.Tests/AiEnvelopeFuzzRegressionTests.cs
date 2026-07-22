using CcpClient.Desktop.Ai;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-019's 62-case strict-envelope fuzz matrix (client/spikes/CcpSpike.AiProvider/Fuzz.cs)
/// ported as the permanent regression suite for the F1 fix (SP-033; admission §3 rule 6).
/// Zero-execution is proven two ways per case: a rejected payload has NO AiExecutionPlan
/// (type-enforced — internal ctor), so the canary CANNOT be invoked; a valid payload's plan
/// must record EXACTLY its commands in the canary (the falsifiable pair).
///
/// F1 DELTA vs the spike: duplicate keys are now REJECTED (the only contract-consistent
/// answer, SP-019 limit 6). The spike's two dup-key probe cases flipped:
/// "dup-key-last-out-of-range" [O]→[M duplicate], "dup-key-first-out-of-range" accepted→rejected.
/// New duplicate-key cases (root/command/data, both orders, unknown-name precedence) appended.
/// </summary>
public class AiEnvelopeFuzzRegressionTests
{
    private sealed record Case(
        string Name,
        string Payload,
        Func<AiEnvelopePolicy> Policy,
        bool Accepted,
        string? RejectCode,
        string[] VerdictTypes,
        AiCommandKind[] PlanKinds);

    /// <summary>The canary executor (test-side): records every command kind it is handed. A rejected envelope has no plan, so it can never be invoked on one.</summary>
    private sealed class Canary
    {
        private readonly List<AiCommandKind> _invocations = [];

        public int Calls { get; private set; }

        public IReadOnlyList<AiCommandKind> Invocations => _invocations;

        public void Execute(AiExecutionPlan plan)
        {
            Calls++;
            foreach (var cmd in plan.Commands)
            {
                _invocations.Add(cmd.Kind);
            }
        }
    }

    private static AiEnvelopePolicy PermitAll() => AiEnvelopePolicy.PermitAll;

    private static AiEnvelopePolicy BlockMarker() => new(
        true, _ => true,
        text => text.Contains("BLOCKME", StringComparison.Ordinal)
            ? new AiModerationVerdict.Block("test-category")
            : AiModerationVerdict.Pass.Instance);

    private static AiEnvelopePolicy SoftHitMarker() => new(
        true, _ => true,
        text => text.Contains("SOFTME", StringComparison.Ordinal)
            ? new AiModerationVerdict.SoftHit("test-soft")
            : AiModerationVerdict.Pass.Instance);

    private static AiEnvelopePolicy MasterOff() => new(false, _ => true, _ => AiModerationVerdict.Pass.Instance);
    private static AiEnvelopePolicy BubblesOff() => new(true, k => k != AiCommandKind.Bubbles, _ => AiModerationVerdict.Pass.Instance);
    private static AiEnvelopePolicy WithAssetsRoot() => new(true, _ => true, _ => AiModerationVerdict.Pass.Instance, AssetsRoot: Path.Combine(Path.GetTempPath(), "ccp-sp033-assets"));

    private const string V = "Valid";
    private const string U = "UnknownCommand";
    private const string M = "MalformedData";
    private const string O = "OutOfRange";
    private const string MB = "ModerationBlocked";
    private const string CG = "ConsentGated";
    private const string NE = "NotExecuted";

    [Fact]
    public void FuzzMatrix_Sp019_62Cases_PlusF1DuplicateCases_AllGreen()
    {
        var failures = new List<string>();
        var verdictTypesSeen = new HashSet<string>();
        var notExecutedReasonsSeen = new HashSet<AiNotExecutedReason>();
        var cases = BuildCases();

        foreach (var c in cases)
        {
            var canary = new Canary();
            var result = AiEnvelopeValidator.Validate(c.Payload, c.Policy());
            var caseFailures = new List<string>();

            if (result.Accepted != c.Accepted)
                caseFailures.Add($"accepted={result.Accepted} expected={c.Accepted}");

            if (!result.Accepted)
            {
                if (result.Plan is not null) caseFailures.Add("rejected envelope produced a plan (contract §8 rule 4)");
                if (result.Reply is not null) caseFailures.Add("rejected envelope surfaced reply text (contract §9 rule 4)");
                if (c.RejectCode is not null && result.EnvelopeRejectionCode != c.RejectCode)
                    caseFailures.Add($"reject-code={result.EnvelopeRejectionCode} expected={c.RejectCode}");
            }
            else if (result.Plan is null)
            {
                caseFailures.Add("accepted envelope without a plan");
            }

            var actualTypes = result.Verdicts.Select(v => v.GetType().Name).ToArray();
            if (!actualTypes.SequenceEqual(c.VerdictTypes))
                caseFailures.Add($"verdicts=[{string.Join(",", actualTypes)}] expected=[{string.Join(",", c.VerdictTypes)}]");

            foreach (var v in result.Verdicts)
            {
                verdictTypesSeen.Add(v.GetType().Name);
                if (v is AiCommandVerdict.NotExecuted ne) notExecutedReasonsSeen.Add(ne.Reason);
                if (v is AiCommandVerdict.MalformedData md && md.Field.Contains("evil", StringComparison.Ordinal))
                    caseFailures.Add("model-supplied field name leaked into a verdict");
                if (AiDiagnosticCodes.VerdictCode(v) is "unknown" or "not-executed:unknown")
                    caseFailures.Add($"diagnostics mapping returned unknown for {v.GetType().Name}");
            }

            if (result.Plan is not null)
            {
                canary.Execute(result.Plan);
                if (!canary.Invocations.SequenceEqual(c.PlanKinds))
                    caseFailures.Add($"canary=[{string.Join(",", canary.Invocations)}] expected=[{string.Join(",", c.PlanKinds)}]");
            }
            else if (c.PlanKinds.Length > 0)
            {
                caseFailures.Add("expected plan commands but no plan");
            }

            if (!result.Accepted && canary.Calls != 0)
                caseFailures.Add("canary fired on a rejected envelope");

            if (caseFailures.Count > 0)
                failures.Add($"{c.Name}: {string.Join("; ", caseFailures)}");
        }

        foreach (var required in new[] { V, U, M, O, MB, CG, NE })
        {
            if (!verdictTypesSeen.Contains(required))
                failures.Add($"vocabulary coverage missing: {required}");
        }

        foreach (var reason in new[] { AiNotExecutedReason.EnvelopeRejected, AiNotExecutedReason.CapExceeded })
        {
            if (!notExecutedReasonsSeen.Contains(reason))
                failures.Add($"NotExecuted reason coverage missing: {reason}");
        }

        Assert.True(failures.Count == 0,
            $"fuzz failures ({failures.Count}/{cases.Length}):\n" + string.Join("\n", failures));
        Assert.Equal(70, cases.Length); // 62 SP-019 cases + 8 new F1 duplicate-key cases
    }

    [Fact]
    public void DuplicateKeys_AreRejectedAtEverySchemaLevel()
    {
        // Root level.
        var root = AiEnvelopeValidator.Validate("""{"reply":"a","reply":"b"}""", PermitAll());
        Assert.False(root.Accepted);
        Assert.Equal("duplicate-field", root.EnvelopeRejectionCode);

        // Command-object level.
        var command = AiEnvelopeValidator.Validate(
            """{"commands":[{"command":"bubbles","command":"bubbles","data":{"on":true,"frequency":1}}]}""", PermitAll());
        Assert.False(command.Accepted);
        var cv = Assert.IsType<AiCommandVerdict.MalformedData>(Assert.Single(command.Verdicts));
        Assert.Equal("command", cv.Field);
        Assert.Equal("duplicate", cv.Code);

        // Data-object level, both orders (the SP-019 parser-differential pair).
        foreach (var payload in new[]
        {
            """{"commands":[{"command":"flash_image","data":{"amount":1,"amount":9,"duration":1,"size":1,"opacity":1}}]}""",
            """{"commands":[{"command":"flash_image","data":{"amount":9,"amount":1,"duration":1,"size":1,"opacity":1}}]}""",
        })
        {
            var data = AiEnvelopeValidator.Validate(payload, PermitAll());
            Assert.False(data.Accepted);
            var dv = Assert.IsType<AiCommandVerdict.MalformedData>(Assert.Single(data.Verdicts));
            Assert.Equal("amount", dv.Field); // schema-known name, never model text
            Assert.Equal("duplicate", dv.Code);
            Assert.Null(data.Plan);
            Assert.Null(data.Reply);
        }
    }

    private static string F(int amount = 1, int duration = 1, int size = 10, int opacity = 50) =>
        $"{{\"command\":\"flash_image\",\"data\":{{\"amount\":{amount},\"duration\":{duration},\"size\":{size},\"opacity\":{opacity}}}}}";
    private static string Env(params string[] commands) =>
        $"{{\"reply\":\"fuzz-reply\",\"commands\":[{string.Join(",", commands)}]}}";

    private static Case[] BuildCases() =>
    [
        // ---- valid falsifiable pair ----
        new("valid-single", Env("{\"command\":\"bubbles\",\"data\":{\"on\":true,\"frequency\":5}}"), PermitAll,
            true, null, [V], [AiCommandKind.Bubbles]),
        new("valid-boundaries", Env(
                F(0, 0, 0, 0), F(8, 10, 150, 100),
                "{\"command\":\"spiral\",\"data\":{\"on\":true,\"intensity\":30}}"), PermitAll,
            true, null, [V, V, V], [AiCommandKind.FlashImage, AiCommandKind.FlashImage, AiCommandKind.Spiral]),
        new("valid-text-limits", Env(
                "{\"command\":\"subliminal\",\"data\":{\"text\":\"" + new string('a', 80) + "\",\"opacity\":60}}",
                "{\"command\":\"mantra_lockscreen\",\"data\":{\"mantra\":\"" + new string('b', 200) + "\",\"amount\":5}}"), PermitAll,
            true, null, [V, V], [AiCommandKind.Subliminal, AiCommandKind.MantraLockscreen]),
        new("valid-astral-79plus1", Env("{\"command\":\"subliminal\",\"data\":{\"text\":\"" + new string('a', 78) + "😀\",\"opacity\":1}}"), PermitAll,
            true, null, [V], [AiCommandKind.Subliminal]),
        new("valid-empty-commands", "{\"reply\":\"ok\",\"commands\":[]}", PermitAll,
            true, null, [], []),
        new("valid-getbacktome-bounds", Env(
                "{\"command\":\"getbacktome\",\"data\":{\"token\":\"t1\",\"delay\":1}}",
                "{\"command\":\"getbacktome\",\"data\":{\"token\":\"t2\",\"delay\":600}}"), PermitAll,
            true, null, [V, V], [AiCommandKind.GetBackToMe, AiCommandKind.GetBackToMe]),

        // ---- envelope-root rejections ----
        new("root-array", "[]", PermitAll, false, "root-not-object", [], []),
        new("root-string", "\"hello\"", PermitAll, false, "root-not-object", [], []),
        new("root-number", "42", PermitAll, false, "root-not-object", [], []),
        new("root-bool", "true", PermitAll, false, "root-not-object", [], []),
        new("root-null", "null", PermitAll, false, "root-not-object", [], []),
        new("root-unknown-field", "{\"reply\":\"x\",\"evil\":1}", PermitAll, false, "unknown-field", [], []),
        new("reply-wrong-type", "{\"reply\":42}", PermitAll, false, "reply-wrong-type", [], []),
        new("commands-wrong-type", "{\"commands\":{}}", PermitAll, false, "commands-wrong-type", [], []),

        // ---- malformed JSON ----
        new("malformed-truncated", "{\"reply\":\"abc\",\"commands\":[{\"command\":\"bubbles\",\"data\":{\"on\":true,\"freq", PermitAll,
            false, "malformed-json", [], []),
        new("malformed-garbage", "not json {{{", PermitAll, false, "malformed-json", [], []),
        new("malformed-trailing-comma", "{\"commands\":[],}", PermitAll, false, "malformed-json", [], []),
        new("malformed-comment", "{\"reply\":/*x*/\"a\"}", PermitAll, false, "malformed-json", [], []),
        new("malformed-nan-token", "{\"reply\":NaN}", PermitAll, false, "malformed-json", [], []),
        new("depth-bomb-deep", string.Concat(Enumerable.Repeat("[", 64)) + string.Concat(Enumerable.Repeat("]", 64)), PermitAll,
            false, "malformed-json", [], []),
        new("depth-shallow-array", "[[[[[[]]]]]]", PermitAll, false, "root-not-object", [], []),

        // ---- duplicate keys (F1: REJECTED — spike expectations flipped, record.md §3.3) ----
        new("dup-key-last-out-of-range", Env("{\"command\":\"flash_image\",\"data\":{\"amount\":1,\"amount\":9,\"duration\":1,\"size\":1,\"opacity\":1}}"), PermitAll,
            false, "command-invalid", [M], []),
        new("dup-key-first-out-of-range", Env("{\"command\":\"flash_image\",\"data\":{\"amount\":9,\"amount\":1,\"duration\":1,\"size\":1,\"opacity\":1}}"), PermitAll,
            false, "command-invalid", [M], []),

        // ---- per-command invalid schema ----
        new("unknown-command", Env("{\"command\":\"explode\",\"data\":{}}"), PermitAll,
            false, "command-invalid", [U], []),
        new("enum-near-miss-casing", Env(F().Replace("flash_image", "Flash_Image")), PermitAll,
            false, "command-invalid", [U], []),
        new("enum-near-miss-space", Env("{\"command\":\"flash image\",\"data\":{}}"), PermitAll,
            false, "command-invalid", [U], []),
        new("missing-command-field", Env("{\"data\":{}}"), PermitAll,
            false, "command-invalid", [M], []),
        new("command-wrong-type", Env("{\"command\":42,\"data\":{}}"), PermitAll,
            false, "command-invalid", [M], []),
        new("missing-data", Env("{\"command\":\"bubbles\"}"), PermitAll,
            false, "command-invalid", [M], []),
        new("data-wrong-type", Env("{\"command\":\"bubbles\",\"data\":[]}"), PermitAll,
            false, "command-invalid", [M], []),
        new("extra-field-in-command", Env("{\"command\":\"bubbles\",\"data\":{\"on\":true,\"frequency\":1},\"evil\":1}"), PermitAll,
            false, "command-invalid", [M], []),
        new("extra-field-in-data", Env("{\"command\":\"bubbles\",\"data\":{\"on\":true,\"frequency\":1,\"evil\":1}}"), PermitAll,
            false, "command-invalid", [M], []),
        new("missing-data-field", Env("{\"command\":\"flash_image\",\"data\":{\"amount\":1,\"duration\":1,\"opacity\":1}}"), PermitAll,
            false, "command-invalid", [M], []),
        new("field-wrong-type-string", Env("{\"command\":\"flash_image\",\"data\":{\"amount\":\"1\",\"duration\":1,\"size\":1,\"opacity\":1}}"), PermitAll,
            false, "command-invalid", [M], []),
        new("field-wrong-type-float", Env("{\"command\":\"flash_image\",\"data\":{\"amount\":1.5,\"duration\":1,\"size\":1,\"opacity\":1}}"), PermitAll,
            false, "command-invalid", [M], []),
        new("field-wrong-type-float-int", Env("{\"command\":\"flash_image\",\"data\":{\"amount\":1.0,\"duration\":1,\"size\":1,\"opacity\":1}}"), PermitAll,
            false, "command-invalid", [M], []),
        new("bool-wrong-type", Env("{\"command\":\"bubbles\",\"data\":{\"on\":\"yes\",\"frequency\":1}}"), PermitAll,
            false, "command-invalid", [M], []),
        new("huge-number", Env("{\"command\":\"flash_image\",\"data\":{\"amount\":999999999999,\"duration\":1,\"size\":1,\"opacity\":1}}"), PermitAll,
            false, "command-invalid", [M], []),

        // ---- mixed: whole-envelope atomic rejection ----
        new("mixed-valid+invalid", Env(
                "{\"command\":\"bubbles\",\"data\":{\"on\":true,\"frequency\":5}}",
                "{\"command\":\"explode\",\"data\":{}}",
                "{\"command\":\"spiral\",\"data\":{\"on\":true,\"intensity\":10}}"), PermitAll,
            false, "command-invalid", [NE, U, NE], []),

        // ---- out-of-range ----
        new("oor-amount-high", Env(F(amount: 9)), PermitAll, false, "command-invalid", [O], []),
        new("oor-amount-negative", Env(F(amount: -1)), PermitAll, false, "command-invalid", [O], []),
        new("oor-subliminal-81", Env("{\"command\":\"subliminal\",\"data\":{\"text\":\"" + new string('a', 81) + "\",\"opacity\":10}}"), PermitAll,
            false, "command-invalid", [O], []),
        new("oor-subliminal-astral-over", Env("{\"command\":\"subliminal\",\"data\":{\"text\":\"" + new string('a', 79) + "😀\",\"opacity\":1}}"), PermitAll,
            false, "command-invalid", [O], []),
        new("oor-mantra-201", Env("{\"command\":\"mantra_lockscreen\",\"data\":{\"mantra\":\"" + new string('b', 201) + "\",\"amount\":1}}"), PermitAll,
            false, "command-invalid", [O], []),
        new("oor-spiral-31", Env("{\"command\":\"spiral\",\"data\":{\"on\":true,\"intensity\":31}}"), PermitAll,
            false, "command-invalid", [O], []),
        new("oor-haptic-over-ceiling", Env("{\"command\":\"haptic\",\"data\":{\"intensity\":0.61,\"duration\":1}}"), PermitAll,
            false, "command-invalid", [O], []),
        new("oor-haptic-negative", Env("{\"command\":\"haptic\",\"data\":{\"intensity\":-0.1,\"duration\":1}}"), PermitAll,
            false, "command-invalid", [O], []),
        new("oor-getbacktome-delay-0", Env("{\"command\":\"getbacktome\",\"data\":{\"token\":\"t\",\"delay\":0}}"), PermitAll,
            false, "command-invalid", [O], []),
        new("oor-getbacktome-delay-601", Env("{\"command\":\"getbacktome\",\"data\":{\"token\":\"t\",\"delay\":601}}"), PermitAll,
            false, "command-invalid", [O], []),
        new("getbacktome-empty-token", Env("{\"command\":\"getbacktome\",\"data\":{\"token\":\"\",\"delay\":5}}"), PermitAll,
            false, "command-invalid", [M], []),

        // ---- media paths ----
        new("media-traversal", Env("{\"command\":\"video\",\"data\":{\"path\":\"../x.mp4\"}}"), WithAssetsRoot,
            false, "command-invalid", [M], []),
        new("media-unc", Env("{\"command\":\"video\",\"data\":{\"path\":\"\\\\\\\\server\\\\x.mp4\"}}"), WithAssetsRoot,
            false, "command-invalid", [M], []),
        new("media-bad-extension", Env("{\"command\":\"video\",\"data\":{\"path\":\"clip.exe\"}}"), WithAssetsRoot,
            false, "command-invalid", [M], []),
        new("media-escapes-root", Env("{\"command\":\"video\",\"data\":{\"path\":\"/etc/passwd.mp4\"}}"), WithAssetsRoot,
            false, "command-invalid", [M], []),
        new("media-path-and-random", Env("{\"command\":\"video\",\"data\":{\"path\":\"a.mp4\",\"random\":true}}"), WithAssetsRoot,
            false, "command-invalid", [M], []),
        new("media-valid-under-root", Env("{\"command\":\"video\",\"data\":{\"path\":\"clips/a.mp4\"}}"), WithAssetsRoot,
            true, null, [V], [AiCommandKind.Video]),

        // ---- moderated (verdict-rejected shape; policy values pending-owner) ----
        new("moderated-block", Env("{\"command\":\"subliminal\",\"data\":{\"text\":\"BLOCKME now\",\"opacity\":10}}"), BlockMarker,
            true, null, [MB], []),
        new("moderated-block-mixed-with-valid", Env(
                "{\"command\":\"bubbles\",\"data\":{\"on\":true,\"frequency\":1}}",
                "{\"command\":\"subliminal\",\"data\":{\"text\":\"BLOCKME\",\"opacity\":10}}"), BlockMarker,
            true, null, [V, MB], [AiCommandKind.Bubbles]),
        new("moderated-softhit-passes", Env("{\"command\":\"subliminal\",\"data\":{\"text\":\"SOFTME\",\"opacity\":10}}"), SoftHitMarker,
            true, null, [V], [AiCommandKind.Subliminal]),

        // ---- consent-gated ----
        new("consent-master-off", Env("{\"command\":\"bubbles\",\"data\":{\"on\":true,\"frequency\":1}}"), MasterOff,
            true, null, [CG], []),
        new("consent-per-effect-off", Env("{\"command\":\"bubbles\",\"data\":{\"on\":true,\"frequency\":1}}"), BubblesOff,
            true, null, [CG], []),

        // ---- cap ----
        new("cap-4-of-3", Env(
                "{\"command\":\"bubbles\",\"data\":{\"on\":true,\"frequency\":1}}",
                "{\"command\":\"spiral\",\"data\":{\"on\":true,\"intensity\":1}}",
                "{\"command\":\"pink\",\"data\":{\"on\":true,\"intensity\":1}}",
                "{\"command\":\"bounce\",\"data\":{\"on\":true}}"), PermitAll,
            true, null, [V, V, V, NE],
            [AiCommandKind.Bubbles, AiCommandKind.Spiral, AiCommandKind.Pink]),

        // ---- NEW F1 duplicate-key cases (SP-033) ----
        new("dup-root-reply", "{\"reply\":\"a\",\"reply\":\"b\"}", PermitAll,
            false, "duplicate-field", [], []),
        new("dup-root-commands", "{\"commands\":[],\"commands\":[]}", PermitAll,
            false, "duplicate-field", [], []),
        new("dup-command-object", Env("{\"command\":\"bubbles\",\"command\":\"bubbles\",\"data\":{\"on\":true,\"frequency\":1}}"), PermitAll,
            false, "command-invalid", [M], []),
        new("dup-command-data-name", Env("{\"command\":\"bubbles\",\"data\":{\"on\":true,\"frequency\":1},\"data\":{\"on\":true,\"frequency\":1}}"), PermitAll,
            false, "command-invalid", [M], []),
        new("dup-data-both-orders-valid-values", Env("{\"command\":\"bubbles\",\"data\":{\"on\":true,\"on\":false,\"frequency\":1}}"), PermitAll,
            false, "command-invalid", [M], []),
        new("dup-root-unknown-wins", "{\"evil\":1,\"evil\":2}", PermitAll,
            false, "unknown-field", [], []),
        new("dup-data-unknown-wins", Env("{\"command\":\"bubbles\",\"data\":{\"on\":true,\"frequency\":1,\"evil\":1,\"evil\":2}}"), PermitAll,
            false, "command-invalid", [M], []),
        new("dup-nested-envelope-reply", "{\"reply\":\"x\",\"commands\":[{\"command\":\"bubbles\",\"data\":{\"on\":true,\"frequency\":1}}],\"reply\":\"y\"}", PermitAll,
            false, "duplicate-field", [], []),
    ];
}
