using System;
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
        /// <summary>The brain's turn log resets on a mod switch.</summary>
        public static volatile Action? ModSwitched;

        /// <summary>The bark rules reload when a built-in mod's content is replaced in place.</summary>
        public static volatile Action? ReloadBarkRules;

        /// <summary>The live event's accent colour, #RRGGBB(AA), or null when no event is on.</summary>
        public static volatile Func<string?>? EventAccentHexProvider;

        /// <summary>Which companion is active, so an unsupported one can be swapped out.</summary>
        public static volatile Func<CompanionId?>? ActiveCompanionProvider;

        /// <summary>Switch to a companion the new mod supports.</summary>
        public static volatile Action<CompanionId>? SwitchCompanion;
    }
}
