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
    /// <para><b>Why the two factors simply multiply.</b> The first cut clamped their PRODUCT into
    /// the user's own 0.5..1.5 range, on the theory that no mod should reach a size the user could
    /// not have asked for alone. On a mod shipping <c>bubbleScale</c> at or below 0.5 that turned
    /// the whole upper half of the slider into a dead zone: every setting from 100% down to 50%
    /// multiplied out at or below 0.5 and clamped back to exactly 0.5, so the user dragged Size,
    /// watched the value save, and not one bubble changed — the exact opposite of the promise. The
    /// slider moving has to move the field, so the two factors now compose freely and sanity is
    /// enforced by the absolute DIP rails (<see cref="ClickableFloorDip"/>,
    /// <see cref="PlayfieldCeilingDip"/>) instead. Those rails do flatten the extreme ends in
    /// compound cases, which is fine: they are physical limits of a clickable moving target on a
    /// real screen, not arithmetic second-guessing the user.</para>
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
        /// someone to hit it. The user's own range cannot reach it alone (50% of 150 is 75), so it
        /// exists for a shrinking mod combined with a low setting, and as a guard on hand-edited
        /// settings.
        /// </summary>
        public const int ClickableFloorDip = 60;

        /// <summary>
        /// Ceiling on the drawn size, in DIPs — the counterpart to <see cref="ClickableFloorDip"/>
        /// now that nothing clamps the multipliers. The compound extreme (150% of a 1.5 mod at the
        /// top of the band) works out near 560 DIP, and a 1366x768 laptop is only 768 DIP tall:
        /// past roughly 500 one sprite owns the play area and there is no field left to aim at.
        /// 500 is chosen to sit ABOVE anything the user's slider can reach on its own (150% of the
        /// band top is 375), so this never trims the plain no-mod case — it only catches
        /// mod-times-user pileups and hand-edited nonsense.
        /// </summary>
        public const int PlayfieldCeilingDip = 500;

        /// <summary>Bounds a mod's declared <c>bubbleScale</c> is clamped into. Same span the user
        /// gets, because the two are the same kind of decision; they now stack rather than fight.</summary>
        public const double ModScaleMin = 0.5;
        public const double ModScaleMax = 1.5;

        /// <summary>
        /// Final drawn size for one ambient bubble.
        /// </summary>
        /// <param name="baseSizeDip">A draw from the untouched band (<see cref="BaseMinDip"/>..<see cref="BaseMaxDip"/>).</param>
        /// <param name="userPercent">The user's <c>BubblesSize</c> setting; clamped here too, so a
        /// hand-edited settings file cannot produce a bubble nobody can click.</param>
        /// <param name="modScale">The active mod's <c>bubbleScale</c>, or null when it declared none.</param>
        /// <returns>
        /// A size in DIPs, always within <see cref="ClickableFloorDip"/>..<see cref="PlayfieldCeilingDip"/>.
        /// With default settings and no mod scale this returns <paramref name="baseSizeDip"/>
        /// UNCHANGED — the no-regression property worth keeping, and it is pinned by a test.
        /// Moving <paramref name="userPercent"/> always changes the result until one of those two
        /// physical rails is hit, which on the shipped band never happens inside the slider's range.
        /// </returns>
        public static int Scale(int baseSizeDip, int userPercent, double? modScale)
        {
            var user = Math.Clamp(userPercent, UserPercentMin, UserPercentMax) / 100.0;

            // A NaN or absurd bubbleScale is an authoring mistake in a hand-written mod.json, not
            // a reason to draw nothing: fall back to "the mod said nothing".
            var mod = 1.0;
            if (modScale.HasValue && !double.IsNaN(modScale.Value) && !double.IsInfinity(modScale.Value))
                mod = Math.Clamp(modScale.Value, ModScaleMin, ModScaleMax);

            // Multiply and do NOT clamp the product: they are independent decisions (how loud the
            // user wants the field, how much margin the art has) and clamping their combination is
            // what silently killed half the slider's travel on shrinking mods. The rails below are
            // the only limits, and they are stated in DIPs because that is what "too small to hit"
            // and "bigger than the screen" actually mean.
            var scaled = (int)Math.Round(baseSizeDip * user * mod);

            return Math.Clamp(scaled, ClickableFloorDip, PlayfieldCeilingDip);
        }
    }
}
