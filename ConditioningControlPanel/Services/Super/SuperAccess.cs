using System;
using System.Linq;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// The eight Super effects. Each is an ADD-ON layered on a base effect (or, for Creep, a new
    /// effect beside the pink filter), never a replacement. Order is stable: it is the weekly free
    /// rotation's wheel, so append, never reorder. See Services/Super/CONTRACT.md.
    /// </summary>
    public enum SuperEffect
    {
        FlickerDeck,   // flashes
        InnerBloom,    // bubbles
        Afterglow,     // subliminals
        Vortex,        // spiral overlay
        Creep,         // pink fog, new, beside the pink filter
        LightsDown,    // mandatory video
        Scrawl,        // bouncing text
        Undertow,      // brain drain and melt
    }

    /// <summary>
    /// The one answer to "may Super X run right now, and has the player switched it on".
    /// Effect lanes call <see cref="IsOn"/> at their spawn points and nothing else; the tier
    /// bar, the weekly free preview and the switch UI all live behind it.
    /// </summary>
    public static class SuperAccess
    {
        /// <summary>Raised when a switch flips, the tier changes or the weekly preview starts or ends.</summary>
        public static event Action<SuperEffect>? Changed;

        /// <summary>
        /// Basic (tier 1) or higher, or this week's free preview. The preview is added by the
        /// scaffold lane; DEBUG honours CCP_SUPER_ALL=1 so a lane can run an effect without an account.
        /// </summary>
        public static bool IsUnlocked(SuperEffect effect)
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("CCP_SUPER_ALL") == "1") return true;
#endif
            return TierGate.HasPremium;
        }

        /// <summary>Unlocked AND switched on. Cheap enough to call per spawn.</summary>
        public static bool IsOn(SuperEffect effect)
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("CCP_SUPER_ALL") == "1") return true;
#endif
            var on = App.Settings?.Current?.SuperEffectsOn;
            return on != null && on.Contains(effect.ToString()) && IsUnlocked(effect);
        }

        /// <summary>The player's switch. Does not check the gate; the UI refuses before calling this.</summary>
        public static void Set(SuperEffect effect, bool on)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var list = s.SuperEffectsOn.ToList();
            var name = effect.ToString();
            if (on && !list.Contains(name)) list.Add(name);
            else if (!on) list.Remove(name);
            else return;
            s.SuperEffectsOn = list;
            Changed?.Invoke(effect);
        }
    }
}
