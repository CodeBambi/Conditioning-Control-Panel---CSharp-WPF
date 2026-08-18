using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Features
{
    public partial class LockCardFeatureControl : UserControl, ISettingsRebindable
    {
        private bool _isLoading = true; // Prevent XAML default values from overwriting settings during InitializeComponent

        public LockCardFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // Tracks WHICH AppSettings instance the hook is attached to, so a cloud restore - which
        // SWAPS the instance - can be followed instead of leaving this permanently-mounted rack
        // panel listening to, and displaying, the discarded object. See ISettingsRebindable.
        private SettingsHook? _settingsHook;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RebindToCurrentSettings();
            // The hero and side plates are mod art; the rack hosts this control permanently, so a
            // mod switch must repaint them (a popup instance never lived long enough to care).
            ApplyFeatureArt();
            if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _settingsHook?.Unhook();
            if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
        }

        /// <inheritdoc/>
        public void RebindToCurrentSettings()
        {
            (_settingsHook ??= new SettingsHook(OnSettingsPropertyChanged)).Rebind();
            LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
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
            if (e.PropertyName == nameof(Models.AppSettings.LockCardEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.LockCardFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.LockCardRepeats) ||
                e.PropertyName == nameof(Models.AppSettings.LockCardStrict) ||
                e.PropertyName == nameof(Models.AppSettings.LockCardVoiceMode))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var on = ChkEnable.IsChecked ?? false;
            s.LockCardEnabled = on;
            App.Settings?.Save();

            // Live-apply: start/stop lock card service if engine is running
            if (App.IsEngineRunning)
            {
                if (on)
                    App.LockCard?.Start();
                else
                    App.LockCard?.Stop();
            }
        }

        private void SliderFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFreq.Text = v.ToString();
            s.LockCardFrequency = v;
            App.Settings?.Save();
        }

        private void SliderRepeats_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtRepeats.Text = $"{v}x";
            s.LockCardRepeats = v;
            App.Settings?.Save();
        }

        private void ChkStrict_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var on = ChkStrict.IsChecked ?? false;
            if (on)
            {
                var owner = Application.Current.MainWindow;
                var confirmed = WarningDialog.ShowDoubleWarning(owner,
                    "Strict Lock Card",
                    "• You will NOT be able to escape lock cards with ESC\n" +
                    "• You MUST type the phrase the required number of times\n" +
                    "• This can be very restrictive!");

                if (!confirmed)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isLoading = true;
                        ChkStrict.IsChecked = false;
                        _isLoading = false;
                    }));
                    return;
                }
            }

            s.LockCardStrict = on;
            App.Settings?.Save();
        }

        private void ChkVoiceMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var on = ChkVoiceMode.IsChecked ?? false;

            // First time on: require mic consent (shared offline-audio contract). Decline => revert.
            if (on && !s.MicConsentGiven)
            {
                var dlg = new MicConsentDialog { Owner = Window.GetWindow(this) ?? Application.Current.MainWindow };
                var ok = dlg.ShowDialog() == true && dlg.ConsentGiven;
                if (!ok)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isLoading = true;
                        ChkVoiceMode.IsChecked = false;
                        _isLoading = false;
                    }));
                    return;
                }
            }

            s.LockCardVoiceMode = on;
            App.Settings?.Save();
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
            if (App.Speech?.IsAvailable == true)
                TxtVoiceHint.Text = "On — speak the phrase to dismiss the card. Typing stays available if the mic can't hear you.";
            else if (App.Speech == null || !Services.Speech.SpeechService.HasCaptureDevice)
                TxtVoiceHint.Text = "No microphone detected — lock cards will use typing until one is connected.";
            else if (App.Speech.ModelStatus == Services.Speech.SpeechModelStatus.LoadFailed)
                TxtVoiceHint.Text = "Speech model found but it would not load — remove any extra model you added under Resources\\Models\\vosk, then restart.";
            else
                TxtVoiceHint.Text = "Speech model not installed yet — lock cards will use typing until it is.";
        }

        private void BtnManagePhrases_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var editor = new TextEditorDialog("Lock Card Phrases", s.LockCardPhrases)
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            if (editor.ShowDialog() == true && editor.ResultData != null)
            {
                s.LockCardPhrases = editor.ResultData;
                App.Settings?.Save();
                App.Logger?.Information("Lock card phrases updated: {Count} items", editor.ResultData.Count);
            }
        }

        private void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            var enabledPhrases = s.LockCardPhrases.Where(p => p.Value).Select(p => p.Key).ToList();
            if (enabledPhrases.Count == 0)
            {
                MessageBox.Show(
                    Loc.Get("msg_no_phrases_enabled_add_some_phrases_first"),
                    "No Phrases",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            App.LockCard?.TestLockCard();
        }

        private void BtnColorSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new LockCardColorDialog
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            dialog.ShowDialog();
        }

        // =====================================================================================
        //  feature art (mod-aware)
        // =====================================================================================

        /// <summary>
        /// This page's art under <c>Resources/features/</c>. Verbatim the file the XAML already
        /// declares as its pack:// default on both plates - naming it here changes WHICH lookup
        /// runs, never WHICH file is asked for.
        /// </summary>
        private const string FeatureArtPath = "features/Phrase_Lock.png";

        /// <summary>
        /// Pushes the (possibly mod-overridden) feature art into the 72px hero plate and the tall
        /// side plate. Both plates author a pack:// default in XAML, so a null resolve here leaves
        /// the built-in art standing rather than blanking the plate - the same degrade rule
        /// <c>RemoteControlTabView.ApplyFeatureArt</c> follows.
        ///
        /// <para>Two widths, not one: the hero is 240px wide and the side plate is a full-height
        /// column, and <see cref="Services.ModResourceResolver.ResolveImageDecoded"/> keys its cache on the
        /// width, so each is decoded once for the whole session per mod.</para>
        ///
        /// <para>The brushes are mutated in place. Swapping the <c>Border.Background</c> object
        /// would work too and would throw away the XAML-declared Stretch/AlignmentX/Opacity with
        /// it; a frozen brush would silently never repaint at all, which is why they are named
        /// rather than declared inline as literals.</para>
        /// </summary>
        private void ApplyFeatureArt()
        {
            try
            {
                var hero = Services.ModResourceResolver.ResolveImageDecoded(FeatureArtPath, 480);
                if (hero != null && HeroArtBrush is { IsFrozen: false }) HeroArtBrush.ImageSource = hero;

                var side = Services.ModResourceResolver.ResolveImageDecoded(FeatureArtPath, 800);
                if (side != null && SideArtBrush is { IsFrozen: false }) SideArtBrush.ImageSource = side;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("LockCardFeatureControl.ApplyFeatureArt: {E}", ex.Message);
            }
        }

        /// <summary>
        /// ModChanged can be raised off the UI thread, so every body it reaches is marshalled.
        /// Subscribed on Loaded and dropped on Unloaded: the rack hosts this control
        /// PERMANENTLY, so an unbalanced hook would accumulate one dead handler per re-host.
        /// </summary>
        private void OnModChanged(object? sender, Models.ModPackage mod)
        {
            Dispatcher.BeginInvoke(new Action(ApplyFeatureArt));
        }

    }
}
