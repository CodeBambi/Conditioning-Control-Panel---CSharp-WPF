using CcpClient.Desktop.Ai;

namespace CcpSpike.AiProvider;

/// <summary>
/// Step-3 strict-envelope fuzz matrix against SP-016's REAL <see cref="AiEnvelopeValidator"/>.
/// Zero-execution is PROVEN two ways per case: a rejected payload has NO
/// <see cref="AiExecutionPlan"/> (type-enforced — the validator's internal ctor makes an
/// invalid envelope unconvertible), so the canary CANNOT be invoked; a valid payload's plan
/// is handed to the canary, which must record EXACTLY the plan's commands (falsifiable pair).
/// Payload classes: envelope-root rejections, invalid schema, mixed, out-of-range, moderated,
/// consent-gated, cap, media-path, malformed JSON, duplicate-key probe (observed semantics,
/// recorded honestly — never overclaimed).
/// </summary>
public static class Fuzz
{
    private sealed record Case(
        string Name,
        string Payload,
        Func<AiEnvelopePolicy> Policy,
        // Expected: accepted? / envelope rejection code / per-position verdict type names / plan kinds
        bool Accepted,
        string? RejectCode,
        string[] VerdictTypes,
        AiCommandKind[] PlanKinds,
        string? Note = null);

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
    private static AiEnvelopePolicy WithAssetsRoot() => new(true, _ => true, _ => AiModerationVerdict.Pass.Instance, AssetsRoot: Path.Combine(Path.GetTempPath(), "ccp-sp019-assets"));

    private const string V = "Valid";
    private const string U = "UnknownCommand";
    private const string M = "MalformedData";
    private const string O = "OutOfRange";
    private const string MB = "ModerationBlocked";
    private const string CG = "ConsentGated";
    private const string NE = "NotExecuted";

