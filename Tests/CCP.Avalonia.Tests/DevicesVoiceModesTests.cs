using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>u1 G2 / G12: Settings > Devices voice. The wake word and push-to-talk switches write (after
/// the premium bar, mic consent and the always-on OK, in WPF order; OFF is never gated), Set key captures
/// the next real key, and the microphone, phrases and headphones apply live. No microphone is opened:
/// the shell (which owns the engine) is absent, and the prompts are answered by the test.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class DevicesVoiceModesTests
{
    [Fact]
    public async Task VoiceSwitchesWrite_GatedOnConsent_AndApplyLive()
    {
        var s = CoreSettings.Current;
        var old = (s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled, s.MicConsentGiven, s.SpeechPushToTalkKey, s.SpeechWakeWords, s.SpeechHeadphonesMode);
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            s.SpeechWakeWordEnabled = false; s.SpeechPushToTalkEnabled = false; s.MicConsentGiven = false;
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var section = new DevicesSettingsSection();
                var window = new Window { Content = section };
                window.Show();
                var wake = section.FindControl<CheckBox>("ChkSpeechWakeWord")!;
                var ptt = section.FindControl<CheckBox>("ChkSpeechPushToTalk")!;

                // Consent declined: the switch falls back, nothing is saved, no mic.
                bool consent = false, alwaysOn = false;
                section.AskMicConsent = () => { if (consent) s.MicConsentGiven = true; return Task.FromResult(consent); };
                section.AskAlwaysOn = () => Task.FromResult(alwaysOn);
                wake.IsChecked = true;
                await Task.Yield();
                Assert.False(wake.IsChecked);
                Assert.False(s.SpeechWakeWordEnabled);
                Assert.Equal(0, section.VoiceApplies);

                // Consent given but the always-on question answered No: still off.
                consent = true;
                wake.IsChecked = true;
                await Task.Yield();
                Assert.False(wake.IsChecked);
                Assert.False(s.SpeechWakeWordEnabled);

                // Both yes: saved and applied live.
                alwaysOn = true;
                wake.IsChecked = true;
                await Task.Yield();
                Assert.True(s.SpeechWakeWordEnabled);
                Assert.Equal(1, section.VoiceApplies);

                // Off is never gated, never asks.
                consent = false; alwaysOn = false;
                wake.IsChecked = false;
                await Task.Yield();
                Assert.False(s.SpeechWakeWordEnabled);
                Assert.Equal(2, section.VoiceApplies);

                // Push to talk: consent is current now, one click writes.
                ptt.IsChecked = true;
                await Task.Yield();
                Assert.True(s.SpeechPushToTalkEnabled);
                Assert.Equal(3, section.VoiceApplies);

                // Set key: a lone modifier keeps waiting, the next real key is stored and shown.
                section.BtnSetPttKey_Click(null, new RoutedEventArgs());
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.LeftCtrl });
                Assert.NotEqual("LeftCtrl", s.SpeechPushToTalkKey);
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F9 });
                Assert.Equal("F9", s.SpeechPushToTalkKey);
                Assert.Equal("F9", section.FindControl<TextBlock>("TxtPttKey")!.Text);
                Assert.Equal(4, section.VoiceApplies);

                // G12: headphones mode applies live, no restart.
                var head = section.FindControl<CheckBox>("ChkHeadphones")!;
                head.IsChecked = !(head.IsChecked == true);
                Assert.Equal(head.IsChecked == true, s.SpeechHeadphonesMode);
                Assert.Equal(5, section.VoiceApplies);
                window.Close();
            });
        }
        finally
        {
            CoreEntitlement.HasPremiumProvider = oldPremium;
            (s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled, s.MicConsentGiven, s.SpeechPushToTalkKey, s.SpeechWakeWords, s.SpeechHeadphonesMode) = old;
            CoreSettings.SaveImmediate();
        }
    }
}
