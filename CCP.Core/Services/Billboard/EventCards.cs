using System;
using System.Collections.Generic;
using System.Globalization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    /// <summary>What the server last read off the lock for this month (the ladder verify the
    /// Chaster service already runs after a push lands). Null when it has not answered yet.</summary>
    public sealed record RaffleReading(int DaysCounted, long Seconds);

    /// <summary>
    /// EVENT, the pure half: Locktober. Only while Chaster is linked and the UTC month is October
    /// (the raffle's month is UTC). The figures are the server's reading (days counted, total
    /// added), never this PC's counter. No reading yet: the card says how the raffle counts.
    /// </summary>
    public static class EventCards
    {
        public const string CardLocktober = "event.locktober";
        public const int RaffleMonth = 10;

        public static BillboardCardSpec? Locktober(bool linked, DateTime nowUtc, RaffleReading? reading,
            Func<string, string> loc, int needDays = ChasterRaffle.DefaultNeedDays, long needSeconds = ChasterRaffle.DefaultNeedSeconds)
        {
            if (!linked || nowUtc.Month != RaffleMonth) return null;
            int daysInMonth = DateTime.DaysInMonth(nowUtc.Year, nowUtc.Month);
            int today = nowUtc.Day;
            var action = new BillboardAction(BillboardActionKind.Tab, "chaster", loc("billboard_card_locktober_button"));
            string eyebrow = loc("billboard_card_locktober_eyebrow");

            if (reading == null)
                return Card(eyebrow, loc("billboard_card_locktober_title"), loc("billboard_card_locktober_unread_line"),
                    action, Art(0, needDays, daysInMonth, today));

            int counted = Math.Clamp(reading.DaysCounted, 0, daysInMonth);
            long seconds = Math.Max(0, reading.Seconds);
            var art = Art(counted, needDays, daysInMonth, today);

            if (counted >= needDays && seconds >= needSeconds)
                return Card(eyebrow, loc("billboard_card_locktober_in_title"),
                    CardText.F(loc, "billboard_card_locktober_in_line", counted), action, art);

            // Days still open: today (unless it already counted) and every day after it.
            int open = daysInMonth - today + 1;
            if (counted < needDays && counted + open < needDays)
                return Card(eyebrow, loc("billboard_card_locktober_title"),
                    CardText.F(loc, "billboard_card_locktober_out_line", counted), action, art);

            var title = CardText.F(loc, "billboard_card_locktober_days_title", counted, needDays);
            string line;
            if (counted >= needDays)
                line = CardText.F(loc, "billboard_card_locktober_time_line", Clock(needSeconds - seconds));
            else if (needDays - counted == 1)
                line = loc("billboard_card_locktober_days_line_one");
            else
                line = CardText.F(loc, "billboard_card_locktober_days_line_many", needDays - counted);
            return Card(eyebrow, title, line, action, art);
        }

        /// <summary>Hours and minutes, "5:20". Rounded up so a few seconds short never reads 0:00.</summary>
        public static string Clock(long seconds)
        {
            var minutes = (long)Math.Ceiling(Math.Max(0, seconds) / 60.0);
            return (minutes / 60).ToString(CultureInfo.InvariantCulture) + ":" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private static IReadOnlyDictionary<string, int> Art(int counted, int need, int days, int today) =>
            new Dictionary<string, int> { ["counted"] = counted, ["need"] = need, ["days"] = days, ["today"] = today };

        private static BillboardCardSpec Card(string eyebrow, string title, string line, BillboardAction action, object art) =>
            new(CardLocktober, BillboardCardKind.Event, 0, eyebrow, title, line, CardHues.Event, CardArt.Calendar, art, action);
    }
}
