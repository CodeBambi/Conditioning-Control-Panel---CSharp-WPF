using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Localization
{
    /// <summary>
    /// Descent vocabulary tokens (master doc §1B): localized strings may write <c>{petname}</c> and
    /// <c>{collective}</c> instead of hard-coding a mod's word for the user, and this class swaps in
    /// whatever the ACTIVE mod calls them — one English sentence, nine languages, every mod.
    ///
    /// The pass runs inside <see cref="LocalizationManager.Get"/>, after the key lookup and after the
    /// English fallback, so a string that only exists in en.json gets its tokens resolved too.
    ///
    /// INERT AS SHIPPED: no language file contains either token yet (this patch does not touch the
    /// nine JSON files), and no built-in mod sets an override, so every call is a single
    /// <c>IndexOf('{')</c> that misses. The plumbing lands first so the strings that need it can be
    /// written without a second round of code.
    ///
    /// Matching is exact and case-sensitive — <c>{petname}</c>, not <c>{PetName}</c> or
    /// <c>{ petname }</c> — because these tokens travel through translator hands and a fuzzy matcher
    /// would silently "fix" typos into strings nobody reviewed.
    /// </summary>
    public static class VocabTokens
    {
        public const string PetNameToken = "{petname}";
        public const string CollectiveToken = "{collective}";

        // ---------------------------------------------------------------------------------
        // VANILLA DEFAULTS — the two lines to edit when the name is decided.
        //
        // OPEN DECISION (planning/one-descent/DECISIONS.md, 2026-08-10): the vanilla petname is
        // still awaiting the owner's sign-off; "dreamer"/"dreamers" is the working candidate, not
        // the final word. Nothing else in the codebase hard-codes either value — changing these two
        // consts changes every unmodded surface — so this is the single config point.
        // ---------------------------------------------------------------------------------

        /// <summary>Vanilla (no mod override) value for <c>{petname}</c>. PENDING OWNER SIGN-OFF.</summary>
        public const string VanillaPetName = "dreamer";

        /// <summary>Vanilla (no mod override) value for <c>{collective}</c>. PENDING OWNER SIGN-OFF.</summary>
        public const string VanillaCollective = "dreamers";

        /// <summary>
        /// Resolved values for one manifest, cached so the substitution pass does not re-walk the
        /// mod chain on every binding read. Immutable + swapped as a whole reference, so a reader on
        /// another thread sees either the old pair or the new pair, never a half-updated one.
        /// </summary>
        private sealed class Resolved
        {
            public ModManifest? Source;
            public string PetName = VanillaPetName;
            public string Collective = VanillaCollective;
        }

        private static Resolved _cache = new();

        /// <summary>The active mod's word for one user, or the vanilla default.</summary>
        public static string PetName => Current().PetName;

        /// <summary>The active mod's word for the user base, or the vanilla default.</summary>
        public static string Collective => Current().Collective;

        /// <summary>
        /// Replace every vocabulary token in <paramref name="text"/>. Returns the input unchanged
        /// when there is nothing to do — which is the overwhelmingly common case, so the check is a
        /// single character scan before anything else happens.
        /// </summary>
        public static string Apply(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            if (text.IndexOf('{') < 0) return text;

            var hasPet = text.Contains(PetNameToken, StringComparison.Ordinal);
            var hasCollective = text.Contains(CollectiveToken, StringComparison.Ordinal);
            if (!hasPet && !hasCollective) return text;

            var resolved = Current();
            var result = text;
            if (hasPet) result = result.Replace(PetNameToken, resolved.PetName, StringComparison.Ordinal);
            if (hasCollective) result = result.Replace(CollectiveToken, resolved.Collective, StringComparison.Ordinal);
            return result;
        }

        /// <summary>
        /// Drop the cached pair. Only needed when a manifest is mutated IN PLACE (the mod creator
        /// editing the active mod) — a normal mod switch installs a new manifest object, which the
        /// reference check below notices on its own.
        /// </summary>
        public static void Invalidate() => _cache = new Resolved { Source = null };

        private static Resolved Current()
        {
            ModManifest? manifest = null;
            try
            {
                manifest = App.Mods?.ActiveMod?.Manifest;
            }
            catch
            {
                // Startup ordering: localization initializes BEFORE the mod system, so the very
                // first reads land here with no ModService at all. Vanilla defaults are the right
                // answer then, and a throwing mod layer must never take a UI string with it.
            }

            var cached = _cache;
            if (ReferenceEquals(cached.Source, manifest)) return cached;

            var fresh = new Resolved { Source = manifest };
            try
            {
                fresh.PetName = App.Mods?.GetPetNameOverride() ?? VanillaPetName;
                fresh.Collective = App.Mods?.GetCollectiveOverride() ?? VanillaCollective;
            }
            catch
            {
                fresh.PetName = VanillaPetName;
                fresh.Collective = VanillaCollective;
            }

            _cache = fresh;
            return fresh;
        }
    }
}
