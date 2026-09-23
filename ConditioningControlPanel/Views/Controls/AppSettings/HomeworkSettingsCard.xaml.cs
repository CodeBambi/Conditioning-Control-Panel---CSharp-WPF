using System;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Homework;

namespace ConditioningControlPanel.Views.Controls.AppSettingsSections
{
    /// <summary>The homework opt-in row. Collapsed unless <c>App.Homework</c> says the feature is on
    /// for the signed-in account. Opening Settings asks the server again (at most every 20 s).</summary>
    public partial class HomeworkSettingsCard : UserControl
    {
        private static readonly TimeSpan RefreshFloor = TimeSpan.FromSeconds(20);
        private DateTime _lastAsk = DateTime.MinValue;
        private bool _busy;

        public HomeworkSettingsCard()
        {
            InitializeComponent();
            Loaded += (_, _) => Wire(true);
            Unloaded += (_, _) => Wire(false);
        }

        /// <summary>The card is collapsed while idle, so its own IsVisibleChanged never fires then.
        /// The page around it does: that is when to ask the server and repaint.</summary>
        private void Wire(bool on)
        {
            var page = Parent as FrameworkElement;
            if (App.Homework != null) App.Homework.Changed -= Render;
            LocalizationManager.Instance.LanguageChanged -= OnLanguage;
            if (page != null) page.IsVisibleChanged -= OnPageVisible;
            if (!on) return;
            if (App.Homework != null) App.Homework.Changed += Render;
            LocalizationManager.Instance.LanguageChanged += OnLanguage;
            if (page != null) page.IsVisibleChanged += OnPageVisible;
            Render();
        }

        private void OnLanguage(object? sender, EventArgs e) => Render();

        private void OnPageVisible(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is not true || App.Homework == null) return;
            Render();
            if (DateTime.UtcNow - _lastAsk < RefreshFloor) return;
            _lastAsk = DateTime.UtcNow;
            _ = App.Homework.RefreshAsync();
        }

        private void Render()
        {
            var hw = App.Homework?.Current ?? HomeworkToday.Idle;
            Visibility = hw.Enabled ? Visibility.Visible : Visibility.Collapsed;
            if (!hw.Enabled) { Explainer.Visibility = Visibility.Collapsed; return; }

            TxtStatus.Text = !hw.OptedIn ? Loc.Get("homework_status_off")
                : hw.Current == null ? Loc.Get("homework_status_none")
                : hw.Done ? Loc.Get("homework_status_done")
                : Loc.GetF("homework_status_due", hw.Current.Title);
            BtnJoin.Visibility = hw.OptedIn ? Visibility.Collapsed : Visibility.Visible;
            BtnLeave.Visibility = hw.OptedIn ? Visibility.Visible : Visibility.Collapsed;
            if (hw.OptedIn) Explainer.Visibility = Visibility.Collapsed;
            TxtNeedsDiscord.Visibility = hw.DiscordLinked ? Visibility.Collapsed : Visibility.Visible;
            BtnJoin.IsEnabled = BtnLeave.IsEnabled = BtnConfirm.IsEnabled = !_busy;
        }

        private void BtnJoin_Click(object sender, RoutedEventArgs e)
        {
            TxtJoinFailed.Visibility = Visibility.Collapsed;
            Explainer.Visibility = Explainer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnNotNow_Click(object sender, RoutedEventArgs e) => Explainer.Visibility = Visibility.Collapsed;

        private async void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (App.Homework == null || _busy) return;
            _busy = true;
            Render();
            try
            {
                var joined = await App.Homework.SetOptInAsync(true);
                TxtJoinFailed.Visibility = joined ? Visibility.Collapsed : Visibility.Visible;
                if (joined) Explainer.Visibility = Visibility.Collapsed;
            }
            finally { _busy = false; Render(); }
        }

        /// <summary>Immediate: the service lets go locally before the server hears about it.</summary>
        private async void BtnLeave_Click(object sender, RoutedEventArgs e)
        {
            if (App.Homework == null) return;
            try { await App.Homework.SetOptInAsync(false); }
            catch (Exception ex) { App.Logger?.Debug("Homework leave failed: {E}", ex.Message); }
            Render();
        }
    }
}
