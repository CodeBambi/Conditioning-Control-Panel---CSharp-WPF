using System;
using System.Collections.Generic;

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
            new LiveProvider(),
            new WaitingProvider(Head),
            new ResumeProvider(Head),
            new EventProvider(Head),
            new TipProvider(),
        };

        /// <summary>What the Core adapters (CCP.Core BillboardAppProviders) read and do on this head.</summary>
        private static readonly BillboardHead Head = new()
        {
            Quests = () => App.Quests,
            Programs = () => App.Programs,
            SessionLog = () => App.SessionLog,
            Chaster = () => App.Chaster,
            SessionRunning = () => App.MainWindowRef?.CompanionSessionRunning == true,
            StartSession = id => App.MainWindowRef?.StartSessionFromCompanion(id) == true,
            PlayDeeper = path => App.MainWindowRef?.OpenDeeperEnhancementInPlayer(path),
            ShowTab = tab => App.MainWindowRef?.ShowTab(tab),
            OpenInvites = () => App.MainWindowRef?.OpenInvitesCard(),
        };
    }
}
