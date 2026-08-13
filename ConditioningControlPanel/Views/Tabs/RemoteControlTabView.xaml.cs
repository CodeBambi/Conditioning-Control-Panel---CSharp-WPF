using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class RemoteControlTabView : UserControl
    {
        public RemoteControlTabView()
        {
            InitializeComponent();

            // Velvet Kit 2 round 4: the 320px banner is gone, but its Image survives (collapsed,
            // zero-size) because MainWindow.xaml.cs's mod-art map still writes
            // ImgRemoteControlFeature.Source on every mod switch - and that file is shared with
            // nine other tabs, so this pass may not touch it. Mirror whatever it writes into the
            // hero plate and the side-art plate so a mod's own remote_control.png still repaints
            // both. DependencyPropertyDescriptor holds a strong reference; this tab lives for the
            // lifetime of the window, so there is nothing to leak into.
            try
            {
                DependencyPropertyDescriptor
                    .FromProperty(Image.SourceProperty, typeof(Image))
                    ?.AddValueChanged(ImgRemoteControlFeature, (_, _) => ApplyFeatureArt());
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("RemoteControl feature-art mirror not attached: {E}", ex.Message);
            }
        }

        /// <summary>
        /// Pushes the (possibly mod-overridden) feature art into the hero and side-art plates.
        /// Both plates author a pack:// default in XAML, so a failure here degrades to the
        /// built-in art rather than to an empty plate.
        /// </summary>
        private void ApplyFeatureArt()
        {
            try
            {
                var src = ImgRemoteControlFeature?.Source;
                if (src == null) return;
                if (HeroArtPlate?.Background is ImageBrush hero && !hero.IsFrozen) hero.ImageSource = src;
                if (SideArtPlate?.Background is ImageBrush side && !side.IsFrozen) side.ImageSource = src;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("RemoteControl feature-art mirror failed: {E}", ex.Message);
            }
        }

        private void BtnCopyRemoteCode_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCopyRemoteCode_Click(sender, e);
        }
        private void BtnCopyRemoteLink_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCopyRemoteLink_Click(sender, e);
        }
        private void BtnEditEmoteCancel_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnEditEmoteCancel_Click(sender, e);
        }
        private void BtnEditEmoteSave_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnEditEmoteSave_Click(sender, e);
        }
        private void BtnEmoteCustomSend_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnEmoteCustomSend_Click(sender, e);
        }
        private void BtnEmoteEdit_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnEmoteEdit_Click(sender, e);
        }
        private void BtnEmotePreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnEmotePreset_Click(sender, e);
        }
        private void BtnGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnGateUnlock_Click(sender, e);
        }
        private void BtnStopRemote_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnStopRemote_Click(sender, e);
        }
        private void ChkOptInTag_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkOptInTag_Click(sender, e);
        }
        private void ChkOptIntoDirectory_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkOptIntoDirectory_Changed(sender, e);
        }
        private void ChkRemoteControlEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkRemoteControlEnabled_Changed(sender, e);
        }
        private void ChkRemoteShareAvatar_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkRemoteShareAvatar_Changed(sender, e);
        }
        private void ChkStopEffectsOnRemoteDisconnect_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkStopEffectsOnRemoteDisconnect_Changed(sender, e);
        }
        private void CmbRemoteTier_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbRemoteTier_SelectionChanged(sender, e);
        }
        private void TierCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TierCard_Click(sender, e);
        }
        private void TxtEditEmoteText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtEditEmoteText_TextChanged(sender, e);
        }
        private void TxtEmoteCustom_KeyDown(object sender, KeyEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtEmoteCustom_KeyDown(sender, e);
        }
        private void TxtOptInStatus_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtOptInStatus_TextChanged(sender, e);
        }
    }
}
