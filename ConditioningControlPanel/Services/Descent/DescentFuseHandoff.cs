using System;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>What the fuse window should do on this frame, once the bloom is out.</summary>
    public enum DescentHandoffAction
    {
        /// <summary>Nothing. Keep holding the bloom.</summary>
        Wait = 0,
        /// <summary>Ask ProfileSyncService for a sync. The ceremony can only arrive on one.</summary>
        Resync = 1,
        /// <summary>The ceremony is on screen. Begin the one-second fade out from under it.</summary>
        CrossfadeToCeremony = 2,
        /// <summary>Forty-five seconds, no offer. Fade the consolation line in.</summary>
        SpeakAwaits = 3,
        /// <summary>Done. Close the window.</summary>
        Close = 4,
    }

    /// <summary>
    /// THE HANDOFF (CONTRACT-FUSE-0816 §2.3) — what the fuse window does between reaching its bloom
    /// and getting off the screen, as a state machine with no WPF, no clock and no network in it.
    ///
    /// <para><b>The happy path is a relay, not a wait.</b> At bloom the window asks for a sync; the
    /// auto-fired server answers <c>descent_migration.required</c>; the EXISTING
    /// DescentMigrationService.OfferReceived opens the ceremony — untouched, exactly as it does on
    /// any other sync — and this machine notices the ceremony is up and fades the fuse out from
    /// under it. The fuse never opens the ceremony itself, and deliberately: a second path into a
    /// one-way question is the last thing that surface needs.</para>
    ///
    /// <para><b>Why it re-syncs on a cadence.</b> ProfileSyncService enforces a thirty-second
    /// client-side cooldown (matching the server's), so the sync fired at bloom is quite likely to
    /// be refused outright — the app almost certainly synced during startup or during the final
    /// minute of the countdown. Asking once and then waiting forty-five seconds in silence would
    /// therefore MISS on a completely healthy install. The retry cadence is what makes the happy
    /// path actually happen: each attempt self-guards, the refused ones cost nothing, and by
    /// <see cref="TimeoutSeconds"/> at least one attempt has landed outside the cooldown.</para>
    ///
    /// <para><b>The timeout is not a failure branch, it is the honest one.</b> If no offer arrives,
    /// the window says <see cref="DescentFuseCopy.ShowAwaits"/> and leaves. The standing re-offer
    /// contract means the ceremony really is still coming, on the next sync — so nothing is written,
    /// nothing is retried in a loop, and the subject is not left staring at a fullscreen window
    /// waiting for a server.</para>
    ///
    /// <para><b>One action per frame, and each transition fires exactly once.</b> The caller drives
    /// this from a render clock at ~50fps, so an action that could fire twice would fire fifty
    /// times. Every state records the moment it was entered and the machine never returns to a
    /// state it has left.</para>
    /// </summary>
    public sealed class DescentFuseHandoff
    {
        /// <summary>Give up on the offer after this. §2.3's number.</summary>
        public const double TimeoutSeconds = 45.0;

        /// <summary>How often to ask for a sync while waiting. See the cooldown note above.</summary>
        public const double ResyncEverySeconds = 8.0;

        /// <summary>The crossfade out from under the ceremony. §2.3's number.</summary>
        public const double CrossfadeSeconds = 1.0;

        /// <summary>How long the consolation line is held before the window goes.</summary>
        public const double AwaitsHoldSeconds = 4.0;

        private enum State { Waiting, HandingOff, Announcing, Done }

        private State _state = State.Waiting;
        private double _enteredAt;
        private double _nextResyncAt;   // 0 ⇒ the first frame asks immediately

        /// <summary>The state, for tests and for the window's "am I fading?" question.</summary>
        public bool IsHandingOff => _state == State.HandingOff;

        /// <summary>True once the line is up. The window fades it in on this.</summary>
        public bool IsAnnouncing => _state == State.Announcing;

        /// <summary>True once <see cref="DescentHandoffAction.Close"/> has been handed out.</summary>
        public bool IsDone => _state == State.Done;

        /// <summary>
        /// How far through the current fade, 0..1 — the crossfade in
        /// <see cref="State.HandingOff"/>, and nothing anywhere else.
        /// </summary>
        public double CrossfadeProgress(double secondsSinceBloom)
        {
            if (_state != State.HandingOff) return 0.0;
            var t = (secondsSinceBloom - _enteredAt) / CrossfadeSeconds;
            return t <= 0 ? 0.0 : t >= 1.0 ? 1.0 : t;
        }

        /// <summary>
        /// Advance one frame.
        /// </summary>
        /// <param name="secondsSinceBloom">Seconds since the bloom began. Negative before it — the
        /// machine simply does nothing, so the caller can drive it from the very first frame of the
        /// show without a guard of its own.</param>
        /// <param name="ceremonyOpen">Whether the migration ceremony window is on screen right now
        /// (<c>DescentMigrationService.IsCeremonyOpen</c>). Polled rather than evented on purpose:
        /// the window already has a 50fps clock, and a poll cannot miss an edge that happened
        /// between subscriptions.</param>
        public DescentHandoffAction Advance(double secondsSinceBloom, bool ceremonyOpen)
        {
            if (double.IsNaN(secondsSinceBloom) || secondsSinceBloom < 0) return DescentHandoffAction.Wait;

            switch (_state)
            {
                case State.Waiting:
                    // The ceremony beats the clock, always — even on the same frame the timeout
                    // would have fired. A subject who gets both should get the ceremony.
                    if (ceremonyOpen)
                    {
                        _state = State.HandingOff;
                        _enteredAt = secondsSinceBloom;
                        return DescentHandoffAction.CrossfadeToCeremony;
                    }

                    if (secondsSinceBloom >= TimeoutSeconds)
                    {
                        _state = State.Announcing;
                        _enteredAt = secondsSinceBloom;
                        return DescentHandoffAction.SpeakAwaits;
                    }

                    if (secondsSinceBloom >= _nextResyncAt)
                    {
                        // Schedule from the SLOT, not from now: a frame that arrives late must not
                        // push every later attempt back with it.
                        _nextResyncAt += ResyncEverySeconds;
                        return DescentHandoffAction.Resync;
                    }

                    return DescentHandoffAction.Wait;

                case State.HandingOff:
                    if (secondsSinceBloom - _enteredAt >= CrossfadeSeconds)
                    {
                        _state = State.Done;
                        return DescentHandoffAction.Close;
                    }
                    return DescentHandoffAction.Wait;

                case State.Announcing:
                    // Deliberately NOT interruptible by a late offer. Once the window has said the
                    // ceremony awaits it is leaving; an offer that lands in these four seconds
                    // opens the ceremony over the top of a window that is already on its way out,
                    // which is the correct outcome and needs no branch of its own.
                    if (secondsSinceBloom - _enteredAt >= AwaitsHoldSeconds)
                    {
                        _state = State.Done;
                        return DescentHandoffAction.Close;
                    }
                    return DescentHandoffAction.Wait;

                default:
                    return DescentHandoffAction.Wait;
            }
        }
    }
}
