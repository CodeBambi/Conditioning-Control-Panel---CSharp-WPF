using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    /// <summary>The last session as the Resume card needs it (from the session log).</summary>
    public sealed record LastSession(string SessionId, string Name, DateTime EndedLocal, double Minutes);

    /// <summary>
    /// RESUME, the pure half: the last session (Start runs it through the Sessions page's own
    /// Start path, confirmation and all) and the last Deeper file (Play opens the player).
    /// A session older than <see cref="MaxAge"/> is history, not something to pick up.
    /// </summary>
    public static class ResumeCards
    {
        public const string CardSession = "resume.session";
        public const string CardDeeper = "resume.deeper";
        public const string SessionPrefix = "session:";
        public const string DeeperPrefix = "deeper:";

        public static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

        public static BillboardCardSpec? Session(LastSession? last, DateTime nowLocal, bool sessionRunning, Func<string, string> loc)
        {
            if (last == null || sessionRunning) return null;
            if (string.IsNullOrWhiteSpace(last.SessionId) || string.IsNullOrWhiteSpace(last.Name)) return null;
            var age = nowLocal - last.EndedLocal;
            if (age > MaxAge || age < TimeSpan.FromMinutes(-5)) return null;

            var title = last.Minutes >= 1
                ? CardText.F(loc, "billboard_card_resume_session_title", last.Name.Trim(), (int)Math.Round(last.Minutes))
                : last.Name.Trim();
            return new BillboardCardSpec(CardSession, BillboardCardKind.Resume, 0,
                loc("billboard_card_resume_eyebrow"), title, When(last.EndedLocal, nowLocal, loc),
                CardHues.Resume, CardArt.Spiral, null,
                new BillboardAction(BillboardActionKind.Callback, SessionPrefix + last.SessionId, loc("billboard_card_resume_start")));
        }

        /// <summary>"your last session, earlier today" / "yesterday" / "on Tuesday" / "on 3 October".</summary>
        public static string When(DateTime endedLocal, DateTime nowLocal, Func<string, string> loc)
        {
            int days = (nowLocal.Date - endedLocal.Date).Days;
            if (days <= 0) return loc("billboard_card_resume_when_today");
            if (days == 1) return loc("billboard_card_resume_when_yesterday");
            var culture = CultureInfo.CurrentUICulture;
            var name = days < 7
                ? culture.DateTimeFormat.GetDayName(endedLocal.DayOfWeek)
                : endedLocal.ToString(culture.DateTimeFormat.MonthDayPattern, culture);
            return CardText.F(loc, "billboard_card_resume_when_day", name);
        }

        /// <param name="path">The newest Deeper file, already known to exist.</param>
        public static BillboardCardSpec? Deeper(string? path, Func<string, string> loc)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var name = DeeperTitle(path);
            if (name.Length == 0) return null;
            return new BillboardCardSpec(CardDeeper, BillboardCardKind.Resume, 1,
                loc("billboard_card_resume_eyebrow"), name, loc("billboard_card_resume_deeper_line"),
                CardHues.Resume, CardArt.Poster, "features/deeper.png",
                new BillboardAction(BillboardActionKind.Callback, DeeperPrefix + path, loc("billboard_card_resume_play")));
        }

        /// <summary>"Deep Pink.ccpenh.json" -> "Deep Pink".</summary>
        public static string DeeperTitle(string path)
        {
            string file;
            try { file = Path.GetFileName(path); } catch { return string.Empty; }
            if (file.EndsWith(DeeperLocalLibrary.FileSuffix, StringComparison.OrdinalIgnoreCase))
                file = file.Substring(0, file.Length - DeeperLocalLibrary.FileSuffix.Length);
            else
                file = Path.GetFileNameWithoutExtension(file);
            return file.Replace('_', ' ').Trim();
        }
    }

    // The adapter (ResumeProvider) reads the head's session log and settings: CCP.Avalonia Controls/Billboard/BillboardAdapters.cs.
}
