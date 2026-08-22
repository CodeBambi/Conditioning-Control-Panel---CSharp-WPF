using System.Text.RegularExpressions;

namespace CcpClient.Desktop.Ai;

/// <summary>
/// Companion reply hygiene — the subtractive half of WPF's reply-text hygiene layer
/// (WPF `AiTextHygiene`, `ConditioningControlPanel/Services/AIService/AiTextHygiene.cs`; leak
/// predicate from `AiResponseParser.cs:53`; shipped user-visible fix `932d829a`, 2026-08-07).
/// Between `IAiProvider.CompleteAsync` returning and the text reaching the bubble, memory, or
/// disk, these three rules are all that touch it besides output moderation and the privacy filters' link
/// strip. Every rule here only ever REMOVES text: nothing is observed, persisted, logged, or
/// transmitted that was not before.
///
/// <para>H1 — reasoning blocks and tokenizer artifacts (WPF `Clean`, :25/:30/:287). Local
/// models emit chain-of-thought wrappers and raw tokenizer bytes; both render as gibberish in
/// the speech bubble.</para>
///
/// <para>H2 — metadata-tag echoes (WPF `StripMetadataTags`, :36/:40/:44/:53/:61, collapse+trim
/// :79-80). The port's OWN trigger: `AiAwarenessService.cs:229` sends the model a context line
/// in exactly the shape `[Category: X | App: Y | Title: Z | Duration: N]`; local models mirror
/// bracketed metadata back. The port sends the shape and — before this packet — stripped
/// nothing.</para>
///
/// <para>H3 — envelope-leak DETECTION only (WPF `AiResponseParser.LooksLikeEnvelopeLeak`, :53).
/// The WPF salvage half (`TryLiftResponseField`, :60, and every repair path around it) is
/// deliberately NOT ported: contract §8 is zero repair, and admitting an envelope would put
/// model text on the command-execution path. Detection yields the existing typed
/// <see cref="AiReplyCodes.MalformedOutput"/>; `AiEnvelopeValidator` stays unwired.</para>
/// </summary>
public static class AiTextHygiene
{
    // H1 (WPF AiTextHygiene.cs:25-28): reasoning models wrap their scratchpad in these.
    // Non-greedy, multi-line, and tolerant of an unclosed opener: a reply truncated mid-thought
    // would otherwise render the whole scratchpad. The (</\1>|$) alternation is the whole
    // question for the unterminated case — ported as-is: chain-of-thought is never
    // user-intended content, so a truncated opener eats to end-of-string (fail-closed).
    private static readonly Regex ReasoningBlock = new(
        @"<(think|thinking|reasoning|thought)>.*?(</\1>|$)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    // H1 (WPF AiTextHygiene.cs:30-33): a stray closing tag left over once its opener was
    // trimmed upstream.
    private static readonly Regex OrphanReasoningClose = new(
        @"</(think|thinking|reasoning|thought)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // H2 patterns (WPF AiTextHygiene.cs:36,40,44,53,61) — the ORDER is semantic, not
    // incidental (record.md §2: the end-anchored unclosed passes must see the string after
    // closed tags are gone; permuting them changes the output on nested-truncation input).
    // 1. Context metadata the model echoes back instead of answering, closed form.
    private static readonly Regex ClosedCategoryTag = new(
        @"\[Category:[^\]]*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 2. Reaction category tags like [Media/Streaming] or [Gaming/Casual].
    private static readonly Regex ReactionCategoryTag = new(
        @"\[[A-Za-z]+/[A-Za-z]+\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 3. Any standalone closed bracket tag that looks like metadata.
    private static readonly Regex ClosedMetadataTag = new(
        @"\[(?:Category|App|Title|Duration|Context):[^\]]*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 4. End-anchored truncation pass: a known metadata keyword (\b so "[Apple pie" isn't
    //    eaten by "App"), colon optional. Excludes newlines — truncation can only land on the
    //    LAST line (WPF comment; the port sends no token cap, but a local model can still cut
    //    its own reply mid-tag).
    private static readonly Regex UnclosedKnownTag = new(
        @"\[(?:Category|App|Title|Duration|Context)\b[^\]\r\n]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 5. End-anchored truncation pass for unknown keywords: anything shaped like "[Word:".
    //    The colon is what marks the fragment as metadata rather than prose, so stage
    //    directions ("[giggles") and citations ("[3") survive (WPF comment).
    private static readonly Regex UnclosedKeyedTag = new(
        @"\[[A-Za-z][A-Za-z0-9 _-]*:[^\]\r\n]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// H1 — strip reasoning blocks and tokenizer artifacts (WPF `AiTextHygiene.Clean`, :287-297).
    /// Whitespace is otherwise left alone; callers layer their own sanitising on top.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var cleaned = ReasoningBlock.Replace(text, "");
        cleaned = OrphanReasoningClose.Replace(cleaned, "");

        // GPT-2/GPT-Neo/llama.cpp tokenizers sometimes emit 'Ġ' for leading spaces, and 'Ċ'
        // for newlines. Seeing either in user-visible text means raw tokens reached the bubble.
        cleaned = cleaned.Replace("Ġ", " ").Replace("Ċ", "\n");

        return cleaned.Trim();
    }

    /// <summary>
    /// H2 — strip leaked context-metadata tags, closed or truncated, in WPF's fixed order
    /// (WPF `AiTextHygiene.StripMetadataTags`, :69-81), then collapse the whitespace the
    /// removal leaves behind. An empty result means the reply was nothing but metadata — the
    /// caller decides the typed outcome.
    /// </summary>
    public static string StripMetadataTags(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var sanitized = ClosedCategoryTag.Replace(text, "");
        sanitized = ReactionCategoryTag.Replace(sanitized, "");
        sanitized = ClosedMetadataTag.Replace(sanitized, "");
        sanitized = UnclosedKnownTag.Replace(sanitized, "");
        sanitized = UnclosedKeyedTag.Replace(sanitized, "");

        sanitized = Regex.Replace(sanitized, @"\s{2,}", " ");
        return sanitized.Trim();
    }

    /// <summary>
    /// H3 — envelope-leak DETECTION, and nothing more (WPF
    /// `AiResponseParser.LooksLikeEnvelopeLeak`, :53-58, verbatim): trimmed text starts with
    /// <c>{</c> AND contains <c>"response"</c>. WPF then lifts the field out
    /// (`TryLiftResponseField`, :60); the port does NOT — no extraction, no JSON-unescape, no
    /// repair, no command execution, no call into `AiEnvelopeValidator`. A true verdict is a
    /// refusal: the caller answers with the existing typed
    /// <see cref="AiReplyCodes.MalformedOutput"/> and no text reaches anyone.
    /// </summary>
    public static bool LooksLikeEnvelopeLeak(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var t = text.TrimStart();
        return t.StartsWith('{') && t.Contains("\"response\"", StringComparison.Ordinal);
    }
}
