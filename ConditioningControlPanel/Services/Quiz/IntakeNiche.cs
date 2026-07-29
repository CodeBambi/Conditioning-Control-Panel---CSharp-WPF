using System;
using System.Windows.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Quiz
{
    /// <summary>
    /// Single source of truth for "which flavour of intake is this user in" - the niche slug
    /// (bambi / sissy / drone / circe) derived from the active mod.
    ///
    /// This existed first as <c>IntakeHostService.DesiredNiche()</c>, which picks the prompt bank
    /// for a run. The weekly pass card and its nudge popup need the SAME answer so the art a user
    /// sees matches the intake they would actually get, so the logic moved here and
    /// <c>DesiredNiche()</c> now delegates. Do not fork this - a second copy silently drifts, and
    /// the failure mode (drone user shown bambi art) reads as a content bug, not a mapping bug.
    /// </summary>
    internal static class IntakeNiche
    {
        /// <summary>Every niche this app ships art and prompt banks for. Order is display order.</summary>
        internal static readonly string[] All = { "bambi", "sissy", "drone", "circe" };

        internal const string Fallback = "bambi";

        /// <summary>
        /// Niche the ACTIVE MOD asks for, before any bank-availability clamp. Built-in ids win;
        /// third-party .ccpmod files declare theirs via a manifest tag. Falls back to the legacy
        /// two-value <see cref="ContentMode"/> enum only when there is no mod signal at all - that
        /// enum has no drone value and collapses every non-sissy mod to bambi, so it is a last
        /// resort, never a primary source.
        /// </summary>
        internal static string Current()
        {
            try
            {
                var modId = App.Mods?.ActiveModId;
                if (modId == BuiltInMods.DronificationId) return "drone";
                if (modId == BuiltInMods.SissyHypnoId) return "sissy";
                if (modId == BuiltInMods.LockedId) return "circe";

                // Locked's own tags ("locked"/"chastity") read as circe too.
                var tags = App.Mods?.ActiveMod?.Manifest?.Tags;
                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        if (string.Equals(tag, "drone", StringComparison.OrdinalIgnoreCase)) return "drone";
                        if (string.Equals(tag, "sissy", StringComparison.OrdinalIgnoreCase)) return "sissy";
                        if (string.Equals(tag, "circe", StringComparison.OrdinalIgnoreCase)) return "circe";
                        if (string.Equals(tag, "locked", StringComparison.OrdinalIgnoreCase)) return "circe";
                        if (string.Equals(tag, "chastity", StringComparison.OrdinalIgnoreCase)) return "circe";
                    }
                }

                return App.Settings?.Current?.ContentMode == ContentMode.SissyHypno ? "sissy" : Fallback;
            }
            catch { return Fallback; }
        }

        /// <summary>
        /// Art for the weekly pass card in the CURRENT niche, shared by the Dashboard tile face and
        /// the weekly nudge popup so the two can never disagree.
        ///
        /// Resolution order matters. A third-party mod override is checked FIRST and explicitly via
        /// <see cref="ModResourceResolver.HasModOverride"/>, because built-in mods have no
        /// InstalledPath - so a plain <c>ResolveImage("intake/pass_card.png")</c> would always fall
        /// straight through to the embedded generic file and the per-niche art below would never be
        /// reached.
        /// </summary>
        /// <returns>An ImageSource, or null when the resource is missing. Callers MUST handle null
        /// by hiding the image and leaving their text intact - the card is designed to read as
        /// vector-only art, so missing PNGs degrade to the pre-art layout rather than a blank tile.</returns>
        internal static ImageSource? PassCardImage()
        {
            try
            {
                const string generic = "intake/pass_card.png";
                if (ModResourceResolver.HasModOverride(generic))
                    return ModResourceResolver.ResolveImage(generic);

                return ModResourceResolver.ResolveImage($"intake/pass_card_{Current()}.png");
            }
            catch { return null; }
        }
    }
}
