using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Descent
{
    // ============================================================================
    // THE DESCENT — typed shape of the server's `descent` block.
    //
    // Wire truth: ccp-server proxy/descent-block.js (composeDescentBlock). The
    // block rides INSIDE the `user` object of GET /v2/user/profile (server.js
    // :14334, `user: await attachDescentBlocks({...})`) and is attached ONLY when
    // DESCENT_BLOCK is armed AND the account passes DESCENT_BLOCK_STAGE (whitelist
    // today). Everyone else gets a byte-identical response with no `descent` key.
    //
    // THE TRI-STATE LAW, and it is not negotiable: key absent => render NOTHING.
    // Malformed => render NOTHING. Only a well-formed block draws a vat. There is
    // no demo dataset on desktop and no local fabrication from client-side XP —
    // a staged rollout is only invisible if the excluded cannot tell they are
    // excluded, and a vat filled from un-synced local XP is a number the server
    // never agreed to.
    //
    // Every reader lives in DescentReader; nothing else may trust a payload.
    // ============================================================================

    /// <summary>
    /// THE DAILY XP METER. cap = xp_to_next_level / divisor (server-tunable 5-7).
    /// 20% of cap banks the day, 100% = cap, and the overflow lip sits on top —
    /// everything above cap flows to lifetime XP only.
    ///
    /// THE LIP IS NOT A CONSTANT: 120% base, 125% from stage 4, 130% from stage 6.
    /// Nothing here may hardcode 120 — read <see cref="FillLipPct"/>, which
    /// defaults to 120 only when the server omitted it.
    /// </summary>
    public sealed class DescentVat
    {
        /// <summary>Level-scaled daily cap in XP. Always &gt; 0 when this object exists.</summary>
        public int Cap { get; init; }

        /// <summary>XP the server has banked into today's vat. Never negative.</summary>
        public int TodayXp { get; init; }

        /// <summary>Percent of cap, 0..FillLipPct+. The server's number, not ours.</summary>
        public double FillPct { get; init; }

        /// <summary>The overflow lip in percent of cap: 120 base, 125 at stage 4+, 130 at 6+.</summary>
        public double FillLipPct { get; init; }

        /// <summary>The lip as the fraction the vat engine speaks (1.20 / 1.25 / 1.30).</summary>
        public double LipFraction => FillLipPct / 100.0;

        /// <summary>
        /// False only at a lip of exactly 100 — the legal "no lip" jar. Such a vat
        /// draws no MAX tick, has no band above the cap and never spills.
        /// </summary>
        public bool HasLip => FillLipPct > DescentReader.CapPct;

        /// <summary>
        /// Fill as the 0..lip+0.02 fraction the engine renders. The 0.02 of
        /// headroom is the brim the engine spills from; a no-lip jar has no brim,
        /// so it fills to the cap and stops.
        /// </summary>
        public double FillFraction
        {
            get
            {
                double ceiling = HasLip ? LipFraction + 0.02 : LipFraction;
                return Math.Max(0.0, Math.Min(ceiling, FillPct / 100.0));
            }
        }
    }

    /// <summary>
    /// Where the subject stands on the seven-stage ladder. Stages are counted in
    /// BANKED DEVOTION DAYS and nothing else. n = 0 is the real pre-begin rung.
    /// Titles are client copy looked up from <see cref="N"/>, never shipped by the server.
    /// </summary>
    public sealed class DescentStage
    {
        public int N { get; init; }
        public string Key { get; init; } = "stage_0";
        public int BankedDays { get; init; }
        public int? NextAt { get; init; }
        public int? DaysToNext { get; init; }
        /// <summary>The whole active ladder ascending, or null when the server sent none/junk.</summary>
        public IReadOnlyList<int>? Thresholds { get; init; }
    }

    /// <summary>
    /// Relapse bonus (never "gravity"): +4%/day away from BANKING, clamped to 2.0x,
    /// plus the 3-day return surge that actually pays out into the vat's fill rate.
    /// </summary>
    public sealed class DescentRelapse
    {
        public double Multiplier { get; init; } = 1.0;
        public int DaysAway { get; init; }
        public bool SurgeActive { get; init; }
        public string? SurgeEndsAt { get; init; }
        /// <summary>The frozen payout the accrual applies during the window. 1.0 outside it.</summary>
        public double SurgeMultiplier { get; init; } = 1.0;
    }

    /// <summary>
    /// The parsed `descent` block. A non-null instance means the server shipped a
    /// well-formed block for this account; there is no other way to get one.
    /// </summary>
    public sealed class DescentBlock
    {
        /// <summary>Lifetime distinct UTC days of server-verified activity.</summary>
        public int DevotionDays { get; init; }

        /// <summary>YYYY-MM-DD UTC of the last banked day, or null.</summary>
        public string? DevotionLastDay { get; init; }

        public DescentStage? Stage { get; init; }
        public DescentRelapse? Relapse { get; init; }

        /// <summary>
        /// The vat, or null when the server shipped it dark (cap absent or &lt;= 0).
        /// Null means DO NOT DRAW THE VAT — there is no honest stand-in for a meter
        /// that does not exist yet, and the bank threshold is not one.
        /// </summary>
        public DescentVat? Vat { get; init; }

        /// <summary>
        /// Per-BANKED-day fill, 0..lip, oldest first. Desktop has no spiral surface
        /// yet; carried because it is the same read and the spiral will want it.
        /// NOT calendar-indexed — index 0 is banked day <see cref="DayFillStartDayN"/>.
        /// </summary>
        public IReadOnlyList<int> DayFill { get; init; } = Array.Empty<int>();

        /// <summary>Calendar date of DayFill[0], or null when empty.</summary>
        public string? DayFillStart { get; init; }

        /// <summary>Banked-day ordinal of DayFill[0], 1-based; 0 when empty.</summary>
        public int DayFillStartDayN { get; init; }

        /// <summary>Where recorded history begins — the backfill boundary. Never read
        /// DayFill.Count as the user's whole history.</summary>
        public string? HistoryFrom { get; init; }
    }
}
