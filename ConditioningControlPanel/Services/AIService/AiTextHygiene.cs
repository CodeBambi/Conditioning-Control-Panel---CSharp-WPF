using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// #739: shared cleanup for anything a language model hands back, whatever provider it came from.
    ///
    /// A user reported the companion "randomly spitting out gibberish text instead of an actual
    /// message". Two hygiene steps existed but were wired into only one of the three provider paths:
    ///
    ///   - the tokenizer-artifact strip lived as a private helper in OpenAiCompatibleService, so the
    ///     cloud path (AiService) and the local path (LocalAiService) never ran it, even though the
    ///     team had already identified that exact class of garbage;
    ///   - nothing anywhere stripped reasoning blocks. LocalAiService asks Ollama for think:false,
    ///     but the cloud proxy sends no equivalent, so a reasoning-capable model's chain of thought
    ///     would be rendered straight into the speech bubble.
    ///
    /// Centralised here so a fourth provider cannot quietly miss it again.
    /// </summary>
    internal static class AiTextHygiene
    {
        // Reasoning models wrap their scratchpad in these. Non-greedy, multi-line, and tolerant of an
        // unclosed opener: a reply truncated mid-thought would otherwise render the whole scratchpad.
        private static readonly Regex ReasoningBlock = new(
            @"<(think|thinking|reasoning|thought)>.*?(</\1>|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        // A stray closing tag left over once its opener was trimmed upstream.
        private static readonly Regex OrphanReasoningClose = new(
            @"</(think|thinking|reasoning|thought)>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Strip tokenizer artifacts and reasoning blocks. Whitespace is normalised but the text is
        /// otherwise left alone - callers layer their own product-specific sanitising on top.
        /// </summary>
        internal static string Clean(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            var cleaned = ReasoningBlock.Replace(text, "");
            cleaned = OrphanReasoningClose.Replace(cleaned, "");

            // GPT-2/GPT-Neo/llama.cpp tokenizers sometimes emit 'Ġ' for leading spaces, and 'Ċ' for
            // newlines. Seeing either in user-visible text means raw tokens reached the bubble.
            cleaned = cleaned.Replace("Ġ", " ").Replace("Ċ", "\n");

            return cleaned.Trim();
        }
    }
}
