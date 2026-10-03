using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Mercy on/off plus "after N fails" (2..10). ccp-bugs #1145. Lives in both the Bubble Count
    /// and the Video panels; the host calls <see cref="Rebind"/> from its own RebindToCurrentSettings
    /// so a cloud restore that swaps the settings instance is followed here too.
    /// </summary>
    public partial class MercyRow : UserControl
    {
        private bool _isLoading = true;
        private SettingsHook? _settingsHook;

        public MercyRow()
        {
            InitializeComponent();
            Loaded += (_, _) => Rebind();
            Unloaded += (_, _) => _settingsHook?.Unhook();
        }

        internal void Rebind()
        {
            (_settingsHook ??= new SettingsHook(OnSettingsPropertyChanged)).Rebind();
            BuildItems();
            LoadFromSettings();
        }

        private void BuildItems()
        {
            _isLoading = true;
            try
            {
                CmbAfter.Items.Clear();
                for (var n = AppSettings.MercyAfterFailsMin; n <= AppSettings.MercyAfterFailsMax; n++)
                    CmbAfter.Items.Add(new ComboBoxItem { Content = Loc.GetF("setting_mercy_after_n", n), Tag = n });
            }
            finally { _isLoading = false; }
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _isLoading = true;
            try
            {
                ChkMercy.IsChecked = s.MercySystemEnabled;
                CmbAfter.Visibility = s.MercySystemEnabled ? Visibility.Visible : Visibility.Collapsed;
                foreach (ComboBoxItem item in CmbAfter.Items)
                {
                    if (item.Tag is int n && n == s.MercyAfterFails)
                    {
                        CmbAfter.SelectedItem = item;
                        break;
                    }
                }
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.MercySystemEnabled) ||
                e.PropertyName == nameof(AppSettings.MercyAfterFails))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkMercy_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.MercySystemEnabled = ChkMercy.IsChecked ?? false;
            CmbAfter.Visibility = s.MercySystemEnabled ? Visibility.Visible : Visibility.Collapsed;
            App.Settings?.Save();
        }

        private void CmbAfter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (CmbAfter.SelectedItem is not ComboBoxItem { Tag: int n }) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.MercyAfterFails = n;
            App.Settings?.Save();
        }
    }
}
