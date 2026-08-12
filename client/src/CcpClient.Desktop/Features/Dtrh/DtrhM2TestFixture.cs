namespace CcpClient.Desktop.Features.Dtrh;

using System.Text.Json;

/// <summary>SP-057: the committed m2test fixture could not be parsed into a
/// <see cref="DtrhSlotDocument"/>. Typed + loud — a test clone NEVER falls back to the
/// live slot document (the SP-052 Run B hazard: a clone of the owner's live profile
/// produced confidently-wrong evidence for a non-owner cell).</summary>
public sealed class DtrhM2TestFixtureException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// <summary>
/// SP-057: the DECLARED starting document for <c>--dtrh-m2test</c> (HARNESS-ONLY).
/// Before this seam the test clone deep-copied the LIVE slot document, so evidence
/// inherited whatever the owner's real profile happened to hold. The clone now starts
/// from this committed fixture — a verbatim JSON document, reviewable in diff.
///
/// The values are SENTINELS, not serialized defaults (777 / 4242 / 3): any log line or
/// screenshot showing these numbers is instantly recognizable as fixture-origin data,
/// and fixture-origin can never be mistaken for live-profile data (consult b2). Values
/// stay schema-valid (no unknown ids).
///
/// In-code because the project file is outside SP-057's File Scope (no new
/// Content/EmbeddedResource glob possible). "Missing" is therefore compile-impossible;
/// "malformed" throws <see cref="DtrhM2TestFixtureException"/> and is pinned by unit
/// test. If a future row moves this to a real file, only <see cref="Load"/>'s default
/// argument changes — the signature already takes the JSON.
/// </summary>
public static class DtrhM2TestFixture
{
    /// <summary>The committed fixture document (camelCase — the slot store's naming policy).
    /// Absent members keep the <see cref="DtrhSlotDocument"/> initializers, which ARE the
    /// WPF additive-only defaults (DtrhSaveSlots.cs header).</summary>
    public const string DefaultFixtureJson = """
        {
          "sparks": 777,
          "bestScore": 4242,
          "runsCompleted": 3
        }
        """;

    /// <summary>Parse a fixture document. Malformed JSON or a non-object payload is a
    /// typed loud failure — never a silent substitute.</summary>
    public static DtrhSlotDocument Load(string json = DefaultFixtureJson)
    {
        try
        {
            return JsonSerializer.Deserialize<DtrhSlotDocument>(json, SerializerOptions)
                ?? throw new DtrhM2TestFixtureException("m2test fixture deserialized to null — refusing to start a test clone from nothing");
        }
        catch (JsonException ex)
        {
            throw new DtrhM2TestFixtureException(
                $"m2test fixture is malformed ({ex.Message}) — refusing to fall back to the live slot document", ex);
        }
    }

    // The same camelCase policy the slot stores persist with (PersistenceStore.cs:89-92).
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
