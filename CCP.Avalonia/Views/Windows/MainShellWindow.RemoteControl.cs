// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.RemoteControl.cs (1575 lines).
// Sorted member by member against the fifteen Core seams. The blanket header claimed all 77 were
// blocked; one is not, and the rest have ONE blocker between them worth naming precisely.
//
// THE BLOCKER IS A NETWORK CLIENT, NOT A SEAM: App.RemoteControl, which is
// ConditioningControlPanel/Services/RemoteControlService.cs - the pairing socket, the code and PIN,
// the controller identity, the command stream and the tier handshake. It is not a Core seam and
// there is no substitute for it; every emote send, every status line, every QR code and every
// session-from-remote command goes through it. Nothing here invents a client, and nothing here
// paints a status from a client that is not there: an "idle" or "connected" line derived from local
// state alone would be a control lying about who is driving this machine, which is the most
// consequential state in the app.
//
// WHAT IS REAL: BtnAvailableSubjects_Click. One ShowTab("availablesubjects"), the same one line as
// WPF (MainWindow.RemoteControl.cs:491). "availablesubjects" IS a mapped tab on this head
// (MainShellWindow.TabNavigation.cs:88 -> AvailableSubjectsTab), so the door opens and the panel
// swaps; that panel's roster is still filled by App.AvailableSubjects, so the click navigates to an
// empty page rather than to nothing.
//
// THE FOUR OTHER HANDLERS MainShellWindow.axaml NAMES STAY EMPTY, deliberately:
//   BtnEmotePresetBig_Click / BtnEmoteCustomSendBig_Click / TxtEmoteCustomBig_KeyDown all end in
//     SendEmoteAndReportAsync -> App.RemoteControl.SendEmote. There is nowhere to send to.
//   BtnEndRemoteSession_Click ends a session on the SUBJECT'S machine. With no session to end it
//     would be a button that reports having stopped something it never touched.
//
// PORTABLE BUT HELD, and each for a stated reason rather than a wait:
//   BuildRemotePairingUrl - two string interpolations, and one of them is App.RemoteControl.ConnectPin.
//     A pairing URL built without the PIN is a link that fails to pair; there is no honest half.
//   GetSelectedRemoteTier / ShowRemoteControlWaiver / RefreshTierCardHighlight / TierCard_Click -
//     the tier picker reads and writes RemoteControlTabView, which loads with
//     AvaloniaXamlLoader.Load(this) and is not owned by this layer. Reaching into another view's
//     name scope to set a tier that no session will use is churn.
//   UpdateOptInStatusCharCount / GetSelectedDirectoryTags / OptInTagCheckBoxes /
//     PopulateOptInFormFromSavedSettings / TxtOptInStatus_TextChanged / ChkOptInTag_Click /
//     ChkOptIntoDirectory_Changed / ShowOptInFeedback / UpdateDirectoryListingStatus / OptInMaxTags.
//     The directory opt-in form is genuinely settings-shaped and would persist through CoreSettings,
//     but it PUBLISHES the user to a public subject directory - RunOptInChainAsync is the server
//     call that makes it real. A form that saves the fields locally and shows "listed" without ever
//     calling the chain is a consent surface that lies about being public, so the whole form waits
//     for the chain. OptInTagCheckBoxes is additionally typed System.Windows.Controls.CheckBox[].
//
// THE REST, by group, all on App.RemoteControl unless noted:
//   Enable / disable and tier - ChkRemoteControlEnabled_Changed, CmbRemoteTier_SelectionChanged,
//     BtnStopRemote_Click, StopRemoteControl, ChkStopEffectsOnRemoteDisconnect_Changed,
//     ChkRemoteShareAvatar_Changed.
//   Pairing display - BtnCopyRemoteCode_Click, BtnCopyRemoteLink_Click, RefreshRemoteQrCode
//     (a QR encoder as well as the code), UpdateRemoteControlUI.
//   Live session - OnRemoteControllerChanged, OnRemoteControllerIdleChanged, OnRemoteSessionEnded,
//     UpdateRemoteStatus, WireRemoteSessionCallbacks, StartRemoteSessionInfoTimer,
//     UpdateRemoteSessionInfo, OnRemoteCommandReceived, AppendRemoteCommandLog,
//     ShowRemoteControlOverlay, HideRemoteControlOverlay, ShowCommandNotification,
//     HideCommandNotification, NotifyRemoteControllerJoined.
//   Emotes - BtnEmotePreset_Click, BtnEmoteCustomSend_Click, TxtEmoteCustom_KeyDown,
//     SendCustomEmoteAsync, SendEmoteAndReportAsync, BtnEmoteEdit_Click, TxtEditEmoteText_TextChanged,
//     BtnEditEmoteSave_Click, BtnEditEmoteCancel_Click, _editingPreset. EmotePreset is a Core model;
//     the sending is not.
//   Subjects roster - BtnBecomeASubject_Click, RefreshBecomeASubjectCta, EnsureAvailableSubjectsBound,
//     OnAvailableSubjectsServicePropertyChanged, UpdateAvailableSubjectsEmptyAndError,
//     BtnConnectSubject_Click, AvailableSubjectsScroller_PreviewMouseWheel (no PreviewMouseWheel in
//     Avalonia either way).
//   Remote-driven session control - StartSessionFromRemote, PauseSessionFromRemote,
//     ResumeSessionFromRemote, StopSessionFromRemote, StopEngineAndSession, TriggerPanicFromRemote,
//     IsSessionRemoteStarted, _remoteStartedSession. These drive SessionEngine
//     (ConditioningControlPanel/Services/Session/SessionEngine.cs) as well as the remote client.
//   Tray - MinimizeToTrayForRemote, MinimizeToTrayForChaos, RestoreFromTrayForRemote, ShowFromTray.
//     A Win32 notify-icon on WPF; a per-platform reimplementation here, not a port.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The Play door's Available Subjects entry. One ShowTab, as in WPF. The roster
        /// on that page is still filled by App.AvailableSubjects, so this opens an empty tab.</summary>
        private void BtnAvailableSubjects_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => ShowTab("availablesubjects");

        // The four below reach App.RemoteControl and stay empty on purpose - see the header. They
        // exist because MainShellWindow.axaml names them and a missing handler is a XAML error.
        private void BtnEmoteCustomSendBig_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        private void BtnEmotePresetBig_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        private void BtnEndRemoteSession_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        private void TxtEmoteCustomBig_KeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e) { }
    }
}
