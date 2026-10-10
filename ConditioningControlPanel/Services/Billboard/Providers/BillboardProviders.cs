using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    /// <summary>
    /// The Tonight Board's app-side providers (lane 4): Live, Waiting, Resume, Event and Tip.
    /// Each one is a pure decision (a static <c>*Cards</c> class, tested) plus a thin adapter
    /// over state the app already holds. None of them calls the network, and none of them blocks:
    /// what needs a disk read is read once in the background and cached.
    /// </summary>
    public static class BillboardProviders
    {
        /// <summary>Every provider of this lane, in deck order. Call once, on the UI thread, after
        /// the App services exist. A provider whose service is missing just has nothing to say.</summary>
        public static IReadOnlyList<IBillboardProvider> CreateAll() => new IBillboardProvider[]
        {
            new LiveProvider(() => App.Lobby, JoinLive),
            new WaitingProvider(),
            new ResumeProvider(),
            new EventProvider(),
            new TipProvider(),
        };

        /// <summary>LIVE's Join, through each game's own door (the WPF half of Core's LiveProvider;
        /// the provider catches and logs).</summary>
        private static void JoinLive(Lobby.LobbyGame game, string key)
        {
            if (BackRoom.BackRoomApi.AppIdentity() == null) { App.MainWindowRef?.OpenUnifiedLoginDialog(); return; }
            if (game == Lobby.LobbyGame.Chess) PieceByPiece.PieceByPieceHostService.JoinOpenTable(key);
            else GoonGame.GoonHostService.Launch(duckMainWindow: true, joinCode: key);
        }
    }

    // The pure half (CardText, BillboardProviderBase) lives once in CCP.Core/Board/Providers, shared with the Avalonia head.

}
