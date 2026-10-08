using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
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
    /// <para>Start/stop/test are live now. The schedule is <see cref="LockCardScheduler"/> in Core;
    /// showing a card goes through <see cref="CoreLockCard"/>, which this head seeds onto
    /// <see cref="Windows.LockCardWindow.ShowOnAllMonitors"/>. The enable toggle keeps WPF's gate,
    /// so with <see cref="CoreSession"/> unseeded here it never arms - Test is what puts a real card
    /// on screen on this head.</para>
    ///
    /// <para>Still head-side: the mod-aware feature art. The voice hint asks
    /// <see cref="CoreSpeech"/> for all four of its branches; with no speech service seeded on this
    /// head it reports no capture device, so the "on" branch lands on "No microphone detected",
    /// which is the honest answer here.</para>
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
            ChkRandomRepeats.IsCheckedChanged += (_, _) => SaveRepeatShape();
            ChkTargetLength.IsCheckedChanged += (_, _) => SaveRepeatShape();
            SliderRepeatsMin.ValueChanged += (_, _) => SaveRepeatShape();
            SliderTargetLength.ValueChanged += (_, _) => SaveRepeatShape();
            SliderTargetVariance.ValueChanged += (_, _) => SaveRepeatShape();
            ChkStrict.IsCheckedChanged += ChkStrict_Changed;
            ChkVoiceMode.IsCheckedChanged += ChkVoiceMode_Changed;
            ChkResetOnTypo.IsCheckedChanged += ChkResetOnTypo_Changed;
            BtnManagePhrases.Click += BtnManagePhrases_Click;
            BtnTest.Click += BtnTest_Click;
            BtnColorSettings.Click += BtnColorSettings_Click;

            Loaded += (_, _) => RebindToCurrentSettings();
            Unloaded += (_, _) => Unhook();

            Helpers.ModArt.BindFeaturePlates(this, "features/Phrase_Lock.png", HeroArt, SideArt);

            RebindToCurrentSettings();
        }

        /// <summary>Re-points the settings hook at the live instance and repaints from it.</summary>
        public void RebindToCurrentSettings()
        {
            Unhook();
            // Hidden rack controls can be constructed without ever loading/unloading.
            if (IsLoaded)
            {
                _hooked = CoreSettings.Current;
                _hooked.PropertyChanged += OnSettingsPropertyChanged;
            }
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
                ChkRandomRepeats.IsChecked = s.LockCardRandomRepeats;
                SliderRepeatsMin.Value = s.LockCardRepeatsMin;
                ChkTargetLength.IsChecked = s.LockCardTargetLengthEnabled;
                SliderTargetLength.Value = s.LockCardTargetLength;
                SliderTargetVariance.Value = s.LockCardTargetLengthVariance;
                UpdateRepeatShapeRows();
                ChkStrict.IsChecked = s.LockCardStrict;
                ChkVoiceMode.IsChecked = s.LockCardVoiceMode && s.MicConsentGiven;
                ChkResetOnTypo.IsChecked = s.LockCardResetOnTypo;
                if (s.LockCardResetOnTypo) FoldMore.IsOpen = true; // never hide a changed setting (WPF :71)
                UpdateVoiceHint();
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.LockCardEnabled) ||
                e.PropertyName == nameof(AppSettings.LockCardFrequency) ||
                e.PropertyName == nameof(AppSettings.LockCardRepeats) ||
                e.PropertyName == nameof(AppSettings.LockCardRandomRepeats) ||
                e.PropertyName == nameof(AppSettings.LockCardRepeatsMin) ||
                e.PropertyName == nameof(AppSettings.LockCardTargetLengthEnabled) ||
                e.PropertyName == nameof(AppSettings.LockCardTargetLength) ||
                e.PropertyName == nameof(AppSettings.LockCardTargetLengthVariance) ||
                e.PropertyName == nameof(AppSettings.LockCardStrict) ||
                e.PropertyName == nameof(AppSettings.LockCardResetOnTypo) ||
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

            // Live-apply, the same shape and the same gate as WPF: start or stop the schedule only
            // while the engine is running (CoreSession seeded from CoreEngine on this head).
            if (CoreSession.IsEngineRunning)
            {
                if (on) LockCardScheduler.Instance.Start();
                else LockCardScheduler.Instance.Stop();
            }
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

        private void ChkResetOnTypo_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkResetOnTypo.IsChecked ?? false;
            if (s.LockCardResetOnTypo == on) return;
            s.LockCardResetOnTypo = on;
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

        /// <summary>Writes the repeat-shape switches and dials (WPF ChkRandomRepeats_Changed ..
        /// SliderTargetVariance_Changed, LockCardFeatureControl.xaml.cs:134-184), saving only on change.</summary>
        private void SaveRepeatShape()
        {
            UpdateRepeatShapeRows();
            if (_isLoading) return;
            var s = CoreSettings.Current;
            bool random = ChkRandomRepeats.IsChecked ?? false, byLength = ChkTargetLength.IsChecked ?? false;
            int min = (int)SliderRepeatsMin.Value, len = (int)SliderTargetLength.Value, vary = (int)SliderTargetVariance.Value;
            if (s.LockCardRandomRepeats == random && s.LockCardTargetLengthEnabled == byLength &&
                s.LockCardRepeatsMin == min && s.LockCardTargetLength == len && s.LockCardTargetLengthVariance == vary) return;
            s.LockCardRandomRepeats = random;
            s.LockCardTargetLengthEnabled = byLength;
            s.LockCardRepeatsMin = min;
            s.LockCardTargetLength = len;
            s.LockCardTargetLengthVariance = vary;
            CoreSettings.Save();
        }

        /// <summary>WPF UpdateRepeatRowVisibility (:197-207): length mode derives the count, so it hides
        /// the flat Repeats row and the random switch; the same precedence as
        /// <see cref="LockCardScheduler.ResolveRepeats"/>.</summary>
        private void UpdateRepeatShapeRows()
        {
            var byLength = ChkTargetLength.IsChecked ?? false;
            var random = ChkRandomRepeats.IsChecked ?? false;
            RowRepeats.IsVisible = !byLength;
            RowRandomRepeats.IsVisible = !byLength;
            RowRepeatsMin.IsVisible = !byLength && random;
            RowTargetLength.IsVisible = byLength;
            RowTargetVariance.IsVisible = byLength;
            TxtRepeatsMin.Text = $"{(int)SliderRepeatsMin.Value}x";
            TxtTargetLength.Text = ((int)SliderTargetLength.Value).ToString();
            TxtTargetVariance.Text = $"\u00B1{(int)SliderTargetVariance.Value}";
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
                var ok = owner != null && await dlg.ShowDialogSafe<bool?>(owner) == true && dlg.ConsentGiven;
                if (!ok)
                {
                    _isLoading = true;
                    ChkVoiceMode.IsChecked = false;
                    _isLoading = false;
                    return;
                }
                // The dialog's own Enable() writes and saves MicConsentGiven, as WPF's did, so
                // nothing to persist here - this control never wrote the consent flag on WPF.
            }

            s.LockCardVoiceMode = on;
            CoreSettings.Save();
            UpdateVoiceHint();
        }

        /// <summary>Refresh the grey hint under the voice toggle to reflect mic availability.</summary>
        private void UpdateVoiceHint() => TxtVoiceHint.Text = VoiceHint(ChkVoiceMode.IsChecked ?? false);

        /// <summary>WPF's four hints (LockCardFeatureControl.xaml.cs:269-285) from CoreSpeech. The two
        /// model hints name the Linux folder: no model ships on this head, and Resources/Models/vosk
        /// sits inside a read-only install.</summary>
        internal static string VoiceHint(bool on)
        {
            if (!on)
                return "Say the phrase out loud instead of typing it (offline mic). Falls back to typing if no mic.";
            var folder = System.IO.Path.Combine(CorePaths.UserData, "Models", "vosk");
            if (CoreSpeech.IsAvailable)
                return "On — speak the phrase to dismiss the card. Typing stays available if the mic can't hear you.";
            if (!CoreSpeech.HasCaptureDevice)
                return "No microphone detected — lock cards will use typing until one is connected.";
            if (CoreSpeech.ModelStatus == CoreSpeechModelStatus.LoadFailed)
                return $"Speech model found but it would not load — remove any extra model you added under {folder}, then restart.";
            return $"Speech model not installed yet — lock cards will use typing until it is. Unzip vosk-model-small-en-us-0.15 into {folder}";
        }

        private async void BtnManagePhrases_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var s = CoreSettings.Current;

            var editor = new Dialogs.TextEditorDialog("Lock Card Phrases", s.LockCardPhrases);
            if (await editor.ShowDialogSafe<bool?>(owner) != true || editor.ResultData == null) return;

            s.LockCardPhrases = editor.ResultData;
            CoreSettings.Save();
            Log.Information("Lock card phrases updated: {Count} items", editor.ResultData.Count);
        }

        /// <summary>Shows a real test card, through the same surface seam the scheduler uses.</summary>
        private async void BtnTest_Click(object? sender, RoutedEventArgs e)
        {
            if (LockCardScheduler.EnabledPhrases().Count == 0)
            {
                // WPF's MessageBox.Show(…, "No Phrases", OK, Warning), through this head's twin.
                if (TopLevel.GetTopLevel(this) is Window owner)
                    await Dialogs.MessageDialog.ShowAsync(
                        owner, "No Phrases", Loc.Get("msg_no_phrases_enabled_add_some_phrases_first"));
                return;
            }
            CoreLockCard.Show(isTest: true);
        }

        private async void BtnColorSettings_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            await new Dialogs.LockCardColorDialog().ShowDialogSafe(owner);
        }
    }
}
