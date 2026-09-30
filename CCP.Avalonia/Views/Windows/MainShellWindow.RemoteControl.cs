// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.RemoteControl.cs (1575 lines).
// Sorted member by member against the fifteen Core seams. The blanket header claimed all 77 were
// blocked; one is not, and the rest have ONE blocker between them worth naming precisely.
//
// THE CLIENT IS PORTED (remote-control-core): Core RemoteRelay (CCP.Core/Services/RemoteControl/) is the
// subject side of WPF RemoteControlService - pairing, PIN, poll, status, the safety gate - and the
// Remote Control tab (Views/Tabs/RemoteControlTabView.axaml.cs) owns the toggle, waiver, QR, status and
// command log. What stays empty here is the SHELL half of WPF MainWindow.RemoteControl.cs:
//   the full-window "someone is controlling you" overlay (ShowRemoteControlOverlay / idle subtitle),
//   its big emote buttons and End Session (BtnEmotePresetBig_Click, BtnEmoteCustomSendBig_Click,
//   TxtEmoteCustomBig_KeyDown, BtnEndRemoteSession_Click), the command toast (ShowCommandNotification),
//   the controller-joined notice, the Start button lock while a controller drives, the remote-driven
//   session verbs (StartSessionFromRemote & co - Core RemoteCommands refuses them "not on this build"),
//   the directory opt-in chain and the tray minimise/restore for remote.
// ponytail: those four handlers exist because MainShellWindow.axaml names them; wire them with the overlay.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The Play door's Available Subjects entry. One ShowTab, as in WPF. The roster
        /// on that page is still filled by App.AvailableSubjects, so this opens an empty tab.</summary>
        private void BtnAvailableSubjects_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => ShowTab("availablesubjects");

        // The four below belong to the controller overlay, which is not ported - see the header. They
        // exist because MainShellWindow.axaml names them and a missing handler is a XAML error.
        private void BtnEmoteCustomSendBig_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        private void BtnEmotePresetBig_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        private void BtnEndRemoteSession_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        private void TxtEmoteCustomBig_KeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e) { }
    }
}
