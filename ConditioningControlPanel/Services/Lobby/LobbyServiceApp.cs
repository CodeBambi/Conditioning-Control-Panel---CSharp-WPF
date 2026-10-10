using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;

namespace ConditioningControlPanel.Services.Lobby;

/// <summary>
/// The WPF half of THE LOBBY (App.Lobby). LobbyService lives in CCP.Core; this builds it with what
/// the WPF 7.1.5 class used directly: a DispatcherTimer at Normal priority, the Goon coalescer
/// (<see cref="GoonOpenTables"/>), the Remote directory through <see cref="AvailableSubjectsService"/>
/// (which also owns the claim flow) and the friends list from App.Friends.
/// </summary>
internal static class LobbyServiceApp
{
    public static LobbyService Create() => new(() => new Timer())
    {
        FetchGoon = () => GoonOpenTables.RefreshAsync(),
        FetchRemote = FetchRemoteFromDirectory,
        ReadFriends = () => App.Friends?.Snapshot.Friends ?? Array.Empty<Friend>(),
    };

    /// <summary>The Remote directory, through the service that already owns it (and its claim
    /// flow). Its collection lives on the UI thread, so the copy is taken there.</summary>
    private static async Task<(IReadOnlyList<RemoteSeat>, bool)> FetchRemoteFromDirectory()
    {
        var dir = App.AvailableSubjects;
        if (dir == null) return (Array.Empty<RemoteSeat>(), false);
        await dir.RefreshAsync().ConfigureAwait(false);
        var app = Application.Current;
        if (app == null) return (Array.Empty<RemoteSeat>(), false);
        return app.Dispatcher.Invoke(() =>
        {
            var seats = dir.Entries
                .Select(e => new RemoteSeat(e.UnifiedId, e.DisplayName, e.Level, e.Tier, e.Tags.ToList(), e.Claimed))
                .ToList();
            return ((IReadOnlyList<RemoteSeat>)seats, !dir.HasError);
        });
    }

    /// <summary>The poll clock the Lobby built for itself before it moved to Core.</summary>
    private sealed class Timer : IUiTimer
    {
        private readonly DispatcherTimer _t = new(DispatcherPriority.Normal);

        public TimeSpan Interval { get => _t.Interval; set => _t.Interval = value; }
        public event EventHandler? Tick { add => _t.Tick += value; remove => _t.Tick -= value; }
        public void Start() => _t.Start();
        public void Stop() => _t.Stop();
        public void OnUiThread(Action action) => _t.Dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
    }
}
