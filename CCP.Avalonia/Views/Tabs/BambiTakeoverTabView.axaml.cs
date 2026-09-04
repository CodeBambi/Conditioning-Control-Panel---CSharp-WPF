using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    /// <para><b>Dropped:</b> the mod-aware feature art (ModService.ModChanged -&gt; the resolver
    /// repainting the hero and side takeover.png plates, including the BambiSleep
    /// "bambi takeover.png" fork). ponytail: the resolver is NOT the blocker - CoreMods raises
    /// ModChanged and <c>Helpers.ModArt.TryLoad("features/bambi takeover.png")</c> resolves
    /// against art the csproj already links. The blocker is that both plates are art-less in
    /// BambiTakeoverTabView.axaml (see its header), so there is no Image to repaint; restore the
    /// markup and this handler in one change.</para>
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

            // ponytail: needs ConditioningControlPanel/Services/AutonomyService.cs (start/stop,
            // TestTrigger, TestVoiceCommand) and the premium gate behind the unlock button.
            BtnAutonomyStartStop.Click += (_, _) => { };   // mw.BtnAutonomyStartStop_Click(...)
            BtnForceStartAutonomy.Click += (_, _) => { };  // mw.BtnForceStartAutonomy_Click(...)
            BtnGateUnlock.Click += (_, _) => { };          // mw.BtnGateUnlock_Click(...)
            BtnTestAutonomy.Click += (_, _) => { };        // mw.BtnTestAutonomy_Click(...)
            BtnTestVoice.Click += (_, _) => { };           // mw.BtnTestVoice_Click(...)
            BtnOpenDeviceSettings.Click += BtnOpenDeviceSettings_Click;
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
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            SyncFromSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            base.OnDetachedFromVisualTree(e);
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
                // ponytail: WPF also calls UpdateAutonomyButtonState here (MainWindow.Autonomy.cs);
                // it reads AutonomyService's live state, which is not on this head.

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
                // ponytail: WPF's RefreshMantraChantHint swaps TxtMantraChantHint between
                // desc_mantra_chant and desc_mantra_chant_none on App.MantraChant.CanChant().
                // Needs ConditioningControlPanel/Services/MantraChantService.cs. The hint keeps its
                // {loc:Str desc_mantra_chant} binding meanwhile - a .Text write here would be undone
                // by the next language change anyway.

                // ponytail: WPF also calls RefreshAutonomyVoiceHint (MainWindow.Autonomy.cs) to
                // amber TxtAutonomyVoiceHint while the mic is being driven by wake word / PTT or the
                // speech model is missing. Needs ConditioningControlPanel/Services/Speech/SpeechService.cs.
            }
            catch (Exception ex)
            {
                Log.Debug("BambiTakeoverTabView.SyncFromSettings: {E}", ex.Message);
            }
            finally { _isLoading = false; }
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
            // ponytail: WPF also calls App.Autonomy.RefreshRandomTimer() so a running takeover picks
            // the new interval up mid-session. Needs ConditioningControlPanel/Services/AutonomyService.cs.
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
            // ponytail: WPF also calls App.Autonomy.RefreshIdleTimer() (Services/AutonomyService.cs).
            CoreSettings.Save();
        }

        private void ChkAutonomyRandom_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.AutonomyRandomTriggerEnabled = ChkAutonomyRandom.IsChecked ?? false;
            // ponytail: WPF also calls App.Autonomy.RefreshRandomTimer() (Services/AutonomyService.cs).
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

            s.AutonomyModeEnabled = enabled;
            CoreSettings.Save();
            _isLoading = true;
            ChkAutonomyEnabled.IsChecked = enabled;
            _isLoading = false;
            // ponytail: WPF then starts or stops App.Autonomy behind the Patreon / daily-free
            // check, and refuses a STOP while Lockdown is active (#514). Needs
            // Services/AutonomyService.cs and Services/LockdownService.cs, neither on this head
            // and neither seamed. Note which way that refusal cuts: it holds Takeover ON, so its
            // absence is permissive rather than dangerous - the user can always turn her off here,
            // which is the safe direction to be wrong in.
            Log.Information("Autonomy Mode toggled: {Enabled} (setting only - no service on this head)", enabled);
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
                var ok = owner != null && await dlg.ShowDialog<bool?>(owner) == true && dlg.ConsentGiven;
                if (!ok)
                {
                    _isLoading = true;
                    ChkAutonomyVoice.IsChecked = false;
                    _isLoading = false;
                    return;
                }
                // ponytail: on WPF the consent dialog itself flips AppSettings.MicConsentGiven and
                // saves; the ported one does not yet (CCP.Avalonia/Views/Dialogs/MicConsentDialog
                // .axaml.cs, Enable()). Until it does, the seed's "&& MicConsentGiven" clears this
                // box again on the next repaint. Fixing it belongs in that dialog, not here.
            }

            s.AutonomyCanTriggerVoiceCommand = on;
            CoreSettings.Save();
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

        private void BtnWallpaperFolder_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs a folder picker AND the refusal that makes it safe -
            // ConditioningControlPanel/Services/Auth/SecurityHelper.cs IsPersonalFolderRoot (#1053).
            // Every top-level image in the chosen folder becomes a wallpaper she can put on screen,
            // so this stays shut rather than opening a picker with no personal-folder guard.
        }

        private void BtnOpenDeviceSettings_Click(object? sender, RoutedEventArgs e)
        {
            // WPF: mw.OpenDeviceSettings() = ShowTab("appsettings") + AppSettingsTab.FocusSection("devices").
            // ponytail: the second half is the shell's helper, and MainShellWindow has no
            // OpenDeviceSettings yet (CCP.Avalonia/Views/Windows/MainShellWindow.Presets.cs lists
            // the stubbed navigation helpers), so this lands on the Settings door's first section.
            (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.ShowTab("appsettings");
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
