using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Lock Card settings panel, ported from the WPF head. Every editor reads and writes
    /// <see cref="CoreSettings.Current"/> and persists through <see cref="CoreSettings.Save"/>,
    /// and the settings hook is inlined for the reason spelled out in
    /// <see cref="BubbleCountFeatureControl"/>.
    ///
    /// <para>Both gates are real: strict mode still has to clear the acknowledgement-gated double
    /// warning, and voice mode still has to clear the mic consent flow before it is written. Both
    /// dialogs are awaited rather than blocking, and a decline reverts the box.</para>
    ///
    /// <para>Still head-side: LockCardService (start/stop/test) and the mod-aware feature art. The
    /// voice hint keeps the WPF literals; with no speech service on this head the "on" branch
    /// lands on "No microphone detected", which is the honest answer here.</para>
    /// </summary>
    public partial class LockCardFeatureControl : UserControl
    {
        private bool _isLoading = true; // stops the XAML defaults overwriting settings while loading
        private AppSettings? _hooked;

        public LockCardFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            ChkEnable.IsCheckedChanged += ChkEnable_Changed;
            SliderFreq.ValueChanged += SliderFreq_Changed;
            SliderRepeats.ValueChanged += SliderRepeats_Changed;
            ChkStrict.IsCheckedChanged += ChkStrict_Changed;
            ChkVoiceMode.IsCheckedChanged += ChkVoiceMode_Changed;
            BtnManagePhrases.Click += BtnManagePhrases_Click;
            BtnTest.Click += BtnTest_Click;
            BtnColorSettings.Click += BtnColorSettings_Click;

            Loaded += (_, _) => RebindToCurrentSettings();
            Unloaded += (_, _) => Unhook();

            // ponytail: WPF also repaints the hero and side plates on ModChanged through
            // Services/ModResourceResolver.cs (WPF head) from Resources/features/Phrase_Lock.png.
            // The Avalonia .axaml drops both plates deliberately, so nothing here to repaint yet.

            RebindToCurrentSettings();
        }

        /// <summary>Re-points the settings hook at the live instance and repaints from it.</summary>
        public void RebindToCurrentSettings()
        {
            Unhook();
            _hooked = CoreSettings.Current;
            _hooked.PropertyChanged += OnSettingsPropertyChanged;
            LoadFromSettings();
        }

        private void Unhook()
        {
            if (_hooked != null) _hooked.PropertyChanged -= OnSettingsPropertyChanged;
            _hooked = null;
        }

        private void LoadFromSettings()
        {
            var s = CoreSettings.Current;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.LockCardEnabled;
                SliderFreq.Value = s.LockCardFrequency;
                TxtFreq.Text = s.LockCardFrequency.ToString();
                SliderRepeats.Value = s.LockCardRepeats;
                TxtRepeats.Text = $"{s.LockCardRepeats}x";
                ChkStrict.IsChecked = s.LockCardStrict;
                ChkVoiceMode.IsChecked = s.LockCardVoiceMode && s.MicConsentGiven;
                UpdateVoiceHint();
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.LockCardEnabled) ||
                e.PropertyName == nameof(AppSettings.LockCardFrequency) ||
                e.PropertyName == nameof(AppSettings.LockCardRepeats) ||
                e.PropertyName == nameof(AppSettings.LockCardStrict) ||
                e.PropertyName == nameof(AppSettings.LockCardVoiceMode))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkEnable.IsChecked ?? false;
            if (s.LockCardEnabled == on) return;
            s.LockCardEnabled = on;
            CoreSettings.Save();

            // ponytail: WPF live-applies here - App.LockCard.Start()/Stop(), gated on
            // App.IsEngineRunning. LockCardService (ConditioningControlPanel/Services/) and
            // App.IsEngineRunning (ConditioningControlPanel/App.xaml.cs) are both still head-side.
        }

        private void SliderFreq_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtFreq.Text = v.ToString();
            if (s.LockCardFrequency == v) return;
            s.LockCardFrequency = v;
            CoreSettings.Save();
        }

        private void SliderRepeats_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtRepeats.Text = $"{v}x";
            if (s.LockCardRepeats == v) return;
            s.LockCardRepeats = v;
            CoreSettings.Save();
        }

        /// <summary>
        /// Strict mode removes the ESC escape, so switching it ON has to clear the
        /// acknowledgement-gated double warning first. A decline puts the box back.
        /// </summary>
        private async void ChkStrict_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkStrict.IsChecked ?? false;
            if (s.LockCardStrict == on) return;

            if (on)
            {
                // No owner Window means no dialog, and an unacknowledged lock must not arm - so
                // "not confirmed" is the honest answer and the box reverts.
                var owner = TopLevel.GetTopLevel(this) as Window;
                var confirmed = owner != null && await Dialogs.WarningDialog.ShowDoubleWarningAsync(owner,
                    "Strict Lock Card",
                    "• You will NOT be able to escape lock cards with ESC\n" +
                    "• You MUST type the phrase the required number of times\n" +
                    "• This can be very restrictive!");

                if (!confirmed)
                {
                    // Back on the UI thread after the await, so WPF's BeginInvoke hop is gone.
                    _isLoading = true;
                    ChkStrict.IsChecked = false;
                    _isLoading = false;
                    return;
                }
            }

            s.LockCardStrict = on;
            CoreSettings.Save();
            Log.Information("Lock card strict mode set to {Enabled}", on);
        }

        /// <summary>
        /// First time on, voice mode requires mic consent (the shared offline-audio contract).
        /// Decline reverts the box and nothing is written.
        /// </summary>
        private async void ChkVoiceMode_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkVoiceMode.IsChecked ?? false;
            if (s.LockCardVoiceMode == on) return;

            if (on && !s.MicConsentGiven)
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                var dlg = new Dialogs.MicConsentDialog();
                var ok = owner != null && await dlg.ShowDialog<bool?>(owner) == true && dlg.ConsentGiven;
                if (!ok)
                {
                    _isLoading = true;
                    ChkVoiceMode.IsChecked = false;
                    _isLoading = false;
                    return;
                }
                // ponytail: on WPF the consent dialog itself flips AppSettings.MicConsentGiven and
                // saves; the ported one does not yet (CCP.Avalonia/Views/Dialogs/MicConsentDialog
                // .axaml.cs, Enable()). Until it does, LoadFromSettings' "LockCardVoiceMode &&
                // MicConsentGiven" clears this box again on the next repaint. Fixing it belongs in
                // that dialog, not here - this control never wrote the consent flag on WPF either.
            }

            s.LockCardVoiceMode = on;
            CoreSettings.Save();
            UpdateVoiceHint();
        }

        /// <summary>Refresh the grey hint under the voice toggle to reflect mic availability.</summary>
        private void UpdateVoiceHint()
        {
            var on = ChkVoiceMode.IsChecked ?? false;
            if (!on)
            {
                TxtVoiceHint.Text = "Say the phrase out loud instead of typing it (offline mic). Falls back to typing if no mic.";
                return;
            }
            // ponytail: WPF picks one of four hints from App.Speech (IsAvailable /
            // HasCaptureDevice / ModelStatus) - ConditioningControlPanel/Services/Speech/
            // SpeechService.cs, still in the WPF head. No service means no mic, which is this one.
            TxtVoiceHint.Text = "No microphone detected — lock cards will use typing until one is connected.";
        }

        private async void BtnManagePhrases_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var s = CoreSettings.Current;

            var editor = new Dialogs.TextEditorDialog("Lock Card Phrases", s.LockCardPhrases);
            if (await editor.ShowDialog<bool?>(owner) != true || editor.ResultData == null) return;

            s.LockCardPhrases = editor.ResultData;
            CoreSettings.Save();
            Log.Information("Lock card phrases updated: {Count} items", editor.ResultData.Count);
        }

        /// <summary>
        /// The "no enabled phrases" guard is settings-backed, so it is real here; what it guards
        /// is not, yet.
        /// </summary>
        private void BtnTest_Click(object? sender, RoutedEventArgs e)
        {
            var s = CoreSettings.Current;

            var enabledPhrases = s.LockCardPhrases.Where(p => p.Value).Select(p => p.Key).ToList();
            if (enabledPhrases.Count == 0)
            {
                // ponytail: WPF says so in a MessageBox (Loc key
                // "msg_no_phrases_enabled_add_some_phrases_first"); this head has no styled
                // message dialog yet, so the refusal is logged rather than shown.
                Log.Warning("Lock card test refused: {Msg}", Loc.Get("msg_no_phrases_enabled_add_some_phrases_first"));
                return;
            }
            // ponytail: needs App.LockCard.TestLockCard() - LockCardService
            // (ConditioningControlPanel/Services/), still in the WPF head.
        }

        private async void BtnColorSettings_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            await new Dialogs.LockCardColorDialog().ShowDialog(owner);
        }
    }
}
