using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Views.Controls.AppSettingsSections
{
    /// <summary>
    /// SETTINGS · UPDATES. Version, the manual update check, and the installed build's patch notes.
    ///
    /// <para>The check button moved verbatim from <c>Features/AppInfoFeatureControl</c>, handler body
    /// included - it was always self-contained (<c>App.CheckForUpdatesManuallyAsync</c>), never a
    /// MainWindow method, so nothing had to be re-pointed. <b>THIS section's own private
    /// <c>BtnCheckUpdates_Click</c> below is what the live button binds to.</b> MainWindow carries a
    /// same-named handler which used to serve the ProgressionTab copy; Phase 8 deleted that button
    /// and re-aimed the handler's "Checking…" affordance at <c>AppSettingsTab.BtnCheckUpdates</c>,
    /// but nothing binds the MainWindow copy any more - it is kept, unbound, per the Phase 8 audit.
    /// If the disabled/"Checking…" affordance is ever wanted on this button, add it HERE rather
    /// than re-binding across the boundary.</para>
    /// </summary>
    public partial class UpdatesSettingsSection : UserControl
    {
        public UpdatesSettingsSection()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtUpdatesVersion.Text = $"v{UpdateService.AppVersion}";
                TxtUpdatesProduct.Text = "Conditioning Control Panel";
                TxtPatchNotes.Text = UpdateService.CurrentPatchNotes;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("UpdatesSettingsSection: version paint failed: {E}", ex.Message);
            }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
                await App.CheckForUpdatesManuallyAsync(owner);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Settings/Updates: check updates failed");
                MessageBox.Show(
                    $"Failed to check for updates: {ex.Message}",
                    "Update Check",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Same dialog the post-update "What's New" popup uses (Dialogs/WhatsNewDialog), so the
        /// notes are never formatted two different ways - and, for the same reason, the same
        /// upgrade-tour offer. Reading the notes late is exactly when someone is looking for the
        /// tour, and the startup showing is one-shot per version (LastSeenVersion).
        /// </summary>
        private void BtnViewPatchNotes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
                var dialog = new WhatsNewDialog(
                    Loc.GetF("set2_whats_new_title_0", UpdateService.AppVersion),
                    UpdateService.CurrentPatchNotes,
                    tourAction: () =>
                    {
                        try { (owner as MainWindow)?.StartTutorial(TutorialType.UpgradeTour); }
                        catch (Exception ex)
                        {
                            App.Logger?.Warning(ex, "Settings/Updates: could not start the v6.8 upgrade tour");
                        }
                    },
                    tourButtonText: "Show me around (60s)")
                {
                    Owner = owner
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Settings/Updates: failed to open patch notes");
            }
        }

        private void InnerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.InnerScrollViewer_PreviewMouseWheel(sender, e);
        }
    }
}
