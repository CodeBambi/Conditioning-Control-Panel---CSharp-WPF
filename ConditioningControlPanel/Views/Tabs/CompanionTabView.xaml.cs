using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class CompanionTabView : UserControl
    {
        public CompanionTabView()
        {
            InitializeComponent();
            // FX lifecycle (PR-4b). Hooked here rather than in ShowTab so the tab owns its own
            // decoration: the hero breath starts when the tab appears and its clock is parked the
            // moment it is collapsed again.
            IsVisibleChanged += CompanionTabView_IsVisibleChanged;
        }

        private void CompanionTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnCompanionTabVisibilityChanged(IsVisible);
        }

        private void BtnAddVideoLink_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnAddVideoLink_Click(sender, e);
        }
        private void BtnBrowsePrompts_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBrowsePrompts_Click(sender, e);
        }
        private void BtnCameraShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCameraShortcut_Click(sender, e);
        }
        private void BtnChatShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnChatShortcut_Click(sender, e);
        }
        private void BtnCompanionPersonality_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCompanionPersonality_Click(sender, e);
        }
        private void BtnCompanionTutorial_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCompanionTutorial_Click(sender, e);
        }
        private void BtnCustomizeCompanion_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCustomizeCompanion_Click(sender, e);
        }
        private void BtnDeactivatePrompt_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeactivatePrompt_Click(sender, e);
        }
        private void BtnDeletePhrasePreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeletePhrasePreset_Click(sender, e);
        }
        private void BtnDetachCompanion_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDetachCompanion_Click(sender, e);
        }
        private void BtnEditTriggers_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnEditTriggers_Click(sender, e);
        }
        private void BtnExportPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnExportPrompt_Click(sender, e);
        }
        private void BtnImportPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnImportPrompt_Click(sender, e);
        }
        private void BtnManagePhrases_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnManagePhrases_Click(sender, e);
        }
        private void BtnOpenAiSamplerSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnOpenAiSamplerSettings_Click(sender, e);
        }
        private void BtnPrivacySpoiler_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnPrivacySpoiler_Click(sender, e);
        }
        private void BtnRefreshPrompts_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnRefreshPrompts_Click(sender, e);
        }
        private void BtnResetCompanionMemory_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnResetCompanionMemory_Click(sender, e);
        }
        private void BtnSavePhrasePreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSavePhrasePreset_Click(sender, e);
        }
        private void BtnSetupLocalAi_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSetupLocalAi_Click(sender, e);
        }
        private void BtnSwitchCompanion_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSwitchCompanion_Click(sender, e);
        }
        private void BtnTestOllamaConnection_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnTestOllamaConnection_Click(sender, e);
        }
        private void BtnTestOpenAiConnection_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnTestOpenAiConnection_Click(sender, e);
        }
        private void ChkAvatarEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAvatarEnabled_Changed(sender, e);
        }
        private void ChkAwarenessMode_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAwarenessMode_Changed(sender, e);
        }
        private void ChkMuteAvatar_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkMuteAvatar_Changed(sender, e);
        }
        private void ChkMuteWhispers_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkMuteWhispers_Changed(sender, e);
        }
        private void ChkPauseBrowser_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkPauseBrowser_Changed(sender, e);
        }
        private void ChkSlutMode_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkSlutMode_Changed(sender, e);
        }
        private void ChkTriggerMode_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkTriggerMode_Changed(sender, e);
        }
        private void CmbPhrasePresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbPhrasePresets_SelectionChanged(sender, e);
        }
        private void CompanionCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CompanionCard_Click(sender, e);
        }
        private void CompanionSection_ExpandedChanged(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CompanionSection_ExpandedChanged(sender, e);
        }
        private void RadioAiCloud_Checked(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.RadioAiCloud_Checked(sender, e);
        }
        private void RadioAiLocal_Checked(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.RadioAiLocal_Checked(sender, e);
        }
        private void RadioAiOff_Checked(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.RadioAiOff_Checked(sender, e);
        }
        private void RadioAiOpenAiCompatible_Checked(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.RadioAiOpenAiCompatible_Checked(sender, e);
        }
        private void SliderAwarenessCooldown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAwarenessCooldown_ValueChanged(sender, e);
        }
        private void SliderAwarenessCooldownMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAwarenessCooldownMax_ValueChanged(sender, e);
        }
        private void SliderBubbleDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderBubbleDuration_ValueChanged(sender, e);
        }
        private void SliderIdleInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderIdleInterval_ValueChanged(sender, e);
        }
        private void SliderTriggerInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderTriggerInterval_ValueChanged(sender, e);
        }
        private void TxtAiHost_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtAiHost_LostFocus(sender, e);
        }
        private void TxtAiModel_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtAiModel_LostFocus(sender, e);
        }
        private void TxtDailyLimit_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtDailyLimit_LostFocus(sender, e);
        }
        private void TxtOpenAiApiKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtOpenAiApiKey_PasswordChanged(sender, e);
        }
        private void TxtOpenAiEndpoint_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtOpenAiEndpoint_LostFocus(sender, e);
        }
        private void TxtOpenAiModel_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtOpenAiModel_LostFocus(sender, e);
        }
    }
}
