using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class PresetsTabView : UserControl
    {
        public PresetsTabView()
        {
            InitializeComponent();
        }

        private void BtnCreateSession_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCreateSession_Click(sender, e);
        }
        private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeletePreset_Click(sender, e);
        }
        private void BtnExportPreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnExportPreset_Click(sender, e);
        }
        private void BtnSharePreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSharePreset_Click(sender, e);
        }
        private void CatalogueCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCatalogue_Click(sender, e);
        }
        private void BtnExportSession_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnExportSession_Click(sender, e);
        }
        private void BtnLoadPreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnLoadPreset_Click(sender, e);
        }
        private void BtnNewPreset_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnNewPreset_Click(sender, e);
        }
        private void BtnRevealSpoilers_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnRevealSpoilers_Click(sender, e);
        }
        private void BtnSaveOverPreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSaveOverPreset_Click(sender, e);
        }
        private void BtnSelectCornerGif_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSelectCornerGif_Click(sender, e);
        }
        private void BtnSessionHistory_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSessionHistory_Click(sender, e);
        }
        private void BtnStartSession_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnStartSession_Click(sender, e);
        }
        private void ChkCornerGifEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkCornerGifEnabled_Changed(sender, e);
        }
        private void PacksScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PacksScrollViewer_PreviewMouseWheel(sender, e);
        }
        private void SessionBtn_Edit(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SessionBtn_Edit(sender, e);
        }
        private void SessionBtn_Export(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SessionBtn_Export(sender, e);
        }
        private void RbCornerPos_Checked(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.RbCornerPos_Checked(sender, e);
        }
        private void SessionCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SessionCard_Click(sender, e);
        }
        private void SliderCornerGifOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderCornerGifOpacity_ValueChanged(sender, e);
        }
        private void SliderCornerGifSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderCornerGifSize_ValueChanged(sender, e);
        }
    }
}
