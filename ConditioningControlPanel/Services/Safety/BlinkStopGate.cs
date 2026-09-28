namespace ConditioningControlPanel.Services.Safety
{
    /// <summary>
    /// When the rapid 6-blink "stop everything and recalibrate" gesture may fire.
    ///
    /// <para>The gesture is a second, hands-free stop button: it kills audio, stops the engine and
    /// pauses the session, the same surface a panic press reaches. It used to ask nothing but its
    /// own toggle, so it was an escape the user had switched off everywhere else. Tester ticket
    /// 2026-09-27 (Mich): a Blink Trainer session under Lockdown, a few fast blinks, everything
    /// stopped, and Lockdown then refused to resume the paused session. Owner, 2026-09-28: it also
    /// cuts through Strict Lock and through a disabled panic key ("no escape").</para>
    ///
    /// <para>The rule: the gesture is never more permissive than the panic key. Wherever the user
    /// (or Lockdown, or a leash holder) has taken the way out away, a blink run is just blinking.</para>
    /// </summary>
    internal static class BlinkStopGate
    {
        /// <summary>Why a blink run was ignored. <see cref="None"/> means the gesture may fire.</summary>
        internal enum Block
        {
            None,
            /// <summary>The Blink Trainer asks for blinks; a fast run of them is the game, not a stop.</summary>
            BlinkTrainer,
            /// <summary>Lockdown is running: a halt leaves a paused session Lockdown will not resume.</summary>
            Lockdown,
            /// <summary>On someone's leash: the sub leaves by cutting the leash, not by blinking.</summary>
            Leashed,
            /// <summary>The panic key is off ("no escape"); the blink stop is a panic press by another name.</summary>
            NoEscape,
            /// <summary>Strict Lock is on: the user promised not to skip, and the halt skipped for them.</summary>
            StrictLock
        }

        internal static Block Check(bool blinkTrainerRunning, bool lockdownActive, bool leashed,
                                    bool panicKeyEnabled, bool strictLockEnabled)
        {
            if (blinkTrainerRunning) return Block.BlinkTrainer;
            if (lockdownActive) return Block.Lockdown;
            if (leashed) return Block.Leashed;
            if (!panicKeyEnabled) return Block.NoEscape;
            if (strictLockEnabled) return Block.StrictLock;
            return Block.None;
        }
    }
}
