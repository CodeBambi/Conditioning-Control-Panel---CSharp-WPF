using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>The WPF head's leash wiring (LeashService moved to CCP.Core; this keeps the App.* reads).</summary>
public static class LeashServiceApp
{
    /// <summary>The app's own wiring: the real wire, the account off AppSettings, the real tab,
    /// the day numbers off the services that already count them, and the friends poll's shared
    /// receipt channel both ways (a <c>seen</c> rides the next poll out; the holder's sender
    /// receipts come back on it).</summary>
    public static LeashService CreateForApp(Action kick)
    {
        LeashCutSafetyApp.Seed();
        var svc = new LeashService(
            new LeashApi(null, BackRoom.BackRoomApi.AppIdentity, BackRoom.BackRoomApi.BaseUrl),
            () => BackRoom.BackRoomApi.AppIdentity()?.UnifiedId,
            dayInputs: AppDayInputs,
            tab: new ChasterLeashTab(),
            kick: kick,
            cutStore: new LeashService.FileCutStore(Path.Combine(App.UserDataPath, "leash_cut_pending.txt")));
        if (App.Friends is { } friends) svc.Attach(friends);
        return svc;
    }

    /// <summary>R's numbers, read fresh. Null when the services are not up yet.</summary>
    internal static LeashDayInputs? AppDayInputs()
    {
        var local = DateTime.Now;
        var minutes = 0;
        try
        {
            var key = local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var day = App.FeatureDayLog?.Log?.Days?.FirstOrDefault(d => d.D == key);
            minutes = day?.Cm ?? 0;
        }
        catch (Exception ex) { Serilog.Log.Debug("Leash minutes read failed: {E}", ex.Message); }

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
        DateTime? ends = null;
        bool hidden = false;
        int? tab = null;
        try
        {
            var c = App.Chaster;
            // chaster_linked is what offers the holder "Chaster time": only while it can book
            // (linked, tab on, the player's "leash" row on). Rows are never switched on here.
            if (c != null && c.TakesLeashTime)
            {
                linked = true;
                var l = c.Lock;
                ends = l?.EndsAtUtc;
                hidden = l?.TimerHidden == true;
                tab = c.BalanceSeconds;
            }
        }
        catch (Exception ex) { Serilog.Log.Debug("Leash chaster read failed: {E}", ex.Message); }

        return new LeashDayInputs(local, minutes, done, total, streak, linked, ends, hidden, tab);
    }

}
