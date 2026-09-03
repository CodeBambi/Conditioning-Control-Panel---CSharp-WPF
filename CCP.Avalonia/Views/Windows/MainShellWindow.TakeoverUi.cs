// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.TakeoverUi.cs (165 lines).
//
// The STATE HERO is real here: the title-bar pill, the orb's dormant/active look, and the two lines
// of status copy under it. All three are plain painting over controls this head already carries
// (MainShellWindow.axaml:426 for the pill, Views/Tabs/BambiTakeoverTabView.axaml:230/236/238 for the
// orb and the copy, Controls/TakeoverOrb.cs for SetActive), and the strings are English literals in
// WPF too - no {loc:Str} binding is being overwritten.
//
// NOTHING CALLS IT YET. On WPF the bool arrives from AutonomyService.EnabledChanged; on this head
// the caller belongs in MainShellWindow.Autonomy.cs, which this layer does not own and which is
// still a stub. Until then the pill keeps its XAML default (hidden) and the tab keeps its authored
// "○ DORMANT", which is the honest resting state rather than a claim.
//
// THE LIVE VOICE PANEL IS NOT RESTORED, and the reason is a seam, not effort. Every one of its
// painters is driven by an event that does not exist on this head:
//   InitTakeoverVoiceUi(…)      App.Speech.PartialTranscript / LevelChanged and App.Autonomy's
//                               EnabledChanged / VoicePromptStarted / VoicePromptFinished.
//                               CoreSpeech (CCP.Core/CoreSpeech.cs) is a CAPABILITY seam only -
//                               IsAvailable, HasCaptureDevice, ModelStatus, EnumerateInputDevices -
//                               and carries no transcript, level or prompt event at all; the
//                               services themselves are ConditioningControlPanel/Services/Speech/
//                               SpeechService.cs and /Services/AutonomyService.Voice.cs.
//   OnVoicePromptStarted(…)     phrase text + the LISTENING tint (Services/FxTheme.cs)
//   OnSpeechPartial(…)          partial transcript
//   OnSpeechLevel / SetVoiceLevel(…)   mic RMS -> the level bar's ScaleTransform + orb energy
//   OnVoicePromptFinished(…)    PhraseResult (ConditioningControlPanel/Services/Speech/
//                               SpeechService.cs): Matched / LoudEnough / Score / TimedOut /
//                               Transcript
//   _voicePanelHideTimer        the 2.6s verdict dwell, and RunOnUi, the marshalling those five
//                               handlers needed. Both go with the handlers.
// Painting that panel from anything else would be a Takeover UI that reports hearing when no mic is
// open - the one thing this surface must never do. VoiceLivePanel stays IsVisible=False, which is
// exactly where HideVoicePanel would put it.
//
// EnsurePr4aFx() is on this head (MainShellWindow.TabFxTakeoverLabStatus.cs) and is called from
// SetTakeoverActiveUi for the same reason WPF calls it from InitTakeoverVoiceUi: the Takeover
// surface is one of the five that wires that funnel up on first use.

using System;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private static readonly Color TakeoverGreenColor = Color.FromRgb(0x90, 0xEE, 0x90);
        private static readonly Color TakeoverMutedColor = Color.FromRgb(0x88, 0x88, 0xA0);

        /// <summary>
        /// The unmistakable ON/OFF state hero. Driven by whatever started or stopped Takeover
        /// (toggle, remote, panic, startup resume), so it never has to guess.
        /// </summary>
        internal void SetTakeoverActiveUi(bool active)
        {
            try
            {
                // The title-bar pill reflects on/off even when the Takeover tab has never been shown.
                var pill = Named<Border>("TakeoverActivePill");
                if (pill != null) pill.IsVisible = active;

                EnsurePr4aFx();

                var tab = Named<Control>("BambiTakeoverTab");
                if (tab == null) return;

                // The orb owns its own colour and its own dormant/active look; all this has to tell
                // it is which of the two it is in.
                tab.FindControl<TakeoverOrb>("TakeoverOrbFx")?.SetActive(active);

                var status = tab.FindControl<TextBlock>("TxtTakeoverStatus");
                if (status != null)
                {
                    status.Text = active ? "● ACTIVE" : "○ DORMANT";
                    status.Foreground = new SolidColorBrush(active ? TakeoverGreenColor : TakeoverMutedColor);
                }

                var sub = tab.FindControl<TextBlock>("TxtTakeoverStatusSub");
                if (sub != null)
                    sub.Text = active
                        ? "She has the reins. Tap stop any time."
                        : "She's not watching right now.";

                if (!active) HideVoicePanel();
            }
            catch (Exception ex) { Log.Warning(ex, "SetTakeoverActiveUi failed"); }
        }

        /// <summary>Puts the live voice panel away. Restored because turning Takeover OFF must
        /// close it whatever left it open - see the header for why nothing opens it here.</summary>
        private void HideVoicePanel()
        {
            var panel = Named<Control>("BambiTakeoverTab")?.FindControl<Border>("VoiceLivePanel");
            if (panel != null) panel.IsVisible = false;
        }
    }
}
