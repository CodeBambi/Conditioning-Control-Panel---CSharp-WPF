using System;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// "She's Listening" — the voice-control Exclusive. A purpose-built surface for the offline
    /// mic features (spoken mantras + the "Hey Bambi" voice commands), with a command cheat-sheet.
    /// The microphone hardware and the voice input modes (device, wake word, push-to-talk,
    /// headphone barge-in) are owned by Settings > Devices since Phase 2 of the UX restructure and
    /// appear here only as read-only chips; what stays live is what belongs to this page - the
    /// master switch, sensitivity, spoken mantras, wake calibration, the test and mic consent.
    /// Every handler still delegates to a MainWindow partial, so no settings logic lives here.
    /// </summary>
    public partial class SheListeningTabView : UserControl
    {
        public SheListeningTabView()
        {
            InitializeComponent();
            Loaded += SheListeningTabView_Loaded;
            Unloaded += SheListeningTabView_Unloaded;
        }

        // ==== mod-aware feature art =====================================================
        // Both audio_whispers.png plates author a pack:// URI in XAML, which is the BASE art
        // and stays as the fallback. A mod shipping resources/features/audio_whispers.png has
        // to repaint them, and only ModChanged is authoritative about that: ApplyActiveModChange
        // is never reached when the ACTIVE mod is uninstalled (ModService activates the fallback
        // itself), which used to leave the dead mod's art on screen. Sources only - the side
        // plate's fixed 520px height and top pin are load-bearing layout, see the XAML note.

        /// <summary>Guards against a double subscription if Loaded fires again after a re-parent.</summary>
        private bool _modArtHooked;

        private void SheListeningTabView_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_modArtHooked && App.Mods != null)
            {
                App.Mods.ModChanged += OnModChangedArt;
                _modArtHooked = true;
            }
            ApplyFeatureArt();
        }

        private void SheListeningTabView_Unloaded(object sender, RoutedEventArgs e)
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
        /// Repaints the hero and side-art plates from the active mod's audio_whispers.png, if it
        /// has one. A null resolve is left alone deliberately: the plate degrades to its authored
        /// pack:// art rather than to an empty rectangle.
        /// </summary>
        private void ApplyFeatureArt()
        {
            try
            {
                const string Art = "features/audio_whispers.png";

                var hero = ModResourceResolver.ResolveImageDecoded(Art, 480);
                if (hero != null && SheListeningHeroArtBrush != null && !SheListeningHeroArtBrush.IsFrozen)
                    SheListeningHeroArtBrush.ImageSource = hero;

                var side = ModResourceResolver.ResolveImageDecoded(Art, 800);
                if (side != null && SheListeningSideArtBrush != null && !SheListeningSideArtBrush.IsFrozen)
                    SheListeningSideArtBrush.ImageSource = side;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("SheListeningTabView feature art: {E}", ex.Message);
            }
        }

        private void ChkSL_Mantras_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_Mantras_Changed(sender, e);
        }
        private void BtnSL_Calibrate_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_Calibrate_Click(sender, e);
        }
        // Phase 2: the mic device picker, the wake-word and push-to-talk toggles and the
        // headphone switch are read-only chips here now - Settings > Devices owns them, so
        // seven shims went with them. This is the link to that page.
        private void BtnSL_OpenDeviceSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.OpenDeviceSettings();
        }
        private void BtnSL_MicMaster_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleVoiceMic();
        }
        private void SldSL_MicSensitivity_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_MicSensitivity_Changed(sender, e);
        }
        private void BtnSL_TestMantra_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnTestVoice_Click(sender, e);
        }
        private void BtnSL_GateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnGateUnlock_Click(sender, e);
        }
        private void BtnSL_RevokeConsent_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SL_RevokeMicConsent_Click(sender, e);
        }
    }
}
