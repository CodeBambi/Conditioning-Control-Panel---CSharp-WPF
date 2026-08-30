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
    /// <see cref="VatFillCoordinator"/> and the glass.
    ///
    /// THE RULE (pitch "The tap holds", owner-approved 2026-08-30, which SUPERSEDES
    /// the 2026-08-13 in-memory hold): earned XP does not move the liquid. It waits
    /// in the faucet until the user PRESSES AND HOLDS the tap, which pours the whole
    /// held amount to the server truth in one stream.
    ///
    /// WHAT CHANGED, AND WHY IT MATTERS: the hold used to be a field that only
    /// accrued while the Profile tab was the on-screen tab, and tab entry threw it
    /// away. Between those two rules there was no reachable path from "I earned XP"
    /// to "I poured it" — you cannot earn XP while staring at the jar, and arriving
    /// at the jar cleared whatever you had. The hold is now DERIVED, not accrued:
    ///
    ///     held = today_xp - lastPouredTodayXp
    ///
    /// where <c>lastPouredTodayXp</c> is persisted (<see cref="IVatPourLedger"/>).
    /// It is recomputed on every reading whether or not anybody is looking, so it
    /// survives tab switches, app launches, and XP earned on web, mobile or Discord —
    /// the server's <c>today_xp</c> already carries all of it.
    ///
    /// WHAT THIS CLASS IS NOT: it is not an XP account. ProgressionService and the
    /// server vat are completely untouched; <see cref="TruthFill"/> is always the
    /// last accepted server fill and every escape valve lands the glass back on it.
    /// Pure and UI-free so the hold/pour decisions can be pinned by tests, exactly
    /// like the coordinator it sits behind.
    ///
    /// THE ESCAPE VALVES (a display-only hold must never go stale):
    ///   • A cap retune (delta 0, scale changed) is NOT earned XP: the held amount is
    ///     preserved and the DISPLAY is re-scaled silently.
    ///   • A negative delta (UTC midnight, correction down) re-stamps the watermark
    ///     at the new today_xp, so the hold is 0 and the glass drains SILENTLY —
    ///     pouring a held delta into a reset day is a lie, and performing the loss
    ///     breaks the Brake. At a real midnight today_xp is 0, so both numbers land
    ///     on 0, which is the ruling written literally.
    ///   • A stale watermark can never invent a hold: it is clamped to today_xp, so
    ///     the worst a wrong row can do is cost one pour animation.
    ///   • A delta landing MID-POUR extends the pour (never restarts), the same rule
    ///     the shared contract gives the glass.
    ///   • A seed (app launch / vat re-arming) SHOWS the hold instead of clearing it,
    ///     and still never auto-pours — the 2026-08-12 first-read ruling stands.
    /// </summary>
    public sealed class VatFaucetHold
    {
        private readonly IVatPourLedger _ledger;

        /// <param name="ledger">
        /// Where <c>lastPouredTodayXp</c> lives. Null gets an in-memory one, which is
        /// what the pure unit tests use and what a settings-less host degrades to.
        /// </param>
        public VatFaucetHold(IVatPourLedger? ledger = null)
            => _ledger = ledger ?? new InMemoryVatPourLedger();

        private bool _seeded;
        private int _todayXp;
        private int _cap;

        /// <summary>
        /// XP earned since the last pour. DERIVED, never accumulated: the persisted
        /// watermark is clamped to today's total so a stale or foreign row can only
        /// ever read as "nothing poured yet", never as a hold bigger than the day.
        /// </summary>
        public int HeldXp => Math.Max(0, _todayXp - Math.Min(_ledger.PouredTodayXp, _todayXp));

        /// <summary>The last accepted server fill — where every pour and valve lands.</summary>
        public double TruthFill { get; private set; }

        /// <summary>
        /// The level the glass should show while holding: truth minus the held
        /// fraction. With no cap known it degrades to truth (never invents a fill).
        /// </summary>
        public double DisplayFill =>
            _cap > 0 ? Math.Max(0, TruthFill - (double)HeldXp / _cap) : TruthFill;

        /// <summary>Forget the in-flight reading — vat disarmed / block withdrawn / logout.
        /// The PERSISTED watermark is deliberately left alone: it is account- and
        /// day-scoped, so it cannot leak into the next account, and wiping it would
        /// re-offer a pour the user already made.</summary>
        public void Reset()
        {
            _seeded = false;
            _todayXp = 0;
            TruthFill = 0;
            _cap = 0;
        }

        /// <summary>
        /// Fold one coordinator reading into the hold and answer what the glass
        /// should do.
        /// </summary>
        /// <param name="read">The coordinator's decision for this server reading.</param>
        /// <param name="pouring">True while the glass is running a pour — a delta
        /// arriving now EXTENDS that pour instead of joining the hold.</param>
        public FaucetStep Fold(VatRead read, bool pouring)
        {
            if (read.Kind == VatReadKind.Ignored)
                return new FaucetStep { Action = FaucetActionKind.None, Fill = DisplayFill };

            TruthFill = read.Fill;
            _cap = read.Cap;
            _todayXp = Math.Max(0, read.TodayXp);

            if (read.Kind == VatReadKind.Seed)
            {
                // APP LAUNCH / FRESH ARM. The hold is whatever the persisted watermark
                // says is still waiting, so the tap can already be wobbling with the
                // class you finished before you opened the app. It is SNAPPED to, never
                // poured: opening the card is not an earn (ruling 2026-08-12).
                _seeded = true;
                return new FaucetStep { Action = FaucetActionKind.Snap, Fill = DisplayFill };
            }

            if (read.DeltaXp < 0)
            {
                // MIDNIGHT RESET / DOWNWARD CORRECTION. Nothing was earned and the held
                // delta no longer describes anything, so the watermark moves to the new
                // total (held -> 0) and the glass drains silently. At a real midnight
                // today_xp is 0 and both numbers are 0, exactly as ruled.
                _ledger.Record(_todayXp);
                _seeded = true;
                return new FaucetStep
                {
                    Action = read.ScaleChanged ? FaucetActionKind.Snap : FaucetActionKind.Ease,
                    Fill = read.Fill,
                };
            }

            bool first = !_seeded;
            _seeded = true;

            if (read.DeltaXp > 0)
            {
                if (pouring)
                {
                    // MID-POUR DELTA EXTENDS THE POUR, never restarts it and never
                    // re-enters the hold — the faucet is already across the glass, so
                    // the watermark moves with the stream.
                    _ledger.Record(_todayXp);
                    return new FaucetStep { Action = FaucetActionKind.Pour, Fill = read.Fill };
                }

                if (first)
                {
                    // A first reading that is not a Seed (this hold was built against a
                    // coordinator that had already seeded — a re-armed vat). Show the
                    // hold, never pour it.
                    return new FaucetStep { Action = FaucetActionKind.Snap, Fill = DisplayFill };
                }

                // THE HOLD. Any positive amount joins it — the wobble has no minimum,
                // and it accrues WHETHER OR NOT THE TAB IS ON SCREEN (that gate is the
                // bug this rewrite removes). With an unchanged cap the display value is
                // mathematically unchanged (truth rose by exactly delta/cap), so the
                // glass is not touched at all.
                return read.ScaleChanged
                    ? new FaucetStep { Action = FaucetActionKind.Snap, Fill = DisplayFill }
                    : new FaucetStep { Action = FaucetActionKind.None, Fill = DisplayFill };
            }

            // deltaXp == 0: a cap/lip retune or a fill correction — not earned XP.
            // The held amount survives; only the DISPLAY re-scales, silently.
            return new FaucetStep
            {
                Action = read.ScaleChanged ? FaucetActionKind.Snap : FaucetActionKind.Ease,
                Fill = DisplayFill,
            };
        }

        /// <summary>
        /// THE POUR (a completed CHARGE-HOLD). Drain the entire held delta into the
        /// vat: the watermark moves up to today's total, the glass pours to truth, and
        /// the hold is empty until new XP lands.
        /// </summary>
        public FaucetStep PourAll()
        {
            _ledger.Record(_todayXp);
            return new FaucetStep { Action = FaucetActionKind.Pour, Fill = TruthFill };
        }
    }
}
