using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Quests tab: quest-completion popups/banners and progress refresh.
    // Extracted verbatim from MainWindow.xaml.cs (no behavior change).
    public partial class MainWindow
    {
        #region Quests

        private QuestCompletePopup? _questCompletePopup;
        private SolidColorBrush? _dailySegmentGold;
        private SolidColorBrush? _dailySegmentGrey;

        private void OnQuestCompleted(object? sender, Services.QuestCompletedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Perk-announcement opt-out (meadow, 2026-08-18). The quest is still completed
                // and the XP is still paid above us in QuestService - what goes is the ping and
                // the floating card. The inline QuestCompleteBanner below stays: it lives inside
                // the Quests tab rather than on top of whatever you were watching, which is the
                // distinction the whole setting is drawn on.
                bool announce = !App.PerkNotificationsSuppressed;

                // Play celebration sound from flashes audio
                if (announce) App.Flash?.PlayRandomSound();

                // Show floating popup notification
                try
                {
                    _questCompletePopup?.Close();
                }
                catch { }
                _questCompletePopup = null;

                if (announce)
                {
                    _questCompletePopup = new QuestCompletePopup(e.QuestDefinition.Name, e.XPAwarded);
                    _questCompletePopup.Show();
                }

                // Also show inline banner if quest tab is visible
                QuestsTab.QuestCompleteBanner.Visibility = Visibility.Visible;
                QuestsTab.TxtQuestComplete.Text = $"{e.QuestDefinition.Name} COMPLETE! +{e.XPAwarded} XP";

                // Refresh the quest UI
                RefreshQuestUI();

                // Event FX (PR-5): burst at the cap of the bar that just filled, or on the Quests
                // nav button when the completion landed off-tab. See MainWindow.EventFx.cs.
                CelebrateQuestComplete(e.QuestType, e.QuestDefinition.Id);

                // NO AUTO-ADVANCE. Under the one-at-a-time board a completion had to be followed by
                // a delayed repaint, because QuestService rolled the NEXT daily quest after raising
                // this event and the card would otherwise sit on "COMPLETED" until the tab was left
                // and re-entered (suggestion thread: Wobberjockey/Rosalyn/Nardda). All three seats
                // are dealt at midnight now: a finished card is SUPPOSED to stay finished, and the
                // other two were already on screen and already correct.
                //
                // The header stamps are not on this tab and do not listen to the same refresh, so
                // they are nudged directly.
                RefreshQuestStamps();

                // Hide inline banner after 5 seconds
                Task.Delay(5000).ContinueWith(_ =>
                {
                    DispatcherHelper.RunOnUISync(() =>
                    {
                        QuestsTab.QuestCompleteBanner.Visibility = Visibility.Collapsed;
                    });
                });

                App.Logger?.Information("Quest completed: {Name} (+{XP} XP)", e.QuestDefinition.Name, e.XPAwarded);

                // Sync quest streak data to server
                if (App.ProfileSync?.IsSyncEnabled == true)
                {
                    _ = App.ProfileSync.SyncProfileAsync();
                }
            });
        }

        private void OnQuestProgressChanged(object? sender, Services.QuestProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Only refresh if we're on the quests tab
                if (QuestsTab.Visibility == Visibility.Visible)
                {
                    RefreshQuestUI();
                }
            });
        }

        #endregion
    }
}
