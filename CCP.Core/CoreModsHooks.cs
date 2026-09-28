using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// What the mod service tells the head, and the two things it asks it, on a mod switch.
    /// It used to reach <c>App.Brain</c>, <c>App.Bark</c>, <c>App.Companion</c> and
    /// <c>App.LiveEvent</c> directly; those are head services and each is one delegate here.
    /// Unseeded is the normal state under tests and on a head without that service: the mod
    /// switch still completes, and the side effect simply does not happen.
    /// </summary>
    public static class CoreModsHooks
    {
        /// <summary>The brain's turn log resets on a mod switch. The argument is the new
        /// companion's name when the switch changed persona, otherwise null.</summary>
        public static volatile Action<string?>? ModSwitched;

        /// <summary>The bark rules reload when a built-in mod's content is replaced in place.</summary>
        public static volatile Action? ReloadBarkRules;

        /// <summary>The live event's accent colour, #RRGGBB(AA), or null when no event is on.</summary>
        public static volatile Func<string?>? EventAccentHexProvider;

        /// <summary>Which companion is active, so an unsupported one can be swapped out.</summary>
        public static volatile Func<CompanionId?>? ActiveCompanionProvider;

        /// <summary>Switch to a companion the new mod supports.</summary>
        public static volatile Action<CompanionId>? SwitchCompanion;

        // Head caches and hosts the mod service touches around a switch or a pack landing. One
        // delegate per WPF call site, deliberately not one "invalidate everything": the service
        // calls each at its own point, inside its own try/catch, exactly as the WPF code does, so
        // these stay raw (a throw reaches that try/catch). Unseeded = the head has no such cache.

        /// <summary>Close DTRH if it is open (it snapshots the mod at launch).</summary>
        public static volatile Action? CloseDtrhHost;

        /// <summary>Close the Arcademy if it is open (same snapshot as DTRH).</summary>
        public static volatile Action? CloseArcademyHost;

        /// <summary><c>ModResourceResolver.ClearCache</c>: resolved mod art/audio paths.</summary>
        public static volatile Action? ClearModResourceCache;

        /// <summary><c>AvatarPortraitLoader.InvalidateAvailabilityCache</c>.</summary>
        public static volatile Action? InvalidatePortraitAvailability;

        /// <summary><c>CompanionPhraseService.RefreshVoiceLineIndex</c>.</summary>
        public static volatile Action? RefreshVoiceLineIndex;

        /// <summary><c>BambiSprite.InvalidateStablePrompt</c>: the companion's cached prompt prefix.</summary>
        public static volatile Action? InvalidateStablePrompt;

        /// <summary>The avatar tube's known video titles (title -> URL), for naming migrated links.
        /// Unseeded = none known, so every name is derived from the URL slug.</summary>
        public static volatile Func<IReadOnlyDictionary<string, string>?>? KnownVideoLinksProvider;
    }
}
