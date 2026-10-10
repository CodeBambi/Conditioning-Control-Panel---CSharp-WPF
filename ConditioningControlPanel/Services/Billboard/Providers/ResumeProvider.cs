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
            if (file.EndsWith(EnhancementLibrary.FileSuffix, StringComparison.OrdinalIgnoreCase))
                file = file.Substring(0, file.Length - EnhancementLibrary.FileSuffix.Length);
            else
                file = Path.GetFileNameWithoutExtension(file);
            return file.Replace('_', ' ').Trim();
        }
    }

    /// <summary>
    /// RESUME, the adapter. The newest session log and the Deeper file check are read ONCE in the
    /// background (disk, never the network) and refreshed when a session ends or the recent list
    /// moves, so <see cref="Current"/> only reads a cached value.
    /// </summary>
    public sealed class ResumeProvider : BillboardProviderBase
    {
        private readonly object _gate = new();
        private LastSession? _session;
        private string? _deeper;
        private bool _hooked;
        private int _deeperReadId;

        public override string Id => "resume";

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Safe(() =>
        {
            Hook();
            LastSession? session;
            string? deeper;
            lock (_gate) { session = _session; deeper = _deeper; }
            var loc = Loc;
            bool running = App.MainWindowRef?.CompanionSessionRunning == true;
            var cards = new List<BillboardCardSpec>(2);
            var s = ResumeCards.Session(session, context.NowLocal, running, loc);
            if (s != null) cards.Add(s);
            var d = ResumeCards.Deeper(deeper, loc);
            if (d != null) cards.Add(d);
            return cards;
        });

        public override void Invoke(string actionTarget)
        {
            try
            {
                var main = App.MainWindowRef;
                if (main == null || string.IsNullOrEmpty(actionTarget)) return;
                if (actionTarget.StartsWith(ResumeCards.SessionPrefix, StringComparison.Ordinal))
                {
                    var id = actionTarget.Substring(ResumeCards.SessionPrefix.Length);
                    // The Sessions page's own Start path: it asks first. A session that is gone
                    // (deleted, a mod switched away) opens the Sessions page instead.
                    if (!main.StartSessionFromCompanion(id)) main.ShowTab("presets");
                }
                else if (actionTarget.StartsWith(ResumeCards.DeeperPrefix, StringComparison.Ordinal))
                {
                    var path = actionTarget.Substring(ResumeCards.DeeperPrefix.Length);
                    if (File.Exists(path)) main.OpenDeeperEnhancementInPlayer(path);
                    else main.ShowTab("deeper");
                }
            }
            catch (Exception ex) { App.Logger?.Debug("[Billboard] resume: {E}", ex.Message); }
        }

        private void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                var log = App.SessionLog;
                if (log != null)
                {
                    log.LogReady += (_, e) =>
                    {
                        var s = FromLog(e.Log);
                        if (s == null) return;
                        lock (_gate) _session = s;
                        RaiseChanged();
                    };
                }
                if (App.Settings?.Current is INotifyPropertyChanged settings)
                {
                    settings.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(Models.AppSettings.DeeperRecentFiles)) ReadDeeperInBackground();
                    };
                }
            }
            catch (Exception ex) { App.Logger?.Debug("[Billboard] resume hooks: {E}", ex.Message); }

            Task.Run(() =>
            {
                try
                {
                    var newest = App.SessionLog?.LoadRecentLogs()?.FirstOrDefault();
                    var s = newest == null ? null : FromLog(newest);
                    if (s == null) return;
                    lock (_gate) _session ??= s;
                    RaiseChanged();
                }
                catch (Exception ex) { App.Logger?.Debug("[Billboard] resume log read: {E}", ex.Message); }
            });
            ReadDeeperInBackground();
        }

        private void ReadDeeperInBackground()
        {
            List<string> recent;
            try { recent = App.Settings?.Current?.DeeperRecentFiles?.ToList() ?? new List<string>(); }
            catch { return; }
            int id;
            lock (_gate) id = ++_deeperReadId;
            Task.Run(() =>
            {
                string? found = null;
                try { found = recent.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)); }
                catch (Exception ex) { App.Logger?.Debug("[Billboard] deeper read: {E}", ex.Message); }
                bool changed;
                lock (_gate)
                {
                    if (id != _deeperReadId) return;
                    changed = !string.Equals(_deeper, found, StringComparison.OrdinalIgnoreCase);
                    _deeper = found;
                }
                if (changed) RaiseChanged();
            });
        }

        private static LastSession? FromLog(Models.SessionLog log)
        {
            if (log == null || string.IsNullOrWhiteSpace(log.SessionId)) return null;
            var ended = log.EndedAt == default ? log.StartedAt : log.EndedAt;
            return new LastSession(log.SessionId, log.SessionName, ended, log.DurationSeconds / 60.0);
        }
    }
}
