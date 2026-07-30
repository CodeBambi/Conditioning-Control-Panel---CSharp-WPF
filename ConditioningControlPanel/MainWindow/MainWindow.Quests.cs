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
                // Play celebration sound from flashes audio
                App.Flash?.PlayRandomSound();

                // Show floating popup notification
                try
                {
                    _questCompletePopup?.Close();
                }
                catch { }

                _questCompletePopup = new QuestCompletePopup(e.QuestDefinition.Name, e.XPAwarded);
                _questCompletePopup.Show();

                // Also show inline banner if quest tab is visible
                QuestsTab.QuestCompleteBanner.Visibility = Visibility.Visible;
                QuestsTab.TxtQuestComplete.Text = $"{e.QuestDefinition.Name} COMPLETE! +{e.XPAwarded} XP";

                // Refresh the quest UI
                RefreshQuestUI();

                // Event FX (PR-5): burst at the cap of the bar that just filled, or on the Quests
                // nav button when the completion landed off-tab. See MainWindow.EventFx.cs.
                CelebrateQuestComplete(e.QuestType);

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
