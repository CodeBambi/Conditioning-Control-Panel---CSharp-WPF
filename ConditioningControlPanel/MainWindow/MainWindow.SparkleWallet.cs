using System;
using System.ComponentModel;
using System.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel;

public partial class MainWindow
{
    private AppSettings? _sparkleWalletSettings;
    private bool _sparkleWalletActive;

    private void InitializeSparkleWallet()
    {
        _sparkleWalletActive = true;
        App.Settings.CurrentReplaced += ReplaceSparkleWalletSettings;
        SparklePointRewards.Awarded += OnSparklePointAwarded;
        HeaderSparkleWallet.VisitRequested += VisitFromSparkleWallet;
        Closed += (_, _) => ShutdownSparkleWallet();
        Deactivated += (_, _) => HeaderSparkleWallet.ClosePopup();
        ReplaceSparkleWalletSettings();
    }

    private void ReplaceSparkleWalletSettings()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ReplaceSparkleWalletSettings)); return; }
        if (!_sparkleWalletActive) return;
        if (_sparkleWalletSettings != null) _sparkleWalletSettings.PropertyChanged -= OnSparkleWalletSettingChanged;
        _sparkleWalletSettings = App.Settings.Current;
        _sparkleWalletSettings.PropertyChanged += OnSparkleWalletSettingChanged;
        RefreshSparkleWalletBalance();
    }

    private void OnSparkleWalletSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.SkillPoints) || string.IsNullOrEmpty(e.PropertyName))
            RefreshSparkleWalletBalance();
        if (e.PropertyName == nameof(AppSettings.SkillPoints))
            QueueSkillTreeBalanceRefresh();
    }

    private bool _skillTreeBalanceQueued;

    /// <summary>
    /// The skill tree drew its balance (and which nodes read affordable) only when the tab was
    /// shown, so a point earned while it was open left it one behind the wallet (#1269). Coalesced
    /// at Background priority because the tree is the most expensive redraw in the app. Skipped
    /// when the tree already shows the balance: a purchase redraws it itself, and a second rebuild
    /// would cut the purchase FX short.
    /// </summary>
    private void QueueSkillTreeBalanceRefresh()
    {
        if (_skillTreeBalanceQueued) return;
        _skillTreeBalanceQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _skillTreeBalanceQueued = false;
            if (!_sparkleWalletActive || EnhancementsTab?.IsVisible != true) return;
            var shown = App.Settings.Current.SkillPoints.ToString("N0");
            if (EnhancementsTab.TxtSkillPoints.Text != shown) RefreshEnhancementsUI();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RefreshSparkleWalletBalance()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(RefreshSparkleWalletBalance)); return; }
        if (_sparkleWalletActive) HeaderSparkleWallet.SetBalance(App.Settings.Current.SkillPoints);
    }

    private void OnSparklePointAwarded(object? sender, SparklePointAward award)
    {
        var creditedSettings = App.Settings.Current;
        void Present()
        {
            if (!_sparkleWalletActive || !ReferenceEquals(App.Settings.Current, creditedSettings)) return;
            // Read the current ledger: another purchase or award may have occurred while queued.
            RefreshSparkleWalletBalance();
            if (IsVisible && WindowState != WindowState.Minimized) HeaderSparkleWallet.ShowReward(award.Amount);
        }
        if (Dispatcher.CheckAccess()) Present();
        else Dispatcher.BeginInvoke(new Action(Present));
    }

    private void VisitFromSparkleWallet(object? sender, EventArgs e) => BtnStartBackRoom_Click(this, new RoutedEventArgs());

    private void ShutdownSparkleWallet()
    {
        _sparkleWalletActive = false;
        App.Settings.CurrentReplaced -= ReplaceSparkleWalletSettings;
        SparklePointRewards.Awarded -= OnSparklePointAwarded;
        if (_sparkleWalletSettings != null) _sparkleWalletSettings.PropertyChanged -= OnSparkleWalletSettingChanged;
        _sparkleWalletSettings = null;
        HeaderSparkleWallet.VisitRequested -= VisitFromSparkleWallet;
        HeaderSparkleWallet.ClosePopup();
    }
}
