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
/// and disarms the real settings behind the premium bar, and the hero follows the armed state.</summary>
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
                Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("shelisten_status_off_title"), tab.SL_StatusTitle.Text);
                Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("shelisten_status_off_sub"), tab.SL_StatusSub.Text);

                // Voice free day: arming defaults to the wake word and the hero says the mic is open
                // (MainShellWindow.VoiceCommands.cs is the consumer; VoiceCommandsTests proves it opens).
                CoreEntitlement.IsFreeTodayProvider = k => k == "voice";
                Master();
                Assert.True(s.SpeechWakeWordEnabled);
                Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("shelisten_status_on_title"), tab.SL_StatusTitle.Text);
                Assert.Equal("The mic is open. Call her, then say a command.", tab.SL_StatusSub.Text);
                Assert.NotEqual(Loc.Get("set2_chip_off"), tab.TxtSL_WakeWordChip.Text);
                // WPF UpdateMicPill / SetSheListeningStatusPulse: the pill lights and the disc breathes.
                var pill = shell.FindControl<Border>("MicActivePill")!;
                Assert.True(pill.IsVisible);
                Assert.True(ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.StatusGlowLayer(tab.SL_StatusDot) is { Opacity: > 0 }, "the mic disc glow layer is dark");
                Assert.False(tab.BtnSL_OpenModels.IsVisible);

                // The pill is the privacy stop, by mouse and by keyboard (P17).
                var at = pill.TranslatePoint(new Point(pill.Bounds.Width / 2, pill.Bounds.Height / 2), shell)!.Value;
                shell.MouseDown(at, global::Avalonia.Input.MouseButton.Left);
                shell.MouseUp(at, global::Avalonia.Input.MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.False(s.SpeechWakeWordEnabled);
                Assert.False(pill.IsVisible);
                Assert.Null(tab.SL_StatusDot.Effect);
                Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("shelisten_status_off_title"), tab.SL_StatusTitle.Text);
                Master();
                Assert.True(pill.IsVisible);
                pill.RaiseEvent(new global::Avalonia.Input.KeyEventArgs
                    { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter });
                Dispatcher.UIThread.RunJobs();
                Assert.False(s.SpeechWakeWordEnabled);
                Assert.False(pill.IsVisible);
                Master();

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

    /// <summary>WPF SheListening.cs:445: the folder button is up exactly while the status line is
    /// about a model (mic present, model missing), never when the mic itself is missing.</summary>
    [Fact]
    public void OpenModelsButtonFollowsTheModelStatus()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                CoreSpeech.IsAvailableProvider = () => false;
                CoreSpeech.HasCaptureDeviceProvider = () => true;
                CoreSpeech.ModelStatusProvider = () => CoreSpeechModelStatus.NoModelFound;
                shell.ShowTab("shelistening");
                Dispatcher.UIThread.RunJobs();
                var tab = shell.SheListeningPage!;
                Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("shelisten_status_not_ready_title"), tab.SL_StatusTitle.Text);
                Assert.True(tab.BtnSL_OpenModels.IsVisible);
                Assert.Null(tab.SL_StatusDot.Effect);

                CoreSpeech.HasCaptureDeviceProvider = () => false;
                shell.RefreshSheListeningTab();
                Assert.False(tab.BtnSL_OpenModels.IsVisible);
            }
            finally
            {
                shell.Close();
                CoreSpeech.IsAvailableProvider = null;
                CoreSpeech.HasCaptureDeviceProvider = null;
                CoreSpeech.ModelStatusProvider = null;
            }
        });
    }
}
