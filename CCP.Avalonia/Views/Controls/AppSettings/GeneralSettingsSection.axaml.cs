using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS ▸ GENERAL, ported from the WPF head. Language, startup, window/tray, Deeper switch.
    ///
    /// The language combo is populated for real from <see cref="LocalizationManager.AvailableLanguages"/>
    /// (Core). The settings logic is restored against <see cref="CoreSettings"/>: the live editors
    /// compare before writing, as on WPF, because the section is seeded from outside and an echo
    /// must not save. What still needs the head is named at each handler: the Windows startup
    /// shortcut, the start-hidden warning dialog, the file picker, the shell's Deeper door.
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

            OnSectionShown();   // seed from settings; every handler above compares before writing
        }

        /// <summary>Re-reads the OS startup registration and the startup-video filename.</summary>
        public void OnSectionShown()
        {
            try
            {
                var s = CoreSettings.Current;
                // ponytail: WPF reconciles RunOnStartup against the Windows startup shortcut here
                // (StartupManager). No equivalent on this head; the box shows the stored value.
                Set(ChkWinStart, s.RunOnStartup);
                // Assign only on a real difference: these raise IsCheckedChanged, and their
                // handlers are live editors.
                Set(ChkStartHidden, s.StartMinimized);
                Set(ChkAutoRun, s.AutoStartEngine);
                Set(ChkVidLaunch, s.ForceVideoOnLaunch);
                Set(ChkEnableDeeper, s.EnableDeeper);
                TxtStartupVideo.Text = string.IsNullOrEmpty(s.StartupVideoPath)
                    ? Loc.Get("label_random")
                    : System.IO.Path.GetFileName(s.StartupVideoPath);
            }
            catch (Exception ex)
            {
                Log.Debug("GeneralSettingsSection.OnSectionShown: {E}", ex.Message);
            }

            static void Set(CheckBox box, bool value)
            {
                if ((box.IsChecked ?? false) != value) box.IsChecked = value;
            }
        }

        private void CmbLanguageSetting_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (CmbLanguageSetting.SelectedItem is not ComboBoxItem selected) return;
            var code = string.IsNullOrWhiteSpace(selected.Tag as string) ? "en" : (string)selected.Tag!;
            var s = CoreSettings.Current;
            if (s.Language == code) return;   // the seed's echo, or the pill re-selecting us
            s.Language = code;
            LocalizationManager.Instance.SetLanguage(code);
            CoreSettings.Save();
            // ponytail: WPF also re-selects the chrome pill and shows the "restart to apply" banner
            // through MainWindow; those live in the shell.
        }

        private void ChkWinStart_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs StartupManager (a Windows Startup-folder shortcut); no equivalent on this head yet
        }

        private void ChkStartHidden_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: WPF first warns (a Yes/No dialog) when hidden is enabled while startup is
            // on, and may revert the box; no dialog on this head yet, so the write is direct.
            var s = CoreSettings.Current;
            var want = ChkStartHidden.IsChecked ?? false;
            if (s.StartMinimized == want) return;
            s.StartMinimized = want;
            CoreSettings.Save();
            Log.Information("Start minimized set to {Enabled} (Settings/General)", want);
        }

        private void BtnSelectStartupVideo_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs a file picker (Avalonia StorageProvider), not wired on this head yet
        }

        private void BtnClearStartupVideo_Click(object? sender, RoutedEventArgs e)
        {
            CoreSettings.Current.StartupVideoPath = null;
            TxtStartupVideo.Text = Loc.Get("label_random");
            CoreSettings.Save();
            Log.Information("Startup video cleared - will use random");
        }

        private void ChkAutoRun_Changed(object? sender, RoutedEventArgs e)
        {
            var s = CoreSettings.Current;
            var want = ChkAutoRun.IsChecked ?? false;
            if (s.AutoStartEngine == want) return;   // seeding echo, not a user edit
            s.AutoStartEngine = want;
            CoreSettings.Save();
            Log.Information("Auto-start engine set to {Enabled} (Settings/General)", want);
        }

        private void ChkVidLaunch_Changed(object? sender, RoutedEventArgs e)
        {
            var s = CoreSettings.Current;
            var want = ChkVidLaunch.IsChecked ?? false;
            if (s.ForceVideoOnLaunch == want) return;
            s.ForceVideoOnLaunch = want;
            CoreSettings.Save();
            Log.Information("Force video on launch set to {Enabled} (Settings/General)", want);
        }

        private void ChkEnableDeeper_Changed(object? sender, RoutedEventArgs e)
        {
            var s = CoreSettings.Current;
            var enabled = ChkEnableDeeper.IsChecked ?? true;
            if (s.EnableDeeper == enabled) return;
            s.EnableDeeper = enabled;
            CoreSettings.Save();
            // ponytail: WPF also hides the shell's Deeper door and falls back to Settings if Deeper
            // is the active tab (MainWindow.DeeperTab.cs); that is the shell's, not this section's.
        }
    }
}
