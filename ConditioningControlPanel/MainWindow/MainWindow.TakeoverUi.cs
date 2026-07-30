using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Speech;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Pass-2 Takeover UI: the unmistakable ON/OFF state hero (orb + status) and the live
    /// "say it for me" voice panel. Driven entirely by service events so it reflects state
    /// no matter what started/stopped Takeover (toggle, remote, panic, startup resume).
    /// </summary>
    public partial class MainWindow
    {
        private static readonly Color GreenColor = Color.FromRgb(0x90, 0xEE, 0x90);
        private static readonly Color MutedColor = Color.FromRgb(0x88, 0x88, 0xA0);
        private DispatcherTimer? _voicePanelHideTimer;

        /// <summary>Subscribe the state hero + live voice panel to speech/autonomy events. Idempotent.</summary>
        internal void InitTakeoverVoiceUi()
        {
            try
            {
                if (App.Speech != null)
                {
                    App.Speech.PartialTranscript -= OnSpeechPartial;
                    App.Speech.LevelChanged -= OnSpeechLevel;
                    App.Speech.PartialTranscript += OnSpeechPartial;
                    App.Speech.LevelChanged += OnSpeechLevel;
                }
                if (App.Autonomy != null)
                {
                    App.Autonomy.EnabledChanged -= OnTakeoverEnabledChanged;
                    App.Autonomy.VoicePromptStarted -= OnVoicePromptStarted;
                    App.Autonomy.VoicePromptFinished -= OnVoicePromptFinished;
                    App.Autonomy.EnabledChanged += OnTakeoverEnabledChanged;
                    App.Autonomy.VoicePromptStarted += OnVoicePromptStarted;
                    App.Autonomy.VoicePromptFinished += OnVoicePromptFinished;
                }
                EnsurePr4aFx();
                SetTakeoverActiveUi(App.Autonomy?.IsEnabled == true);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitTakeoverVoiceUi failed"); }
        }

        private void RunOnUi(Action a)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.HasShutdownStarted) return;
            if (d.CheckAccess()) { try { a(); } catch { } }
            else d.BeginInvoke(a);
        }

        // ── State hero ────────────────────────────────────────────────────────

        private void OnTakeoverEnabledChanged(object? sender, bool active) => RunOnUi(() => SetTakeoverActiveUi(active));

        private void SetTakeoverActiveUi(bool active)
        {
            // Title-bar status pill — always reflects on/off, even when the Takeover tab isn't built yet.
            if (TakeoverActivePill != null)
                TakeoverActivePill.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

            var tab = BambiTakeoverTab;
            if (tab == null) return;

            // The orb owns its own colour (from FxTheme) and its own dormant/active look; all this
            // has to tell it is which of the two it is in.
            tab.TakeoverOrbFx?.SetActive(active);

            if (tab.TxtTakeoverStatus != null)
            {
                tab.TxtTakeoverStatus.Text = active ? "● ACTIVE" : "○ DORMANT";
                tab.TxtTakeoverStatus.Foreground = new SolidColorBrush(active ? GreenColor : MutedColor);
            }
            if (tab.TxtTakeoverStatusSub != null)
                tab.TxtTakeoverStatusSub.Text = active
                    ? "She has the reins. Tap stop any time."
                    : "She's not watching right now.";

            if (!active) HideVoicePanel();
        }

        // ── Live voice panel ──────────────────────────────────────────────────

        private void OnVoicePromptStarted(object? sender, string phrase) => RunOnUi(() =>
        {
            var tab = BambiTakeoverTab;
            if (tab == null) return;
            _voicePanelHideTimer?.Stop();

            if (tab.TxtVoicePromptPhrase != null) tab.TxtVoicePromptPhrase.Text = $"“ {phrase} ”";
            if (tab.TxtVoiceHeard != null) tab.TxtVoiceHeard.Text = "I heard: …";
            if (tab.VoiceVerdictChip != null) tab.VoiceVerdictChip.Visibility = Visibility.Collapsed;
            SetVoiceLevel(0);
            if (tab.VoiceLivePanel != null) tab.VoiceLivePanel.Visibility = Visibility.Visible;

            if (tab.TxtTakeoverStatus != null)
            {
                tab.TxtTakeoverStatus.Text = "● LISTENING";
                // The accent, from the mod palette rather than the literal pink that used to be
                // baked in here - LISTENING is her colour, and hers changes with the mod.
                tab.TxtTakeoverStatus.Foreground = new SolidColorBrush(Services.FxTheme.GlowColor);
            }
        });

        private void OnSpeechPartial(object? sender, string text) => RunOnUi(() =>
        {
            var t = BambiTakeoverTab?.TxtVoiceHeard;
            if (t != null) t.Text = string.IsNullOrWhiteSpace(text) ? "I heard: …" : $"I heard: {text}";
        });

        private void OnSpeechLevel(object? sender, double level) => RunOnUi(() => SetVoiceLevel(level));

        private void SetVoiceLevel(double level)
        {
            if (BambiTakeoverTab?.VoiceLevelFill?.RenderTransform is ScaleTransform st)
                st.ScaleX = Math.Min(1.0, Math.Max(0.0, level / 0.2)); // speech RMS ~0..0.2 -> full bar

            // Status-driven, not ambient: the orb only ever brightens off a level the mic actually
            // reported, and the value decays inside the orb's own tick. Setting a float on a parked
            // orb costs nothing, so there is no gate to check here.
            BambiTakeoverTab?.TakeoverOrbFx?.SetEnergy(level);
        }

        private void OnVoicePromptFinished(object? sender, PhraseResult r) => RunOnUi(() =>
        {
            var tab = BambiTakeoverTab;
            if (tab == null) return;

            if (tab.VoiceVerdictChip != null && tab.TxtVoiceVerdict != null)
            {
                string label; Color bg;
                if (r.Matched) { label = "✓ MATCHED"; bg = Color.FromRgb(0x2E, 0x7D, 0x32); }
                else if (!r.LoudEnough && r.Score >= 0.45) { label = "🔊 LOUDER"; bg = Color.FromRgb(0xB8, 0x86, 0x0B); }
                else if (r.TimedOut && string.IsNullOrWhiteSpace(r.Transcript)) { label = "… NO REPLY"; bg = Color.FromRgb(0x5A, 0x5A, 0x70); }
                else { label = "✗ MISS"; bg = Color.FromRgb(0xA0, 0x3A, 0x3A); }
                tab.TxtVoiceVerdict.Text = label;
                tab.VoiceVerdictChip.Background = new SolidColorBrush(bg);
                tab.VoiceVerdictChip.Visibility = Visibility.Visible;
            }
            SetVoiceLevel(0);

            // Hold the verdict on screen briefly, then fall back to the resting ACTIVE state.
            _voicePanelHideTimer?.Stop();
            _voicePanelHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
            _voicePanelHideTimer.Tick += (_, _) =>
            {
                _voicePanelHideTimer?.Stop();
                HideVoicePanel();
                SetTakeoverActiveUi(App.Autonomy?.IsEnabled == true);
            };
            _voicePanelHideTimer.Start();
        });

        private void HideVoicePanel()
        {
            if (BambiTakeoverTab?.VoiceLivePanel != null)
                BambiTakeoverTab.VoiceLivePanel.Visibility = Visibility.Collapsed;
        }
    }
}
