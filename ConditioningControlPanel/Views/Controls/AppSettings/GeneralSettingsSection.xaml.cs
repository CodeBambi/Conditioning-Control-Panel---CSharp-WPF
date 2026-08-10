using System;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.AppSettingsSections
{
    /// <summary>
    /// SETTINGS ▸ GENERAL. Language, startup, window/tray behaviour, Deeper master switch.
    ///
    /// <para>Three handler shapes live here and the difference matters:</para>
    /// <list type="number">
    ///   <item><b>Pure forwards</b> (<c>ChkWinStart</c>, <c>ChkStartHidden</c>, the startup-video
    ///   buttons, <c>ChkEnableDeeper</c>). The controls MOVED out of the dashboard's
    ///   LegacyDashboardHost keeping their x:Names, so the MainWindow partials that own their logic
    ///   still address them - only the prefix changed, <c>SettingsTab.</c> to
    ///   <c>AppSettingsTab.</c>. These shims add nothing.</item>
    ///   <item><b>Local writes</b> (<c>ChkVidLaunch</c>, <c>ChkAutoRun</c>). Those two never had a
    ///   MainWindow handler: they were write-only mirrors that <c>SaveSettings()</c> read on its way
    ///   past. As a live editor they have to persist themselves, exactly as their twins in
    ///   Features/SystemFeatureControl.xaml.cs always did.</item>
    ///   <item><b>Shared path</b> (<c>CmbLanguageSetting</c>). Owner decision #8 keeps the chrome
    ///   pill AND lists languages here, so this is deliberately a second SURFACE - but not a second
    ///   implementation. It calls <c>MainWindow.ApplyLanguageSelection</c>, the same method the pill
    ///   calls, and that method re-selects both combos when it is done.</item>
    /// </list>
    ///
    /// <para><b>Why the local writes compare before writing instead of using an _isLoading flag.</b>
    /// <c>MainWindow.LoadSettings()</c> seeds these checkboxes from outside this class, so a private
    /// guard would not be set when the assignment lands and every load would fire a handler and a
    /// Save. Comparing against the stored value makes the echo a no-op without needing to know who
    /// is assigning or when - and a real user toggle always differs by definition.</para>
    /// </summary>
    public partial class GeneralSettingsSection : UserControl, IAppSettingsSection
    {
        public GeneralSettingsSection()
        {
            InitializeComponent();
        }

        private MainWindow? Main => Window.GetWindow(this) as MainWindow
                                    ?? App.MainWindowRef;

        /// <summary>
        /// Re-read the two values that can change behind this page's back: the Windows startup
        /// registration (an external tool or the System popup's read-out can disagree with the
        /// stored flag) and the startup-video filename. Everything else on this page is seeded by
        /// <c>MainWindow.LoadSettings()</c>.
        /// </summary>
        public void OnSectionShown()
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;

                // StartupManager is the authority for RunOnStartup - the registry shortcut can be
                // removed by the user or a cleaner without the settings file ever hearing about it.
                var registered = Services.StartupManager.IsRegistered();
                if ((ChkWinStart.IsChecked ?? false) != registered)
                {
                    ChkWinStart.IsChecked = registered;   // Click handler: no echo from this
                    s.RunOnStartup = registered;
                    App.Settings?.Save();
                    App.Logger?.Information(
                        "Settings/General: RunOnStartup re-synced to the OS shortcut ({Registered})", registered);
                }

                // Assign only on a real difference. These four raise Checked/Unchecked, and their
                // handlers are live editors - a blind re-seed would round-trip through a Save (and,
                // for Deeper, through MainWindow's tab-fallback logic) on every visit.
                Set(ChkStartHidden, s.StartMinimized);
                Set(ChkAutoRun, s.AutoStartEngine);
                Set(ChkVidLaunch, s.ForceVideoOnLaunch);
                Set(ChkEnableDeeper, s.EnableDeeper);

                static void Set(CheckBox box, bool value)
                {
                    if ((box.IsChecked ?? false) != value) box.IsChecked = value;
                }

                TxtStartupVideo.Text = string.IsNullOrEmpty(s.StartupVideoPath)
                    ? Localization.Loc.Get("label_random")
                    : System.IO.Path.GetFileName(s.StartupVideoPath);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("GeneralSettingsSection.OnSectionShown: {E}", ex.Message);
            }
        }

        // =====================================================================================
        //  language (shared path with the chrome pill - see class docs)
        // =====================================================================================

        private void CmbLanguageSetting_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLanguageSetting.SelectedItem is not ComboBoxItem selected) return;
            Main?.ApplyLanguageSelection(selected.Tag as string);
        }

        // =====================================================================================
        //  startup - pure forwards
        // =====================================================================================

        private void ChkWinStart_Click(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.ChkWinStart_Click(sender, e);
        }

        private void ChkStartHidden_Click(object sender, RoutedEventArgs e)
        {
            if (Main is not { } mw) return;
            mw.ChkStartHidden_Click(sender, e);   // may revert the box after its warning

            // MainWindow's handler only warns; StartMinimized was persisted by SaveSettings() back
            // when this checkbox was an invisible mirror. A live editor has to write it, and it has
            // to read the box AFTER the possible revert above.
            var s = App.Settings?.Current;
            if (s == null) return;
            var want = ChkStartHidden.IsChecked ?? false;
            if (s.StartMinimized == want) return;
            s.StartMinimized = want;
            App.Settings?.Save();
            App.Logger?.Information("Start minimized set to {Enabled} (Settings/General)", want);
        }

        private void BtnSelectStartupVideo_Click(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.BtnSelectStartupVideo_Click(sender, e);
        }

        private void BtnClearStartupVideo_Click(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.BtnClearStartupVideo_Click(sender, e);
        }

        // =====================================================================================
        //  startup - local writes (no MainWindow handler ever existed for these two)
        // =====================================================================================

        private void ChkAutoRun_Changed(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var want = ChkAutoRun.IsChecked ?? false;
            if (s.AutoStartEngine == want) return;   // seeding echo, not a user edit
            s.AutoStartEngine = want;
            App.Settings?.Save();
            App.Logger?.Information("Auto-start engine set to {Enabled} (Settings/General)", want);
        }

        private void ChkVidLaunch_Changed(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var want = ChkVidLaunch.IsChecked ?? false;
            if (s.ForceVideoOnLaunch == want) return;
            s.ForceVideoOnLaunch = want;
            App.Settings?.Save();
            App.Logger?.Information("Force video on launch set to {Enabled} (Settings/General)", want);
        }

        // =====================================================================================
        //  Deeper
        // =====================================================================================

        private void ChkEnableDeeper_Changed(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.ChkEnableDeeper_Changed(sender, e);
        }
    }
}
