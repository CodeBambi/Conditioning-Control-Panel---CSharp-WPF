using System;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// The one place the companion's user-input frames are written.
    ///
    /// <para>Before Train 1 each of the six one-shot <see cref="IAiService"/> methods carried its own
    /// copy of the frame text in all three providers — 18 copies of 6 strings, which had already
    /// drifted (the awareness frame's <c>Duration</c> was hardcoded to <c>0m</c> in every provider
    /// until Train 0, and the tokenizer-artifact cleanup existed only in the OpenAI-compatible one).
    /// Every provider now formats through this class, so a wording change is one edit and the golden
    /// tests prove the wording did not move.</para>
    ///
    /// <para>Two vocabularies live here on purpose:</para>
    /// <list type="bullet">
    /// <item><b>Frames</b> (<c>*Frame</c>) are the LEGACY one-shot user messages, byte-identical to
    /// what the providers sent before this refactor. They are what goes on the wire when
    /// <c>UseCompanionBrain</c> is off.</item>
    /// <item><b>Events</b> (<c>*Event</c>) are the Train 1 ambient descriptors handed to
    /// <c>CompanionBrain.ReactAsync</c>. They are prose, not tags, because the brain wraps them in the
    /// <c>«event: …»</c> sigil and clamps them to ~25 tokens — a bracketed frame does not survive that
    /// clamp (the default lock-screen frame alone is ~130 characters).</item>
    /// </list>
    /// </summary>
    internal static class FrameFormatter
    {
        // ===================== shared bits =====================

        /// <summary>
        /// The duration bucketing every activity frame uses: seconds under a minute, minutes under an
        /// hour, hours above. Null reads as zero (the awareness path's "not known").
        /// </summary>
        public static string Duration(TimeSpan? duration)
        {
            var elapsed = duration ?? TimeSpan.Zero;
            if (elapsed.TotalMinutes < 1) return $"{(int)elapsed.TotalSeconds}s";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m";
            return $"{(int)elapsed.TotalHours}h";
        }

        /// <summary>App label for an awareness frame: the service name when we have one, else the raw detection.</summary>
        private static string AppLabel(string detectedName, string serviceName) =>
            string.IsNullOrEmpty(serviceName) ? detectedName : serviceName;

        /// <summary>Title for an awareness frame: the page/tab title when we have one, else the raw detection.</summary>
        private static string TitleLabel(string detectedName, string pageTitle) =>
            string.IsNullOrEmpty(pageTitle) ? detectedName : pageTitle;

        // ===================== legacy frames =====================

        /// <summary>
        /// Awareness context tag. Exact legacy shape:
        /// <c>[Category: X | App: Y | Title: Z | Duration: Nm]</c>.
        /// </summary>
        public static string AwarenessFrame(string detectedName, string category, string serviceName,
            string pageTitle, TimeSpan? duration)
        {
            var website = AppLabel(detectedName, serviceName);
            var tabName = TitleLabel(detectedName, pageTitle);
            return $"[Category: {category} | App: {website} | Title: {tabName} | Duration: {Duration(duration)}]";
        }

        /// <summary>
        /// "Still on" context tag. Same shape as <see cref="AwarenessFrame"/>, with the display name
        /// standing in for both App and Title (the still-on path has no separate tab title).
        /// </summary>
        public static string StillOnFrame(string displayName, string category, TimeSpan duration) =>
            $"[Category: {category} | App: {displayName} | Title: {displayName} | Duration: {Duration(duration)}]";

        /// <summary>
        /// Keyword-trigger frame. <paramref name="promptTemplate"/> is user-authored (per keyword
        /// trigger) and wins when present; its <c>{keyword}</c> placeholder is substituted.
        /// </summary>
        public static string KeywordFrame(string keyword, string? promptTemplate) =>
            string.IsNullOrEmpty(promptTemplate)
                ? $"You just caught the user on the word '{keyword}'. React in character, one short line."
                : promptTemplate.Replace("{keyword}", keyword);

        /// <summary>
        /// Lock-card frame. User-authored template wins; placeholders are <c>{sentance}</c> (sic —
        /// the misspelling is the shipped contract users have in their settings), <c>{mistakes}</c>
        /// and <c>{amount}</c>.
        /// </summary>
        public static string LockScreenFrame(string sentance, int mistakes, int amount, string? promptTemplate)
        {
            if (string.IsNullOrEmpty(promptTemplate))
            {
                return $"The user made {mistakes} mistakes in '{sentance}' for the lock screen. " +
                       $"They had to type it {amount} of time. React in character, one short line.";
            }

            var text = promptTemplate.Replace("{sentance}", sentance);
            text = text.Replace("{mistakes}", mistakes.ToString());
            text = text.Replace("{amount}", amount.ToString());
            return text;
        }

        /// <summary>Mandatory-video-finished frame. User-authored template wins; placeholder is <c>{title}</c>.</summary>
        public static string VideoDoneFrame(string title, string? promptTemplate) =>
            string.IsNullOrEmpty(promptTemplate)
                ? $"The user has just finished the mandatory video {title}. React in character, one short line."
                : promptTemplate.Replace("{title}", title);

        // ===================== Train 1 ambient descriptors =====================

        /// <summary>
        /// True when an ambient moment can be expressed as a compact <c>«event: …»</c> descriptor.
        ///
        /// <para>A user-authored <paramref name="promptTemplate"/> is an INSTRUCTION, not a record of
        /// something that happened: wrapping it in the event sigil misreads it, and the brain's 25-token
        /// clamp would truncate anything longer than ~100 characters. Train 1 has nowhere else to put a
        /// per-call instruction (that is the prompt assembler's dynamic tail, Train 2), so those calls
        /// stay on the legacy stateless path and behave exactly as they do today.</para>
        /// </summary>
        public static bool CanRouteAmbient(string? promptTemplate) => string.IsNullOrEmpty(promptTemplate);

        /// <summary>Ambient descriptor for a freshly detected activity. Title goes last so the clamp eats it first.</summary>
        public static string AwarenessEvent(string detectedName, string category, string serviceName,
            string pageTitle, TimeSpan? duration)
        {
            var website = AppLabel(detectedName, serviceName);
            var tabName = TitleLabel(detectedName, pageTitle);
            var head = $"user is on {website} ({category}) for {Duration(duration)}";
            return string.Equals(tabName, website, StringComparison.Ordinal)
                ? head
                : head + $", tab \"{tabName}\"";
        }

        /// <summary>Ambient descriptor for "they are STILL on this".</summary>
        public static string StillOnEvent(string displayName, string category, TimeSpan duration) =>
            $"user is still on {displayName} ({category}) after {Duration(duration)}";

        /// <summary>Ambient descriptor for a keyword trigger.</summary>
        public static string KeywordEvent(string keyword) => $"user just said the word \"{keyword}\"";

        /// <summary>Ambient descriptor for a completed lock-card mantra.</summary>
        public static string LockScreenEvent(string sentance, int mistakes, int amount) =>
            $"user typed the mantra \"{sentance}\" {amount}x with {mistakes} mistake(s)";

        /// <summary>Ambient descriptor for a finished mandatory video.</summary>
        public static string VideoDoneEvent(string title) => $"user finished the mandatory video \"{title}\"";
    }
}
