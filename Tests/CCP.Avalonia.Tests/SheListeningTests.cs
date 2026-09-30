using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>She's Listening on this head (WPF MainWindow.SheListening.cs): the master button arms
/// and disarms the real settings behind the premium bar, and - with no voice-command consumer
/// ported - the hero never claims the mic is open (avalonia-decisions.md "voice arm without consumer").</summary>
public sealed class SheListeningTests
{
    [Fact]
    public void MasterButtonArmsBehindThePremiumBarAndTheHeroStaysHonest()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.MicConsentGiven, s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                CoreSpeech.IsAvailableProvider = () => true;
                CoreSpeech.HasCaptureDeviceProvider = () => true;
                (s.MicConsentGiven, s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled) = (true, false, false);
                shell.ShowTab("shelistening");
                Dispatcher.UIThread.RunJobs();
                var tab = shell.SheListeningPage!;
                void Master() { tab.BtnSL_MicMaster.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

                // Lapsed free account: the bar refuses to arm.
                (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = (() => false, _ => false);
                Master();
                Assert.False(s.SpeechWakeWordEnabled);
                Assert.Equal("Mic off", tab.SL_StatusTitle.Text);
                Assert.StartsWith("Tap Start listening", tab.SL_StatusSub.Text);

                // Voice free day: arming defaults to the wake word, and the hero says the mic stays closed.
                CoreEntitlement.IsFreeTodayProvider = k => k == "voice";
                Master();
                Assert.True(s.SpeechWakeWordEnabled);
                Assert.Equal("Mic off", tab.SL_StatusTitle.Text);
                Assert.Equal(Loc.Get("sl_voice_not_on_this_build"), tab.SL_StatusSub.Text);
                Assert.NotEqual(Loc.Get("set2_chip_off"), tab.TxtSL_WakeWordChip.Text);

                // Disarming is never barred.
                CoreEntitlement.IsFreeTodayProvider = _ => false;
                Master();
                Assert.False(s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled);
                Assert.Equal(Loc.Get("set2_chip_off"), tab.TxtSL_WakeWordChip.Text);
            }
            finally
            {
                shell.Close();
                (s.MicConsentGiven, s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled,
                    CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = saved;
                CoreSpeech.IsAvailableProvider = null;
                CoreSpeech.HasCaptureDeviceProvider = null;
            }
        });
    }
}
