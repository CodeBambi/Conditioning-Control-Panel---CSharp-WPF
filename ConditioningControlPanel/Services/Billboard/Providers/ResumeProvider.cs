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
    // The pure half (LastSession, ResumeCards) lives once in CCP.Core/Board/Providers, shared with the Avalonia head.

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
