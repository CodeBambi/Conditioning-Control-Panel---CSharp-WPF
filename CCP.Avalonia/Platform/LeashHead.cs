// PORTED from WPF 7.1.5 App.xaml.cs:2375-2404 (the leash wiring), Services/Leash/LeashService.cs
// CreateForApp + AppDayInputs, Services/Leash/LeashCutSafety.cs AppTargets and LeashTab.cs
// ChasterLeashTab, Controls/Leash/LeashSurfaces.Cut (the one-click cut). Ledger social#1.
// The service itself is Core (CCP.Core/Services/Leash, moved unchanged); this is the head's half.
using System;
using System.IO;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF App.Leash on this head. Built after the friends poll; rides it (no timer of its own).</summary>
internal static class LeashHead
{
    /// <summary>WPF App.Leash. Null until startup built it (signed out sandbox, no friends wire).</summary>
    internal static ILeashService? Service { get; set; }

    /// <summary>WPF App.LeashedChanged: the leash came on or off (tray, hook, gate).</summary>
    internal static event Action<bool>? LeashedChanged;

    /// <summary>Raised after the leashed side pressed Cut anywhere (page, gate, tray).</summary>
    internal static event Action? CutDone;

    /// <summary>True while someone holds this account's leash (the tray shows Cut leash).</summary>
    internal static bool IsLeashed => Service?.Snapshot.Me != null;

    /// <summary>Seeds Core's static seams for this head; safe to call more than once.</summary>
    internal static void Seed()
    {
        LeashCutSafety.LiveTargets = Targets.Instance;
        LeashCutSafety.RunOnUi = a =>
        {
            if (Dispatcher.UIThread.CheckAccess()) a();
            else Dispatcher.UIThread.Invoke(a);
        };
        LeashGuard.IsLeashed = () => Service?.Snapshot.Me != null;
        LeashTaskRunner.TimerFactory ??= () => new FriendsHead.Timer();
    }

    /// <summary>WPF App.xaml.cs LEASH: the service on the friends poll (report out, block in, 20 s
    /// cadence while leashed or holding), the receipt channel, and the cut file.</summary>
    internal static LeashService? Start(FriendsService friends, string? userDataDir, string? overrideUrl)
    {
        Seed();
        if (FriendsHead.BaseUrl(userDataDir, overrideUrl) is not { } url) return null;
        var svc = new LeashService(
            new LeashApi(null, FriendsHead.Identity, url),
            () => FriendsHead.Identity()?.UnifiedId,
            dayInputs: DayInputs,
            tab: new Tab(),
            kick: friends.Kick,
            cutStore: new LeashService.FileCutStore(Path.Combine(CorePaths.UserData, "leash_cut_pending.txt")));
        svc.Attach(friends);
        friends.LeashReportProvider = svc.BuildReportJson;
        friends.LeashActive = () => svc.Active;
        friends.LeashBlockArrived += svc.ApplyBlock;
        svc.LeashedChanged += on =>
        {
            try { LeashedChanged?.Invoke(on); }
            catch (Exception ex) { Serilog.Log.Debug("Leash changed handler failed: {E}", ex.Message); }
        };
        Service = svc;
        Views.Windows.MainShellWindow.LeashGateDueProvider = () => Service?.GateDue != null;
        Views.Windows.MainShellWindow.PresentLeashGateProvider = w => w.CheckLeashGate();
        return svc;
    }

    /// <summary>WPF LeashSurfaces.Cut: one click, never gated, never priced. The service runs the
    /// cut safety (Lockdown off, remote ended, strict video stopped, Strict Lock off, panic on)
    /// LOCALLY before it calls the server.</summary>
    internal static async void Cut()
    {
        try { CutDone?.Invoke(); } catch { }
        try
        {
            if (Service is { } s) await s.CutAsync();
            else LeashCutSafety.Apply();   // nothing to cut on the wire; the way out still runs
        }
        catch (Exception ex) { Serilog.Log.Warning("Leash cut failed: {E}", ex.Message); }
        try { OsNotifications.Show("CCP", global::ConditioningControlPanel.Localization.Loc.Get("leash_cut_done")); } catch { }
    }

    /// <summary>WPF LeashService.AppDayInputs: R's numbers, read fresh.</summary>
    internal static LeashDayInputs? DayInputs()
    {
        int done = 0, total = 0;
        try
        {
            var slots = App.Quests?.GetDailySlots();
            if (slots != null)
                foreach (var (q, _) in slots)
                {
                    if (q == null) continue;
                    total++;
                    if (q.IsCompleted) done++;
                }
        }
        catch (Exception ex) { Serilog.Log.Debug("Leash quests read failed: {E}", ex.Message); }

        var streak = 0;
        try { streak = App.Achievements?.Progress?.ConsecutiveDays ?? 0; } catch { }

        bool linked = false;
        int? tab = null;
        try
        {
            var c = ChasterHead.Service;
            if (c != null && c.TakesLeashTime) { linked = true; tab = c.BalanceSeconds; }
        }
        catch (Exception ex) { Serilog.Log.Debug("Leash chaster read failed: {E}", ex.Message); }

        // ponytail: minutes today (WPF FeatureDayLog day Cm) and the Chaster lock end/hidden flag are
        // not on this head yet; they read 0 / none, which only thins the holder's day card.
        return new LeashDayInputs(DateTime.Now, 0, done, total, streak, linked, null, false, tab);
    }

    /// <summary>WPF ChasterLeashTab: the same NoteSeconds path, literal row ids.</summary>
    private sealed class Tab : ILeashTab
    {
        public int BookPunish(int seconds)
        {
            if (seconds <= 0) return 0;
            try { return ChasterHead.Service?.NoteSeconds("leash", seconds).AppliedSeconds ?? 0; }
            catch (Exception ex) { Serilog.Log.Debug("Leash tab punish failed: {E}", ex.Message); return 0; }
        }

        public int BookCredit(int seconds)
        {
            if (seconds <= 0) return 0;
            try { return ChasterHead.Service?.NoteSeconds("leash_credit", -seconds).AppliedSeconds ?? 0; }
            catch (Exception ex) { Serilog.Log.Debug("Leash tab credit failed: {E}", ex.Message); return 0; }
        }
    }

    /// <summary>WPF LeashCutSafety.AppTargets on this head. Every member tolerates a service that is not built.</summary>
    internal sealed class Targets : LeashCutSafety.ITargets
    {
        internal static readonly Targets Instance = new();

        public bool LockdownActive => LockdownService.Current?.IsActive == true;
        public void EndLockdown() => LockdownService.Current?.Deactivate();
        public void DiscardLockdownRecovery() => LockdownService.DiscardRecovery();

        public bool RemoteActive =>
            Views.Tabs.RemoteControlTabView.Relay.IsValueCreated && Views.Tabs.RemoteControlTabView.Relay.Value.IsActive;
        public void EndRemote() => Views.Tabs.RemoteControlTabView.Relay.Value.EndSessionNow();

        public bool StrictVideoRunning => CoreEngine.Video is { IsPlaying: true, IsStrict: true };
        public void StopVideo() => CoreEngine.Video?.ForceCleanup();

        public void ReleaseSafetySettings()
        {
            var s = CoreSettings.Current;
            s.StrictLockEnabled = false;
            s.PanicKeyEnabled = true;
            CoreSettings.SaveImmediate();
        }

        /// <summary>OWED, as on WPF (LeashCutSafety AppTargets): the tab keeps one pooled balance.</summary>
        public bool DropUnpushedLeashBookings() => false;
    }
}
