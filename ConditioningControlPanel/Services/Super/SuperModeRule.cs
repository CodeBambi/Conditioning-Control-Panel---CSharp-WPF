using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>How a Super effect sits beside its base effect (owner, 2026-10-01).</summary>
    public enum SuperMode
    {
        /// <summary>Only the classic effect runs.</summary>
        Classic = 0,
        /// <summary>Super stacks on top of the classic effect. The default for everyone who can use it.</summary>
        Both = 1,
        /// <summary>Super runs and the classic effect's own look steps aside.</summary>
        SuperOnly = 2,
    }

    /// <summary>
    /// The pure rules behind the per-effect Classic / Both / Super only pick. No WPF, no App:
    /// <see cref="SuperAccess"/> feeds it the stored dictionary.
    /// </summary>
    public static class SuperModeRule
    {
        /// <summary>Nothing stored for an effect means this. A lapsed or free player never reaches it: the tier gate still decides.</summary>
        public const SuperMode Default = SuperMode.Both;

        /// <summary>
        /// Effects whose base draws a look of its own that Super can stand in for. The others extend
        /// the base (flashes, bouncing text), need it to exist (Undertow is a lens in the blur) or
        /// have no base look (Lights Down, Inner Bloom), so they only offer Classic and Both.
        /// </summary>
        public static bool CanReplace(SuperEffect effect)
            => effect is SuperEffect.Afterglow or SuperEffect.Vortex or SuperEffect.Creep;

        /// <summary>The modes the box offers for an effect, in display order.</summary>
        public static SuperMode[] Offered(SuperEffect effect)
            => CanReplace(effect)
                ? new[] { SuperMode.Classic, SuperMode.Both, SuperMode.SuperOnly }
                : new[] { SuperMode.Classic, SuperMode.Both };

        /// <summary>A stored value turned into a legal mode for the effect (unknown numbers and Super only on a non-replacing effect fall back).</summary>
        public static SuperMode Clamp(SuperEffect effect, int stored)
        {
            if (!Enum.IsDefined(typeof(SuperMode), stored)) return Default;
            var mode = (SuperMode)stored;
            return mode == SuperMode.SuperOnly && !CanReplace(effect) ? SuperMode.Both : mode;
        }

        /// <summary>The mode for an effect from the stored map; absent or malformed = <see cref="Default"/>.</summary>
        public static SuperMode Resolve(SuperEffect effect, IReadOnlyDictionary<string, int>? stored)
            => stored != null && stored.TryGetValue(effect.ToString(), out var v) ? Clamp(effect, v) : Default;

        /// <summary>Does Super run at all in this mode.</summary>
        public static bool RunsSuper(SuperMode mode) => mode != SuperMode.Classic;

        /// <summary>
        /// Should the classic look hide: the effect is really running as Super only. A weekly try
        /// (<paramref name="trying"/>) never hides the base: it is a taste, not a swap.
        /// </summary>
        public static bool HidesBase(SuperEffect effect, SuperMode mode, bool superIsOn, bool trying)
            => superIsOn && !trying && mode == SuperMode.SuperOnly && CanReplace(effect);

        /// <summary>The map after a pick. Both is the default so it is stored as an explicit value anyway (a later default change never moves a player).</summary>
        public static Dictionary<string, int> With(IReadOnlyDictionary<string, int>? stored, SuperEffect effect, SuperMode mode)
        {
            var map = stored == null ? new Dictionary<string, int>() : new Dictionary<string, int>(stored);
            map[effect.ToString()] = (int)(mode == SuperMode.SuperOnly && !CanReplace(effect) ? SuperMode.Both : mode);
            return map;
        }
    }
}
