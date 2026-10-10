// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Roadmap.cs (500 lines), split by owner:
//
// - The VIEW half (sub-tab and track swaps, RefreshRoadmapUI, node painting, the click/photo
//   submission flow, stats) lives in CCP.Avalonia/Views/Tabs/QuestsTabView(.Roadmap).cs, because
//   on this head the Quests markup is that view's own and its x:Name fields are private to it.
// - This file carries the head's single RoadmapService instance (Core, CCP.Core/Services/
//   RoadmapService.cs) and OnRoadmapStepCompleted (WPF :452): popup, chime, milestone messages.
//   The node repaint WPF did there and in OnRoadmapTrackUnlocked is QuestsTabView's own
//   RefreshRoadmapUI after SubmitPhoto (SubmitPhoto is the only completer, and it raises
//   TrackUnlocked synchronously inside it). SystemSounds.Exclamation becomes App.PlayExclamationChime.

using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private static RoadmapService? _roadmap;

        /// <summary>
        /// This head's one <see cref="RoadmapService"/>, the twin of WPF's <c>App.Roadmap</c>.
        ///
        /// <para>It lives here rather than as a static on the service itself precisely because WPF
        /// constructs its own in <c>App</c>: a Core-side singleton would be a SECOND writer to the
        /// same roadmap.json in the WPF process. One instance per head, and no head reaches into
        /// another's.</para>
        ///
        /// <para>Static, not per-window, because the dialogs that need it (RoadmapStepPopup,
        /// RoadmapDiaryDialog) are constructed without a shell in --render-view. Built on first
        /// read on the UI thread, so no lock.</para>
        ///
        /// <para>The desktop application calls <see cref="DisposeRoadmapIfCreated"/> on actual
        /// application exit. That method checks the backing field rather than this property, so a
        /// profile that never opened the roadmap does not create a service just to dispose it.</para>
        /// </summary>
        internal static RoadmapService Roadmap => _roadmap ??= CreateRoadmap();

        private static RoadmapService CreateRoadmap()
        {
            var roadmap = new RoadmapService();
            roadmap.StepCompleted += OnRoadmapStepCompleted;
            return roadmap;
        }

        /// <summary>WPF MainWindow.Roadmap.cs:452: the step popup, then the milestone messages.</summary>
        internal static void OnRoadmapStepCompleted(object? sender, RoadmapStepCompletedEventArgs e)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    new RoadmapStepPopup(e.StepDefinition, e.StepProgress).Show();
                    // WPF :459 SystemSounds.Exclamation; no stock sound on Linux, so the quest chime.
                    App.PlayExclamationChime("roadmap-step");

                    var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                    if (owner is not { IsVisible: true }) return;
                    if (e.UnlockedNewTrack)
                        await MessageDialog.ShowAsync(owner,
                            ConditioningControlPanel.Localization.Loc.Get("roadmap_track_unlocked_title"),
                            ConditioningControlPanel.Localization.Loc.Get("roadmap_track_unlocked_body"));
                    if (e.EarnedBadge)
                        await MessageDialog.ShowAsync(owner,
                            ConditioningControlPanel.Localization.Loc.Get("roadmap_badge_earned_title"),
                            ConditioningControlPanel.Localization.Loc.Get("roadmap_badge_earned_body"));
                }
                catch (Exception ex) { Log.Warning(ex, "Roadmap step popup failed"); }
            });
        }

        internal static void DisposeRoadmapIfCreated() => _roadmap?.Dispose();
    }
}
