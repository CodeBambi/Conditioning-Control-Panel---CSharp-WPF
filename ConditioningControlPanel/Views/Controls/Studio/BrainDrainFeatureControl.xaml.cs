using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Studio
{
    /// <summary>
    /// Phase 4 rescue of gap-report G2 — the Studio rack's Brain Drain panel.
    ///
    /// Behaviour is copied verbatim from the dead handlers in
    /// <c>MainWindow/MainWindow.LevelFeatures.cs:282-334</c> (which were never wired to any XAML,
    /// so they have only ever been a specification). The single substitution is
    /// <c>App.IsEngineRunning</c> for MainWindow's private <c>_isRunning</c> — the same field,
    /// exposed as <c>MainWindow.IsEngineRunning</c> and mirrored onto App by StartStop.
    ///
    /// Everything here drives <see cref="Services.BrainDrainService"/> (audio) only. The blur half
    /// is withheld behind <c>OverlayService.BrainDrainWithheld</c>; this panel reads that flag and
    /// nothing else, so it can never disagree with the gate.
    /// </summary>
    public partial class BrainDrainFeatureControl : UserControl, Features.ISettingsRebindable
    {
        private bool _isLoading = true;

        public BrainDrainFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // Tracks WHICH AppSettings instance the hook is attached to, so a cloud restore - which
        // SWAPS the instance - can be followed instead of leaving this permanently-mounted rack
        // panel listening to, and displaying, the discarded object. See ISettingsRebindable.
        private Features.SettingsHook? _settingsHook;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplyWithheldPresentation();
            // Re-scan before painting the readout: the rack hosts this control permanently, so
            // without this the count shown is whatever the folder held at app launch and a clip
            // dropped in since would read as "0 clips" forever.
            try { App.BrainDrain?.ReloadAudioFiles(); } catch { }
            RebindToCurrentSettings();
            // The hero and side plates are mod art; the rack hosts this control permanently, so a
            // mod switch must repaint them (a popup instance never lived long enough to care).
            ApplyFeatureArt();
            if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
            // The armed banner has to be right even when this panel is opened mid-wait, so it is
            // painted here as well as on every later state change.
            if (App.BrainDrain != null) App.BrainDrain.OnsetStateChanged += OnOnsetStateChanged;
            RefreshOnsetState();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _settingsHook?.Unhook();
            if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
            if (App.BrainDrain != null) App.BrainDrain.OnsetStateChanged -= OnOnsetStateChanged;
        }

        /// <summary>
        /// ModChanged can be raised off the UI thread, so every body it reaches is marshalled.
        /// Subscribed on Loaded and dropped on Unloaded: the rack hosts this control
        /// PERMANENTLY, so an unbalanced hook would accumulate one dead handler per re-host.
        /// </summary>
        private void OnModChanged(object? sender, ConditioningControlPanel.Models.ModPackage mod)
        {
            Dispatcher.BeginInvoke(new Action(ApplyFeatureArt));
        }

        /// <inheritdoc/>
        public void RebindToCurrentSettings()
        {
            (_settingsHook ??= new Features.SettingsHook(OnSettingsPropertyChanged)).Rebind();
            LoadFromSettings();
        }

        /// <summary>
        /// The rework notice is the honest half of this panel: while
        /// <c>OverlayService.BrainDrainWithheld</c> is true the screen effect is silently skipped
        /// for every caller, so the copy has to say so. Read once per load rather than cached —
        /// the flag is <c>static readonly</c>, not <c>const</c>, precisely so the surrounding code
        /// stays reachable when it flips.
        /// </summary>
        private void ApplyWithheldPresentation()
        {
            WithheldNotice.Visibility = ConditioningControlPanel.Services.OverlayService.BrainDrainWithheld
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.BrainDrainEnabled;
                SliderIntensity.Value = s.BrainDrainIntensity;
                TxtIntensity.Text = $"{s.BrainDrainIntensity}%";
                ChkHighRefresh.IsChecked = s.BrainDrainHighRefresh;
                SliderBlurStrength.Value = s.BrainDrainBlurStrength;
                TxtBlurStrength.Text = $"{s.BrainDrainBlurStrength}%";
                ChkMelt.IsChecked = s.BrainDrainMeltEnabled;
                ChkAllowCapture.IsChecked = s.AllowOverlayCapture;
                SliderRandomStart.Value = s.BrainDrainRandomStartMaxMinutes;
                TxtRandomStart.Text = FormatRandomStart(s.BrainDrainRandomStartMaxMinutes);

                RefreshClipCount();
                RefreshOnsetState();
            }
            finally { _isLoading = false; }
        }

        /// <summary>
        /// Repaint the clip-library readout. An empty clip pool makes the whole audio half a silent
        /// no-op (the service warns to the log and returns), so the count is shown even when it is
        /// fine - "0 clips" is the answer to "why is nothing happening?" and it is the number the
        /// user watches change after dropping a file in and hitting Refresh.
        /// <para>The count is the MERGED pool: the assets folder plus whatever is still sitting in
        /// the old install-directory folder, de-duped by file name.</para>
        /// </summary>
        private void RefreshClipCount()
        {
            var clips = App.BrainDrain?.AudioFileCount ?? 0;
            TxtClipCount.Text = Localization.Loc.GetF("st4_braindrain_clips_loaded_0", clips);
            NoAudioHint.Visibility = clips == 0 ? Visibility.Visible : Visibility.Collapsed;

            // The literal path, straight off the service. Read here rather than hardcoded in XAML
            // because BrainDrainService.AudioFolderPath is the ONLY definition of it, and it moves
            // with AppSettings.CustomAssetsPath - a wrong path printed in the UI is worse than none
            // (support would be chasing a folder that does not exist).
            try { TxtAudioFolderPath.Text = Services.BrainDrainService.AudioFolderPath; }
            catch { TxtAudioFolderPath.Text = string.Empty; }

            // The legacy install-directory folder gets a line ONLY while it still holds clips.
            // They keep playing (the service merges both folders), but that folder is wiped by a
            // reinstall and does not travel with a portable copy, so the honest thing is to say
            // "these still work, move them across". Nothing is migrated for the user.
            try
            {
                var legacy = Services.BrainDrainService.LegacyAudioFileCount();
                var show = legacy > 0 ? Visibility.Visible : Visibility.Collapsed;
                TxtLegacyFolderNote.Visibility = show;
                TxtLegacyFolderPath.Visibility = show;
                if (legacy > 0)
                    TxtLegacyFolderPath.Text = Services.BrainDrainService.LegacyAudioFolderPath;
            }
            catch
            {
                TxtLegacyFolderNote.Visibility = Visibility.Collapsed;
                TxtLegacyFolderPath.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Open the PRIMARY (assets) clip folder in Explorer, creating it first. Same shape as
        /// <c>SpiralFeatureControl.BtnOpenSpiralFolder_Click</c> (ProcessStartInfo with
        /// UseShellExecute, after a CreateDirectory) so Explorer never opens onto nothing.
        /// <para>Wired from BOTH the library header button and the empty-state banner's copy of
        /// it - one handler, so the two can never drift apart.</para>
        /// </summary>
        private void BtnOpenAudioFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = Services.BrainDrainService.EnsureAudioFolder();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Brain Drain: open clip folder failed");
            }
        }

        /// <summary>
        /// Re-scan the clip folder. <c>BrainDrainService.ReloadAudioFiles</c> has existed since the
        /// service was written and NOTHING in the app ever called it, so a clip dropped into the
        /// folder mid-session did nothing until the next full app restart - reported by several
        /// users in one evening. MindWipe wires the identical call at
        /// <c>Features/MindWipeFeatureControl.xaml.cs</c> (ApplyAudioChange); this is the same
        /// wiring, on a button because Brain Drain reads a whole folder rather than one picked file.
        /// </summary>
        private void BtnRefreshAudio_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.BrainDrain?.ReloadAudioFiles();
                RefreshClipCount();
                App.Logger?.Information("Brain Drain: clip folder re-scanned, {Count} clips now loaded",
                    App.BrainDrain?.AudioFileCount ?? 0);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Brain Drain: clip reload failed");
            }
        }

        /// <summary>
        /// Keeps the panel honest when something else moves the dials: DTRH, the Deeper editor,
        /// preset apply (<c>Models/Preset.cs:413</c>), the autonomy voice command, and
        /// <c>MainWindow.EnableBrainDrain</c> / <c>UpdateBrainDrainIntensity</c> all write these
        /// three settings directly.
        /// </summary>
        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.BrainDrainEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BrainDrainIntensity) ||
                e.PropertyName == nameof(Models.AppSettings.BrainDrainHighRefresh) ||
                e.PropertyName == nameof(Models.AppSettings.BrainDrainBlurStrength) ||
                e.PropertyName == nameof(Models.AppSettings.BrainDrainMeltEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.AllowOverlayCapture) ||
                e.PropertyName == nameof(Models.AppSettings.BrainDrainRandomStartMaxMinutes))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var isEnabled = ChkEnable.IsChecked ?? false;
            s.BrainDrainEnabled = isEnabled;

            if (App.IsEngineRunning)
            {
                try
                {
                    if (isEnabled)
                        App.BrainDrain?.Start();
                    else
                        App.BrainDrain?.Stop();
                }
                catch (Exception ex) { App.Logger?.Warning(ex, "Brain Drain start/stop failed"); }
                App.Logger?.Information("Brain Drain toggled: {Enabled}", isEnabled);
            }

            App.Settings?.Save();
        }

        private void SliderIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var v = (int)e.NewValue;
            TxtIntensity.Text = $"{v}%";
            s.BrainDrainIntensity = v;

            if (App.IsEngineRunning)
            {
                try { App.BrainDrain?.UpdateSettings(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "Brain Drain UpdateSettings failed"); }
            }

            App.Settings?.Save();
        }

        /// <summary>
        /// VISUAL half: the screen blur's strength (BrainDrainBlurStrength). No explicit apply
        /// call needed - OverlayService subscribes to AppSettings.PropertyChanged and pushes the
        /// new strength onto the live compositor layer (RefreshBrainDrainState), so writing the
        /// setting IS the live update.
        /// </summary>
        private void SliderBlurStrength_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var v = (int)e.NewValue;
            TxtBlurStrength.Text = $"{v}%";
            s.BrainDrainBlurStrength = v;

            App.Settings?.Save();
        }

        /// <summary>
        /// VISUAL half: melt variant toggle (BrainDrainMeltEnabled). Same mechanism as the blur
        /// strength slider - OverlayService's settings hook owns the live apply, including the
        /// stop/start bounce the capture pump's per-run melt flag requires.
        /// </summary>
        private void ChkMelt_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            s.BrainDrainMeltEnabled = ChkMelt.IsChecked ?? false;

            App.Logger?.Information("Brain Drain melt toggled: {Enabled}", s.BrainDrainMeltEnabled);
            App.Settings?.Save();
        }

        /// <summary>
        /// VISUAL half: let the Brain Drain overlay appear in screenshots / recordings / screen
        /// shares (AllowOverlayCapture). Default off keeps the historical privacy behaviour; users
        /// asked for this because the effect was invisible in every screenshot they tried to share.
        /// <para>No explicit apply call needed here - OverlayService's settings hook owns the live
        /// apply for BOTH render paths (compositor hosts and the legacy per-screen windows), so
        /// writing the setting IS the live update, exactly like the blur-strength slider.</para>
        /// </summary>
        private void ChkAllowCapture_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            s.AllowOverlayCapture = ChkAllowCapture.IsChecked ?? false;

            App.Logger?.Information("Brain Drain capture visibility toggled: {Allow}", s.AllowOverlayCapture);
            App.Settings?.Save();
        }

        private void ChkHighRefresh_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var isHighRefresh = ChkHighRefresh.IsChecked ?? false;
            s.BrainDrainHighRefresh = isHighRefresh;

            // The tick interval is only read at Start(), so a running service has to be bounced
            // for the new interval to take effect. Verbatim from the LevelFeatures.cs spec.
            if (App.IsEngineRunning && (App.BrainDrain?.IsRunning ?? false))
            {
                try
                {
                    App.BrainDrain?.Stop();
                    App.BrainDrain?.Start();
                }
                catch (Exception ex) { App.Logger?.Warning(ex, "Brain Drain restart failed"); }
            }

            App.Logger?.Information("Brain Drain High Refresh toggled: {Enabled}", isHighRefresh);
            App.Settings?.Save();
        }

        // PHASE 8 — MirrorToLegacyProgressionControls is GONE, exactly as its own doc-comment
        // instructed. It existed for one reason: MainWindow.SaveSettings() read all three Brain
        // Drain settings back out of the dead ProgressionTab checkboxes, and SaveSettings runs on
        // session start, so an edit made here would have been reverted the next time the user
        // pressed Start. Those reads were deleted in the same change (MainWindow.Settings.cs), and
        // ProgressionTabView no longer exists. This panel's own writes at ChkEnable_Changed /
        // SliderIntensity_Changed / ChkHighRefresh_Changed plus App.Settings.Save() are untouched.

        // =====================================================================================
        //  random onset (#general 2026-08-31: "i wish it kicked on randomly not as soon as i
        //  press start")
        // =====================================================================================

        /// <summary>
        /// Max wait, in minutes, before Brain Drain kicks in after Start. 0 reads as "Off" rather
        /// than "0 min", because zero here is the old always-instant behaviour, not a zero wait.
        /// </summary>
        private static string FormatRandomStart(int minutes) => minutes <= 0
            ? Localization.Loc.Get("st4_braindrain_random_start_off")
            : Localization.Loc.GetF("st4_braindrain_random_start_max_0", minutes);

        private void SliderRandomStart_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var v = (int)e.NewValue;
            TxtRandomStart.Text = FormatRandomStart(v);
            s.BrainDrainRandomStartMaxMinutes = v;

            // Nothing to apply live: the wait is rolled once, at engine start
            // (MainWindow.StartEngine -> BrainDrainService.ArmRandomOnset).
            App.Settings?.Save();
        }

        /// <summary>
        /// The service raises this on the UI thread, but it is reachable from a stop on the panic
        /// thread, so the dispatcher check is the project's standard guard rather than decoration.
        /// </summary>
        private void OnOnsetStateChanged(object? sender, EventArgs e)
        {
            if (Application.Current?.Dispatcher == null) return;
            Dispatcher.BeginInvoke(new Action(RefreshOnsetState));
        }

        /// <summary>
        /// Show the armed banner while a random onset is counting down. Without it an armed
        /// Brain Drain is indistinguishable from a broken one: the user pressed Start and nothing
        /// happened, on purpose.
        /// </summary>
        private void RefreshOnsetState()
        {
            try
            {
                var pending = App.BrainDrain?.OnsetPending == true;
                OnsetWaitingNotice.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
                if (pending)
                {
                    TxtOnsetWaiting.Text = Localization.Loc.GetF(
                        "st4_braindrain_waiting_0", App.BrainDrain?.OnsetMinutesRemaining ?? 1);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BrainDrainFeatureControl.RefreshOnsetState: {E}", ex.Message);
            }
        }

        // =====================================================================================
        //  feature art (mod-aware)
        // =====================================================================================

        /// <summary>
        /// This page's art under <c>Resources/features/</c>. Verbatim the file the XAML already
        /// declares as its pack:// default on both plates - naming it here changes WHICH lookup
        /// runs, never WHICH file is asked for.
        /// </summary>
        private const string FeatureArtPath = "features/brain_drain.png";

        /// <summary>
        /// Pushes the (possibly mod-overridden) feature art into the 72px hero plate and the tall
        /// side plate. Both plates author a pack:// default in XAML, so a null resolve here leaves
        /// the built-in art standing rather than blanking the plate - the same degrade rule
        /// <c>RemoteControlTabView.ApplyFeatureArt</c> follows.
        ///
        /// <para>Two widths, not one: the hero is 240px wide and the side plate is a full-height
        /// column, and <see cref="ConditioningControlPanel.Services.ModResourceResolver.ResolveImageDecoded"/> keys its cache on the
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
                var hero = ConditioningControlPanel.Services.ModResourceResolver.ResolveImageDecoded(FeatureArtPath, 480);
                if (hero != null && HeroArtBrush is { IsFrozen: false }) HeroArtBrush.ImageSource = hero;

                var side = ConditioningControlPanel.Services.ModResourceResolver.ResolveImageDecoded(FeatureArtPath, 800);
                if (side != null && SideArtBrush is { IsFrozen: false }) SideArtBrush.ImageSource = side;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BrainDrainFeatureControl.ApplyFeatureArt: {E}", ex.Message);
            }
        }

    }
}
