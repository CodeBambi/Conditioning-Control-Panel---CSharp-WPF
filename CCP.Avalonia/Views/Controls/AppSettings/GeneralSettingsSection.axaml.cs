using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS ▸ GENERAL, ported from the WPF head. Language, startup, window/tray, Deeper switch.
    ///
    /// The language combo is populated for real from <see cref="LocalizationManager.AvailableLanguages"/>
    /// (Core). Every handler on the WPF side forwards to MainWindow or writes <c>App.Settings</c>,
    /// neither of which exists on this head yet, so they are stubs with the x:Names preserved.
    /// <c>IAppSettingsSection</c> lives in the WPF head's AppSettingsTabView; <see cref="OnSectionShown"/>
    /// keeps the shape so the host can pick it up when it is ported.
    /// </summary>
    public partial class GeneralSettingsSection : UserControl
    {
        public GeneralSettingsSection()
        {
            InitializeComponent();

            var current = LocalizationManager.Instance.CurrentLanguage;
            for (int i = 0; i < LocalizationManager.AvailableLanguages.Length; i++)
            {
                var (code, displayName, _) = LocalizationManager.AvailableLanguages[i];
                var item = new ComboBoxItem { Content = displayName, Tag = code };
                ToolTip.SetTip(item, displayName);
                CmbLanguageSetting.Items.Add(item);
                if (code == current) CmbLanguageSetting.SelectedIndex = i;
            }
            if (CmbLanguageSetting.SelectedIndex < 0) CmbLanguageSetting.SelectedIndex = 0; // WPF PopulateLanguageCombo falls back to the first entry

            CmbLanguageSetting.SelectionChanged += CmbLanguageSetting_SelectionChanged;
            ChkWinStart.Click += ChkWinStart_Click;
            ChkStartHidden.Click += ChkStartHidden_Click;
            ChkAutoRun.IsCheckedChanged += ChkAutoRun_Changed;
            ChkVidLaunch.IsCheckedChanged += ChkVidLaunch_Changed;
            ChkEnableDeeper.IsCheckedChanged += ChkEnableDeeper_Changed;
            BtnSelectStartupVideo.Click += BtnSelectStartupVideo_Click;
            BtnClearStartupVideo.Click += BtnClearStartupVideo_Click;
        }

        /// <summary>Re-reads the OS startup registration and the startup-video filename.</summary>
        public void OnSectionShown()
        {
            // ponytail: needs App.Settings + StartupManager, wired when they move to Core
        }

        private void CmbLanguageSetting_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // ponytail: needs MainWindow.ApplyLanguageSelection, wired when it moves to Core
        }

        private void ChkWinStart_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs StartupManager, wired when it moves to Core
        }

        private void ChkStartHidden_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.Settings (StartMinimized), wired when it moves to Core
        }

        private void BtnSelectStartupVideo_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs a file picker + App.Settings (StartupVideoPath), wired when it moves to Core
        }

        private void BtnClearStartupVideo_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.Settings (StartupVideoPath), wired when it moves to Core
        }

        private void ChkAutoRun_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.Settings (AutoStartEngine), wired when it moves to Core
        }

        private void ChkVidLaunch_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.Settings (ForceVideoOnLaunch), wired when it moves to Core
        }

        private void ChkEnableDeeper_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs MainWindow.ChkEnableDeeper_Changed (EnableDeeper + BtnDeeper visibility), wired when it moves to Core
        }
    }
}
