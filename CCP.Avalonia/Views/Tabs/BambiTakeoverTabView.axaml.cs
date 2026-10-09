using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// "Bambi Takeover" - the autonomy Exclusive, PORTED from
    /// ConditioningControlPanel/Views/Tabs/BambiTakeoverTabView.xaml.cs with its settings logic
    /// restored against <see cref="CoreSettings"/>.
    ///
    /// <para><b>Where the logic came from.</b> On WPF this view is almost all one-line shims into
    /// <c>MainWindow.Autonomy.cs</c>, and the seed lives in <c>MainWindow.Settings.cs</c>
    /// (LoadSettingsIntoUI, lines 155-207). Every one of those bodies that is a pure
    /// <c>App.Settings.Current.X = …; App.Settings.Save();</c> pair is here directly now - the four
    /// autonomy bars, the three trigger toggles, the fourteen-box behaviour grid, resume-on-startup,
    /// the countdown bar, the whole wallpaper block and Mantra Chant. What is left is only what
    /// needs a service, a device or a Win32 dialog, and each of those is named where it sits.</para>
    ///
    /// <para><b>_isLoading starts true</b> and the seed runs inside it, exactly as WPF's
    /// <c>_isLoading</c> gate does: Avalonia raises <c>IsCheckedChanged</c> and <c>ValueChanged</c>
    /// on a PROGRAMMATIC set, so without the guard the seed would write itself back over the user's
    /// file. Each bar paints its readout BEFORE the guard returns, which is why the numbers are
    /// honest on the first frame (#485).</para>
    ///
    /// <para><b>Re-read on every show.</b> WPF hooked <c>IsVisibleChanged</c> for Mantra Chant
    /// because panic clears <c>MantraChantEnabled</c> behind this tab's back (#685). The shell here
    /// shows a tab by flipping <c>IsVisible</c> and never re-attaches, so the whole seed re-runs on
    /// the IsVisible edge as well as on attach and on a settings-instance swap.</para>
    ///
    /// <para><b>Feature art is wired.</b> <see cref="CoreMods.ModChanged"/> is the repaint signal
    /// and <see cref="Helpers.ModArt.TryLoad"/> the resolver - mod override first, this head's
    /// linked <c>Assets/features</c> copy second - so the hero strip, the description thumb and the
    /// side plate all repaint on a mod switch, BambiSleep's "bambi takeover.png" fork included.
    /// WPF's DecodePixelWidth hints (480 / 800) have no Avalonia equivalent on a brush, so one
    /// decode feeds all three.</para>
    /// </summary>
    public partial class BambiTakeoverTabView : UserControl
    {
        /// <summary>True while the seed writes the controls, so no handler mistakes the echo for a
        /// user edit. Starts true: the .axaml gives every Slider a Value, which raises ValueChanged
        /// during InitializeComponent, before settings are read.</summary>
        private bool _isLoading = true;

        public BambiTakeoverTabView()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            BtnAutonomyStartStop.Click += BtnAutonomyStartStop_Click;
            // ponytail: Force Start is a hidden debug button on WPF too (bypasses every gate); not ported.
            BtnGateUnlock.Click += (s, e) => Shell?.BtnGateUnlock_Click(s, e);
            BtnTestAutonomy.Click += (_, _) => Shell?.TestAutonomy();
            BtnTestVoice.Click += (_, _) => Shell?.TestSpokenMantra();   // WPF TestVoiceCommand
            BtnOpenDeviceSettings.Click += (_, _) => Shell?.OpenDeviceSettings();   // Settings -> Devices
            BtnAutonomyOpenModels.Click += (_, _) => Shell?.OpenSpeechModelFolder();
            BtnWallpaperFolder.Click += BtnWallpaperFolder_Click;

            // Triggers + session toggles. WPF split these across Checked/Unchecked; Avalonia 11
            // raises one IsCheckedChanged for both edges.
            ChkAutonomyIdle.IsCheckedChanged += ChkAutonomyIdle_Changed;
            ChkAutonomyRandom.IsCheckedChanged += ChkAutonomyRandom_Changed;
            ChkAutonomyTimeAware.IsCheckedChanged += ChkAutonomyTimeAware_Changed;
            ChkAutonomyResumeOnStartup.IsCheckedChanged += ChkAutonomyResume_Changed;
            ChkAutonomyVoice.IsCheckedChanged += ChkAutonomyVoice_Changed;
            ChkShowTakeoverCountdown.IsCheckedChanged += ChkShowTakeoverCountdown_Changed;
            ChkWallpaperKeep.IsCheckedChanged += ChkWallpaperKeep_Changed;
            ChkAutonomyEnabled.IsCheckedChanged += ChkAutonomyEnabled_Changed;

            // The behaviour grid: fourteen toggles that all route to the SAME handler on WPF,
            // which rewrites every one of them on any change. Kept that way.
            foreach (var chk in new[]
                     {
                         ChkAutonomyFlash, ChkAutonomyVideo, ChkAutonomyWebVideo, ChkProtectBrowserVideo,
                         ChkAutonomySubliminal, ChkAutonomyComment, ChkAutonomyBubbles, ChkAutonomyPinkFilter,
                         ChkAutonomyLockCard, ChkAutonomyBouncingText, ChkAutonomyMindWipe, ChkAutonomyWallpaper,
                         ChkAutonomySpiral, ChkAutonomyBubbleCount,
                     })
                chk.IsCheckedChanged += ChkAutonomyBehavior_Changed;

            // Each bar paints its readout first and writes second. Formats verbatim from
            // MainWindow.Autonomy.cs - a truncating (int) cast, not Math.Round, so 59.9 reads 59.
            SliderAutonomyInterval.ValueChanged += SliderAutonomyInterval_Changed;
            SliderAutonomyCooldown.ValueChanged += SliderAutonomyCooldown_Changed;
            SliderAutonomyIntensity.ValueChanged += SliderAutonomyIntensity_Changed;
            SliderAutonomyAnnounce.ValueChanged += SliderAutonomyAnnounce_Changed;
            SliderWallpaperDuration.ValueChanged += SliderWallpaperDuration_Changed;

            // Mantra Chant (#653) is the one block whose handlers really live in this view on WPF
            // too. Its two readouts use LoadMantraChant's Math.Round format, not the bars' cast.
            SldMantraChantVolume.ValueChanged += SldMantraChantVolume_Changed;
            SldMantraChantGap.ValueChanged += SldMantraChantGap_Changed;
            ChkMantraChant.IsCheckedChanged += ChkMantraChant_Changed;

            SyncFromSettings();
            ApplyFeatureArt();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            CoreMods.ModChanged += OnModChanged;
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
            SyncFromSettings();
            ApplyFeatureArt();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            CoreMods.ModChanged -= OnModChanged;
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>The voice and chant hints are chosen in code (local value beats {loc:Str}), so a
        /// language switch re-runs the sync that picks their keys instead of leaving them stale.</summary>
        private void OnLanguageChanged(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(SyncFromSettings);

        /// <summary>ModChanged can be raised off the UI thread, so the repaint is marshalled.</summary>
        private void OnModChanged(object? sender, Models.ModPackage mod) =>
            Dispatcher.UIThread.Post(ApplyFeatureArt);

        /// <summary>
        /// WPF: MainWindow.xaml.cs:2633 picks the path and BambiTakeoverTabView.xaml.cs:87 repaints
        /// the two brushes. BambiSleep ships its own takeover art under a different name, so the
        /// fork is on the ACTIVE MOD ID, not on the resolver.
        /// </summary>
        private static string TakeoverArtPath =>
            string.Equals(CoreMods.ActiveModId, Models.BuiltInMods.BambiSleepId, StringComparison.OrdinalIgnoreCase)
                ? "features/bambi takeover.png"
                : "features/takeover.png";

        /// <summary>
        /// Repaints the hero strip, the description thumb and the side plate. A null resolve is
        /// left alone deliberately, exactly as the WPF version does: the plate degrades to its
        /// authored wash + glyph rather than to an empty rectangle.
        /// </summary>
        private void ApplyFeatureArt()
        {
            var art = Helpers.ModArt.TryLoad(TakeoverArtPath);
            if (art == null) return;

            TakeoverHeroArt.Background = new global::Avalonia.Media.ImageBrush(art)
            {
                Stretch = global::Avalonia.Media.Stretch.UniformToFill,
                AlignmentX = global::Avalonia.Media.AlignmentX.Right,
            };
            TakeoverSideArt.Background = new global::Avalonia.Media.ImageBrush(art)
            {
                Stretch = global::Avalonia.Media.Stretch.UniformToFill,
            };
            ImgBambiTakeoverDesc.Source = art;
        }

        /// <summary>The shell shows a tab by flipping IsVisible, never by re-attaching. This is the
        /// Avalonia twin of WPF's <c>IsVisibleChanged -&gt; LoadMantraChant()</c>: re-read on every
        /// show, or the toggles lie after panic disarmed something behind our back (#685).</summary>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && change.GetNewValue<bool>()) SyncFromSettings();
        }

        // A cloud restore or a factory reset swaps the instance; repaint from it, on the UI thread.
        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(SyncFromSettings);

        // =====================================================================================
        //  seed - MainWindow.Settings.cs LoadSettingsIntoUI, the Autonomy Mode block
        // =====================================================================================

        internal void SyncFromSettings()
        {
            _isLoading = true;
            try
            {
                var s = CoreSettings.Current;

                ChkAutonomyEnabled.IsChecked = s.AutonomyModeEnabled;

                // Clamp persisted values into the bars' ranges BEFORE assigning: an out-of-range
                // stored value (old-version scale, cloud-restored settings) would silently snap the
                // bar while the label kept its XAML default (#485). Write the clamped value back so
                // the setting and the UI agree.
                s.AutonomyIntensity = Math.Clamp(s.AutonomyIntensity,
                    (int)SliderAutonomyIntensity.Minimum, (int)SliderAutonomyIntensity.Maximum);
                s.AutonomyCooldownSeconds = Math.Clamp(s.AutonomyCooldownSeconds,
                    (int)SliderAutonomyCooldown.Minimum, (int)SliderAutonomyCooldown.Maximum);
                s.AutonomyRandomIntervalSeconds = Math.Clamp(s.AutonomyRandomIntervalSeconds,
                    (int)SliderAutonomyInterval.Minimum, (int)SliderAutonomyInterval.Maximum);
                s.AutonomyAnnouncementChance = Math.Clamp(s.AutonomyAnnouncementChance, 0, 100);

                SliderAutonomyIntensity.Value = s.AutonomyIntensity;
                SliderAutonomyCooldown.Value = s.AutonomyCooldownSeconds;
                SliderAutonomyInterval.Value = s.AutonomyRandomIntervalSeconds;
                SliderAutonomyAnnounce.Value = s.AutonomyAnnouncementChance;

                ChkAutonomyIdle.IsChecked = s.AutonomyIdleTriggerEnabled;
                ChkAutonomyRandom.IsChecked = s.AutonomyRandomTriggerEnabled;
                ChkAutonomyTimeAware.IsChecked = s.AutonomyTimeAwareEnabled;

                ChkAutonomyFlash.IsChecked = s.AutonomyCanTriggerFlash;
                ChkAutonomyVideo.IsChecked = s.AutonomyCanTriggerVideo;
                ChkAutonomyWebVideo.IsChecked = s.AutonomyCanTriggerWebVideo;
                ChkProtectBrowserVideo.IsChecked = s.ProtectBrowserVideoPlayback;
                ChkAutonomySubliminal.IsChecked = s.AutonomyCanTriggerSubliminal;
                ChkAutonomyBubbles.IsChecked = s.AutonomyCanTriggerBubbles;
                ChkAutonomyComment.IsChecked = s.AutonomyCanComment;
                ChkAutonomyMindWipe.IsChecked = s.AutonomyCanTriggerMindWipe;
                ChkAutonomyLockCard.IsChecked = s.AutonomyCanTriggerLockCard;
                ChkAutonomySpiral.IsChecked = s.AutonomyCanTriggerSpiral;
                ChkAutonomyPinkFilter.IsChecked = s.AutonomyCanTriggerPinkFilter;
                ChkAutonomyBouncingText.IsChecked = s.AutonomyCanTriggerBouncingText;
                ChkAutonomyBubbleCount.IsChecked = s.AutonomyCanTriggerBubbleCount;
                ChkAutonomyWallpaper.IsChecked = s.AutonomyCanTriggerWallpaper;

                RefreshWallpaperBlock(s);

                // The mic gate is part of the seed on WPF too: an armed voice toggle with no
                // consent on file reads OFF rather than claiming a microphone it may not open.
                ChkAutonomyVoice.IsChecked = s.AutonomyCanTriggerVoiceCommand && s.MicConsentGiven;
                ChkAutonomyResumeOnStartup.IsChecked = s.AutonomyResumeOnStartup;
                ChkShowTakeoverCountdown.IsChecked = s.ShowTakeoverCountdownBar;

                // The bars' handlers paint their labels themselves, but only for a value that
                // actually changed; set all four explicitly so a seed that lands on the current
                // value still leaves the number and the bar agreeing (#485).
                TxtAutonomyIntensity.Text = $"{s.AutonomyIntensity}";
                TxtAutonomyCooldown.Text = $"{s.AutonomyCooldownSeconds}s";
                TxtAutonomyInterval.Text = $"{s.AutonomyRandomIntervalSeconds}s";
                TxtAutonomyAnnounce.Text = $"{s.AutonomyAnnouncementChance}%";

                // Mantra Chant, WPF's LoadMantraChant.
                ChkMantraChant.IsChecked = s.MantraChantEnabled;
                SldMantraChantVolume.Value = s.MantraChantVolume;
                TxtMantraChantVolume.Text = $"{(int)Math.Round(s.MantraChantVolume)}%";
                SldMantraChantGap.Value = s.MantraChantGapSeconds;
                TxtMantraChantGap.Text = $"{s.MantraChantGapSeconds}s";
                // WPF RefreshMantraChantHint: a mod with no voiced mantras can't chant, so say so.
                // CanChant() is MantraVoice.HasVoicedMantras() on WPF too.
                TxtMantraChantHint.Text = Loc.Get(App.MantraVoice.HasVoicedMantras() ? "desc_mantra_chant" : "desc_mantra_chant_none");

                RefreshAutonomyVoiceHint();
            }
            catch (Exception ex)
            {
                Log.Debug("BambiTakeoverTabView.SyncFromSettings: {E}", ex.Message);
            }
            finally { _isLoading = false; }
        }

        /// <summary>WPF MainWindow.Autonomy.cs:501 RefreshAutonomyVoiceHint: amber while the surprise
        /// mantras cannot run (no mic, model missing or broken) or are paused by wake word / PTT. The
        /// "Open models folder" button is up only while the model is the problem.</summary>
        internal void RefreshAutonomyVoiceHint()
        {
            var s = CoreSettings.Current;
            var on = s.AutonomyCanTriggerVoiceCommand && s.MicConsentGiven;
            BtnAutonomyOpenModels.IsVisible = on && Windows.MainShellWindow.SpeechModelIsTheProblem();
            var amber = on && (!CoreSpeech.IsAvailable || s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled);
            TxtAutonomyVoiceHint.Foreground = new global::Avalonia.Media.SolidColorBrush(amber
                ? global::Avalonia.Media.Color.FromRgb(0xFF, 0xC1, 0x07)
                : global::Avalonia.Media.Color.FromRgb(0x88, 0x88, 0x88));
            TxtAutonomyVoiceHint.Text = Loc.Get(!on ? "takeover_voice_hint_off"
                : !CoreSpeech.IsAvailable
                    ? (!CoreSpeech.HasCaptureDevice ? "takeover_voice_hint_no_mic"
                        : CoreSpeech.ModelStatus == CoreSpeechModelStatus.LoadFailed ? "takeover_voice_hint_model_failed"
                        : "takeover_voice_hint_model_missing")
                : amber ? "takeover_voice_hint_paused_mic" : "takeover_voice_hint_on");
        }

        /// <summary>WPF's RefreshWallpaperFolderLabel + RefreshWallpaperDurationVisibility.</summary>
        private void RefreshWallpaperBlock(Models.AppSettings s)
        {
            TxtWallpaperFolder.Text = string.IsNullOrWhiteSpace(s.WallpaperSourceFolder)
                ? Loc.Get("label_wallpaper_folder_default")
                : s.WallpaperSourceFolder;

            ChkWallpaperKeep.IsChecked = s.WallpaperEnabled;
            // Clamp before assigning, like the other Takeover bars (#485).
            s.WallpaperPulseSeconds = Math.Clamp(s.WallpaperPulseSeconds,
                (int)SliderWallpaperDuration.Minimum, (int)SliderWallpaperDuration.Maximum);
            SliderWallpaperDuration.Value = s.WallpaperPulseSeconds;
            TxtWallpaperDuration.Text = $"{s.WallpaperPulseSeconds}s";

            // The pulse duration is meaningless while "keep it" is on - hide it rather than lie.
            PanelWallpaperDuration.IsVisible = !s.WallpaperEnabled;
        }

        // =====================================================================================
        //  bars
        // =====================================================================================

        private void SliderAutonomyIntensity_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            TxtAutonomyIntensity.Text = $"{(int)e.NewValue}";
            if (_isLoading) return;
            CoreSettings.Current.AutonomyIntensity = (int)e.NewValue;
            CoreSettings.Save();
        }

        private void SliderAutonomyCooldown_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            TxtAutonomyCooldown.Text = $"{(int)e.NewValue}s";
            if (_isLoading) return;
            CoreSettings.Current.AutonomyCooldownSeconds = (int)e.NewValue;
            CoreSettings.Save();
        }

        private void SliderAutonomyInterval_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            TxtAutonomyInterval.Text = $"{(int)e.NewValue}s";
            if (_isLoading) return;
            CoreSettings.Current.AutonomyRandomIntervalSeconds = (int)e.NewValue;
            Shell?.Autonomy.RefreshRandomTimer();
            CoreSettings.Save();
        }

        private void SliderAutonomyAnnounce_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            TxtAutonomyAnnounce.Text = $"{(int)e.NewValue}%";
            if (_isLoading) return;
            CoreSettings.Current.AutonomyAnnouncementChance = (int)e.NewValue;
            CoreSettings.Save();
        }

        private void SliderWallpaperDuration_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            TxtWallpaperDuration.Text = $"{(int)e.NewValue}s";
            if (_isLoading) return;
            CoreSettings.Current.WallpaperPulseSeconds = (int)e.NewValue;
            CoreSettings.Save();
        }

        // =====================================================================================
        //  triggers and session toggles
        // =====================================================================================

        private void ChkAutonomyIdle_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.AutonomyIdleTriggerEnabled = ChkAutonomyIdle.IsChecked ?? false;
            Shell?.Autonomy.RefreshIdleTimer();
            CoreSettings.Save();
        }

        private void ChkAutonomyRandom_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.AutonomyRandomTriggerEnabled = ChkAutonomyRandom.IsChecked ?? false;
            Shell?.Autonomy.RefreshRandomTimer();
            CoreSettings.Save();
        }

        private void ChkAutonomyTimeAware_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.AutonomyTimeAwareEnabled = ChkAutonomyTimeAware.IsChecked ?? false;
            CoreSettings.Save();
        }

        private void ChkAutonomyResume_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.AutonomyResumeOnStartup = ChkAutonomyResumeOnStartup.IsChecked == true;
            CoreSettings.Save();
        }

        private void ChkShowTakeoverCountdown_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.ShowTakeoverCountdownBar = ChkShowTakeoverCountdown.IsChecked == true;
            CoreSettings.Save();
        }

        /// <summary>
        /// One handler, every box, exactly as WPF does it: the grid is rewritten whole on any
        /// change, so a box set from somewhere else can never be left behind.
        /// </summary>
        private void ChkAutonomyBehavior_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            s.AutonomyCanTriggerFlash = ChkAutonomyFlash.IsChecked ?? false;
            s.AutonomyCanTriggerVideo = ChkAutonomyVideo.IsChecked ?? false;
            s.AutonomyCanTriggerWebVideo = ChkAutonomyWebVideo.IsChecked ?? false;
            s.ProtectBrowserVideoPlayback = ChkProtectBrowserVideo.IsChecked ?? false;
            // Takeover has no strictness toggle of its own: a Takeover video is a plain mandatory
            // video and follows the global StrictLockEnabled flag, which carries its own warning.
            s.AutonomyCanTriggerSubliminal = ChkAutonomySubliminal.IsChecked ?? false;
            s.AutonomyCanTriggerBubbles = ChkAutonomyBubbles.IsChecked ?? false;
            s.AutonomyCanComment = ChkAutonomyComment.IsChecked ?? false;
            s.AutonomyCanTriggerMindWipe = ChkAutonomyMindWipe.IsChecked ?? false;
            s.AutonomyCanTriggerLockCard = ChkAutonomyLockCard.IsChecked ?? false;
            s.AutonomyCanTriggerSpiral = ChkAutonomySpiral.IsChecked ?? false;
            s.AutonomyCanTriggerPinkFilter = ChkAutonomyPinkFilter.IsChecked ?? false;
            s.AutonomyCanTriggerBouncingText = ChkAutonomyBouncingText.IsChecked ?? false;
            s.AutonomyCanTriggerBubbleCount = ChkAutonomyBubbleCount.IsChecked ?? false;
            s.AutonomyCanTriggerWallpaper = ChkAutonomyWallpaper.IsChecked ?? false;
            CoreSettings.Save();
        }

        /// <summary>
        /// The master switch. WPF gates it on a consent dialog, the Patreon check and the lockdown
        /// refusal before it starts AutonomyService. The consent gate is the one of those three
        /// that is real here, and it is the one that matters: the setting is not written until the
        /// answer is in, so no path can arm her on a "yes" that never came.
        ///
        /// <para><b>Async, so the order is deliberate.</b> The WPF box was synchronous and the
        /// checkbox stayed visibly on across it; here nothing is written before the await, and the
        /// box is put back under <see cref="_isLoading"/> AFTER the answer either way — a repaint
        /// during the modal cannot leave it disagreeing with the setting.</para>
        /// </summary>
        private async void ChkAutonomyEnabled_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var enabled = ChkAutonomyEnabled.IsChecked ?? false;
            if (s.AutonomyModeEnabled == enabled) return;

            if (enabled && !s.AutonomyConsentGiven)
            {
                // The WPF text verbatim (MainWindow.Autonomy.cs:55) - an English literal there
                // too, with no loc key, so nothing is invented here.
                var owner = TopLevel.GetTopLevel(this) as Window;
                var consented = owner != null && await Dialogs.MessageDialog.ConfirmAsync(owner,
                    "Enable Autonomy Mode",
                    "AUTONOMY MODE\n\n" +
                    "This feature allows the companion to autonomously trigger effects:\n" +
                    "• Flash images\n" +
                    "• Videos\n" +
                    "• Subliminal messages\n" +
                    "• Make comments\n\n" +
                    "She will act on her own within your configured intensity settings.\n\n" +
                    "You can disable this at any time. Videos triggered autonomously are skippable " +
                    "unless you explicitly enable Strict Videos in the Takeover settings.\n\n" +
                    "Do you consent to enable Autonomy Mode?");

                // No owner is not a "yes". A headless host cannot ask, so it refuses, exactly as
                // the mic-consent flow below does.
                if (!consented)
                {
                    _isLoading = true;
                    ChkAutonomyEnabled.IsChecked = false;
                    _isLoading = false;
                    Log.Information("Takeover master switch refused: consent declined or no window to ask in");
                    return;
                }

                s.AutonomyConsentGiven = true;
            }

            // WPF: Lockdown refuses a running stop (#514), no premium saves but does not start.
            var shown = Shell?.SetAutonomyEnabled(enabled) ?? enabled;
            if (Shell == null) { s.AutonomyModeEnabled = enabled; CoreSettings.Save(); }
            _isLoading = true;
            ChkAutonomyEnabled.IsChecked = shown;
            _isLoading = false;
        }

        private Windows.MainShellWindow? Shell => TopLevel.GetTopLevel(this) as Windows.MainShellWindow;

        /// <summary>WPF BtnAutonomyStartStop_Click: its own consent text (MainWindow.Autonomy.cs:143), then the toggle.</summary>
        private async void BtnAutonomyStartStop_Click(object? sender, RoutedEventArgs e)
        {
            var s = CoreSettings.Current;
            var turningOn = !s.AutonomyModeEnabled;
            if (turningOn && !s.AutonomyConsentGiven)
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                if (owner == null || !await Dialogs.MessageDialog.ConfirmAsync(owner, "Autonomy Mode Consent",
                        "AUTONOMY MODE\n\n" +
                        "This feature allows the companion to autonomously trigger effects:\n" +
                        "• Flash images\n" +
                        "• Videos (skippable unless you enable Strict Videos)\n" +
                        "• Subliminal messages\n" +
                        "• Make comments\n\n" +
                        "She will act on her own schedule based on your intensity setting.\n" +
                        "You can stop her at any time by clicking the Stop button.\n\n" +
                        "Do you consent to enabling Autonomy Mode?"))
                    return;
                s.AutonomyConsentGiven = true;
            }
            var shown = Shell?.SetAutonomyEnabled(turningOn) ?? false;
            _isLoading = true;
            ChkAutonomyEnabled.IsChecked = shown;
            _isLoading = false;
        }

        /// <summary>
        /// First time on, the surprise-mantra prompt needs mic consent - the shared offline-audio
        /// contract, the same flow LockCardFeatureControl uses. Decline reverts the box and writes
        /// nothing.
        /// </summary>
        private async void ChkAutonomyVoice_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkAutonomyVoice.IsChecked == true;
            if (s.AutonomyCanTriggerVoiceCommand == on) return;

            if (on && !s.MicConsentGiven)
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                var dlg = new Dialogs.MicConsentDialog();
                var ok = owner != null && await dlg.ShowDialogSafe<bool?>(owner) == true && dlg.ConsentGiven;
                if (!ok)
                {
                    _isLoading = true;
                    ChkAutonomyVoice.IsChecked = false;
                    _isLoading = false;
                    return;
                }
            }

            s.AutonomyCanTriggerVoiceCommand = on;
            CoreSettings.Save();
            RefreshAutonomyVoiceHint();
        }

        // =====================================================================================
        //  wallpaper
        // =====================================================================================

        /// <summary>
        /// "Keep the wallpaper": her changes stay on the desktop instead of reverting after a few
        /// seconds (#694). The pulse bar is meaningless while it is on, so it hides.
        /// </summary>
        private void ChkWallpaperKeep_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var keep = ChkWallpaperKeep.IsChecked ?? false;
            if (s.WallpaperEnabled == keep) return;

            s.WallpaperEnabled = keep;
            CoreSettings.Save();
            PanelWallpaperDuration.IsVisible = !keep;
            // ponytail: WPF also puts the original wallpaper straight back when this goes off
            // (App.Wallpaper.Deactivate) and warns on an empty library when it goes on
            // (MainWindow.StartStop.cs WarnIfWallpaperLibraryEmpty). Needs
            // ConditioningControlPanel/Services/WallpaperService.cs, which is Win32.
        }

        /// <summary>WPF MainWindow.Autonomy.cs:329. The folder is saved for Takeover's wallpaper
        /// action, which this head does not perform yet (no wallpaper service), so it is inert here.</summary>
        private async void BtnWallpaperFolder_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            try
            {
                var s = CoreSettings.Current;
                var storage = owner.StorageProvider;
                var start = !string.IsNullOrWhiteSpace(s.WallpaperSourceFolder) && System.IO.Directory.Exists(s.WallpaperSourceFolder)
                    ? s.WallpaperSourceFolder
                    : System.IO.Path.Combine(CorePaths.EffectiveAssets, "wallpapers");
                var picked = await storage.OpenFolderPickerAsync(new global::Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Pick the folder she pulls desktop wallpapers from",
                    SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(start),
                });
                if (picked.Count != 1 || picked[0].TryGetLocalPath() is not { } path) return;

                // #1053: every top-level image in it becomes a wallpaper she can put on screen.
                if (Windows.MainShellWindow.IsPersonalFolderRoot(path))
                {
                    await Dialogs.MessageDialog.ShowAsync(owner, "Pick a folder of your own",
                        "That folder is one of your system's own - your Desktop, Documents, Pictures, Downloads, " +
                        "your home folder or a whole drive." + Environment.NewLine + Environment.NewLine +
                        "Every image sitting in it would become a wallpaper she can put on your screen, so pick " +
                        "a folder you filled with wallpapers on purpose instead.");
                    return;
                }
                s.WallpaperSourceFolder = path;
                CoreSettings.Save();
                RefreshWallpaperBlock(s);
            }
            catch (Exception ex) { Log.Warning(ex, "Wallpaper folder pick failed"); }
        }

        // =====================================================================================
        //  Mantra Chant (#653) - the one block whose handlers live in this view on WPF too
        // =====================================================================================

        private void ChkMantraChant_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkMantraChant.IsChecked == true;
            if (s.MantraChantEnabled == on) return;
            s.MantraChantEnabled = on;
            CoreSettings.Save();
            // ponytail: WPF then calls App.MantraChant.Start()/Stop() and re-reads the hint. Needs
            // ConditioningControlPanel/Services/MantraChantService.cs.
        }

        private void SldMantraChantVolume_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            TxtMantraChantVolume.Text = $"{(int)Math.Round(e.NewValue)}%";
            if (_isLoading) return;
            CoreSettings.Current.MantraChantVolume = e.NewValue;
            CoreSettings.Save();
            // ponytail: WPF also live-applies to a clip already playing (App.MantraChant.ApplyVolume).
        }

        private void SldMantraChantGap_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            var seconds = (int)Math.Round(e.NewValue);
            TxtMantraChantGap.Text = $"{seconds}s";
            if (_isLoading) return;
            CoreSettings.Current.MantraChantGapSeconds = seconds;
            CoreSettings.Save();
        }
    }
}
