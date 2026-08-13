using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class BambiTakeoverTabView : UserControl
    {
        public BambiTakeoverTabView()
        {
            InitializeComponent();
            Loaded += (_, _) => LoadMantraChant();
            // Tabs are shown/hidden rather than rebuilt, so Loaded fires once. Re-read on every show
            // or the toggle lies after something else disarms the chant behind our back — panic clears
            // MantraChantEnabled (#685) and the checkbox would still read ON.
            IsVisibleChanged += (_, _) => { if (IsVisible) LoadMantraChant(); };
            Loaded += BambiTakeoverTabView_Loaded;
            Unloaded += BambiTakeoverTabView_Unloaded;
        }

        // ==== mod-aware feature art =====================================================
        // Both plates author a pack:// URI in XAML, which is the BASE art and stays as the
        // fallback. A mod shipping its own takeover art has to repaint them, and only
        // ModChanged is authoritative about that: ApplyActiveModChange is never reached when
        // the ACTIVE mod is uninstalled (ModService activates the fallback itself), which used
        // to leave the dead mod's art on screen.
        //
        // THE TAKEOVER FORK. This feature is the one with two base files: BambiSleep's art is
        // "features/bambi takeover.png", every other mod's is "features/takeover.png". That
        // fork is not local to this view - MainWindow.xaml.cs picks the same pair for
        // ImgBambiTakeoverDesc, the collapsed Image in the description card - so the hero and
        // the description icon must agree or the page shows two different takeovers at once.
        // Replicated inline rather than shared because MainWindow.xaml.cs is a shared partial
        // this pass may not edit. Keep the two in step if either ever changes.

        /// <summary>Guards against a double subscription if Loaded fires again after a re-parent.</summary>
        private bool _modArtHooked;

        /// <summary>
        /// The base art path for the active mod. BambiSleep ships a dedicated file; a mod that
        /// overrides either name still wins through the resolver, because whichever name is
        /// picked here is the one the resolver probes under the mod's resources/ folder.
        /// </summary>
        private static string TakeoverArtPath =>
            App.Mods?.ActiveModId == Models.BuiltInMods.BambiSleepId
                ? "features/bambi takeover.png"
                : "features/takeover.png";

        private void BambiTakeoverTabView_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_modArtHooked && App.Mods != null)
            {
                App.Mods.ModChanged += OnModChangedArt;
                _modArtHooked = true;
            }
            ApplyFeatureArt();
        }

        private void BambiTakeoverTabView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_modArtHooked && App.Mods != null)
            {
                App.Mods.ModChanged -= OnModChangedArt;
                _modArtHooked = false;
            }
        }

        // ModChanged can be raised off the UI thread; marshal before touching the brushes.
        private void OnModChangedArt(object? sender, Models.ModPackage mod)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(ApplyFeatureArt));
                return;
            }
            ApplyFeatureArt();
        }

        /// <summary>
        /// Repaints the hero and side-art plates from the active mod's takeover art. A null
        /// resolve is left alone deliberately: the plate degrades to its authored pack:// art
        /// rather than to an empty rectangle.
        /// </summary>
        private void ApplyFeatureArt()
        {
            try
            {
                var art = TakeoverArtPath;

                var hero = ModResourceResolver.ResolveImageDecoded(art, 480);
                if (hero != null && TakeoverHeroArtBrush != null && !TakeoverHeroArtBrush.IsFrozen)
                    TakeoverHeroArtBrush.ImageSource = hero;

                var side = ModResourceResolver.ResolveImageDecoded(art, 800);
                if (side != null && TakeoverSideArtBrush != null && !TakeoverSideArtBrush.IsFrozen)
                    TakeoverSideArtBrush.ImageSource = side;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BambiTakeoverTabView feature art: {E}", ex.Message);
            }
        }

        // ── Mantra Chant (suggestion #653) ────────────────────────────────────────
        // Unique to this tab (no Takeover twin to mirror), so its controls are wired directly here
        // instead of delegating to MainWindow like the shared voice toggles above.

        private bool _mantraChantLoading;

        /// <summary>Populate the chant controls from settings + refresh the voiced-content note. Runs on tab show.</summary>
        private void LoadMantraChant()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _mantraChantLoading = true;
            try
            {
                ChkMantraChant.IsChecked = s.MantraChantEnabled;
                SldMantraChantVolume.Value = s.MantraChantVolume;
                TxtMantraChantVolume.Text = $"{(int)Math.Round(s.MantraChantVolume)}%";
                SldMantraChantGap.Value = s.MantraChantGapSeconds;
                TxtMantraChantGap.Text = $"{s.MantraChantGapSeconds}s";
                RefreshMantraChantHint();
            }
            finally { _mantraChantLoading = false; }
        }

        /// <summary>A mod with no voiced mantras can't chant, so say so rather than looping silence.</summary>
        private void RefreshMantraChantHint()
        {
            if (TxtMantraChantHint == null) return;
            var voiced = App.MantraChant?.CanChant() == true;
            TxtMantraChantHint.Text = Loc.Get(voiced ? "desc_mantra_chant" : "desc_mantra_chant_none");
        }

        private void ChkMantraChant_Changed(object sender, RoutedEventArgs e)
        {
            if (_mantraChantLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.MantraChantEnabled = ChkMantraChant.IsChecked == true;
            App.Settings?.Save();
            if (s.MantraChantEnabled) App.MantraChant?.Start();
            else App.MantraChant?.Stop();
            RefreshMantraChantHint();
        }

        private void SldMantraChantVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mantraChantLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.MantraChantVolume = e.NewValue;
            App.Settings?.Save();
            if (TxtMantraChantVolume != null) TxtMantraChantVolume.Text = $"{(int)Math.Round(e.NewValue)}%";
            App.MantraChant?.ApplyVolume(); // live-apply to a clip that's already playing
        }

        private void SldMantraChantGap_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mantraChantLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.MantraChantGapSeconds = (int)Math.Round(e.NewValue);
            App.Settings?.Save();
            if (TxtMantraChantGap != null) TxtMantraChantGap.Text = $"{s.MantraChantGapSeconds}s";
        }

        private void BtnAutonomyStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnAutonomyStartStop_Click(sender, e);
        }
        private void BtnForceStartAutonomy_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnForceStartAutonomy_Click(sender, e);
        }
        private void BtnGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnGateUnlock_Click(sender, e);
        }
        private void BtnTestAutonomy_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnTestAutonomy_Click(sender, e);
        }
        private void BtnTestVoice_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnTestVoice_Click(sender, e);
        }
        private void ChkAutonomyVoice_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyVoice_Changed(sender, e);
        }
        private void ChkAutonomyResume_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyResume_Changed(sender, e);
        }
        private void ChkShowTakeoverCountdown_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkShowTakeoverCountdown_Changed(sender, e);
        }
        // The four voice-input shims that used to sit here went with TakeoverVoiceInputLegacy to
        // Settings → Devices in Phase 2 (see the signpost in this tab's XAML).
        private void BtnOpenDeviceSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OpenDeviceSettings();
        }
        private void ChkAutonomyBehavior_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyBehavior_Changed(sender, e);
        }
        private void BtnWallpaperFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnWallpaperFolder_Click(sender, e);
        }
        private void ChkWallpaperKeep_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkWallpaperKeep_Changed(sender, e);
        }
        private void SliderWallpaperDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderWallpaperDuration_Changed(sender, e);
        }
        private void ChkAutonomyEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyEnabled_Changed(sender, e);
        }
        private void ChkAutonomyIdle_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyIdle_Changed(sender, e);
        }
        private void ChkAutonomyRandom_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyRandom_Changed(sender, e);
        }
        private void ChkAutonomyTimeAware_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyTimeAware_Changed(sender, e);
        }
        private void SliderAutonomyAnnounce_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAutonomyAnnounce_Changed(sender, e);
        }
        private void SliderAutonomyCooldown_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAutonomyCooldown_Changed(sender, e);
        }
        private void SliderAutonomyIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAutonomyIntensity_Changed(sender, e);
        }
        private void SliderAutonomyInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAutonomyInterval_Changed(sender, e);
        }
    }
}
