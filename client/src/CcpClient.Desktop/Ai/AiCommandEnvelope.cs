using System.Text.Json;

namespace CcpClient.Desktop.Ai;

// Strict AI command envelope (contract §8) and per-command results (contract §9).
// Greenfield-decision: REPLACES the WPF/first-attempt lenient parser (brace balancing,
// markdown-fence extraction, mixed-format salvage — AiResponseParser.cs:159-232,
// AiCommandData.cs:36-47). Reject-by-default, zero repair, whole-envelope atomic
// rejection: one invalid command rejects the entire envelope and ZERO commands execute.

/// <summary>The 11 WPF command types are the inventory baseline (contract §8 rule 1). Admissibility of each is pending-owner.</summary>
public enum AiCommandKind
{
    FlashImage,
    Bubbles,
    Subliminal,
    MantraLockscreen,
    Spiral,
    Pink,
    Bounce,
    Haptic,
    Video,
    Audio,
    GetBackToMe,
}

/// <summary>Typed per-command data. Bounds are schema rules (contract §8 rule 5): out-of-range is a rejection verdict, never a silent clamp.</summary>
public abstract record AiCommandData
{
    private AiCommandData() { }

    /// <summary>flash_image: Amount 0–8, DurationSeconds 0–10, SizePercent 0–150, OpacityPercent 0–100.</summary>
    public sealed record FlashImage(int Amount, int DurationSeconds, int SizePercent, int OpacityPercent) : AiCommandData;

    /// <summary>bubbles: FrequencyPerMinute 0–10.</summary>
    public sealed record Bubbles(bool On, int FrequencyPerMinute) : AiCommandData;

    /// <summary>subliminal: Text ≤ 80 chars, OpacityPercent 0–60.</summary>
    public sealed record Subliminal(string Text, int OpacityPercent) : AiCommandData;

    /// <summary>mantra_lockscreen: Mantra ≤ 200 chars, Amount 0–5.</summary>
    public sealed record MantraLockscreen(string Mantra, int Amount) : AiCommandData;

    /// <summary>spiral / pink: Intensity 0–30.</summary>
    public sealed record Overlay(bool On, int Intensity) : AiCommandData;

    /// <summary>bounce: on/off with optional words.</summary>
    public sealed record Bounce(bool On, string? Words) : AiCommandData;

    /// <summary>haptic: Intensity 0..policy ceiling (WPF MaxAiHapticIntensity default 0.6), DurationSeconds 0–10.</summary>
    public sealed record Haptic(double Intensity, int DurationSeconds) : AiCommandData;

    /// <summary>video / audio: an assets-contained path OR random selection (never both).</summary>
    public sealed record Media(string? Path, bool Random) : AiCommandData;

    /// <summary>getbacktome: DelaySeconds 1–600. Depth ≤ 2 is an execution rule, not envelope vocabulary.</summary>
    public sealed record GetBackToMe(string Token, int DelaySeconds, bool JsonOnly, string? Text) : AiCommandData;
}

/// <summary>A schema-valid command: typed kind + typed data.</summary>
public sealed record AiCommand(AiCommandKind Kind, AiCommandData Data);

/// <summary>Why a valid command did not execute (contract §9 rule 1).</summary>
public enum AiNotExecutedReason
{
    /// <summary>The envelope was rejected; valid siblings never run — never silently dropped.</summary>
    EnvelopeRejected,

    /// <summary>The per-response cap (WPF baseline 3) was exceeded.</summary>
    CapExceeded,

    /// <summary>The owning generation was invalidated before execution (provider switch / panic — contract §3).</summary>
    SupersededGeneration,
}

/// <summary>
/// Per-command typed verdict (contract §9). Every command in a submitted envelope
/// receives exactly one, in order. Codes/strings in verdicts are stable machine tokens —
/// never raw JSON or user text.
/// </summary>
public abstract record AiCommandVerdict
{
    private AiCommandVerdict() { }

    /// <summary>Schema-valid and executable (execution may still be gated — see ConsentGated/ModerationBlocked).</summary>
    public sealed record Valid : AiCommandVerdict
    {
        public static readonly Valid Instance = new();
    }

