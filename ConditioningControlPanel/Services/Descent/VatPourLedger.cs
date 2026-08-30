using System;
using System.Globalization;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// THE PERSISTED HALF OF THE HOLD (pitch "The tap holds", owner-approved
    /// 2026-08-30).
    ///
    /// One number: <c>lastPouredTodayXp</c> - the server's <c>today_xp</c> as it
    /// stood the last time the user actually poured. Held XP is then simply
    /// <c>today_xp - lastPouredTodayXp</c>, recomputed on every reading, which is
    /// why it survives tab switches, app launches and XP earned on another client:
    /// the server's <c>today_xp</c> already carries all of it.
    ///
    /// THIS IS NOT AN XP ACCOUNT. It is a display watermark. Nothing here is ever
    /// added to, subtracted from, or reconciled against <c>PlayerXP</c>; the server
    /// block stays the only ledger. Losing this number costs the user one
    /// unnecessary pour animation and nothing else.
    /// </summary>
    public interface IVatPourLedger
    {
        /// <summary>
        /// today_xp as of the last completed pour, for the CURRENT account on the
        /// CURRENT UTC day. 0 when the stored row belongs to somebody else or to a
        /// day that has finished - both of which mean "nothing has been poured yet
        /// today".
        /// </summary>
        int PouredTodayXp { get; }

        /// <summary>
        /// Stamp the watermark at <paramref name="todayXp"/> for the current account
        /// and UTC day. Called by a completed pour and by the silent midnight drain.
        /// </summary>
        void Record(int todayXp);
    }

    /// <summary>
    /// The test/default ledger: remembers the watermark for the life of the object
    /// and forgets it on exit. <see cref="VatFaucetHold"/> falls back to this when
    /// nothing persistent was handed in, so the hold class never has to be null-safe
    /// about its own storage.
    /// </summary>
    public sealed class InMemoryVatPourLedger : IVatPourLedger
    {
        private int _poured;
        private DateTime _day = DateTime.MinValue;

        /// <summary>Overridable clock so a test can walk past midnight.</summary>
        public Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

        public int PouredTodayXp => UtcNow().Date == _day ? _poured : 0;

        public void Record(int todayXp)
        {
            _poured = Math.Max(0, todayXp);
            _day = UtcNow().Date;
        }
    }

    /// <summary>
    /// The shipping ledger: three <see cref="Models.AppSettings"/> properties, saved
    /// on write.
    ///
    /// WHY AppSettings AND NOT A FILE OF ITS OWN: this is exactly the shape the
    /// XP watermark already uses (<c>LastConfirmedServerXp</c> +
    /// <c>...XpAccount</c> + <c>...XpSeason</c>) - a small scalar plus the scope it
    /// belongs to - and that pattern already has migration, DPAPI-free plain JSON
    /// and a single Save() path. A second file would be a second thing to keep in
    /// step for one int.
    ///
    /// ACCOUNT-SCOPED because two people sharing a machine have nothing to say about
    /// each other's day, and DAY-SCOPED (UTC, the same clock the server rolls the
    /// vat on) because a watermark from a finished day describes nothing. A mismatch
    /// on either reads as 0 rather than being repaired, so a stale row can only ever
    /// cost one extra pour animation.
    /// </summary>
    public sealed class AppSettingsVatPourLedger : IVatPourLedger
    {
        /// <summary>Overridable clock, for tests. Always UTC in the app.</summary>
        public Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

        private static string DayKey(DateTime utc) => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>
        /// The account this watermark belongs to. UnifiedId when there is one; the
        /// empty string for a legacy/V1 identity, which is shared by every legacy
        /// account on the machine - acceptable here, unlike the XP watermark, because
        /// the worst case is a wrongly-sized wobble rather than a rewritten ledger.
        /// </summary>
        private static string Account => App.Settings?.Current?.UnifiedId ?? string.Empty;

        public int PouredTodayXp
        {
            get
            {
                try
                {
                    var s = App.Settings?.Current;
                    if (s == null) return 0;
                    if (!string.Equals(s.VatPouredAccount ?? string.Empty, Account, StringComparison.Ordinal)) return 0;
                    if (!string.Equals(s.VatPouredDayUtc ?? string.Empty, DayKey(UtcNow()), StringComparison.Ordinal)) return 0;
                    return Math.Max(0, s.VatPouredTodayXp);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("VatPourLedger read: {E}", ex.Message);
                    return 0;
                }
            }
        }

        public void Record(int todayXp)
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;
                s.VatPouredTodayXp = Math.Max(0, todayXp);
                s.VatPouredDayUtc = DayKey(UtcNow());
                s.VatPouredAccount = Account;
                App.Settings?.Save();
            }
            catch (Exception ex) { App.Logger?.Debug("VatPourLedger write: {E}", ex.Message); }
        }
    }
}
