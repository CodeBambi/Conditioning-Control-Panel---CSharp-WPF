using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Compat passthroughs for SETTINGS ▸ GENERAL (Workshop-cell precedent, see the header of
    /// AppSettingsTabView.xaml.cs for why these live in a per-section partial).
    ///
    /// <para>Every name below used to be <c>SettingsTab.&lt;name&gt;</c> inside the dashboard's
    /// permanently-Collapsed LegacyDashboardHost. The controls moved into
    /// <c>GeneralSettingsSection</c> keeping their x:Names, and the MainWindow partials that own
    /// their logic were re-pointed to <c>AppSettingsTab.&lt;name&gt;</c> - so the readers and writers
    /// listed against each property below are unchanged code, just a different prefix.</para>
    ///
    /// <para>These must be non-null the moment <c>MainWindow.LoadSettings()</c> runs: it
    /// dereferences four of them without a guard, exactly as the old dashboard code did. That holds
    /// because <c>SectionGeneral</c> is a XAML-instantiated child and sections are never lazy
    /// (ground rule §2.3).</para>
    /// </summary>
    public partial class AppSettingsTabView
    {
        /// <summary>Read/written by MainWindow.Settings.cs (Load/Save), .UiUpdates.cs
        /// (ChkWinStart_Click, ChkStartHidden_Click) and .xaml.cs (RequestToggleWindowsStartup).</summary>
        internal CheckBox ChkWinStart => SectionGeneral.ChkWinStart;

        /// <summary>Read/written by MainWindow.Settings.cs (Load/Save) and .UiUpdates.cs
        /// (both startup warning paths).</summary>
        internal CheckBox ChkStartHidden => SectionGeneral.ChkStartHidden;

        /// <summary>Read/written by MainWindow.Settings.cs (Load/Save).</summary>
        internal CheckBox ChkVidLaunch => SectionGeneral.ChkVidLaunch;

        /// <summary>Read/written by MainWindow.Settings.cs (Load/Save).</summary>
        internal CheckBox ChkAutoRun => SectionGeneral.ChkAutoRun;

        /// <summary>Written by MainWindow.Settings.cs (LoadSettings) and by both startup-video
        /// buttons in MainWindow.UiUpdates.cs.</summary>
        internal TextBlock TxtStartupVideo => SectionGeneral.TxtStartupVideo;

        /// <summary>Read/written by MainWindow.Settings.cs (Load/Save) and .DeeperTab.cs
        /// (ChkEnableDeeper_Changed, which also drives BtnDeeper's visibility).</summary>
        internal CheckBox ChkEnableDeeper => SectionGeneral.ChkEnableDeeper;

        /// <summary>Populated and re-selected by MainWindow.AccountShell.cs alongside the chrome's
        /// CmbLanguagePill - owner decision #8 keeps both surfaces, sharing one code path.</summary>
        internal ComboBox CmbLanguageSetting => SectionGeneral.CmbLanguageSetting;
    }
}
