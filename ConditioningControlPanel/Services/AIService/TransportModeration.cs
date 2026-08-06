using System;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// The Layer-1 moderation spine of ONE provider, in one object.
    ///
    /// <para>Semantics are lifted verbatim from the per-provider copies and must not change:</para>
    /// <list type="number">
    /// <item><b>Hard categories block.</b> The request never leaves the client (input) / the text is
    /// never shown (output), and the hit is written to <see cref="ModerationLog"/> — the CCBill
    /// compliance record.</item>
    /// <item><b><see cref="ProhibitedCategory.ProfessionalAdvice"/> is soft</b> (<c>Allow=true</c> with
    /// the category set): logged, never blocked, never escalated.</item>
    /// <item><b>Only user-typed input escalates.</b> <c>ModerationCounter.RecordHit</c> fires solely on
    /// the interactive chat path. Background reactions and model OUTPUT are logged for compliance but
    /// never push the user toward the Content Policy Notice / chat cooldown — that filtering is on us,
    /// not on them.</item>
    /// </list>
    ///
    /// <para><b>Why it exists.</b> Train 1 gives every provider a second entry point
    /// (<see cref="IAiService.SendAsync"/>) alongside its legacy one-shot path. Two entry points with
    /// two copies of the spine is exactly how the OpenAI-compatible provider ended up with no
    /// moderation at all for a whole release. Both paths in a provider call this instance, so they
    /// cannot fork.</para>
    ///
    /// <para><b>Seams.</b> The three <c>*Override</c> properties default to the <c>App.*</c> statics
    /// (null in a headless test host, which the checks treat as "no guard configured", i.e. allow).
    /// Tests set them to drive a provider's real, provider-configured spine — see
    /// <c>LocalAiService.CreateModeration</c> / <c>OpenAiCompatibleService.CreateModeration</c>.</para>
    /// </summary>
    internal sealed class TransportModeration
    {
        private readonly string _label;
        private readonly string _counterSource;
        private readonly Func<string> _modelHint;

        /// <param name="label">Serilog prefix, e.g. <c>LocalAiService</c>.</param>
        /// <param name="counterSource">
        /// Provider token in the <c>ModerationCounter</c> source string (<c>input:{counterSource}</c>).
        /// </param>
        /// <param name="modelHint">
        /// Evaluated per check, because the configured model can change between calls. Written to
        /// moderation.log as the <c>model_hint</c> column (<c>cloud</c>, <c>local:qwen3.5</c>, …).
        /// </param>
        public TransportModeration(string label, string counterSource, Func<string> modelHint)
        {
            _label = label;
            _counterSource = counterSource;
            _modelHint = modelHint ?? (() => "unknown");
        }

        /// <summary>Test seam for <c>App.ModerationGuard</c>.</summary>
        internal Func<IModerationGuard?>? GuardOverride { get; set; }

        /// <summary>Test seam for <c>App.ModerationLog.Record</c> — <c>(category, source, modelHint)</c>.</summary>
        internal Action<ProhibitedCategory, string, string>? RecordOverride { get; set; }

        /// <summary>Test seam for <c>App.ModerationCounter.RecordHit</c> — <c>(category, source)</c>.</summary>
        internal Action<ProhibitedCategory, string>? CounterOverride { get; set; }

        /// <summary>The counter source string this provider reports, e.g. <c>input:local</c>.</summary>
        public string InputCounterSource => "input:" + _counterSource;

        private IModerationGuard? Guard => GuardOverride != null ? GuardOverride() : App.ModerationGuard;

        private void Record(ProhibitedCategory category, string source)
        {
            var hint = _modelHint();
            if (RecordOverride != null) { RecordOverride(category, source, hint); return; }
            App.ModerationLog?.Record(category, source, hint);
        }

        private void Escalate(ProhibitedCategory category)
        {
            if (CounterOverride != null) { CounterOverride(category, InputCounterSource); return; }
            App.ModerationCounter?.RecordHit(category, InputCounterSource);
        }

        /// <summary>
        /// Scans text destined for the model. Returns the blocking category, or <c>null</c> when the
        /// text may proceed (including soft ProfessionalAdvice hits, which are logged here).
        /// </summary>
        /// <param name="escalate">
        /// True only when the user actually typed this text (<see cref="AiCallOptions.Interactive"/> /
        /// the legacy <c>returnRefusalSentinel</c> flag). The sole trigger for the user-facing
        /// Content Policy Notice.
        /// </param>
        public ProhibitedCategory? CheckInput(string? text, bool escalate)
        {
            var guard = Guard;
            if (guard == null) return null;

            var check = guard.CheckInput(text ?? string.Empty);
            if (!check.Allow && check.Category.HasValue)
            {
                Record(check.Category.Value, "input");
                if (escalate) Escalate(check.Category.Value);
                App.Logger?.Information("{Provider}: input blocked by ModerationGuard (category={Cat})",
                    _label, check.Category);
                return check.Category;
            }

            if (check.Allow && check.Category == ProhibitedCategory.ProfessionalAdvice)
                Record(ProhibitedCategory.ProfessionalAdvice, "input");

            return null;
        }

        /// <summary>
        /// Scans model output before it is shown. Returns the blocking category, or <c>null</c> when
        /// the text may be displayed. Never escalates the counter: model output tripping the filter is
        /// not the user's doing.
        /// </summary>
        public ProhibitedCategory? CheckOutput(string? text)
        {
            var guard = Guard;
            if (guard == null) return null;

            var check = guard.CheckOutput(text ?? string.Empty);
            if (!check.Allow && check.Category.HasValue)
            {
                Record(check.Category.Value, "output");
                App.Logger?.Information("{Provider}: output blocked by ModerationGuard (category={Cat})",
                    _label, check.Category);
                return check.Category;
            }

            if (check.Allow && check.Category == ProhibitedCategory.ProfessionalAdvice)
                Record(ProhibitedCategory.ProfessionalAdvice, "output");

            return null;
        }
    }
}
