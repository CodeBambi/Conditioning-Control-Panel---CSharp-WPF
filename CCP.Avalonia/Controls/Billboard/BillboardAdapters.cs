using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using ConditioningControlPanel.Services.Chaster;
using Serilog;
using AppHost = global::ConditioningControlPanel.Avalonia.App;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>
    /// What a card's Callback asks of the shell. The shell partial fills it; every member may be
    /// null (a head that cannot do the thing falls back to opening the page).
    /// </summary>
    public sealed class BillboardShellHooks
    {
        public Action<string>? ShowTab { get; init; }
        public Action? OpenInvites { get; init; }
        /// <summary>Starts a session by id the way the Sessions page does (it asks first). False = no such session.</summary>
        public Func<string, bool>? StartSession { get; init; }
    }

    /// <summary>
    /// WAITING, the adapter (WPF 7.1.5 WaitingProvider): today's quests from App.Quests and an unused
    /// invite code from <see cref="WaitingSignals"/>. The port constructs no ProgramService yet, so
    /// the program-day card is absent until it does (the Core rule <see cref="WaitingCards.Program"/>
    /// is ready for it).
    /// </summary>
    public sealed class WaitingProvider : BillboardProviderBase
    {
        private readonly BillboardShellHooks _hooks;
        private readonly Func<(int Slots, int Done)> _quests;
        private bool _hooked;

        public WaitingProvider(BillboardShellHooks hooks) : this(hooks, null) { }

        /// <summary>Test seam: the quest reader.</summary>
        internal WaitingProvider(BillboardShellHooks hooks, Func<(int Slots, int Done)>? quests)
        {
            _hooks = hooks ?? new BillboardShellHooks();
            _quests = quests ?? ReadQuests;
        }

        public override string Id => "waiting";

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Safe(() =>
        {
            Hook();
            var loc = Loc;
            var cards = new List<BillboardCardSpec>(2);
            var (slots, done) = _quests();
            if (WaitingCards.Quests(slots, done, loc) is { } quests) cards.Add(quests);
            if (WaitingCards.Invite(context.Tier, WaitingSignals.Invites, loc) is { } invite) cards.Add(invite);
            return cards;
        });

        public override void Invoke(string actionTarget)
        {
            if (actionTarget != WaitingCards.InviteCallback) return;
            try
            {
                if (_hooks.OpenInvites != null) _hooks.OpenInvites();
                else _hooks.ShowTab?.Invoke("exclusives");
            }
            catch (Exception ex) { Log.Debug("[Billboard] invites card: {E}", ex.Message); }
        }

        private void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                if (AppHost.Quests is { } q)
                {
                    q.QuestsRefreshed += (_, _) => RaiseChanged();
                    q.QuestCompleted += (_, _) => RaiseChanged();
                }
                WaitingSignals.InvitesChanged += RaiseChanged;
            }
            catch (Exception ex) { Log.Debug("[Billboard] waiting hooks: {E}", ex.Message); }
        }

        private static (int Slots, int Done) ReadQuests()
        {
            var quests = AppHost.Quests;
            if (quests == null) return (0, 0);
            var board = quests.GetDailySlots();
            int slots = board.Count(s => s.Quest != null && s.Definition != null);
            int done = board.Count(s => s.Quest is { IsCompleted: true } && s.Definition != null);
            return (slots, done);
        }
    }

    /// <summary>
    /// RESUME, the adapter (WPF 7.1.5 ResumeProvider): the newest session log (read once off the UI
    /// thread, then each LogReady) and the newest Deeper file that still exists. The port has no
    /// in-place Deeper player opener, so the Deeper card opens the Deeper page.
    /// </summary>
    public sealed class ResumeProvider : BillboardProviderBase
    {
        private readonly BillboardShellHooks _hooks;
        private readonly object _gate = new();
        private LastSession? _session;
        private string? _deeper;
        private int _deeperReadId;
        private bool _hooked;

        public ResumeProvider(BillboardShellHooks hooks) => _hooks = hooks ?? new BillboardShellHooks();

        public override string Id => "resume";

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Safe(() =>
        {
            Hook();
            LastSession? session;
            string? deeper;
            lock (_gate) { session = _session; deeper = _deeper; }
            var loc = Loc;
            var cards = new List<BillboardCardSpec>(2);
            if (ResumeCards.Session(session, context.NowLocal, CoreSession.IsSessionRunning, loc) is { } s) cards.Add(s);
            if (ResumeCards.Deeper(deeper, loc) is { } d) cards.Add(d);
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
                    // The Sessions page's own Start path (it asks first). A session that is gone
                    // (deleted, a mod switched away) opens the Sessions page instead.
                    if (_hooks.StartSession?.Invoke(id) != true) _hooks.ShowTab?.Invoke("presets");
                }
                else if (actionTarget.StartsWith(ResumeCards.DeeperPrefix, StringComparison.Ordinal))
                {
                    _hooks.ShowTab?.Invoke("deeper");
                }
            }
            catch (Exception ex) { Log.Debug("[Billboard] resume: {E}", ex.Message); }
        }

        /// <summary>Test seam: a session log as the provider reads it.</summary>
        internal void NoteLog(SessionLog log)
        {
            var s = FromLog(log);
            if (s == null) return;
            lock (_gate) _session = s;
            RaiseChanged();
        }

        private void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            var log = AppHost.Sessions?.SessionLog;
            try
            {
                if (log != null) log.LogReady += (_, e) => NoteLog(e.Log);
                if (AppHost.Settings?.Current is INotifyPropertyChanged settings)
                {
                    settings.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(AppSettings.DeeperRecentFiles)) ReadDeeperInBackground();
                    };
                }
            }
            catch (Exception ex) { Log.Debug("[Billboard] resume hooks: {E}", ex.Message); }

            if (log != null)
            {
                Task.Run(() =>
                {
                    try
                    {
                        var newest = log.LoadRecentLogs()?.FirstOrDefault();
                        var s = newest == null ? null : FromLog(newest);
                        if (s == null) return;
                        lock (_gate) _session ??= s;
                        RaiseChanged();
                    }
                    catch (Exception ex) { Log.Debug("[Billboard] resume log read: {E}", ex.Message); }
                });
            }
            ReadDeeperInBackground();
        }

        private void ReadDeeperInBackground()
        {
            List<string> recent;
            try { recent = AppHost.Settings?.Current?.DeeperRecentFiles?.ToList() ?? new List<string>(); }
            catch { return; }
            int id;
            lock (_gate) id = ++_deeperReadId;
            Task.Run(() =>
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

        private static LastSession? FromLog(SessionLog log)
        {
            if (log == null || string.IsNullOrWhiteSpace(log.SessionId)) return null;
            var ended = log.EndedAt == default ? log.StartedAt : log.EndedAt;
            return new LastSession(log.SessionId, log.SessionName, ended, log.DurationSeconds / 60.0);
        }
    }

    /// <summary>EVENT, the adapter (WPF 7.1.5 EventProvider): the Chaster link and the last ladder
    /// verify, both kept in memory by the head's ChasterService.</summary>
    public sealed class EventProvider : BillboardProviderBase
    {
        private readonly Func<ChasterService?> _chaster;
        private ChasterService? _hookedOn;

        public EventProvider(Func<ChasterService?> chaster) => _chaster = chaster ?? (() => null);

        public override string Id => "event";

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Safe(() =>
        {
            var chaster = _chaster();
            if (chaster == null) return Array.Empty<BillboardCardSpec>();
            if (!ReferenceEquals(_hookedOn, chaster)) { chaster.LinkChanged += RaiseChanged; _hookedOn = chaster; }
            var verify = chaster.LastLadderVerify;
            var reading = verify is { Ok: true } ? new RaffleReading(verify.DaysCounted, verify.Seconds) : null;
            var card = EventCards.Locktober(chaster.IsLinked, context.NowUtc, reading, Loc);
            return card == null ? Array.Empty<BillboardCardSpec>() : new[] { card };
        });
    }

    /// <summary>
    /// Notes every invites read the shell makes (the header ticket's server read) into
    /// <see cref="WaitingSignals"/>, so the board knows about an unused code without a second
    /// request. Wraps the shell's own reader; RedeemAsync passes straight through.
    /// </summary>
    internal sealed class NotingInviteApi : ConditioningControlPanel.Services.Invites.IInviteApi
    {
        private readonly ConditioningControlPanel.Services.Invites.IInviteApi _inner;

        public NotingInviteApi(ConditioningControlPanel.Services.Invites.IInviteApi inner) => _inner = inner;

        public async Task<ConditioningControlPanel.Services.Invites.InviteMine> MineAsync(System.Threading.CancellationToken ct = default)
        {
            var mine = await _inner.MineAsync(ct).ConfigureAwait(false);
            try { WaitingSignals.NoteInvites(mine); } catch (Exception ex) { Log.Debug("[Billboard] invite note: {E}", ex.Message); }
            return mine;
        }

        public Task<ConditioningControlPanel.Services.Invites.RedeemOutcome> RedeemAsync(string code, System.Threading.CancellationToken ct = default)
            => _inner.RedeemAsync(code, ct);
    }
}
