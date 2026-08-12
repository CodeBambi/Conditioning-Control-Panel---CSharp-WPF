using System;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>What a fresh server reading asks the vat visual to do.</summary>
    public enum VatReadKind
    {
        /// <summary>Nothing moved. Do not touch the engine.</summary>
        Ignored,

        /// <summary>First reading of this session — snap to the server's fill, no theater.</summary>
        Seed,

        /// <summary>Ease the level with no faucet. Small grants, and every drop (day rollover).</summary>
        Silent,

        /// <summary>Run the faucet: slide in from the left, pour, splash.</summary>
        Pour,
    }

    /// <summary>The decision for one reading, plus the numbers the engine needs.</summary>
    public readonly struct VatRead
    {
        public VatReadKind Kind { get; init; }

        /// <summary>Target fill as the 0..lip+0.02 fraction the engine speaks.</summary>
        public double Fill { get; init; }

        /// <summary>The overflow lip as a fraction (1.20 / 1.25 / 1.30). The meter's scale.</summary>
        public double Lip { get; init; }

        /// <summary>today_xp delta since the previous accepted reading. Negative on rollover.</summary>
        public int DeltaXp { get; init; }

        /// <summary>
        /// True when the scale itself changed (cap or lip). The engine is scaled to
        /// the lip and captioned by the cap, so the host must re-scale rather than
        /// animate — a cap change is a different meter, not a different reading.
        /// </summary>
        public bool ScaleChanged { get; init; }
    }

    /// <summary>
    /// THE VAT'S ANIMATION CONTRACT, shared verbatim with mobile and web
    /// (planning/one-descent PLAN.md, session log 2026-08-12 "SHARED ANIM CONTRACT").
    ///
    /// Pure, allocation-light, and deliberately free of any UI type so it is
    /// testable: the whole point of pulling this out of the renderer is that the
    /// threshold and the tri-state can be pinned by unit tests.
    ///
    /// THE LAW IT ENFORCES: the server's block is the only truth. The coordinator
    /// never derives a fill from local XP that has not synced, never invents a
    /// reading, and answers <see cref="VatReadKind.Ignored"/> for a null or vat-less
    /// block so the surface can render nothing at all.
    /// </summary>
    public sealed class VatFillCoordinator
    {
        /// <summary>
        /// THE POUR THRESHOLD — the one definition on this client.
        ///
        /// A grant smaller than this eases the level silently; anything at or above
        /// it swings the faucet in. Scaled to the meter so it reads the same at
        /// every depth: 1% of the daily cap, with a 25 XP floor for low-level
        /// accounts whose cap is small enough that 1% is a rounding error.
        ///
        /// Engineering ruling 2026-08-12 (planning/one-descent — PLAN.md session
        /// log), owner-vetoable. If it moves, it moves here, on mobile and on web
        /// together: three clients disagreeing about when the faucet appears is a
        /// bug report nobody can reproduce.
        /// </summary>
        public static int VatPourMinXp(int cap) => Math.Max(25, (int)Math.Ceiling(cap * 0.01));

        /// <summary>Below this the two fills are the same number and nothing animates.</summary>
        private const double FillEpsilon = 0.0005;

        private bool _seeded;
        private int _lastTodayXp;
        private int _lastCap;
        private double _lastLipPct;
        private double _lastFill;

        /// <summary>The last accepted server fill, or null before the first reading.</summary>
        public double? LastFill => _seeded ? _lastFill : null;

        /// <summary>today_xp of the last accepted reading.</summary>
        public int LastTodayXp => _lastTodayXp;

        /// <summary>Forget everything — used when the block disappears (logout, dial change).</summary>
        public void Reset()
        {
            _seeded = false;
            _lastTodayXp = 0;
            _lastCap = 0;
            _lastLipPct = 0;
            _lastFill = 0;
        }

        /// <summary>
        /// Fold one server reading in. A null block or a block with no vat resets the
        /// coordinator and answers Ignored: the vat does not exist for this account,
        /// and the next block that does arrive must seed rather than pour a delta
        /// measured against a meter that was not being watched.
        /// </summary>
        public VatRead Apply(DescentBlock? block)
        {
            var vat = block?.Vat;
            if (vat is null)
            {
                Reset();
                return new VatRead { Kind = VatReadKind.Ignored };
            }

            double fill = vat.FillFraction;
            double lip = vat.LipFraction;
            int delta = vat.TodayXp - _lastTodayXp;
            bool scaleChanged = _seeded && (vat.Cap != _lastCap || Math.Abs(vat.FillLipPct - _lastLipPct) > 0.001);

            VatReadKind kind;
            if (!_seeded)
            {
                // FIRST READ RENDERS LAST-KNOWN SERVER FILL, NEVER POURS IT. Opening
                // the Trainer Card is not an earn; a faucet on open would announce XP
                // the user banked hours ago, every single time.
                kind = VatReadKind.Seed;
            }
            else if (delta >= VatPourMinXp(vat.Cap) && fill > _lastFill + FillEpsilon)
            {
                // Both halves are required. The XP delta is what the threshold is
                // written against, but a delta that moves no liquid (already parked
                // at the ceiling) has nothing to pour into.
                kind = VatReadKind.Pour;
            }
            else if (Math.Abs(fill - _lastFill) > FillEpsilon || scaleChanged)
            {
                // Small grants, corrections, and the daily drain all land here. The
                // drop at UTC midnight is deliberately silent: a faucet pouring a
                // vat DOWN is a lie about which direction the day went.
                kind = VatReadKind.Silent;
            }
            else
            {
                kind = VatReadKind.Ignored;
            }

            _seeded = true;
            _lastTodayXp = vat.TodayXp;
            _lastCap = vat.Cap;
            _lastLipPct = vat.FillLipPct;
            _lastFill = fill;

            return new VatRead
            {
                Kind = kind,
                Fill = fill,
                Lip = lip,
                DeltaXp = delta,
                ScaleChanged = scaleChanged,
            };
        }
    }
}
