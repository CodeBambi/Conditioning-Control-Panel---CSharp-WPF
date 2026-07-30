using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class AwarenessTabView : UserControl
    {
        public AwarenessTabView()
        {
            InitializeComponent();
        }

        private void AwarenessHighlightSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.AwarenessHighlightSwatch_Click(sender, e);
        }
        private void BtnAwarenessTutorial_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnAwarenessTutorial_Click(sender, e);
        }
        private void BtnGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnGateUnlock_Click(sender, e);
        }
        private void ChkAwarenessHighlightVisibleInCapture_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAwarenessHighlightVisibleInCapture_Changed(sender, e);
        }
        private void ChkAwarenessHighlight_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAwarenessHighlight_Changed(sender, e);
        }
        private void ChkAwarenessIgnoreOwnUi_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAwarenessIgnoreOwnUi_Changed(sender, e);
        }
        private void ChkAwarenessIgnoreOwnFocus_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAwarenessIgnoreOwnFocus_Changed(sender, e);
        }
        private void CmbAwarenessAppScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbAwarenessAppScope_SelectionChanged(sender, e);
        }
        private void TxtAwarenessAppList_KeyDown(object sender, KeyEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtAwarenessAppList_KeyDown(sender, e);
        }
        private void TxtAwarenessAppList_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtAwarenessAppList_LostFocus(sender, e);
        }
        private void ChkAwarenessKeyboard_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAwarenessKeyboard_Changed(sender, e);
        }
        private void ChkAwarenessLoopProtection_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAwarenessLoopProtection_Changed(sender, e);
        }
        private void ChkAwarenessMaster_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAwarenessMaster_Changed(sender, e);
        }
        private void ChkAwarenessOcr_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAwarenessOcr_Changed(sender, e);
        }
        private void LnkAwarenessAdvanced_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.LnkAwarenessAdvanced_Click(sender, e);
        }
        private void SliderAwarenessGlobalCooldown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAwarenessGlobalCooldown_ValueChanged(sender, e);
        }
        private void SliderAwarenessSameWordCooldown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAwarenessSameWordCooldown_ValueChanged(sender, e);
        }
        private void TxtAwarenessHighlightHex_KeyDown(object sender, KeyEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtAwarenessHighlightHex_KeyDown(sender, e);
        }
        private void TxtAwarenessHighlightHex_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtAwarenessHighlightHex_LostFocus(sender, e);
        }
    }
}
