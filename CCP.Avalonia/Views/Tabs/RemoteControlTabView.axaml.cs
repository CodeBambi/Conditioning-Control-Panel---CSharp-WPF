using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class RemoteControlTabView : UserControl
    {
        /// <summary>True while the seed writes the controls, so no handler mistakes the echo for a
        /// user edit. Starts TRUE, the directory's established pattern (AwarenessTabView,
        /// BambiTakeoverTabView): the .axaml wires IsCheckedChanged itself, so the moment any box
        /// here is given IsChecked="True" in markup the handler fires from inside
        /// InitializeComponent, before the ctor has read settings - and would save the markup
        /// default over the user's file.</summary>
        private bool _isLoading = true;

        public RemoteControlTabView()
        {
            InitializeComponent(); // generated: loads the XAML AND fills the x:Name fields

            // The emote slots are AppSettings.RemoteEmotePresets, which is in Core - the same five
            // EmotePreset instances the WPF picker binds (MainWindow.Settings.cs:93). OnDeserialized
            // has already padded/truncated to exactly 5, so the ItemsControl never sees an odd count,
            // and an unseeded head gets DefaultRemoteEmotePresets().
            var s = CoreSettings.Current;
            LstEmotePresets.ItemsSource = s.RemoteEmotePresets;
            ChkStopEffectsOnRemoteDisconnect.IsChecked = s.StopEffectsOnRemoteDisconnect;
            ChkRemoteShareAvatar.IsChecked = s.RemoteShareAvatar;
            _isLoading = false;

            // Placeholder feed. On WPF the log is written by RemoteControlService
            // (ConditioningControlPanel/Services/RemoteControlService.cs), which is head-side: it
            // owns the HTTP session, the poll loop and the code minting. Seeded so --render-all
            // proves the row template actually draws (CLAUDE.md trap 4).
            LstRemoteCommandLog.ItemsSource = new[]
            {
                "20:14  controller connected",
                "20:14  tier set to Light",
            };
        }

        // Pure settings round-trip, as MainWindow.RemoteControl.cs:297 does it.
        private void ChkStopEffectsOnRemoteDisconnect_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var want = ChkStopEffectsOnRemoteDisconnect.IsChecked ?? false;
            if (CoreSettings.Current.StopEffectsOnRemoteDisconnect == want) return;
            CoreSettings.Current.StopEffectsOnRemoteDisconnect = want;
            CoreSettings.Save();
        }

        // Settings half of MainWindow.RemoteControl.cs:309. The other half pushes the new value to a
        // LIVE controller within one poll instead of ~15s; that needs App.RemoteControl
        // (ConditioningControlPanel/Services/RemoteControlService.cs), and no session can exist on
        // this head, so the privacy setting is stored honestly and no push is being skipped.
        private void ChkRemoteShareAvatar_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var want = ChkRemoteShareAvatar.IsChecked ?? false;
            if (CoreSettings.Current.RemoteShareAvatar == want) return;
            CoreSettings.Current.RemoteShareAvatar = want;
            CoreSettings.Save();
        }

        // View half of MainWindow.RemoteControl.cs:647 - reveal the opt-in form, then pre-populate.
        private void ChkOptIntoDirectory_Changed(object? sender, RoutedEventArgs e)
        {
            var checkedNow = ChkOptIntoDirectory.IsChecked == true;
            OptInFormPanel.IsVisible = checkedNow;
            if (checkedNow) PopulateOptInFormFromSavedSettings();
        }

        // Mirrors MainWindow.RemoteControl.cs:660. SavedDirectoryTags is on AppSettings, in Core.
        private void PopulateOptInFormFromSavedSettings()
        {
            var saved = CoreSettings.Current.SavedDirectoryTags;
            if (saved == null) return;
            foreach (var cb in new[]
            {
                ChkTagBimbo, ChkTagDrone, ChkTagTrance, ChkTagFeminization, ChkTagSubmission,
                ChkTagDegradation, ChkTagAudioOk, ChkTagSoftOnly, ChkTagLockdownOk, ChkTagChastity,
            })
            {
                cb.IsChecked = cb.Tag is string tag && saved.Contains(tag);
            }
        }

        // REFUSED, not merely missing: on WPF this handler is a premium gate that mints a session
        // code for someone else to drive this app (MainWindow.RemoteControl.cs:35 -
        // TierGate.RequiresPremium, revert-then-refuse). Persisting the flag here would leave a
        // toggle reading "remote control on" with no gate consulted and no session behind it. Needs
        // ConditioningControlPanel/Services/RemoteControlService.cs and TierGate.
        private void ChkRemoteControlEnabled_Changed(object? sender, RoutedEventArgs e) { }

        // ponytail: the rest forward to MainWindow on WPF (Window.GetWindow(this) is MainWindow mw
        // -> mw.<same name>) and need ConditioningControlPanel/Services/RemoteControlService.cs -
        // the session code, the share link, the command push and the directory listing. Names kept
        // identical so that wiring diffs cleanly.
        private void BtnCopyRemoteCode_Click(object? sender, RoutedEventArgs e) { }
        private void BtnCopyRemoteLink_Click(object? sender, RoutedEventArgs e) { }
        private void BtnEditEmoteCancel_Click(object? sender, RoutedEventArgs e) { }
        private void BtnEditEmoteSave_Click(object? sender, RoutedEventArgs e) { }
        private void BtnEmoteCustomSend_Click(object? sender, RoutedEventArgs e) { }
        private void BtnEmoteEdit_Click(object? sender, RoutedEventArgs e) { }
        private void BtnEmotePreset_Click(object? sender, RoutedEventArgs e) { }
        private void BtnGateUnlock_Click(object? sender, RoutedEventArgs e) { }
        private void BtnStopRemote_Click(object? sender, RoutedEventArgs e) { }
        private void ChkOptInTag_Click(object? sender, RoutedEventArgs e) { }
        private void CmbRemoteTier_SelectionChanged(object? sender, SelectionChangedEventArgs e) { }
        private void TierCard_Click(object? sender, PointerReleasedEventArgs e) { }
        private void TxtEditEmoteText_TextChanged(object? sender, TextChangedEventArgs e) { }
        private void TxtEmoteCustom_KeyDown(object? sender, KeyEventArgs e) { }
        private void TxtOptInStatus_TextChanged(object? sender, TextChangedEventArgs e) { }
    }
}
