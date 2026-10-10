using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Services.Program;
using Serilog;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    /// <summary>
    /// What the Waiting, Resume and Event providers read from and do in a head. Each head fills it
    /// once (WPF <c>BillboardProviders.CreateAll</c>, Avalonia <c>MainShellWindow.BillboardProviders</c>).
    /// A missing service stays null and its provider has nothing to say: never a made-up card.
    /// </summary>
    public sealed class BillboardHead
    {
        public Func<QuestService?> Quests { get; init; } = () => null;
        public Func<ProgramService?> Programs { get; init; } = () => null;
        public Func<SessionLogService?> SessionLog { get; init; } = () => null;
        public Func<ChasterService?> Chaster { get; init; } = () => null;
        public Func<bool> SessionRunning { get; init; } = () => false;
        /// <summary>The Sessions page's own Start path (it asks first); false when the session is gone.</summary>
        public Func<string, bool> StartSession { get; init; } = _ => false;
        public Action<string> PlayDeeper { get; init; } = _ => { };
        public Action<string> ShowTab { get; init; } = _ => { };
        public Action OpenInvites { get; init; } = () => { };
    }

    /// <summary>Shared plumbing for the adapters. Changed is raised on the caller's thread: each
    /// head's deck marshals it to its UI thread before it marks the deck dirty.</summary>
    public abstract class BillboardProviderBase : IBillboardProvider
    {
        public abstract string Id { get; }

        public abstract IEnumerable<BillboardCardSpec> Current(BillboardContext context);

        public virtual void Invoke(string actionTarget) { }

        public event EventHandler? Changed;

        protected void RaiseChanged()
        {
            try { Changed?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { Log.Debug("Billboard provider {Id}: Changed handler threw: {E}", Id, ex.Message); }
        }

        /// <summary>Wraps a provider body so a broken service never takes the deck down.</summary>
        protected IEnumerable<BillboardCardSpec> Safe(Func<IEnumerable<BillboardCardSpec>> body)
        {
            try { return body() ?? Array.Empty<BillboardCardSpec>(); }
            catch (Exception ex)
            {
                Log.Debug("Billboard provider {Id}: Current threw: {E}", Id, ex.Message);
                return Array.Empty<BillboardCardSpec>();
            }
        }

        protected static Func<string, string> Loc => global::ConditioningControlPanel.Localization.Loc.Get;
    }

    /// <summary>WAITING, the adapter: quests and programs from their services, the invite from
    /// <see cref="WaitingSignals"/>.</summary>
    public sealed class WaitingProvider : BillboardProviderBase
    {
        private readonly BillboardHead _head;
        private bool _hooked;

        public WaitingProvider(BillboardHead head) => _head = head;

        public override string Id => "waiting";

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Safe(() =>
        {
            Hook();
            var loc = Loc;
            var cards = new List<BillboardCardSpec>(3);

            var program = WaitingCards.Program(ReadProgram(), loc);
            if (program != null) cards.Add(program);

            var (slots, done) = ReadQuests();
            var quests = WaitingCards.Quests(slots, done, loc);
            if (quests != null) cards.Add(quests);

            var invite = WaitingCards.Invite(context.Tier, WaitingSignals.Invites, loc);
            if (invite != null) cards.Add(invite);
            return cards;
        });

        public override void Invoke(string actionTarget)
        {
            if (actionTarget != WaitingCards.InviteCallback) return;
            try { _head.OpenInvites(); }
            catch (Exception ex) { Log.Debug("[Billboard] invites card: {E}", ex.Message); }
        }

        private void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                if (_head.Quests() is { } q)
                {
                    q.QuestsRefreshed += (_, _) => RaiseChanged();
                    q.QuestCompleted += (_, _) => RaiseChanged();
                }
                if (_head.Programs() is { } p)
                {
                    p.TodayChanged += (_, _) => RaiseChanged();
                    p.DayCompleted += (_, _) => RaiseChanged();
                }
                WaitingSignals.InvitesChanged += RaiseChanged;
            }
            catch (Exception ex) { Log.Debug("[Billboard] waiting hooks: {E}", ex.Message); }
        }

        /// <summary>Today's slots as the Quests page shows them: a slot counts once it has a quest
        /// and a known definition; done is the completed ones among those.</summary>
        private (int Slots, int Done) ReadQuests()
        {
            var quests = _head.Quests();
            if (quests == null) return (0, 0);
            var board = quests.GetDailySlots();
            int slots = board.Count(s => s.Quest != null && s.Definition != null);
            int done = board.Count(s => s.Quest is { IsCompleted: true } && s.Definition != null);
            return (slots, done);
        }

        private ProgramToday? ReadProgram()
        {
            var programs = _head.Programs();
            var enrollment = programs?.ActiveEnrollment;
            if (programs == null || enrollment == null) return null;
            if (enrollment.State != Models.Program.ProgramEnrollmentState.Active) return null;
            var program = programs.ActiveProgram;
            if (program == null || programs.Today == null) return null;
            var record = programs.TodayRecord;
            return new ProgramToday(program.Title, enrollment.CurrentDay, program.LengthDays,
                record?.SessionCompleted == true, record?.DayCompleted == true);
        }
    }

    /// <summary>
    /// RESUME, the adapter. The newest session log and the Deeper file check are read ONCE in the
    /// background (disk, never the network) and refreshed when a session ends or the recent list
    /// moves, so <see cref="Current"/> only reads a cached value.
    /// </summary>
    public sealed class ResumeProvider : BillboardProviderBase
    {
        private readonly BillboardHead _head;
        private readonly object _gate = new();
        private LastSession? _session;
        private string? _deeper;
        private bool _hooked;
        private int _deeperReadId;

        public ResumeProvider(BillboardHead head) => _head = head;

        public override string Id => "resume";

        /// <summary>The last background read (test seam: await it instead of sleeping).</summary>
        internal Task Pending { get; private set; } = Task.CompletedTask;

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Safe(() =>
        {
            Hook();
            LastSession? session;
            string? deeper;
            lock (_gate) { session = _session; deeper = _deeper; }
            var loc = Loc;
            var cards = new List<BillboardCardSpec>(2);
            var s = ResumeCards.Session(session, context.NowLocal, _head.SessionRunning(), loc);
            if (s != null) cards.Add(s);
            var d = ResumeCards.Deeper(deeper, loc);
            if (d != null) cards.Add(d);
            return cards;
        });

        public override void Invoke(string actionTarget)
        {
            try
            {
                if (string.IsNullOrEmpty(actionTarget)) return;
                if (actionTarget.StartsWith(ResumeCards.SessionPrefix, StringComparison.Ordinal))
                {
                    var id = actionTarget.Substring(ResumeCards.SessionPrefix.Length);
                    // The Sessions page's own Start path: it asks first. A session that is gone
                    // (deleted, a mod switched away) opens the Sessions page instead.
                    if (!_head.StartSession(id)) _head.ShowTab("presets");
                }
                else if (actionTarget.StartsWith(ResumeCards.DeeperPrefix, StringComparison.Ordinal))
                {
                    var path = actionTarget.Substring(ResumeCards.DeeperPrefix.Length);
                    if (File.Exists(path)) _head.PlayDeeper(path);
                    else _head.ShowTab("deeper");
                }
            }
            catch (Exception ex) { Log.Debug("[Billboard] resume: {E}", ex.Message); }
        }

        private static Models.AppSettings? Settings() => CoreSettings.HasProvider ? CoreSettings.Current : null;

        private void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            var log = _head.SessionLog();
            try
            {
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
                if (Settings() is INotifyPropertyChanged settings)
                {
                    settings.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(Models.AppSettings.DeeperRecentFiles)) ReadDeeperInBackground();
                    };
                }
            }
            catch (Exception ex) { Log.Debug("[Billboard] resume hooks: {E}", ex.Message); }

            var sessions = Task.Run(() =>
            {
                try
                {
                    var newest = log?.LoadRecentLogs()?.FirstOrDefault();
                    var s = newest == null ? null : FromLog(newest);
                    if (s == null) return;
                    lock (_gate) _session ??= s;
                    RaiseChanged();
                }
                catch (Exception ex) { Log.Debug("[Billboard] resume log read: {E}", ex.Message); }
            });
            ReadDeeperInBackground();
            Pending = Task.WhenAll(sessions, Pending);
        }

        private void ReadDeeperInBackground()
        {
            List<string> recent;
            try { recent = Settings()?.DeeperRecentFiles?.ToList() ?? new List<string>(); }
            catch { return; }
            int id;
            lock (_gate) id = ++_deeperReadId;
            Pending = Task.Run(() =>
            {
                string? found = null;
                try { found = recent.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)); }
                catch (Exception ex) { Log.Debug("[Billboard] deeper read: {E}", ex.Message); }
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

    /// <summary>EVENT, the adapter: <see cref="ChasterService.IsLinked"/> (a cached token read) and
    /// <see cref="ChasterService.LastLadderVerify"/> (kept in memory by the service).</summary>
    public sealed class EventProvider : BillboardProviderBase
    {
        private readonly BillboardHead _head;
        private bool _hooked;

        public EventProvider(BillboardHead head) => _head = head;

        public override string Id => "event";

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Safe(() =>
        {
            var chaster = _head.Chaster();
            if (chaster == null) return Array.Empty<BillboardCardSpec>();
            if (!_hooked) { chaster.LinkChanged += RaiseChanged; _hooked = true; }
            var verify = chaster.LastLadderVerify;
            var reading = verify is { Ok: true } ? new RaffleReading(verify.DaysCounted, verify.Seconds) : null;
            var card = EventCards.Locktober(chaster.IsLinked, context.NowUtc, reading, Loc);
            return card == null ? Array.Empty<BillboardCardSpec>() : new[] { card };
        });
    }
}