    public static int Run()
    {
        var cases = BuildCases();
        var failures = new List<string>();
        var verdictTypesSeen = new HashSet<string>();
        var notExecutedReasonsSeen = new HashSet<AiNotExecutedReason>();
        string? dupKeyObservation = null;

        foreach (var c in cases)
        {
            var canary = new CanaryExecutor();
            var result = AiEnvelopeValidator.Validate(c.Payload, c.Policy());
            var caseFailures = new List<string>();

            if (result.Accepted != c.Accepted)
                caseFailures.Add($"accepted={result.Accepted} expected={c.Accepted}");

            if (!result.Accepted)
            {
                // Zero-execution, type-enforced: no plan exists for a rejected envelope.
                if (result.Plan is not null) caseFailures.Add("rejected envelope produced a plan (IMPOSSIBLE per contract §8 rule 4)");
                if (result.Reply is not null) caseFailures.Add("rejected envelope surfaced reply text (contract §9 rule 4)");
                if (c.RejectCode is not null && result.EnvelopeRejectionCode != c.RejectCode)
                    caseFailures.Add($"reject-code={result.EnvelopeRejectionCode} expected={c.RejectCode}");
            }
            else
            {
                if (result.Plan is null) caseFailures.Add("accepted envelope without a plan");
            }

            var actualTypes = result.Verdicts.Select(v => v.GetType().Name).ToArray();
            if (!actualTypes.SequenceEqual(c.VerdictTypes))
                caseFailures.Add($"verdicts=[{string.Join(",", actualTypes)}] expected=[{string.Join(",", c.VerdictTypes)}]");

            foreach (var v in result.Verdicts)
            {
                verdictTypesSeen.Add(v.GetType().Name);
                if (v is AiCommandVerdict.NotExecuted ne) notExecutedReasonsSeen.Add(ne.Reason);
                // Verdict payloads must be schema-known names / stable tokens, never model text.
                if (v is AiCommandVerdict.MalformedData md && md.Field.Contains("evil", StringComparison.Ordinal))
                    caseFailures.Add("model-supplied field name leaked into a verdict");
                // The closed diagnostics mapping must cover every verdict (never "unknown").
                if (AiDiagnosticCodes.VerdictCode(v) is "unknown" or "not-executed:unknown")
                    caseFailures.Add($"diagnostics mapping returned unknown for {v.GetType().Name}");
            }

            if (result.Plan is not null)
            {
                canary.Execute(result.Plan);
                var actualKinds = canary.Invocations.ToArray();
                if (!actualKinds.SequenceEqual(c.PlanKinds))
                    caseFailures.Add($"canary=[{string.Join(",", actualKinds)}] expected=[{string.Join(",", c.PlanKinds)}]");
            }
            else if (c.PlanKinds.Length > 0)
            {
                caseFailures.Add("expected plan commands but no plan");
            }
            // A rejected envelope can never reach the canary: Calls==0 by construction
            // (no plan to hand it) — assert the silence explicitly.
            if (!result.Accepted && canary.Calls != 0)
                caseFailures.Add("canary fired on a rejected envelope");

            var pass = caseFailures.Count == 0;
            SpikeLog.Line("fuzz", $"{(pass ? "PASS" : "FAIL")} {c.Name}{(pass ? "" : " — " + string.Join("; ", caseFailures))}{(c.Note is null ? "" : $" [{c.Note}]")}");
            if (!pass) failures.Add(c.Name);

            if (c.Name.StartsWith("dup-key", StringComparison.Ordinal))
                dupKeyObservation = $"{c.Name}: accepted={result.Accepted} code={result.EnvelopeRejectionCode ?? "-"} verdicts=[{string.Join(",", actualTypes)}] plan=[{string.Join(",", result.Plan?.Commands.Select(x => x.Kind.ToString()) ?? Enumerable.Empty<string>())}]";
        }

        // Vocabulary coverage: every verdict type and both fuzz-reachable NotExecuted reasons exercised.
        foreach (var required in new[] { V, U, M, O, MB, CG, NE })
        {
            var ok = verdictTypesSeen.Contains(required);
            SpikeLog.Line("fuzz", $"{(ok ? "PASS" : "FAIL")} vocabulary coverage: {required}");
            if (!ok) failures.Add($"vocabulary:{required}");
        }
        foreach (var reason in new[] { AiNotExecutedReason.EnvelopeRejected, AiNotExecutedReason.CapExceeded })
        {
            var ok = notExecutedReasonsSeen.Contains(reason);
            SpikeLog.Line("fuzz", $"{(ok ? "PASS" : "FAIL")} NotExecuted reason coverage: {reason}");
            if (!ok) failures.Add($"notexecuted:{reason}");
        }

        if (dupKeyObservation is not null)
            SpikeLog.Line("fuzz", $"OBSERVED duplicate-key semantics (recorded honestly, not overclaimed): {dupKeyObservation}");

        SpikeLog.Line("fuzz", failures.Count == 0
            ? $"FUZZ GREEN: {cases.Length} cases, zero execution proven on every rejected class"
            : $"FUZZ FAILED: {failures.Count} — {string.Join(", ", failures)}");
        Console.WriteLine(failures.Count == 0 ? $"FUZZ: {cases.Length} cases pass" : $"FUZZ: {failures.Count} FAILURES");
        return failures.Count == 0 ? 0 : 1;
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
            true, null, [V], [AiCommandKind.Subliminal],
            Note: "78 ASCII + 1 astral (2 UTF-16 units) = 80 units — schema bound is UTF-16 length"),
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
        new("malformed-trailing-comma", "{\"commands\":[],}", PermitAll, false, "malformed-json", [], [],
            Note: "AllowTrailingCommas=false"),
        new("malformed-comment", "{\"reply\":/*x*/\"a\"}", PermitAll, false, "malformed-json", [], [],
            Note: "CommentHandling.Disallow"),
        new("malformed-nan-token", "{\"reply\":NaN}", PermitAll, false, "malformed-json", [], []),
        new("depth-bomb-deep", string.Concat(Enumerable.Repeat("[", 64)) + string.Concat(Enumerable.Repeat("]", 64)), PermitAll,
            false, "malformed-json", [], [], Note: "depth 64 > MaxDepth 16"),
        new("depth-shallow-array", "[[[[[[]]]]]]", PermitAll, false, "root-not-object", [], [],
            Note: "within MaxDepth but root is an array"),

