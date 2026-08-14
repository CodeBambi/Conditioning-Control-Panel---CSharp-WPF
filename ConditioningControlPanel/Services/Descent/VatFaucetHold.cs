using System;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>What the display layer should do with the glass after folding a reading.</summary>
    public enum FaucetActionKind
    {
        /// <summary>Leave the liquid exactly where it is (the hold doing its job).</summary>
        None,

        /// <summary>Set the level at once — seeds and scale changes (a different meter).</summary>
        Snap,

        /// <summary>Slide the level silently — corrections and the midnight drain.</summary>
        Ease,

        /// <summary>Run the faucet stream to this level.</summary>
        Pour,
    }

    /// <summary>One display instruction: the action plus the fill it aims at.</summary>
    public readonly struct FaucetStep
    {
        public FaucetActionKind Action { get; init; }
        public double Fill { get; init; }
    }

    /// <summary>
    /// THE FAUCET'S HOLD — the desktop Trainer Card's display-only layer between
    /// <see cref="VatFillCoordinator"/> and the glass (owner survey 2026-08-13,
    /// which OVERRIDES the shared auto-pour contract FOR THIS SURFACE ONLY; the
    /// web/mobile contract is untouched).
    ///
    /// THE RULE: while the Profile tab is on screen, earned XP does not move the
    /// liquid. It accumulates here as a held delta — the faucet perched on the
    /// jar's lip wobbles with it — until the user CLICKS the faucet, which pours
    /// the whole held amount to the server truth in one stream.
    ///
    /// WHAT THIS CLASS IS NOT: it is not an XP account. ProgressionService and the
    /// server vat are completely untouched; <see cref="TruthFill"/> is always the
    /// last accepted server fill and every escape valve lands the glass back on
    /// it. Pure and UI-free so the hold/pour decisions can be pinned by tests,
    /// exactly like the coordinator it sits behind.
    ///
    /// THE ESCAPE VALVES (display-only hold must never go stale):
    ///   • Seed (app launch / vat re-arming) clears the hold — truth on screen.
    ///   • Tab re-entry calls <see cref="ClearHeld"/> — truth on screen.
    ///   • A cap retune (delta 0, scale changed) is NOT earned XP: the held
    ///     amount is preserved and the DISPLAY is re-scaled silently.
    ///   • A negative delta (UTC midnight, correction down) drains silently and
    ///     clears the hold — pouring a held delta into a reset day is a lie.
    ///   • A delta landing MID-POUR extends the pour (never restarts), the same
    ///     rule the shared contract gives the glass.
    /// </summary>
    public sealed class VatFaucetHold
    {
        /// <summary>XP earned since the last poured/shown level. Never persisted.</summary>
        public int HeldXp { get; private set; }

        /// <summary>The last accepted server fill — where every pour and valve lands.</summary>
        public double TruthFill { get; private set; }

        private int _cap;

        /// <summary>
        /// The level the glass should show while holding: truth minus the held
        /// fraction. With no cap known it degrades to truth (never invents a fill).
        /// </summary>
        public double DisplayFill =>
            _cap > 0 ? Math.Max(0, TruthFill - (double)HeldXp / _cap) : TruthFill;

        /// <summary>Forget everything — vat disarmed / block withdrawn / logout.</summary>
        public void Reset()
        {
            HeldXp = 0;
            TruthFill = 0;
            _cap = 0;
        }

        /// <summary>
        /// The tab-entry escape valve: drop the hold so the glass can show truth.
        /// The caller snaps the glass to <see cref="TruthFill"/> after this.
        /// </summary>
        public void ClearHeld() => HeldXp = 0;

        /// <summary>
        /// Fold one coordinator reading into the hold and answer what the glass
        /// should do.
        /// </summary>
        /// <param name="read">The coordinator's decision for this server reading.</param>
        /// <param name="holdActive">True while the Profile tab is the visible tab —
        /// the only state in which earned XP is held instead of applied.</param>
        /// <param name="pouring">True while the glass is running a pour — a delta
        /// arriving now EXTENDS that pour instead of joining the hold.</param>
        public FaucetStep Fold(VatRead read, bool holdActive, bool pouring)
        {
            if (read.Kind == VatReadKind.Ignored)
                return new FaucetStep { Action = FaucetActionKind.None, Fill = DisplayFill };

            if (read.Kind == VatReadKind.Seed)
            {
                // App launch / fresh arm: truth, no pending hold carried across sessions.
                HeldXp = 0;
                TruthFill = read.Fill;
                _cap = read.Cap;
                return new FaucetStep { Action = FaucetActionKind.Snap, Fill = read.Fill };
            }

            TruthFill = read.Fill;
            _cap = read.Cap;

            if (read.DeltaXp < 0)
            {
                // Midnight reset or a downward correction: nothing was earned and the
                // held delta no longer describes anything — drain silently, hold gone.
                HeldXp = 0;
                return new FaucetStep
                {
                    Action = read.ScaleChanged ? FaucetActionKind.Snap : FaucetActionKind.Ease,
                    Fill = read.Fill,
                };
            }

            if (read.DeltaXp > 0)
            {
                if (!holdActive)
                {
                    // Tab not on screen: the hold does not engage. Behave exactly as the
                    // pre-faucet host did — silently, since nobody is watching.
                    HeldXp = 0;
                    return new FaucetStep
                    {
                        Action = read.ScaleChanged ? FaucetActionKind.Snap : FaucetActionKind.Ease,
                        Fill = read.Fill,
                    };
                }

                if (pouring)
                {
                    // MID-POUR DELTA EXTENDS THE POUR, never restarts it and never
                    // re-enters the hold — the faucet is already across the glass.
                    HeldXp = 0;
                    return new FaucetStep { Action = FaucetActionKind.Pour, Fill = read.Fill };
                }

                // THE HOLD. Any positive amount joins it — the wobble has no minimum.
                // With an unchanged cap the display value is mathematically unchanged
                // (truth rose by exactly delta/cap), so the glass is not touched at all.
                HeldXp += read.DeltaXp;
                return read.ScaleChanged
                    ? new FaucetStep { Action = FaucetActionKind.Snap, Fill = DisplayFill }
                    : new FaucetStep { Action = FaucetActionKind.None, Fill = DisplayFill };
            }

            // deltaXp == 0: a cap/lip retune or a fill correction — not earned XP.
            // The held amount survives; only the DISPLAY re-scales, silently.
            double shown = holdActive ? DisplayFill : read.Fill;
            return new FaucetStep
            {
                Action = read.ScaleChanged ? FaucetActionKind.Snap : FaucetActionKind.Ease,
                Fill = shown,
            };
        }

        /// <summary>
        /// THE CLICK. Drain the entire held delta into the vat: the glass pours to
        /// truth and the hold is empty (the wobble stops until new XP accrues).
        /// </summary>
        public FaucetStep PourAll()
        {
            HeldXp = 0;
            return new FaucetStep { Action = FaucetActionKind.Pour, Fill = TruthFill };
        }
    }
}
