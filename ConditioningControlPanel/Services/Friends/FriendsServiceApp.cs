using System;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>The WPF wiring of the Core <see cref="FriendsService"/> (was FriendsService.CreateForApp).</summary>
public static class FriendsServiceApp
{
    public static FriendsService CreateForApp() => new(
        new FriendsApi(null, Services.BackRoom.BackRoomApi.AppIdentity, Services.BackRoom.BackRoomApi.BaseUrl,
            MergedAccountRecovery.TryHandle),
        () => Services.BackRoom.BackRoomApi.AppIdentity()?.UnifiedId,
        AnyAppWindowActive,
        readShared: () => App.Settings?.Current?.FriendsPresenceShared == true,
        writeShared: v =>
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FriendsPresenceShared = v;
            try { App.Settings?.Save(); } catch { }
        });

    private static bool AnyAppWindowActive()
    {
        try
        {
            var app = Application.Current;
            if (app == null) return false;
            if (!app.Dispatcher.CheckAccess()) return app.Dispatcher.Invoke(AnyAppWindowActive);
            foreach (Window w in app.Windows) if (w.IsActive) return true;
            return false;
        }
        catch { return false; }
    }

    /// <summary>The poll's timer: what FriendsService built for itself before it moved to Core.</summary>
    public sealed class Timer : IUiTimer
    {
        private readonly DispatcherTimer _t = new(DispatcherPriority.Background);

        public TimeSpan Interval { get => _t.Interval; set => _t.Interval = value; }
        public event EventHandler? Tick { add => _t.Tick += value; remove => _t.Tick -= value; }
        public void Start() => _t.Start();
        public void Stop() => _t.Stop();

        public void OnUiThread(Action action)
        {
            if (_t.Dispatcher.CheckAccess()) action();
            else _t.Dispatcher.BeginInvoke(action);
        }
    }
}