    /// <summary>The command discriminator is not in the schema (reject-by-default).</summary>
    public sealed record UnknownCommand(string Name) : AiCommandVerdict;

    /// <summary>A field is missing, wrong-typed, or structurally invalid. Code is a stable token (missing, wrong-type, unknown-field, …), never the raw value.</summary>
    public sealed record MalformedData(string Field, string Code) : AiCommandVerdict;

    /// <summary>A field violates its schema bound. Limit names the bound (e.g. "0-8", "max-80"), never the submitted value.</summary>
    public sealed record OutOfRange(string Field, string Limit) : AiCommandVerdict;

    /// <summary>A free-text command field was blocked by moderation (contract §7 rule 2 — every command field).</summary>
    public sealed record ModerationBlocked(string CategoryCode) : AiCommandVerdict;

    /// <summary>The master or per-effect consent toggle denied this command (contract §8 rule 6). Toggle is a stable token ("master" or the command name).</summary>
    public sealed record ConsentGated(string Toggle) : AiCommandVerdict;

    /// <summary>The command was valid but did not run, with a typed reason.</summary>
    public sealed record NotExecuted(AiNotExecutedReason Reason) : AiCommandVerdict;
}

/// <summary>
/// The executable shape of a fully valid envelope (contract §8 rule 4). Only the
/// validator can construct one (internal ctor): an invalid envelope has NO executable
/// representation — zero execution semantics are types, not runtime hope.
/// </summary>
public sealed record AiExecutionPlan
{
    internal AiExecutionPlan(IReadOnlyList<AiCommand> commands) => Commands = commands;

    /// <summary>The commands admitted to execution (Valid verdicts within the per-response cap), in envelope order.</summary>
    public IReadOnlyList<AiCommand> Commands { get; }
}

/// <summary>
/// The typed result of validating one envelope (contract §9 rule 2): the honest,
/// serializable record of why nothing (or something) ran.
/// </summary>
public sealed record AiEnvelopeResult(
    bool Accepted,
    string? EnvelopeRejectionCode,
    string? Reply,
    IReadOnlyList<AiCommandVerdict> Verdicts,
    AiExecutionPlan? Plan);

/// <summary>
/// Policy inputs to validation (contract §8 rule 6): consent toggles, the moderation
/// boundary for free-text command fields, and the bounds that are settings-derived.
/// Values come from the owner-approved settings rows; this slice only fixes the seam.
/// </summary>
public sealed record AiEnvelopePolicy(
    bool MasterEffectsEnabled,
    Func<AiCommandKind, bool> IsEffectAllowed,
    Func<string, AiModerationVerdict> ModerateText,
    double MaxHapticIntensity = 0.6,
    string? AssetsRoot = null,
    int MaxCommandsPerResponse = 3)
{
    /// <summary>All effects allowed, moderation passes everything — test/development convenience.</summary>
    public static AiEnvelopePolicy PermitAll { get; } = new(true, _ => true, _ => AiModerationVerdict.Pass.Instance);
}