        // ---- duplicate-key probe (OBSERVED semantics — recorded, never overclaimed) ----
        new("dup-key-last-out-of-range", Env("{\"command\":\"flash_image\",\"data\":{\"amount\":1,\"amount\":9,\"duration\":1,\"size\":1,\"opacity\":1}}"), PermitAll,
            false, "command-invalid", [O], [],
            Note: "TryGetProperty is last-wins; EnumerateObject sees both — expected rejection via the LAST value"),
        new("dup-key-first-out-of-range", Env("{\"command\":\"flash_image\",\"data\":{\"amount\":9,\"amount\":1,\"duration\":1,\"size\":1,\"opacity\":1}}"), PermitAll,
            true, null, [V], [AiCommandKind.FlashImage],
            Note: "OBSERVED GAP: duplicate keys are NOT rejected per se; the last value wins — recorded as a validator finding"),

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
            false, "command-invalid", [M], [], Note: "1.0 raw text is not an int for TryGetInt32"),
        new("bool-wrong-type", Env("{\"command\":\"bubbles\",\"data\":{\"on\":\"yes\",\"frequency\":1}}"), PermitAll,
            false, "command-invalid", [M], []),
        new("huge-number", Env("{\"command\":\"flash_image\",\"data\":{\"amount\":999999999999,\"duration\":1,\"size\":1,\"opacity\":1}}"), PermitAll,
            false, "command-invalid", [M], [], Note: "int32 overflow surfaces as wrong-type, not out-of-range — observed"),

        // ---- mixed: whole-envelope atomic rejection ----
        new("mixed-valid+invalid", Env(
                "{\"command\":\"bubbles\",\"data\":{\"on\":true,\"frequency\":5}}",
                "{\"command\":\"explode\",\"data\":{}}",
                "{\"command\":\"spiral\",\"data\":{\"on\":true,\"intensity\":10}}"), PermitAll,
            false, "command-invalid", [NE, U, NE], [],
            Note: "ATOMIC: valid siblings are NotExecuted(envelope-rejected); reply suppressed; plan null; canary silent"),

        // ---- out-of-range ----
        new("oor-amount-high", Env(F(amount: 9)), PermitAll, false, "command-invalid", [O], []),
        new("oor-amount-negative", Env(F(amount: -1)), PermitAll, false, "command-invalid", [O], []),
        new("oor-subliminal-81", Env("{\"command\":\"subliminal\",\"data\":{\"text\":\"" + new string('a', 81) + "\",\"opacity\":10}}"), PermitAll,
            false, "command-invalid", [O], []),
        new("oor-subliminal-astral-over", Env("{\"command\":\"subliminal\",\"data\":{\"text\":\"" + new string('a', 79) + "😀\",\"opacity\":1}}"), PermitAll,
            false, "command-invalid", [O], [], Note: "79 ASCII + astral = 81 UTF-16 units"),
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
            true, null, [V], [AiCommandKind.Video],
            Note: "validator checks shape/containment, not file existence"),

        // ---- moderated (verdict-rejected shape; policy values pending-owner) ----
        new("moderated-block", Env("{\"command\":\"subliminal\",\"data\":{\"text\":\"BLOCKME now\",\"opacity\":10}}"), BlockMarker,
            true, null, [MB], [],
            Note: "schema-valid but moderation-gated: NOT in the plan → canary never sees it"),
        new("moderated-block-mixed-with-valid", Env(
                "{\"command\":\"bubbles\",\"data\":{\"on\":true,\"frequency\":1}}",
                "{\"command\":\"subliminal\",\"data\":{\"text\":\"BLOCKME\",\"opacity\":10}}"), BlockMarker,
            true, null, [V, MB], [AiCommandKind.Bubbles],
            Note: "gating is per-command post-validation; the valid sibling executes, the moderated one never does"),
        new("moderated-softhit-passes", Env("{\"command\":\"subliminal\",\"data\":{\"text\":\"SOFTME\",\"opacity\":10}}"), SoftHitMarker,
            true, null, [V], [AiCommandKind.Subliminal],
            Note: "SoftHit does not gate (only Block does) — taxonomy behavior recorded"),

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
            [AiCommandKind.Bubbles, AiCommandKind.Spiral, AiCommandKind.Pink],
            Note: "4th valid command = NotExecuted(cap-exceeded); canary records exactly 3"),
    ];
}
