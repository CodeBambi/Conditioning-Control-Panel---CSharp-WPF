using System;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.Fyp.Online
{
    /// <summary>
    /// Why adding a subreddit did not work, in one vocabulary.
    ///
    /// <para>The refusals are not interchangeable and the difference is the whole of the answer:
    /// a name Scrolller does not carry will never work no matter how many times it is retyped,
    /// while a name we could not REACH is worth trying again in a minute. The old wording -
    /// "r/X doesn't exist or has no media" - fused those two and named neither the provider nor
    /// the reason, which is how a report arrives reading "App won't let me add the category! Is
    /// the criteria for 'media' pretty restrictive?" (support, 2026-09-19). There is no criteria:
    /// the feed is Scrolller's, and Scrolller either carries a community or it does not.</para>
    /// </summary>
    internal enum RemoteSubAddOutcome
    {
        /// <summary>It went in. Nothing to say.</summary>
        Added,

        /// <summary>Not a subreddit name at all (or a link we could not read one out of).</summary>
        NotAName,

        /// <summary>A real answer: Scrolller does not carry this community.</summary>
        NotCarried,

        /// <summary>We could not reach the provider, so we learned nothing about the name.</summary>
        Unreachable,

        /// <summary>Kept already AND already feeding this surface.</summary>
        AlreadyAdded,

        /// <summary>The kept library is at its cap; something has to go first.</summary>
        LibraryFull,
    }

    /// <summary>
    /// The one place those outcomes turn into words. Three surfaces ask the same question about a
    /// typed name (the Assets tab, the For You popover, and the Back Room's niche field), and
    /// before this they carried three separate wordings that drifted apart.
    ///
    /// <para>Pure apart from <see cref="Loc"/>: <see cref="Classify"/> takes the probe's two
    /// fields and nothing else, so the decision is unit-testable without a network or a window.</para>
    /// </summary>
    internal static class RemoteSubAddMessages
    {
        /// <summary>
        /// Read a probe result. <paramref name="probeError"/> is <see cref="SubProbe.Error"/>:
        /// null with <paramref name="probeOk"/> false is the provider's real "no such community"
        /// answer, which is why it is a verdict worth remembering and a transport failure is not.
        /// </summary>
        internal static RemoteSubAddOutcome Classify(bool probeOk, string? probeError)
        {
            if (probeOk) return RemoteSubAddOutcome.Added;
            if (string.Equals(probeError, "offline", StringComparison.Ordinal))
                return RemoteSubAddOutcome.Unreachable;
            if (string.Equals(probeError, "invalid", StringComparison.Ordinal))
                return RemoteSubAddOutcome.NotAName;
            // Any OTHER worded error is still only something that happened to the request, not a
            // statement about the community. Treat it the way a timeout is treated: try again.
            if (!string.IsNullOrEmpty(probeError)) return RemoteSubAddOutcome.Unreachable;
            return RemoteSubAddOutcome.NotCarried;
        }

        /// <summary>Localization key for an outcome. <see cref="RemoteSubAddOutcome.Added"/> has
        /// none: success is the pill appearing, not a line of text.</summary>
        internal static string? KeyFor(RemoteSubAddOutcome outcome) => outcome switch
        {
            RemoteSubAddOutcome.NotAName => "msg_remote_sub_invalid",
            RemoteSubAddOutcome.NotCarried => "msg_remote_sub_not_found",
            RemoteSubAddOutcome.Unreachable => "msg_remote_sub_offline",
            RemoteSubAddOutcome.AlreadyAdded => "msg_remote_sub_duplicate",
            RemoteSubAddOutcome.LibraryFull => "msg_remote_sub_library_cap",
            _ => null,
        };

        /// <summary>
        /// The line to show under the box. <paramref name="name"/> is the sanitized sub (no
        /// "r/"); <paramref name="libraryCap"/> only reaches
        /// <see cref="RemoteSubAddOutcome.LibraryFull"/>. Null for a success.
        /// </summary>
        internal static string? Describe(RemoteSubAddOutcome outcome, string? name, int libraryCap)
        {
            var key = KeyFor(outcome);
            if (key == null) return null;

            var text = Loc.Get(key);
            // A missing key resolves to the key itself in some hosts; a raw key on screen is
            // worse than the English sentence it stands for.
            if (string.IsNullOrWhiteSpace(text) || string.Equals(text, key, StringComparison.Ordinal))
                text = Fallback(outcome);

            var arg = outcome == RemoteSubAddOutcome.LibraryFull
                ? (object)libraryCap
                : name ?? string.Empty;
            try { return string.Format(text, arg); }
            catch (FormatException) { return text; }
        }

        /// <summary>English of last resort, kept next to the keys it stands in for.</summary>
        private static string Fallback(RemoteSubAddOutcome outcome) => outcome switch
        {
            RemoteSubAddOutcome.NotAName =>
                "That is not a subreddit name. Try something like r/EroticHypnosis, or paste its link.",
            RemoteSubAddOutcome.NotCarried =>
                "Scrolller does not carry r/{0}. The feed is theirs, so only the communities they "
                + "host can be added here.",
            RemoteSubAddOutcome.Unreachable =>
                "Could not reach Scrolller, so r/{0} is still unchecked. Try again in a moment.",
            RemoteSubAddOutcome.AlreadyAdded => "r/{0} is already in your list.",
            RemoteSubAddOutcome.LibraryFull =>
                "You can keep up to {0} subreddits. Remove one with its X first.",
            _ => string.Empty,
        };
    }
}