/// <summary>
/// The strict envelope validator (contract §8). Schema authority: the schema — never the
/// model's prose — defines what parses. No brace balancing, no fence extraction, no
/// salvage. Validation of every command runs before execution of any; one schema failure
/// rejects the whole envelope atomically (zero execution on mixed/invalid payloads).
/// </summary>
public static class AiEnvelopeValidator
{
    private static readonly JsonDocumentOptions StrictOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16,
    };

    private static readonly Dictionary<string, AiCommandKind> CommandNames = new(StringComparer.Ordinal)
    {
        ["flash_image"] = AiCommandKind.FlashImage,
        ["bubbles"] = AiCommandKind.Bubbles,
        ["subliminal"] = AiCommandKind.Subliminal,
        ["mantra_lockscreen"] = AiCommandKind.MantraLockscreen,
        ["spiral"] = AiCommandKind.Spiral,
        ["pink"] = AiCommandKind.Pink,
        ["bounce"] = AiCommandKind.Bounce,
        ["haptic"] = AiCommandKind.Haptic,
        ["video"] = AiCommandKind.Video,
        ["audio"] = AiCommandKind.Audio,
        ["getbacktome"] = AiCommandKind.GetBackToMe,
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm" };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".wma", ".ogg", ".flac", ".aac", ".m4a" };

    /// <summary>
    /// Validates raw model output against the strict schema. Never throws on bad input:
    /// malformed JSON, wrong shapes, unknown commands/fields, and out-of-range values all
    /// produce typed rejections.
    /// </summary>
    public static AiEnvelopeResult Validate(string rawOutput, AiEnvelopePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(rawOutput);
        ArgumentNullException.ThrowIfNull(policy);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawOutput, StrictOptions);
        }
        catch (JsonException)
        {
            return Reject("malformed-json");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Reject("root-not-object");

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name is not ("reply" or "commands"))
                    return Reject("unknown-field");
            }

            string? reply = null;
            if (root.TryGetProperty("reply", out var replyEl))
            {
                if (replyEl.ValueKind != JsonValueKind.String && replyEl.ValueKind != JsonValueKind.Null)
                    return Reject("reply-wrong-type");
                reply = replyEl.ValueKind == JsonValueKind.String ? replyEl.GetString() : null;
            }

            if (!root.TryGetProperty("commands", out var commandsEl))
                return new AiEnvelopeResult(true, null, reply, [], new AiExecutionPlan([]));

            if (commandsEl.ValueKind != JsonValueKind.Array)
                return Reject("commands-wrong-type");

            // Phase 1 — schema validation of EVERY command before any gating (contract §8 rule 3).
            var entries = commandsEl.EnumerateArray().ToArray();
            var commands = new AiCommand?[entries.Length];
            var failures = new AiCommandVerdict?[entries.Length];
            var anyFailure = false;
            for (var i = 0; i < entries.Length; i++)
            {
                (commands[i], failures[i]) = ValidateCommand(entries[i], policy);
                anyFailure |= failures[i] is not null;
            }

            if (anyFailure)
            {
                // Whole-envelope atomic rejection: schema-valid siblings are NotExecuted,
                // never silently dropped and never executed (contract §8 rule 3, §9 rule 1).
                var verdicts = new AiCommandVerdict[entries.Length];
                for (var i = 0; i < entries.Length; i++)
                    verdicts[i] = failures[i] ?? new AiCommandVerdict.NotExecuted(AiNotExecutedReason.EnvelopeRejected);
                return new AiEnvelopeResult(false, "command-invalid", reply, verdicts, null);
            }

            // Phase 2 — gating: consent toggles then moderation on every free-text field
            // (contract §8 rule 6, §7 rule 2). Gated commands are typed verdicts, not drops.
            var finalVerdicts = new AiCommandVerdict[entries.Length];
            var admitted = new List<AiCommand>(entries.Length);
            for (var i = 0; i < entries.Length; i++)
            {
                var command = commands[i]!;
                finalVerdicts[i] = Gate(command, policy);
                if (finalVerdicts[i] is AiCommandVerdict.Valid)
                {
                    if (admitted.Count >= policy.MaxCommandsPerResponse)
                        finalVerdicts[i] = new AiCommandVerdict.NotExecuted(AiNotExecutedReason.CapExceeded);
                    else
                        admitted.Add(command);
                }
            }

            return new AiEnvelopeResult(true, null, reply, finalVerdicts, new AiExecutionPlan(admitted));
        }

        static AiEnvelopeResult Reject(string code) =>
            new(false, code, null, [], null);
    }

    private static (AiCommand?, AiCommandVerdict?) ValidateCommand(JsonElement el, AiEnvelopePolicy policy)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return (null, new AiCommandVerdict.MalformedData("command", "wrong-type"));

        foreach (var prop in el.EnumerateObject())
        {
            if (prop.Name is not ("command" or "data"))
                return (null, new AiCommandVerdict.MalformedData(prop.Name, "unknown-field"));
        }

        if (!el.TryGetProperty("command", out var nameEl))
            return (null, new AiCommandVerdict.MalformedData("command", "missing"));
        if (nameEl.ValueKind != JsonValueKind.String)
            return (null, new AiCommandVerdict.MalformedData("command", "wrong-type"));

        var name = nameEl.GetString()!;
        if (!CommandNames.TryGetValue(name, out var kind))
            return (null, new AiCommandVerdict.UnknownCommand(name));

        if (!el.TryGetProperty("data", out var dataEl))
            return (null, new AiCommandVerdict.MalformedData("data", "missing"));
        if (dataEl.ValueKind != JsonValueKind.Object)
            return (null, new AiCommandVerdict.MalformedData("data", "wrong-type"));

        var (data, failure) = ValidateData(kind, dataEl, policy);
        return failure is not null ? (null, failure) : (new AiCommand(kind, data!), null);
    }

    private static (AiCommandData?, AiCommandVerdict?) ValidateData(AiCommandKind kind, JsonElement data, AiEnvelopePolicy policy)
    {
        AiCommandVerdict? failure;
        switch (kind)
        {
            case AiCommandKind.FlashImage:
            {
                if ((failure = CheckFields(data, "amount", "duration", "size", "opacity")) is not null) return (null, failure);
                if (!Int(data, "amount", out var amount, out failure)) return (null, failure);
                if (!Int(data, "duration", out var duration, out failure)) return (null, failure);
                if (!Int(data, "size", out var size, out failure)) return (null, failure);
                if (!Int(data, "opacity", out var opacity, out failure)) return (null, failure);
                if ((failure = Range("amount", amount, 0, 8)) is not null) return (null, failure);
                if ((failure = Range("duration", duration, 0, 10)) is not null) return (null, failure);
                if ((failure = Range("size", size, 0, 150)) is not null) return (null, failure);
                if ((failure = Range("opacity", opacity, 0, 100)) is not null) return (null, failure);
                return (new AiCommandData.FlashImage(amount, duration, size, opacity), null);
            }
            case AiCommandKind.Bubbles:
            {
                if ((failure = CheckFields(data, "on", "frequency")) is not null) return (null, failure);
                if (!Bool(data, "on", out var on, out failure)) return (null, failure);
                if (!Int(data, "frequency", out var frequency, out failure)) return (null, failure);
                if ((failure = Range("frequency", frequency, 0, 10)) is not null) return (null, failure);
                return (new AiCommandData.Bubbles(on, frequency), null);
            }
            case AiCommandKind.Subliminal:
            {
                if ((failure = CheckFields(data, "text", "opacity")) is not null) return (null, failure);
                if (!Str(data, "text", out var text, out failure)) return (null, failure);
                if (!Int(data, "opacity", out var opacity, out failure)) return (null, failure);
                if ((failure = MaxLen("text", text, 80)) is not null) return (null, failure);
                if ((failure = Range("opacity", opacity, 0, 60)) is not null) return (null, failure);
                return (new AiCommandData.Subliminal(text, opacity), null);
            }
            case AiCommandKind.MantraLockscreen:
            {
                if ((failure = CheckFields(data, "mantra", "amount")) is not null) return (null, failure);
                if (!Str(data, "mantra", out var mantra, out failure)) return (null, failure);
                if (!Int(data, "amount", out var amount, out failure)) return (null, failure);
                if ((failure = MaxLen("mantra", mantra, 200)) is not null) return (null, failure);
                if ((failure = Range("amount", amount, 0, 5)) is not null) return (null, failure);
                return (new AiCommandData.MantraLockscreen(mantra, amount), null);
            }
            case AiCommandKind.Spiral or AiCommandKind.Pink:
            {
                if ((failure = CheckFields(data, "on", "intensity")) is not null) return (null, failure);
                if (!Bool(data, "on", out var on, out failure)) return (null, failure);
                if (!Int(data, "intensity", out var intensity, out failure)) return (null, failure);
                if ((failure = Range("intensity", intensity, 0, 30)) is not null) return (null, failure);
                return (new AiCommandData.Overlay(on, intensity), null);
            }
            case AiCommandKind.Bounce:
            {
                if ((failure = CheckFields(data, "on", "words")) is not null) return (null, failure);
                if (!Bool(data, "on", out var on, out failure)) return (null, failure);
                if (!OptStr(data, "words", out var words, out failure)) return (null, failure);
                return (new AiCommandData.Bounce(on, words), null);
            }
            case AiCommandKind.Haptic:
            {
                if ((failure = CheckFields(data, "intensity", "duration")) is not null) return (null, failure);
                if (!Num(data, "intensity", out var intensity, out failure)) return (null, failure);
                if (!Int(data, "duration", out var duration, out failure)) return (null, failure);
                if (intensity < 0 || intensity > policy.MaxHapticIntensity)
                    return (null, new AiCommandVerdict.OutOfRange("intensity", $"0-{policy.MaxHapticIntensity}"));
                if ((failure = Range("duration", duration, 0, 10)) is not null) return (null, failure);
                return (new AiCommandData.Haptic(intensity, duration), null);
            }
            case AiCommandKind.Video or AiCommandKind.Audio:
            {
                if ((failure = CheckFields(data, "path", "random")) is not null) return (null, failure);
                if (!OptStr(data, "path", out var path, out failure)) return (null, failure);
                if (!OptBool(data, "random", out var random, out failure)) return (null, failure);
                if (random && path is not null)
                    return (null, new AiCommandVerdict.MalformedData("path", "path-and-random"));
                if (!random && string.IsNullOrEmpty(path))
                    return (null, new AiCommandVerdict.MalformedData("path", "missing"));
                if (path is not null && (failure = ValidateMediaPath(kind, path, policy.AssetsRoot)) is not null)
                    return (null, failure);
                return (new AiCommandData.Media(path, random), null);
            }
            case AiCommandKind.GetBackToMe:
            {
                if ((failure = CheckFields(data, "token", "delay", "jsonOnly", "text")) is not null) return (null, failure);
                if (!Str(data, "token", out var token, out failure)) return (null, failure);
                if (token.Length == 0) return (null, new AiCommandVerdict.MalformedData("token", "empty"));
                if (!Int(data, "delay", out var delay, out failure)) return (null, failure);
                if (!OptBool(data, "jsonOnly", out var jsonOnly, out failure)) return (null, failure);
                if (!OptStr(data, "text", out var text, out failure)) return (null, failure);
                if ((failure = Range("delay", delay, 1, 600)) is not null) return (null, failure);
                return (new AiCommandData.GetBackToMe(token, delay, jsonOnly, text), null);
            }
            default:
                return (null, new AiCommandVerdict.UnknownCommand(kind.ToString()));
        }
    }

    /// <summary>Consent then moderation (contract §8 rule 6, §7 rule 2). Every free-text field is moderated before a command is executable.</summary>
    private static AiCommandVerdict Gate(AiCommand command, AiEnvelopePolicy policy)
    {
        if (!policy.MasterEffectsEnabled)
            return new AiCommandVerdict.ConsentGated("master");
        if (!policy.IsEffectAllowed(command.Kind))
            return new AiCommandVerdict.ConsentGated(command.Kind.ToString());

        foreach (var text in FreeTextFields(command.Data))
        {
            if (policy.ModerateText(text) is AiModerationVerdict.Block block)
                return new AiCommandVerdict.ModerationBlocked(block.CategoryCode);
        }

        return AiCommandVerdict.Valid.Instance;
    }

    /// <summary>Every free-text command field (contract §7 rule 2): subliminal text, mantra, bounce words, media titles/paths.</summary>
    private static IEnumerable<string> FreeTextFields(AiCommandData data) => data switch
    {
        AiCommandData.Subliminal s => [s.Text],
        AiCommandData.MantraLockscreen m => [m.Mantra],
        AiCommandData.Bounce b when b.Words is not null => [b.Words],
        AiCommandData.Media m when m.Path is not null => [m.Path],
        AiCommandData.GetBackToMe g when g.Text is not null => [g.Text],
        _ => [],
    };

    /// <summary>Path rules (contract §8 rule 5): traversal rejected, UNC rejected, extension allow-list, root containment when a root is configured.</summary>
    private static AiCommandVerdict? ValidateMediaPath(AiCommandKind kind, string path, string? assetsRoot)
    {
        if (path.Contains("..", StringComparison.Ordinal))
            return new AiCommandVerdict.MalformedData("path", "traversal");
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            return new AiCommandVerdict.MalformedData("path", "unc");

        var allowed = kind == AiCommandKind.Video ? VideoExtensions : AudioExtensions;
        if (!allowed.Contains(Path.GetExtension(path)))
            return new AiCommandVerdict.MalformedData("path", "extension");

        if (assetsRoot is not null)
        {
            string full;
            try
            {
                full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(assetsRoot, path));
            }
            catch (Exception)
            {
                return new AiCommandVerdict.MalformedData("path", "invalid");
            }

            var rootFull = Path.GetFullPath(assetsRoot);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar)) rootFull += Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                return new AiCommandVerdict.MalformedData("path", "escapes-root");
        }

        return null;
    }

    private static AiCommandVerdict? CheckFields(JsonElement data, params string[] allowed)
    {
        foreach (var prop in data.EnumerateObject())
        {
            if (!allowed.Contains(prop.Name, StringComparer.Ordinal))
                return new AiCommandVerdict.MalformedData(prop.Name, "unknown-field");
        }
        return null;
    }

    private static bool Int(JsonElement data, string field, out int value, out AiCommandVerdict? failure)
    {
        value = 0;
        failure = null;
        if (!data.TryGetProperty(field, out var el)) { failure = new AiCommandVerdict.MalformedData(field, "missing"); return false; }
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out value)) { failure = new AiCommandVerdict.MalformedData(field, "wrong-type"); return false; }
        return true;
    }

    private static bool Num(JsonElement data, string field, out double value, out AiCommandVerdict? failure)
    {
        value = 0;
        failure = null;
        if (!data.TryGetProperty(field, out var el)) { failure = new AiCommandVerdict.MalformedData(field, "missing"); return false; }
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetDouble(out value)) { failure = new AiCommandVerdict.MalformedData(field, "wrong-type"); return false; }
        return true;
    }

    private static bool Bool(JsonElement data, string field, out bool value, out AiCommandVerdict? failure)
    {
        value = false;
        failure = null;
        if (!data.TryGetProperty(field, out var el)) { failure = new AiCommandVerdict.MalformedData(field, "missing"); return false; }
        if (el.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) { failure = new AiCommandVerdict.MalformedData(field, "wrong-type"); return false; }
        value = el.GetBoolean();
        return true;
    }

    private static bool OptBool(JsonElement data, string field, out bool value, out AiCommandVerdict? failure)
    {
        value = false;
        failure = null;
        if (!data.TryGetProperty(field, out var el) || el.ValueKind == JsonValueKind.Null) return true;
        if (el.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) { failure = new AiCommandVerdict.MalformedData(field, "wrong-type"); return false; }
        value = el.GetBoolean();
        return true;
    }

    private static bool Str(JsonElement data, string field, out string value, out AiCommandVerdict? failure)
    {
        value = "";
        failure = null;
        if (!data.TryGetProperty(field, out var el)) { failure = new AiCommandVerdict.MalformedData(field, "missing"); return false; }
        if (el.ValueKind != JsonValueKind.String) { failure = new AiCommandVerdict.MalformedData(field, "wrong-type"); return false; }
        value = el.GetString()!;
        return true;
    }

    private static bool OptStr(JsonElement data, string field, out string? value, out AiCommandVerdict? failure)
    {
        value = null;
        failure = null;
        if (!data.TryGetProperty(field, out var el) || el.ValueKind == JsonValueKind.Null) return true;
        if (el.ValueKind != JsonValueKind.String) { failure = new AiCommandVerdict.MalformedData(field, "wrong-type"); return false; }
        value = el.GetString();
        return true;
    }

    private static AiCommandVerdict? Range(string field, int value, int min, int max) =>
        value < min || value > max ? new AiCommandVerdict.OutOfRange(field, $"{min}-{max}") : null;

    private static AiCommandVerdict? MaxLen(string field, string value, int max) =>
        value.Length > max ? new AiCommandVerdict.OutOfRange(field, $"max-{max}") : null;
}
