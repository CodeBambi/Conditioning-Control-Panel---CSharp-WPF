using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Speech;
using Xunit;
using WavMic = CCP.Avalonia.Tests.LockCardVoiceTests.WavMicSource;

namespace CCP.Avalonia.Tests;

/// <summary>She's Listening's consumer on this head (WPF AutonomyService.Voice/VoiceCommands): the
/// wake loop opens the mic only when armed + consented + entitled, Stop / revoke / lapse close it,
/// panic aborts the capture in flight but leaves the loop armed, and the spoken safe word works
/// under Lockdown (decisions "Panic ↔ mic"). Fed by Vosk's test.wav through a fake mic, never a
/// real microphone. The WAV says "one zero zero zero one", which the user sets as the wake phrase.</summary>
public sealed class VoiceCommandsTests
{
    private const string WavPhrase = "one zero zero zero one";

    [Fact]
    public Task TheWakePhraseInTheWavOpensTheCommandListen() => Run(async (shell, mic, engine) =>
    {
        shell.RefreshVoiceInputModes();
        Assert.True(shell.WakeLoopArmed);
        await Until(() => shell.VoicePromptActive, 30);
        await Until(() => { Dispatcher.UIThread.RunJobs(); return shell.Tube?.HasBubbleUp == true; }, 5);   // the listening dots
    });

    [Fact]
    public Task WithoutEntitlementOrConsentTheMicNeverOpens() => Run(async (shell, mic, engine) =>
    {
        CoreEntitlement.HasPremiumProvider = () => false;
        shell.RefreshVoiceInputModes();
        Assert.False(shell.WakeLoopArmed);
        CoreEntitlement.HasPremiumProvider = () => true;
        CoreSettings.Current.MicConsentGiven = false;
        shell.RefreshVoiceInputModes();
        Assert.False(shell.WakeLoopArmed);
        // The shell armed on open (entitled then); the reconcile closed it and nothing reopens it.
        await Until(() => !engine.IsListening && mic.Starts == mic.Stops, 5);
        var starts = mic.Starts;
        await Task.Delay(1000);
        Assert.Equal(starts, mic.Starts);
        Assert.False(engine.IsListening);
    });

    [Fact]
    public Task StopRevokeAndLapseEachCloseTheMic() => Run(async (shell, mic, engine) =>
    {
        var s = CoreSettings.Current;
        async Task Opened() { shell.RefreshVoiceInputModes(); await Until(() => engine.IsListening, 10); }
        async Task Closed() { await Until(() => !engine.IsListening && mic.Starts == mic.Stops, 5); Assert.False(shell.WakeLoopArmed); }

        await Opened();
        shell.DisarmVoiceMic();                       // She's Listening Stop
        await Closed();

        s.SpeechWakeWordEnabled = true;
        await Opened();
        shell.RevokeMicConsent();
        await Closed();

        (s.MicConsentGiven, s.SpeechWakeWordEnabled) = (true, true);
        await Opened();
        CoreEntitlement.HasPremiumProvider = () => false;   // lapse: every repaint re-reads entitlement
        shell.RefreshSheListeningTab();
        await Closed();
    });

    [Fact]
    public Task PanicAbortsTheCaptureButTheLoopStaysArmed() => Run(async (shell, mic, engine) =>
    {
        var s = CoreSettings.Current;
        (s.PanicKeyEnabled, s.PanicKey) = (true, "F8");
        s.SpeechWakeWords = "hey bambi";                 // the WAV never wakes her: the prompt is ours
        shell.RefreshVoiceInputModes();
        await Until(() => engine.IsListening, 10);
        shell.OnWakeWordHeard(null);                     // push-to-talk: a command prompt takes the mic
        await Until(() => shell.VoicePromptActive && engine.IsListening, 10);
        shell.HandlePanicKeyPress(DateTime.Now);
        await Until(() => !shell.VoicePromptActive, 3); // no re-prompt, no retry, no chain
        Assert.True(shell.WakeLoopArmed);
        var starts = mic.Starts;
        await Until(() => mic.Starts > starts && engine.IsListening, 10);   // the wake loop listens again
    });

    [Fact]
    public Task TheSpokenSafeWordWorksUnderLockdown() => Run(async (shell, mic, engine) =>
    {
        var s = CoreSettings.Current;
        s.AutonomyConsentGiven = true;
        using var ld = LockdownService.Current = new LockdownService();
        try
        {
            Assert.True(shell.SetAutonomyEnabled(true));
            ld.Activate(TimeSpan.FromMinutes(30));
            var escapes = 0;
            ld.EscapeAttempted += _ => escapes++;
            var wake = new[] { "hey bambi" };

            Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi stop taking over", wake));
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.Autonomy.IsEnabled);       // refused (#514) ...
            Assert.Equal(1, escapes);                    // ... and it trips the escape wire

            Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi red", wake));
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.Autonomy.IsEnabled);      // the safe word is never refused
        }
        finally
        {
            shell.Autonomy.Stop();
            LockdownService.Current = null;
            (s.AutonomyModeEnabled, s.AutonomyConsentGiven) = (false, false);
        }
        await Task.CompletedTask;
    });

    private static Task Run(Func<MainShellWindow, WavMic, SpeechEngine, Task> body) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var mic = new WavMic(LockCardVoiceTests.Wav());
            using var engine = new SpeechEngine(mic, new[] { LockCardVoiceTests.Model() });
            var s = CoreSettings.Current;
            var saved = (s.MicConsentGiven, s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled, s.SpeechWakeWords,
                s.AvatarEnabled, s.PanicKeyEnabled, s.PanicKey, s.SpokenMantrasEnabled, s.LockCardVoiceMode);
            (s.MicConsentGiven, s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled, s.SpeechWakeWords, s.AvatarEnabled)
                = (true, true, false, WavPhrase, true);
            CoreEntitlement.HasPremiumProvider = () => true;
            PulseMicSource.Use(engine, mic);
            var shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                await body(shell, mic, engine);
            }
            finally
            {
                shell.StopVoiceInput();
                await Until(() => !engine.IsListening, 5);
                shell.RequestExit();
                (s.MicConsentGiven, s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled, s.SpeechWakeWords,
                    s.AvatarEnabled, s.PanicKeyEnabled, s.PanicKey, s.SpokenMantrasEnabled, s.LockCardVoiceMode) = saved;
                CoreEntitlement.HasPremiumProvider = null;
                CoreSpeech.IsAvailableProvider = null;
                CoreSpeech.HasCaptureDeviceProvider = null;
                CoreSpeech.ModelStatusProvider = null;
                CoreSpeech.EnumerateInputDevicesProvider = null;
                CoreEngine.Stop();
            }
        });

    private static async Task Until(Func<bool> cond, int seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (!cond())
        {
            Assert.True(DateTime.UtcNow < end, "timed out");
            await Task.Delay(50);
        }
    }
}
