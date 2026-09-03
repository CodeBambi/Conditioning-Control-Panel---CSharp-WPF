using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS · UPDATES, ported from the WPF head. Version, manual update check, patch notes.
    /// <c>UpdateService</c> (AppVersion, CurrentPatchNotes, the manual check) and
    /// <c>WhatsNewDialog</c> are still in the WPF head, so the three text blocks carry placeholder
    /// text and both buttons are stubs.
    /// </summary>
    public partial class UpdatesSettingsSection : UserControl
    {
        public UpdatesSettingsSection()
        {
            InitializeComponent();

            // ponytail: needs UpdateService.AppVersion / CurrentPatchNotes, wired when it moves to Core
            TxtUpdatesVersion.Text = "v0.0.0";
            TxtUpdatesProduct.Text = "Conditioning Control Panel";
            TxtPatchNotes.Text = "Placeholder patch notes. The installed build's notes come from UpdateService.CurrentPatchNotes.";

            BtnCheckUpdates.Click += BtnCheckUpdates_Click;
            BtnViewPatchNotes.Click += BtnViewPatchNotes_Click;
        }

        private void BtnCheckUpdates_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.CheckForUpdatesManuallyAsync, wired when UpdateService moves to Core
        }

        private void BtnViewPatchNotes_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs WhatsNewDialog (Loc.GetF("set2_whats_new_title_0", version)) + the upgrade tour, wired when they are ported
        }
    }
}
