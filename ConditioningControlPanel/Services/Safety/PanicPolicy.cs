using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Safety
{
    /// <summary>
    /// The pure decision behind the panic key (v6.8.5, suggestion thread 1541736938703167550,
    /// ccp-bugs #1054 / #1066 - "panic button is panic button").
    ///
    /// <para>Before this, one press was handed down a six rung ladder - LockCard, Ctrl+K palette,
    /// Chaos descent, DtRH, Arcademy, For You feed - and then spent as the #735 video grace pause,
    /// so the engine only stopped on press two or three while the screen flickered from one owner
    /// to the next. <see cref="AppSettings.PanicOverridesAll"/> (default TRUE) collapses all of
    /// that into a single stop-everything pass; turning it off restores the old ladder exactly.</para>
    ///
    /// <para>Kept free of WPF and of <c>App</c> statics so the decision can be unit tested on its
    /// own: MainWindow supplies the booleans, this says which rung the press lands on.</para>
    /// </summary>
    internal static class PanicPolicy
    {
        /// <summary>What a single panic press does, once it has been received.</summary>
        internal enum Rung
        {
            /// <summary>An open Lock Card outranks everything, in BOTH modes. The card is dismissed,
            /// the press is consumed there, and it never advances the double-press exit ladder.</summary>
            DismissLockCard,

            /// <summary>Override mode: stop every surface at once, then the engine.</summary>
            StopEverything,

            /// <summary>Legacy mode: run the pre-6.8.5 hand-off ladder unchanged.</summary>
            RunLadder
        }

        /// <summary>
        /// The rung this press lands on. <paramref name="lockCardOpen"/> wins in both modes - see
        /// <see cref="Rung.DismissLockCard"/>; that ordering is the whole Lock Card contract and is
        /// deliberately NOT conditional on <paramref name="overrideAll"/>.
        /// </summary>
        internal static Rung Decide(bool lockCardOpen, bool overrideAll)
        {
            if (lockCardOpen) return Rung.DismissLockCard;
            return overrideAll ? Rung.StopEverything : Rung.RunLadder;
        }

        /// <summary>Reads the master switch off settings, defaulting to ON when settings are missing
        /// (a panic with no settings loaded should still stop everything).</summary>
        internal static bool OverrideEnabled(AppSettings? settings) => settings?.PanicOverridesAll != false;

        /// <summary>
        /// Whether the press may still advance the double-press "exit the app" counter. Only the
        /// Lock Card rung refuses: a press spent dismissing a card must never be the tap that quits.
        /// </summary>
        internal static bool AdvancesExitLadder(Rung rung) => rung != Rung.DismissLockCard;

        /// <summary>
        /// Whether a press of the PANIC key may still be consumed as the #735 video grace pause.
        /// In override mode it may not - the grace pause moved to <see cref="AppSettings.PauseKey"/>.
        /// The pause key itself never goes through here.
        /// </summary>
        internal static bool AllowGracePauseFromPanicKey(bool overrideAll) => !overrideAll;

        /// <summary>
        /// True when <paramref name="pressed"/> is the user's optional pause-key binding. An unset
        /// (or whitespace) binding matches nothing, so the default install has no pause key at all.
        /// Compared as <c>Key.ToString()</c> text, exactly like the panic key.
        /// </summary>
        internal static bool IsPauseKeyPress(string? pauseKey, string? pressed)
        {
            if (string.IsNullOrWhiteSpace(pauseKey) || string.IsNullOrWhiteSpace(pressed)) return false;
            return string.Equals(pauseKey.Trim(), pressed.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The panic key always wins a collision: if the user binds the pause key to the panic key,
        /// the press panics and never parks a video. Checked before the pause-key branch runs.
        /// </summary>
        internal static bool PauseKeyIsShadowedByPanicKey(string? panicKey, bool panicKeyEnabled, string? pauseKey)
        {
            if (!panicKeyEnabled) return false;
            if (string.IsNullOrWhiteSpace(panicKey) || string.IsNullOrWhiteSpace(pauseKey)) return false;
            return string.Equals(panicKey.Trim(), pauseKey.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
