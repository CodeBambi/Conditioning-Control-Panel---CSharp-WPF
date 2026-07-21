namespace CcpClient.Desktop.Ai;

/// <summary>Terminal classification of an AI operation for diagnostics (contract §12).</summary>
public enum AiDiagnosticOutcome
{
    Completed,
    Cancelled,
    Refused,
    Unavailable,
    Fallback,
    Faulted,
}

/// <summary>
/// Content-free diagnostic record for AI operations (contract §12). Schema-level rule,
/// not a convention: this type exposes ONLY enums, stable machine-readable codes,
/// counts, durations, generation identifiers, and endpoint/verdict classes. It has NO
/// free-text field that could carry a prompt, a completion, a user message, a window
/// title, a keyword, memory contents, or command payload text. <see cref="StableCode"/>
/// and <see cref="CommandVerdictCodes"/> carry stable tokens only — never exception
/// messages (messages can embed user input).
/// The content-freedom proof is structural: tests assert the serialized property set is
/// exactly this closed allow-list.
/// </summary>
public sealed record AiDiagnosticRecord(
    AiOperationClass OperationClass,
    AiEndpointClass EndpointClass,
    AiDiagnosticOutcome Outcome,
    string? StableCode,
    int Generation,
    long DurationMilliseconds,
    int CommandCount,
    string[] CommandVerdictCodes);

/// <summary>
/// The closed verdict→code mapping (contract §12 rule 1): diagnostic verdict codes map
/// from verdict TYPE names only — Field/Category/payload values never enter diagnostics.
/// </summary>
public static class AiDiagnosticCodes
{
    public static string VerdictCode(AiCommandVerdict verdict) => verdict switch
    {
        AiCommandVerdict.Valid => "valid",
        AiCommandVerdict.UnknownCommand => "unknown-command",
        AiCommandVerdict.MalformedData => "malformed-data",
        AiCommandVerdict.OutOfRange => "out-of-range",
        AiCommandVerdict.ModerationBlocked => "moderation-blocked",
        AiCommandVerdict.ConsentGated => "consent-gated",
        AiCommandVerdict.NotExecuted r => r.Reason switch
        {
            AiNotExecutedReason.EnvelopeRejected => "not-executed:envelope-rejected",
            AiNotExecutedReason.CapExceeded => "not-executed:cap-exceeded",
            AiNotExecutedReason.SupersededGeneration => "not-executed:superseded-generation",
            _ => "not-executed:unknown",
        },
        _ => "unknown",
    };
}
