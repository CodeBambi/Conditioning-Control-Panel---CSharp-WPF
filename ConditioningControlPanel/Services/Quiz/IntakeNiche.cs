using System;
using System.Windows.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Quiz
{
    /// <summary>
    /// Single source of truth for "which flavour of intake is this user in" - the niche slug
    /// (default / bambi / sissy / drone / circe) derived from the active mod. "default" is the
    /// neutral house niche an unmodded install gets; the four themed niches are opt-ins a mod asks
    /// for. It is a REAL niche, not a stand-in: it ships its own pass card
    /// (Resources/intake/pass_card_default.png) and prompt bank
    /// (Resources/web/intake/banks/default.json).
    ///
    /// This existed first as <c>IntakeHostService.DesiredNiche()</c>, which picks the prompt bank
    /// for a run. The weekly pass card and its nudge popup need the SAME answer so the art a user
    /// sees matches the intake they would actually get, so the logic moved here and
    /// <c>DesiredNiche()</c> now delegates. Do not fork this - a second copy silently drifts, and
    /// the failure mode (drone user shown bambi art) reads as a content bug, not a mapping bug.
    /// </summary>
    internal static class IntakeNiche
    {
        /// <summary>Every niche this app ships art and prompt banks for. Order is display order,
        /// neutral first.</summary>
        internal static readonly string[] All = { "default", "bambi", "sissy", "drone", "circe" };

        /// <summary>Niche for an install with no themed mod signal. Neutral by design - a themed
        /// niche here would hand every unmodded user someone else's persona.</summary>
        internal const string Fallback = "default";

        /// <summary>
        /// Niche the ACTIVE MOD asks for, before any bank-availability clamp. Built-in ids win;
        /// third-party .ccpmod files declare theirs via a manifest tag. The legacy two-value
        /// <see cref="ContentMode"/> enum is consulted only for its one positive signal (SissyHypno)
        /// when there is no mod signal at all; its other value is "not sissy", which is NOT a request
        /// for bambi, so it resolves to the neutral <see cref="Fallback"/>.
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

                // Only the positive SissyHypno reading counts. ContentMode's other value means
                // "no sissy mod", not "bambi", so everything else lands on the neutral niche.
                if (App.Settings?.Current?.ContentMode == ContentMode.SissyHypno) return "sissy";
                return Fallback;
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
