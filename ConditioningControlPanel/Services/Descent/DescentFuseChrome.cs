using System;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// THE DIMMING (CONTRACT-FUSE-0816 §2.2) — the room going dark as the ceremony gets close.
    ///
    /// <para>From T-24h the app's NEUTRAL chrome (window ground, panels, card faces) is nudged one
    /// step per six hours toward the ceremony's black. Four steps, one day, and by the last six
    /// hours the panel a subject has stared at for months is noticeably darker than it was
    /// yesterday — without a single dialog explaining why.</para>
    ///
    /// <para><b>ONE CHOKE POINT, and this class is only half of it.</b> The other half is
    /// <c>MainWindow.RefreshThemeAwareElements</c>, which is already the app's single writer of the
    /// neutral colour family (it re-derives the whole set on every mod switch). The fuse does not
    /// get its own resource-writing path: it hands that method a darkened trio and lets it derive,
    /// write and freeze exactly as it always has. That is what makes the dimming survive a mod
    /// switch, and what makes the restore free — step 0 returns the colours untouched, so simply
    /// re-running the existing method puts the original chrome back.</para>
    ///
    /// <para><b>ACCENT IS UNTOUCHABLE — the hard law.</b> Nothing here reads or returns an accent
    /// colour. The call site is placed so that only the neutral block sees darkened values; the
    /// accent-tinted backgrounds a few lines below it are still blended from the mod's TRUE
    /// background, so a mod's pink stays exactly the pink its author chose, at every step. A fuse
    /// that quietly restyled someone's mod would be a bug, not a mood.</para>
    ///
    /// <para><b>Restorable instantly.</b> The kill switch (a cleared timestamp) drops the step to 0
    /// on the next phase announcement, and the surface layer re-runs the theme refresh — mid-flight,
    /// no restart, byte-identical to before. There is no saved "original palette" to get out of
    /// sync, because the originals are re-read from the active mod every single time.</para>
    ///
    /// <para><b>Not motion.</b> Reduced-motion and Off users get the dimming too. It is a colour,
    /// not an animation — there is nothing here for a vestibular system to object to, and a
    /// snapped-state build that skipped it would simply be missing the beat (contract §0: reduced
    /// motion gets a snapped state, never a missing one).</para>
    /// </summary>
    public static class DescentFuseChrome
    {
        /// <summary>
        /// The ceremony's ground — the same #0A0514 the fullscreen show paints. The chrome walks
        /// toward this colour so the moment the show opens, it opens out of a room that was already
        /// most of the way there.
        /// </summary>
        public static readonly Color CeremonyBlack = Color.FromRgb(0x0A, 0x05, 0x14);

        /// <summary>
        /// How far each of the four steps travels toward <see cref="CeremonyBlack"/>. Deliberately
        /// short of anything dramatic: at the final step the chrome is 44% of the way there, which
        /// reads as "the lights are low" rather than "the app is broken", and every foreground
        /// contrast ratio the theme was designed around still holds.
        /// </summary>
        public const double StepFraction = 0.11;

        /// <summary>Steps in the walk. Six hours each, over the last day.</summary>
        public const int MaxStep = 4;

        /// <summary>
        /// The live step, 0 when the fuse is dark or still more than a day out. Reading it costs a
        /// null check on every install that has no countdown, which is all of them today.
        /// </summary>
        public static int CurrentStep => App.DescentCountdown?.DimStep ?? 0;

        /// <summary>
        /// Darken one neutral colour by <paramref name="step"/>. Step 0 returns the input
        /// unchanged — reference-identical behaviour, so the no-fuse path is provably a no-op.
        /// Alpha is preserved: some of these colours are semi-transparent panel washes.
        /// </summary>
        public static Color Dim(Color color, int step)
        {
            if (step <= 0) return color;
            var t = Math.Clamp(step, 1, MaxStep) * StepFraction;

            return Color.FromArgb(
                color.A,
                Lerp(color.R, CeremonyBlack.R, t),
                Lerp(color.G, CeremonyBlack.G, t),
                Lerp(color.B, CeremonyBlack.B, t));
        }

        /// <summary>Convenience overload using the live step.</summary>
        public static Color Dim(Color color) => Dim(color, CurrentStep);

        private static byte Lerp(byte from, byte to, double t) =>
            (byte)Math.Clamp(Math.Round(from + (to - from) * t), 0, 255);
    }
}
