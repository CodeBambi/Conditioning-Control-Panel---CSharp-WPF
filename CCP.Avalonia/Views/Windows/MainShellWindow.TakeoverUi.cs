// PORTED from ConditioningControlPanel/MainWindow/MainWindow.TakeoverUi.cs (165 lines).
//
// The STATE HERO: the title-bar pill, the orb's dormant/active look, and the two lines of status
// copy under it (MainShellWindow.axaml for the pill, Views/Tabs/BambiTakeoverTabView.axaml for the
// orb and the copy, Controls/TakeoverOrb.cs for SetActive). The status word is an English literal in
// WPF too; the sub line is Loc takeover_status_sub_*, as WPF.
//
// The caller is Core AutonomyScheduler.EnabledChanged, hooked in MainShellWindow.Autonomy.cs (as WPF's
// AutonomyService.EnabledChanged), so start, stop, panic and startup resume all repaint it.
//
// The live voice panel opens only from Core SpokenMantra's PromptStarted (a real prompt about to
// listen), shows SpeechEngine's partials and level while that prompt runs, and the verdict after.
//
// EnsurePr4aFx() is on this head (MainShellWindow.TabFxTakeoverLabStatus.cs) and is called from
// SetTakeoverActiveUi for the same reason WPF calls it from InitTakeoverVoiceUi: the Takeover
// surface is one of the five that wires that funnel up on first use.

using System;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Speech;
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
                    sub.Text = Loc.Get(active ? "takeover_status_sub_active" : "takeover_status_sub_dormant");

                if (!active) HideVoicePanel();
            }
            catch (Exception ex) { Log.Warning(ex, "SetTakeoverActiveUi failed"); }
        }

        private global::Avalonia.Threading.DispatcherTimer? _voicePanelHideTimer;

        /// <summary>WPF OnVoicePromptStarted: a spoken mantra is about to listen - the phrase, a reset
        /// readout and the LISTENING status in her accent. Raised only by SpokenMantra, i.e. a real prompt.</summary>
        internal void ShowVoicePrompt(string phrase)
        {
            var tab = Named<Control>("BambiTakeoverTab");
            if (tab == null) return;
            _voicePanelHideTimer?.Stop();
            if (tab.FindControl<TextBlock>("TxtVoicePromptPhrase") is { } p) p.Text = $"“ {phrase} ”";
            ShowVoiceHeard("");
            if (tab.FindControl<Border>("VoiceVerdictChip") is { } chip) chip.IsVisible = false;
            SetVoiceLevel(0);
            if (tab.FindControl<Border>("VoiceLivePanel") is { } panel) panel.IsVisible = true;
            if (tab.FindControl<TextBlock>("TxtTakeoverStatus") is { } status)
            {
                status.Text = "● LISTENING";
                if (Color.TryParse(CoreMods.AccentColorHex, out var accent)) status.Foreground = new SolidColorBrush(accent);
            }
        }

        /// <summary>WPF OnSpeechPartial.</summary>
        internal void ShowVoiceHeard(string? text)
        {
            if (Named<Control>("BambiTakeoverTab")?.FindControl<TextBlock>("TxtVoiceHeard") is { } t)
                t.Text = string.IsNullOrWhiteSpace(text) ? "I heard: …" : $"I heard: {text}";
        }

        /// <summary>WPF SetVoiceLevel: speech RMS ~0..0.2 fills the bar; the orb brightens off it.</summary>
        internal void SetVoiceLevel(double level)
        {
            var tab = Named<Control>("BambiTakeoverTab");
            if (tab?.FindControl<Border>("VoiceLevelFill")?.RenderTransform is ScaleTransform st)
                st.ScaleX = Math.Min(1.0, Math.Max(0.0, level / 0.2));
            tab?.FindControl<TakeoverOrb>("TakeoverOrbFx")?.SetEnergy(level);
        }

        /// <summary>WPF OnVoicePromptFinished: the verdict chip, held 2.6 s, then the resting state.</summary>
        internal void ShowVoiceVerdict(ConditioningControlPanel.Services.Speech.PhraseResult r)
        {
            var tab = Named<Control>("BambiTakeoverTab");
            if (tab == null) return;
            if (tab.FindControl<Border>("VoiceVerdictChip") is { } chip && tab.FindControl<TextBlock>("TxtVoiceVerdict") is { } txt)
            {
                string label; Color bg;
                if (r.Matched) { label = "✓ MATCHED"; bg = Color.FromRgb(0x2E, 0x7D, 0x32); }
                else if (!r.LoudEnough && r.Score >= 0.45) { label = "🔊 LOUDER"; bg = Color.FromRgb(0xB8, 0x86, 0x0B); }
                else if (r.TimedOut && string.IsNullOrWhiteSpace(r.Transcript)) { label = "… NO REPLY"; bg = Color.FromRgb(0x5A, 0x5A, 0x70); }
                else { label = "✗ MISS"; bg = Color.FromRgb(0xA0, 0x3A, 0x3A); }
                txt.Text = label;
                chip.Background = new SolidColorBrush(bg);
                chip.IsVisible = true;
            }
            SetVoiceLevel(0);
            _voicePanelHideTimer?.Stop();
            _voicePanelHideTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
            _voicePanelHideTimer.Tick += (_, _) =>
            {
                _voicePanelHideTimer?.Stop();
                HideVoicePanel();
                SetTakeoverActiveUi(Autonomy.IsEnabled);
            };
            _voicePanelHideTimer.Start();
        }

        /// <summary>WPF MainWindow.Autonomy.cs SpeechModelIsTheProblem: speech is unavailable and the
        /// microphone is not the reason, so the answer is a model (missing, refusing to load, unprobed).</summary>
        internal static bool SpeechModelIsTheProblem() =>
            CoreSpeech.HasCaptureDevice && !CoreSpeech.IsAvailable
            && CoreSpeech.ModelStatus is CoreSpeechModelStatus.LoadFailed or CoreSpeechModelStatus.NoModelFound
                                      or CoreSpeechModelStatus.NotProbed;

        /// <summary>WPF OpenSpeechModelFolder: open the folder the model goes in, creating it first; a
        /// refusal is SHOWN as a toast naming the path. This head probes two roots (beside the app,
        /// then user data), so the first one that exists or can be created is opened - an install
        /// directory the user cannot write falls through to the user-data one instead of failing.</summary>
        internal async void OpenSpeechModelFolder()
        {
            var roots = SpeechEngine.DefaultModelRoots;
            var tried = roots[0];
            foreach (var root in roots)
            {
                tried = root;
                try { System.IO.Directory.CreateDirectory(root); }
                catch (Exception ex) { Log.Debug("Speech model folder {Root} not creatable: {E}", root, ex.Message); continue; }
                if (await Platform.ExternalOpener.OpenAsync(this, root)) return;
                break;
            }
            Log.Warning("Could not open the speech model folder at {Root}", tried);
            try
            {
                App.Notifications.Show(Loc.GetF("msg_open_models_folder_failed", tried),
                    Helpers.NotificationType.Warning, TimeSpan.FromSeconds(12));
            }
            catch (Exception ex) { Log.Debug("Models-folder toast failed: {E}", ex.Message); }
        }

        /// <summary>Puts the live voice panel away (Takeover OFF, or the verdict's dwell ending).</summary>
        private void HideVoicePanel()
        {
            var panel = Named<Control>("BambiTakeoverTab")?.FindControl<Border>("VoiceLivePanel");
            if (panel != null) panel.IsVisible = false;
        }
    }
}
