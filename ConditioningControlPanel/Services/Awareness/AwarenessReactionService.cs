using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The parsed result of one awareness LLM call.
    /// </summary>
    /// <param name="Line">The line to speak. Empty whenever <see cref="Passed"/> is true or nothing usable came back.</param>
    /// <param name="Callback">
    /// The optional past-tense variant the model was invited to add (doc 02 §4.3). The arbiter speaks
    /// this one instead when the user has already moved on by the time the reply lands — the delivery
    /// re-tag, rather than dropping the line or delivering a stale one.
    /// </param>
    /// <param name="Passed">The model answered <c>[PASS]</c>: it had nothing good, and we honour that.</param>
    /// <param name="IsAiGenerated">True only for genuine model text. Same badge semantics as everywhere else.</param>
    /// <param name="Refusal">Non-null when the moderation spine blocked the call in either direction.</param>
    /// <param name="Reason">Log-safe outcome token: <c>ok</c>, <c>pass</c>, <c>refused</c>, <c>no-text</c>, …</param>
    public sealed record AwarenessReaction(
        string Line,
        string? Callback,
        bool Passed,
        bool IsAiGenerated,
        ModerationRefusalInfo? Refusal,
        string Reason)
    {
        /// <summary>Nothing to say, for the stated reason. Never a delivery, so no cooldown is burnt.</summary>
        public static AwarenessReaction None(string reason) =>
            new(string.Empty, null, Passed: false, IsAiGenerated: false, Refusal: null, Reason: reason);

        /// <summary>The model declined, on purpose. Distinct from a failure so the arbiter can refund the slot.</summary>
        public static AwarenessReaction Pass { get; } =
            new(string.Empty, null, Passed: true, IsAiGenerated: true, Refusal: null, Reason: "pass");

        /// <summary>True when there is a line worth putting in a bubble.</summary>
        public bool HasLine => !Passed && Line.Length > 0;
    }

    /// <summary>
    /// The v2 awareness LLM path: one cut frame in, at most one line out (doc 02 §3.1).
    ///
    /// <para><b>It is only ever reached from a frame.</b> There is no timer, no poll and no "check in
    /// with the AI every N seconds" entry point here, and doc 02 §7 item 6 says there must never be
    /// one. The single public method takes a <see cref="ContextFrame"/>, which by construction only
    /// exists because something happened.</para>
    ///
    /// <para><b>It does not pace itself, on purpose.</b> Cooldowns, the hourly line budget and the
    /// "burn the cooldown on delivery, not on attempt" rule all live in the one shared ledger the
    /// arbiter owns (<see cref="ReactionCooldownLedger"/>). A second cooldown dialect in here is
    /// exactly how the two-mouths bug happened the first time (doc 02 §1.5), so this class assumes its
    /// caller already decided the moment was worth spending on, and returning a line is NOT a delivery
    /// — the arbiter records that when the line actually reaches a bubble.</para>
    ///
    /// <para><b>Moderation is untouched.</b> The full Layer-1 spine — <c>CheckInput</c> on the frame
    /// message, <c>CheckOutput</c> on the reply, <c>ModerationLog</c> — runs inside
    /// <see cref="IAiService.SendAsync"/> exactly as it does for chat, which is why the frame is sent
    /// as a user-role message rather than being folded into the system prompt: a system-only request
    /// would leave the input check with nothing to look at. <see cref="AiCallOptions.Interactive"/>
    /// stays false, so a blocked ambient line is logged for compliance but never escalates the
    /// user-facing Content Policy Notice for text the user did not type.</para>
    /// </summary>
    public sealed class AwarenessReactionService
    {
        /// <summary>The output contract's hard cap. Anything longer is trimmed rather than dropped.</summary>
        public const int MaxLineLength = 140;

        /// <summary>The silence token the contract offers and doc 02 §3.4 asks us to honour.</summary>
        public const string PassToken = "[PASS]";

        /// <summary>Prefix of the optional past-tense second line (doc 02 §4.3).</summary>
        public const string CallbackPrefix = "CALLBACK:";

        private readonly Func<IAiService?> _transport;
        private readonly AwarenessPromptBuilder _builder;
        private readonly Func<bool> _isMachineLocal;
        private readonly Func<bool> _isEnabled;

        /// <param name="transport">Defaults to <c>App.Ai</c>, resolved per call so a provider switch takes effect live.</param>
        /// <param name="builder">Prompt builder. Defaults to a fresh one (it caches its own prefixes).</param>
        /// <param name="isMachineLocal">
        /// Whether the active provider runs on this machine, which is what licenses the fuller
        /// projection. Defaults to "the provider is Ollama" and nothing else: an OpenAI-compatible
        /// endpoint MAY be a localhost server, but it may equally be a hosted one, and the projection
        /// layer does not get to guess (addendum D — when privacy cannot answer, take the narrow path).
        /// </param>
        /// <param name="isEnabled">
        /// The v2 kill switch / awareness toggle / consent gate, defaulting to
        /// <see cref="AwarenessObserver.IsEnabled"/>. The observer and the arbiter already refuse to
        /// reach here when awareness is off, so this is defence in depth for future callers (a privacy
        /// panel preview, say) rather than the primary gate — but a "no consent" that only holds in
        /// one of three places is not a consent gate.
        /// </param>
        public AwarenessReactionService(
            Func<IAiService?>? transport = null,
            AwarenessPromptBuilder? builder = null,
            Func<bool>? isMachineLocal = null,
            Func<bool>? isEnabled = null)
        {
            _transport = transport ?? (() => App.Ai);
            _builder = builder ?? new AwarenessPromptBuilder();
            _isMachineLocal = isMachineLocal ?? DefaultIsMachineLocal;
            _isEnabled = isEnabled ?? (() => AwarenessObserver.IsEnabled);
        }

        /// <summary>The prompt builder in use. Exposed so the privacy panel can show the real prompt.</summary>
        public AwarenessPromptBuilder Prompts => _builder;

        /// <summary>
        /// Asks the model for a reaction to one frame. Never throws: awareness degrading to silence is
        /// a missed joke, awareness degrading to an unhandled exception on a background path is a crash
        /// log (the observer runs on a timer).
        /// </summary>
        public async Task<AwarenessReaction> GetAwarenessReactionAsync(
            ContextFrame? frame, CancellationToken cancellationToken = default)
        {
            if (frame == null) return AwarenessReaction.None("null-frame");
            if (!_isEnabled()) return AwarenessReaction.None("v2-off");

            var transport = _transport();
            if (transport == null) return AwarenessReaction.None("no-transport");

            AwarenessPrompt prompt;
            try
            {
                prompt = _builder.Build(frame, local: _isMachineLocal());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[AWARE] prompt build failed");
                return AwarenessReaction.None("prompt-failed");
            }

            App.Logger?.Debug("{Line}", prompt.LogLine);

            AiReplyResult result;
            try
            {
                var options = AiCallOptions.Reaction with
                {
                    MaxTokens = AwarenessPromptBuilder.ResponseMaxTokens
                };
                result = await transport.SendAsync(prompt.Messages, options, cancellationToken)
                                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return AwarenessReaction.None("cancelled");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[AWARE] reaction call failed");
                return AwarenessReaction.None("transport-failed");
            }

            if (result == null) return AwarenessReaction.None("no-result");

            if (result.Refusal != null)
            {
                App.Logger?.Debug("[AWARE] reaction refused by the moderation spine");
                return new AwarenessReaction(string.Empty, null, Passed: false, IsAiGenerated: false,
                    Refusal: result.Refusal, Reason: "refused");
            }

            if (!result.IsAiGenerated) return AwarenessReaction.None("no-ai");

            var parsed = Parse(result.Text);
            App.Logger?.Debug("[AWARE] reaction key={Key} tier={Tier} outcome={Outcome} len={Len}",
                AwarenessText.SanitizeId(prompt.CardKey), prompt.Tier, parsed.Reason, parsed.Line.Length);
            return parsed;
        }

        /// <summary>
        /// Pulls the contracted shape out of a raw reply: the one line, the optional
        /// <c>CALLBACK:</c> variant, and the <c>[PASS]</c> silence token.
        ///
        /// <para>Runs the same hygiene every other reply path runs first — reasoning blocks, tokenizer
        /// artifacts, echoed context tags and the bark-echo sigil — because a small model imitating the
        /// prompt's own scaffolding is a live failure mode, not a hypothetical one.</para>
        /// </summary>
        internal static AwarenessReaction Parse(string? raw)
        {
            var cleaned = AiTextHygiene.StripMetadataTags(
                AiTextHygiene.UnwrapSpokenSigil(AiTextHygiene.Clean(raw)));
            if (cleaned.Length == 0) return AwarenessReaction.None("empty");

            string? line = null;
            string? callback = null;

            foreach (var rawLine in cleaned.Replace("\r\n", "\n").Split('\n'))
            {
                var text = Unquote(rawLine.Trim());
                if (text.Length == 0) continue;

                if (text.StartsWith(CallbackPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    callback ??= Clamp(Unquote(text.Substring(CallbackPrefix.Length).Trim()));
                    continue;
                }

                line ??= text;
            }

            if (line == null)
            {
                // A reply that was nothing but a CALLBACK line still said something usable — the
                // arbiter would rather have the past-tense variant than silence.
                return callback is { Length: > 0 }
                    ? new AwarenessReaction(callback, null, false, true, null, "callback-only")
                    : AwarenessReaction.None("no-line");
            }

            if (IsPass(line)) return AwarenessReaction.Pass;

            var clamped = Clamp(line);
            if (clamped.Length == 0) return AwarenessReaction.None("no-line");

            return new AwarenessReaction(clamped, string.IsNullOrEmpty(callback) ? null : callback,
                Passed: false, IsAiGenerated: true, Refusal: null, Reason: "ok");
        }

        /// <summary>
        /// True for the silence token in the shapes models actually emit it in: bare, quoted, with a
        /// trailing full stop, or lowercased.
        /// </summary>
        internal static bool IsPass(string? line)
        {
            var text = Unquote((line ?? string.Empty).Trim()).TrimEnd('.', '!', '~', ' ');
            return string.Equals(text, PassToken, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "PASS", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Trims to <see cref="MaxLineLength"/> at a word boundary. Truncation is a last resort — the
        /// contract asks for 140 — but a clipped line still reads better than a wall of text in a
        /// speech bubble sized for one.
        /// </summary>
        internal static string Clamp(string? text)
        {
            var line = (text ?? string.Empty).Trim();
            if (line.Length <= MaxLineLength) return line;

            var cut = line.Substring(0, MaxLineLength - 1);
            int lastSpace = cut.LastIndexOf(' ');
            if (lastSpace > MaxLineLength / 2) cut = cut.Substring(0, lastSpace);
            return cut.TrimEnd() + "…";
        }

        /// <summary>Sheds one symmetric layer of wrapping quotes, which models add despite being told not to.</summary>
        internal static string Unquote(string text)
        {
            if (text.Length < 2) return text;

            char first = text[0];
            char last = text[text.Length - 1];
            bool matched =
                (first == '"' && last == '"') ||
                (first == '\'' && last == '\'') ||
                (first == '“' && last == '”') ||
                (first == '‘' && last == '’');

            return matched ? text.Substring(1, text.Length - 2).Trim() : text;
        }

        private static bool DefaultIsMachineLocal()
        {
            try
            {
                return App.Settings?.Current?.CompanionPrompt?.AiProvider == AiProviderType.Local;
            }
            catch { return false; }
        }
    }
}
