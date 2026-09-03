using System;
using System.Collections.Generic;
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

            /// <summary>The Ctrl+K quick-settings palette claimed this Escape, in BOTH modes. The
            /// palette closes and the press ends there: no stop pass, no engine stop, no session
            /// pause, no Relapse tracking, no exit-ladder advance. Escape is the DEFAULT panic key
            /// AND the universal "close this popup" key, so a user who opens the palette mid-session
            /// to nudge a slider and dismisses it the normal way must not lose their session's
            /// effects and 100 XP to it. The pre-6.8.5 ladder answered the same press this way.</summary>
            DismissSettingsPalette,

            /// <summary>Override mode: stop every surface at once, then the engine.</summary>
            StopEverything,

            /// <summary>Legacy mode: run the pre-6.8.5 hand-off ladder unchanged.</summary>
            RunLadder
        }

        /// <summary>
        /// The rung this press lands on. <paramref name="lockCardOpen"/> wins in both modes - see
        /// <see cref="Rung.DismissLockCard"/>; that ordering is the whole Lock Card contract and is
        /// deliberately NOT conditional on <paramref name="overrideAll"/>. The palette claim is
        /// second, also in both modes (<see cref="Rung.DismissSettingsPalette"/>).
        ///
        /// <para>The caller must not even ASK the palette whether it claims the press while a lock
        /// card is open: the question closes the palette as a side effect. Hence the order here is
        /// the order the booleans must be evaluated in.</para>
        /// </summary>
        internal static Rung Decide(bool lockCardOpen, bool paletteClaimedPress, bool overrideAll)
        {
            if (lockCardOpen) return Rung.DismissLockCard;
            if (paletteClaimedPress) return Rung.DismissSettingsPalette;
            return overrideAll ? Rung.StopEverything : Rung.RunLadder;
        }

        /// <summary>Reads the master switch off settings, defaulting to ON when settings are missing
        /// (a panic with no settings loaded should still stop everything).
        /// <para><see cref="AppSettings.PanicSinglePress"/> outranks it: "one press does the lot"
        /// cannot mean anything else, so it turns the override on even when the master is off.</para>
        /// </summary>
        internal static bool OverrideEnabled(AppSettings? settings)
            => settings == null || settings.PanicSinglePress || settings.PanicOverridesAll;

        /// <summary>Whether this press should HIDE every CCP window rather than restore the control
        /// panel. Only single-press panic does; the default panic still brings the app back so the
        /// user can see what stopped.</summary>
        internal static bool HidesEverything(AppSettings? settings) => settings?.PanicSinglePress == true;

        /// <summary>
        /// Whether the press may still advance the double-press "exit the app" counter. The two
        /// dismiss rungs refuse: a press spent dismissing a Lock Card or the Ctrl+K palette must
        /// never be the tap that quits the app. Both of those rungs return before any stop runs, so
        /// this is really a statement about the two rungs that DO reach the stop tail.
        /// </summary>
        internal static bool AdvancesExitLadder(Rung rung)
            => rung != Rung.DismissLockCard && rung != Rung.DismissSettingsPalette;

        /// <summary>
        /// The same question, for a press that lands on <see cref="Rung.StopEverything"/> while a
        /// mini-game or the feed owns the screen (a Rabbit Hole descent, the DtRH window, the
        /// Arcademy, the For You feed, Just Drop).
        ///
        /// <para>Before v6.8.5 such a press was CONSUMED by that surface's own rung and returned,
        /// so it never touched the double-press counter and the app could not be quit from inside a
        /// mini-game. Override mode closes the window instead - which is the point - but it must not
        /// also arm "press again within 2 s to exit the whole app": the For You rung's own comment
        /// records that a reflexive Esc-Esc double-tap is real, play-tested user behaviour, and the
        /// second tap would land with the engine stopped and quit the app on them.</para>
        ///
        /// <para>So the press that closed a game window stops the world and nothing more. The next
        /// press, with the game already down, counts normally, so double-press-to-exit is still
        /// reachable - it just cannot be reached by the same two taps that closed the game.</para>
        /// </summary>
        internal static bool AdvancesExitLadder(Rung rung, bool aGameSurfaceOwnedTheScreen)
            => AdvancesExitLadder(rung)
               && !(rung == Rung.StopEverything && aGameSurfaceOwnedTheScreen);

        /// <summary>
        /// Whether this rung tears surfaces down at all. The two dismiss rungs do not: they answer
        /// the surface that owns the press and stop there, leaving a running session alone. This is
        /// the fix for "Escape closed my quick-settings palette and paused my session for 100 XP".
        /// </summary>
        internal static bool StopsSurfaces(Rung rung)
            => rung == Rung.StopEverything || rung == Rung.RunLadder;

        /// <summary>
        /// Whether a press of the PANIC key may still be consumed as the #735 video grace pause.
        /// In override mode it may not - the grace pause moved to <see cref="AppSettings.PauseKey"/>.
        /// The pause key itself never goes through here.
        /// </summary>
        internal static bool AllowGracePauseFromPanicKey(bool overrideAll) => !overrideAll;

        /// <summary>
        /// The full grace-pause door, for the video window's own key handlers. Only a press of the
        /// PANIC key is subject to the override; every other door into the grace pause is untouched
        /// by it. That matters most for the STRICT lock window, whose Escape handler exists only
        /// when Escape is NOT the panic key and is the strict-locked user's "someone walked in" out:
        /// the v6.8.5 override moved the pause off the panic key, not off that door.
        /// </summary>
        internal static bool AllowGracePause(bool fromPanicKey, bool overrideAll)
            => !fromPanicKey || AllowGracePauseFromPanicKey(overrideAll);

        /// <summary>
        /// True when the Escape key IS the user's panic key (and the panic key is on) - i.e. when an
        /// Escape arriving at a video window is a panic press rather than the hardcoded
        /// "dismiss this video" key. The video handlers use it to decide what to pass as
        /// <c>fromPanicKey</c>, so the override reaches exactly the presses it owns.
        /// </summary>
        internal static bool EscapeIsThePanicKey(bool panicKeyEnabled, string? panicKey)
            => panicKeyEnabled && string.Equals(panicKey?.Trim(), "Escape", StringComparison.OrdinalIgnoreCase);

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

        /// <summary>One bare-key binding that rides the modifier-blind global keyboard hook.
        /// <see cref="Name"/> is the owning setting (<c>PanicKey</c> / <c>PauseKey</c>), for logs.</summary>
        internal readonly record struct HookBinding(string Name, string Key);

        /// <summary>
        /// The SET of bare keys currently bound on the WH_KEYBOARD_LL hook (GlobalKeyboardHook),
        /// each with the setting that owns it. Every binding on that hook is compared MODIFIER-BLIND
        /// and does not consume the keystroke, so a Win32 RegisterHotKey chord whose base key is in
        /// this set fires BOTH handlers at once: Ctrl+Alt+G with panic on G runs Quick Recal and
        /// tears the session down. The Quick Recal hotkey asks this set before it arms and again
        /// when it fires, instead of checking the panic key alone, so the optional pause key (and
        /// any binding added to the hook later) is covered by the same guard.
        ///
        /// <para>Membership: <c>PanicKey</c> while <c>PanicKeyEnabled</c>; <c>PauseKey</c> whenever
        /// it is non-blank (it has no enable flag of its own - see <see cref="IsPauseKeyPress"/>).
        /// The pause key is counted even while the panic key shadows it and even if the hook
        /// happens not to be installed right now: hook state flips at runtime (lockdown, remote
        /// control, preset loads), and the only cost of a false positive is the global chord.
        /// Panic is listed first so a key bound to both is reported as the panic clash.</para>
        /// </summary>
        internal static IReadOnlyList<HookBinding> HookBoundBaseKeys(bool panicKeyEnabled, string? panicKey, string? pauseKey)
        {
            var bound = new List<HookBinding>(2);
            if (panicKeyEnabled && !string.IsNullOrWhiteSpace(panicKey))
                bound.Add(new HookBinding(nameof(AppSettings.PanicKey), panicKey.Trim()));
            if (!string.IsNullOrWhiteSpace(pauseKey))
                bound.Add(new HookBinding(nameof(AppSettings.PauseKey), pauseKey.Trim()));
            return bound;
        }

        /// <summary>Settings-shaped overload for the MainWindow call sites. Missing settings bind nothing.</summary>
        internal static IReadOnlyList<HookBinding> HookBoundBaseKeys(AppSettings? settings)
            => settings == null
                ? Array.Empty<HookBinding>()
                : HookBoundBaseKeys(settings.PanicKeyEnabled, settings.PanicKey, settings.PauseKey);

        /// <summary>
        /// The hook binding whose bare key equals <paramref name="baseKey"/> (case- and
        /// whitespace-insensitive, like every other key compare in here), or null when the chord's
        /// base key is free. A blank base key never clashes.
        /// </summary>
        internal static HookBinding? FindHookClash(string? baseKey, IReadOnlyList<HookBinding> bound)
        {
            if (string.IsNullOrWhiteSpace(baseKey)) return null;
            var wanted = baseKey.Trim();
            foreach (var b in bound)
            {
                if (string.Equals(b.Key, wanted, StringComparison.OrdinalIgnoreCase)) return b;
            }
            return null;
        }
    }
}
