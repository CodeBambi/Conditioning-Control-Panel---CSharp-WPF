using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class DeeperTabView : UserControl
    {
        public DeeperTabView()
        {
            InitializeComponent();
            // FX lifecycle (PR-4b): the header glyph's drift starts when the tab appears and is
            // parked the moment it is collapsed again.
            IsVisibleChanged += DeeperTabView_IsVisibleChanged;
            Loaded += DeeperTabView_Loaded;
            Unloaded += DeeperTabView_Unloaded;
        }

        private void DeeperTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnDeeperTabVisibilityChanged(IsVisible);
        }

        // ==== mod-aware feature art =====================================================
        // Both deeper.png plates author a pack:// URI in XAML, which is the BASE art and stays
        // as the fallback. A mod shipping resources/features/deeper.png has to repaint them,
        // and only ModChanged is authoritative about that: ApplyActiveModChange is never reached
        // when the ACTIVE mod is uninstalled (ModService activates the fallback itself), which
        // used to leave the dead mod's art on screen.
        //
        // SOURCES ONLY. The side plate's explicit Height=520 + VerticalAlignment=Top is load-
        // bearing (a background-only Border has no natural height, and a Stretch-aligned one
        // gets centred in the leftover column space, which floated the art mid-page). Nothing
        // below touches layout.

        /// <summary>Guards against a double subscription if Loaded fires again after a re-parent.</summary>
        private bool _modArtHooked;

        private void DeeperTabView_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_modArtHooked && App.Mods != null)
            {
                App.Mods.ModChanged += OnModChangedArt;
                _modArtHooked = true;
            }
            ApplyFeatureArt();
        }

        private void DeeperTabView_Unloaded(object sender, RoutedEventArgs e)
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
        /// Repaints the hero and side-art plates from the active mod's deeper.png, if it has one.
        /// A null resolve is left alone deliberately: the plate degrades to its authored pack://
        /// art rather than to an empty rectangle.
        /// </summary>
        private void ApplyFeatureArt()
        {
            try
            {
                const string Art = "features/deeper.png";

                var hero = ModResourceResolver.ResolveImageDecoded(Art, 480);
                if (hero != null && DeeperHeroArtBrush != null && !DeeperHeroArtBrush.IsFrozen)
                    DeeperHeroArtBrush.ImageSource = hero;

                var side = ModResourceResolver.ResolveImageDecoded(Art, 800);
                if (side != null && DeeperSideArtBrush != null && !DeeperSideArtBrush.IsFrozen)
                    DeeperSideArtBrush.ImageSource = side;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperTabView feature art: {E}", ex.Message);
            }
        }

        // Row hover lift. Composed with (not a replacement for) the row's existing IsMouseOver
        // border-brush reveal, which stays in the DataTemplate's own style.
        private void DeeperRow_MouseEnter(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnDeeperRowHover(sender as FrameworkElement, true);
        }

        private void DeeperRow_MouseLeave(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnDeeperRowHover(sender as FrameworkElement, false);
        }

        private void BtnDeeperCatalogue_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperCatalogue_Click(sender, e);
        }
        private void BtnDeeperImport_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperImport_Click(sender, e);
        }
        private void BtnDeeperNewEnhancement_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperNewEnhancement_Click(sender, e);
        }
        private void BtnDeeperOpenLibraryFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperOpenLibraryFolder_Click(sender, e);
        }
        private void BtnDeeperOpenPlayer_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperOpenPlayer_Click(sender, e);
        }
        private void BtnDeeperTutorial_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperTutorial_Click(sender, e);
        }
        private void BtnDeeperWebcamCalibrate_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWebcamCalibrate_Click(sender, e);
        }
        private void BtnDeeperWebcamManageConsent_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWebcamManageConsent_Click(sender, e);
        }
        private void BtnDeeperWebcamQuickRecal_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWebcamQuickRecal_Click(sender, e);
        }
        private void BtnDeeperWebcamRevokeConsent_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWebcamRevokeConsent_Click(sender, e);
        }
        private void BtnDeeperWebcamStartStopTracker_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWebcamStartStopTracker_Click(sender, e);
        }
        private void BtnDeeperWelcomeDemo_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWelcomeDemo_Click(sender, e);
        }
        private void BtnDeeperWelcomeDismiss_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWelcomeDismiss_Click(sender, e);
        }
        private void BtnDeeperWelcomeTour_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeeperWelcomeTour_Click(sender, e);
        }
        // Phase 2: blink-recal, the camera/monitor pickers and restrict-gaze moved to
        // Settings → Devices (one editor per setting). Only the chip's link back is left.
        private void BtnOpenDeviceSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OpenDeviceSettings();
        }
        private void DeeperPillAll_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperPillAll_Click(sender, e);
        }
        private void DeeperPillAudio_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperPillAudio_Click(sender, e);
        }
        private void DeeperPillHaptics_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperPillHaptics_Click(sender, e);
        }
        private void DeeperPillVideo_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperPillVideo_Click(sender, e);
        }
        private void DeeperPillWebcam_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperPillWebcam_Click(sender, e);
        }
        private void DeeperRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperRow_Click(sender, e);
        }

        private void DeeperRowDelete_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperRowDelete_Click(sender, e);
        }
        private void DeeperRowPlay_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperRowPlay_Click(sender, e);
        }
        private void DeeperRowSubmit_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperRowSubmit_Click(sender, e);
        }
        private void DeeperSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperSearch_TextChanged(sender, e);
        }
        private void DeeperSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.DeeperSort_SelectionChanged(sender, e);
        }
    }
}
