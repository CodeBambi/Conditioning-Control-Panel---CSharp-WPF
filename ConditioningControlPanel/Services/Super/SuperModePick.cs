using System;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>What a press on one segment of the Classic / Both / Super only row does.</summary>
    public enum SuperPickOutcome
    {
        /// <summary>Nothing to do (already the shown pick, or a weekly try is running).</summary>
        Ignore,
        /// <summary>Store the pick through <see cref="SuperAccess.SetMode"/>.</summary>
        Store,
        /// <summary>Locked: shake, nudge and show the tier refusal. Nothing is stored.</summary>
        Refuse,
    }

    /// <summary>
    /// The pure rules of the segmented pick in the gold v2 box (owner, 2026-10-01). No WPF:
    /// <c>Controls/SuperModePicker</c> only draws what these answer.
    /// </summary>
    public static class SuperModePick
    {
        /// <summary>
        /// The segment drawn lit. A locked player sees Classic (that is what runs for them), a
        /// free player's weekly try shows Both (a try stacks, it never swaps), otherwise the pick.
        /// </summary>
        public static SuperMode Shown(SuperEffect effect, bool unlocked, bool freeTrying, SuperMode stored)
        {
            if (freeTrying) return SuperMode.Both;
            if (!unlocked) return SuperMode.Classic;
            return SuperModeRule.Clamp(effect, (int)stored);
        }

        /// <summary>
        /// A press on <paramref name="target"/>. A running try is not the player's pick, so the row
        /// holds still; a locked player may keep Classic but every Super segment is refused.
        /// </summary>
        public static SuperPickOutcome Decide(bool unlocked, bool freeTrying, SuperMode shown, SuperMode target)
        {
            if (freeTrying) return SuperPickOutcome.Ignore;
            if (!unlocked && target != SuperMode.Classic) return SuperPickOutcome.Refuse;
            return target == shown ? SuperPickOutcome.Ignore : SuperPickOutcome.Store;
        }

        /// <summary>Arrow keys: the next offered segment in <paramref name="dir"/> (-1 or +1), held at the ends.</summary>
        public static SuperMode Step(SuperEffect effect, SuperMode from, int dir)
        {
            var offered = SuperModeRule.Offered(effect);
            int i = Array.IndexOf(offered, from);
            if (i < 0) i = Array.IndexOf(offered, SuperModeRule.Default);
            return offered[Math.Clamp(i + Math.Sign(dir), 0, offered.Length - 1)];
        }

        /// <summary>The segment label key: Classic, Both, Super only.</summary>
        public static string LabelKey(SuperMode mode) => "super_mode_" + Slug(mode);

        /// <summary>The one plain line under the row, which changes with the pick.</summary>
        public static string LineKey(SuperMode mode) => "super_mode_" + Slug(mode) + "_line";

        /// <summary>Lighting up: from a pick where Super did not run to one where it does (the small gold burst).</summary>
        public static bool LightsUp(SuperMode from, SuperMode to)
            => !SuperModeRule.RunsSuper(from) && SuperModeRule.RunsSuper(to);

        private static string Slug(SuperMode mode) => mode switch
        {
            SuperMode.Classic => "classic",
            SuperMode.SuperOnly => "superonly",
            _ => "both",
        };
    }
}
