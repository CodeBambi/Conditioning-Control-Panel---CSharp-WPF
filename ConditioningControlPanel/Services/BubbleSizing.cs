using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// How big an ambient Bubble Pop bubble is drawn.
    ///
    /// <para><b>Why this is a thing at all.</b> The ambient field's size was
    /// <c>random.Next(150, 250)</c> DIP, hardcoded, with no setting anywhere — a user who found the
    /// bubbles overwhelming ("is there a way to make the floating bubbles smaller? this is kinda
    /// wild") had nothing to turn down. Chaos mode already had the concept on its side of the house
    /// (<c>ChaosBubbleVariants.GLOBAL_SIZE_SCALE</c> plus a per-variant <c>sizeScale</c>), so this
    /// closes the gap for the plain field.</para>
    ///
    /// <para><b>Why a mod gets a say too.</b> Perceived size is driven by how much transparent
    /// margin the sprite has, not by the box it is drawn in. The embedded <c>bubble.png</c> has a
    /// soft padded rim; a full-bleed replacement (a pill, a capsule) reads dramatically larger at
    /// an identical box size. Without <c>bubbleScale</c> every user of such a mod has to discover
    /// the size slider and compensate by hand for a decision the mod author could have made once.</para>
    ///
    /// <para>Static and Dispatcher-free so the arithmetic is directly testable — the same split as
    /// <c>ModImageSlotRules</c>. Chaos/variant bubbles do NOT come through here: they carry an
    /// explicit <c>SizePx</c> from a spec that is balanced against its own scale system.</para>
    /// </summary>
    public static class BubbleSizing
    {
        /// <summary>Inclusive lower bound of the untouched size band, in DIPs.</summary>
        public const int BaseMinDip = 150;

        /// <summary>
        /// EXCLUSIVE upper bound of the untouched size band, in DIPs — it is fed straight to
        /// <c>Random.Next(min, max)</c>, whose max is exclusive. Named to match that call so the
        /// band stays literally the one that shipped.
        /// </summary>
        public const int BaseMaxDip = 250;

        /// <summary>Smallest the user may ask for, as a percentage.</summary>
        public const int UserPercentMin = 50;

        /// <summary>
        /// Largest the user may ask for, as a percentage. Capped at 150 rather than opened wide:
        /// past that a single bubble covers most of a 1080p screen, and the honest answer to
        /// "I want them enormous" is Solid mode plus a higher spawn rate, not one huge sprite.
        /// </summary>
        public const int UserPercentMax = 150;

        /// <summary>Default: the band exactly as it shipped.</summary>
        public const int UserPercentDefault = 100;

        /// <summary>
        /// Floor on the drawn size, in DIPs. Deliberately the same 60 that
        /// <c>BubbleService</c> already imposes on spec-driven bubbles rather than a new number:
        /// a bubble is a MOVING click target, and below roughly this it stops being fair to ask
        /// someone to hit it. The multiplier ranges cannot reach it on their own (50% of 150 is
        /// 75), so it exists for the combined case and as a guard on hand-edited settings.
        /// </summary>
        public const int ClickableFloorDip = 60;

        /// <summary>Bounds on the combined multiplier, so no mod can push the field outside the
        /// range the user could have chosen for themselves.</summary>
        public const double CombinedScaleMin = 0.5;
        public const double CombinedScaleMax = 1.5;

        /// <summary>
        /// Final drawn size for one ambient bubble.
        /// </summary>
        /// <param name="baseSizeDip">A draw from the untouched band (<see cref="BaseMinDip"/>..<see cref="BaseMaxDip"/>).</param>
        /// <param name="userPercent">The user's <c>BubblesSize</c> setting; clamped here too, so a
        /// hand-edited settings file cannot produce a bubble nobody can click.</param>
        /// <param name="modScale">The active mod's <c>bubbleScale</c>, or null when it declared none.</param>
        /// <returns>
        /// A size in DIPs. With default settings and no mod scale this returns
        /// <paramref name="baseSizeDip"/> UNCHANGED — the no-regression property worth keeping,
        /// and it is pinned by a test.
        /// </returns>
        public static int Scale(int baseSizeDip, int userPercent, double? modScale)
        {
            var user = Math.Clamp(userPercent, UserPercentMin, UserPercentMax) / 100.0;

            // A NaN or absurd bubbleScale is an authoring mistake in a hand-written mod.json, not
            // a reason to draw nothing: fall back to "the mod said nothing".
            var mod = 1.0;
            if (modScale.HasValue && !double.IsNaN(modScale.Value) && !double.IsInfinity(modScale.Value))
                mod = Math.Clamp(modScale.Value, CombinedScaleMin, CombinedScaleMax);

            // Multiply, then clamp the PRODUCT: the two are independent decisions (how loud the
            // user wants the field, how much margin the art has), but their combination must not
            // land outside what the user could have asked for alone.
            var combined = Math.Clamp(user * mod, CombinedScaleMin, CombinedScaleMax);

            return Math.Max(ClickableFloorDip, (int)Math.Round(baseSizeDip * combined));
        }
    }
}
