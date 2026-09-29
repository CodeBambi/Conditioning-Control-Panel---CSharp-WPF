using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS · UPDATES, ported from the WPF head. Version, manual update check, patch notes.
    ///
    /// The version and the installed build's notes now come from <see cref="CoreReleaseContent"/>
    /// (the Windows head seeds them from <c>UpdateService</c>; an unseeded head answers with its
    /// assembly version and empty notes, so a headless render still paints something true). The
    /// patch-notes button opens the same <c>WhatsNewDialog</c> the post-update popup uses, as on
    /// WPF. The manual update check is <see cref="Platform.AppUpdater.ManualCheckAsync"/>.
    /// </summary>
    public partial class UpdatesSettingsSection : UserControl
    {
        public UpdatesSettingsSection()
        {
            InitializeComponent();

            TxtUpdatesVersion.Text = $"v{CoreReleaseContent.AppVersion}";
            TxtUpdatesProduct.Text = "Conditioning Control Panel";
            TxtPatchNotes.Text = CoreReleaseContent.PatchNotes;

            BtnCheckUpdates.Click += BtnCheckUpdates_Click;
            BtnViewPatchNotes.Click += BtnViewPatchNotes_Click;
        }

        // WPF UpdatesSettingsSection -> App.CheckForUpdatesManuallyAsync (Linux: notify only).
        private async void BtnCheckUpdates_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is Window owner) await Platform.AppUpdater.ManualCheckAsync(owner);
        }

        /// <summary>
        /// Same dialog the post-update "What's New" popup uses, so the notes are never formatted
        /// two different ways - and, for the same reason, the same upgrade-tour offer, now routed
        /// through <see cref="CoreTutorial"/>.
        /// </summary>
        private async void BtnViewPatchNotes_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (TopLevel.GetTopLevel(this) is not Window owner) return;
                // The upgrade-tour offer is the WPF original's, restored: CoreTutorial.Start
                // takes the head's tutorial-type NAME, and an unseeded head simply does nothing -
                // which is why offering it here is safe rather than a button that lies. Reading
                // the notes late is exactly when someone wants the tour, and the startup showing
                // is one-shot per version.
                var dialog = new Dialogs.WhatsNewDialog(
                    Loc.GetF("set2_whats_new_title_0", CoreReleaseContent.AppVersion),
                    CoreReleaseContent.PatchNotes,
                    tourAction: () => CoreTutorial.Start("UpgradeTour"),
                    tourButtonText: "Show me around (60s)");
                await dialog.ShowDialog(owner);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Settings/Updates: failed to open patch notes");
            }
        }
    }
}
