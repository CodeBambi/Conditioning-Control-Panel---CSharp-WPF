using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using ConditioningControlPanel.Models;
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

    /// <summary>Revoke while a command's first listen is open: the prompt ends there. Its silence must
    /// not become the spoken "you called?" re-prompt, which would reopen the mic.</summary>
    [Fact]
    public Task RevokingMidCommandClosesTheMicForGood() => Run(async (shell, mic, engine) =>
    {
        CoreSettings.Current.SpeechWakeWords = "hey bambi";   // the WAV never wakes her: the prompt is ours
        shell.RefreshVoiceInputModes();
        await Until(() => engine.IsListening, 10);
        shell.OnWakeWordHeard(null);
        await Until(() => shell.VoicePromptActive && engine.IsListening, 10);
        shell.RevokeMicConsent();
        await Until(() => !shell.VoicePromptActive && !engine.IsListening, 3);
        var starts = mic.Starts;
        await Task.Delay(2500);
        Assert.Equal(starts, mic.Starts);
        Assert.False(engine.IsListening);
    });

    /// <summary>"what can I say" and the tab's list name only what this head can run.</summary>
    [Fact]
    public Task HelpAndTheTabNameOnlyRunnableCommands() => Run(async (shell, mic, engine) =>
    {
        var help = shell.VoiceCmds.HelpLine();
        Assert.Contains("take over", help);
        Assert.DoesNotContain("spiral", help);
        Assert.DoesNotContain("quiz", help);
        shell.ShowTab("shelistening");
        Dispatcher.UIThread.RunJobs();
        var rows = global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(shell.SheListeningPage!)
            .OfType<global::Avalonia.Controls.TextBlock>().Where(t => t.Tag is string g && g.StartsWith("voice:")).ToList();
        Assert.False(rows.Single(t => t.Text!.Contains("Spiral")).IsVisible);
        Assert.True(rows.Single(t => t.Text!.Contains("Takeover")).IsVisible);
        var toys = rows.Single(t => t.Text!.Contains("Quick toys"));   // freeze runs here only if its seam is seeded
        Assert.Equal(shell.VoiceCmds.Available.Any(i => i.Name == "freeze_once"), toys.IsVisible);
        await Task.CompletedTask;
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

    /// <summary>Takeover's surprise Spoken Mantra (WPF RunSpokenMantraAsync): only picked when the mod
    /// ships mantras; the WAV says the phrase, so it matches, credits 30 XP and paints the verdict.</summary>
    [Fact]
    public Task TakeoverSpokenMantraHeardInTheWavCreditsXp() => WithMantras(async (shell, mic, engine, xp) =>
    {
        CoreProgression.TrackMantraCompletedProvider = () => xp.Add(-1);
        shell.PerformAutonomy(AutonomyActionType.SpokenMantra);
        await Until(() => { Dispatcher.UIThread.RunJobs(); return xp.Contains(30); }, 30);
        Assert.Contains(-1, xp);                         // the quest/program verifier too
        Dispatcher.UIThread.RunJobs();
        var tab = shell.FindControl<Control>("BambiTakeoverTab")!;
        Assert.Equal("\u2713 MATCHED", tab.FindControl<TextBlock>("TxtVoiceVerdict")!.Text);
        Assert.True(tab.FindControl<Border>("VoiceVerdictChip")!.IsVisible);
    });

    /// <summary>Panic while she is still saying the prompt: the mic never opens, nothing is credited.</summary>
    [Fact]
    public Task PanicBeforeTheMantraListenKeepsTheMicShut() => WithMantras(async (shell, mic, engine, xp) =>
    {
        var s = CoreSettings.Current;
        (s.PanicKeyEnabled, s.PanicKey) = (true, "F8");
        var starts = mic.Starts;
        shell.PerformAutonomy(AutonomyActionType.SpokenMantra);
        await Until(() => shell.VoicePromptActive, 3);
        shell.HandlePanicKeyPress(DateTime.Now);
        await Until(() => !shell.VoicePromptActive, 3);
        await Task.Delay(2500);
        Assert.Equal(starts, mic.Starts);
        Assert.Empty(xp);
    });

    private static Task WithMantras(Func<MainShellWindow, WavMic, SpeechEngine, System.Collections.Generic.List<double>, Task> body) =>
        Run(async (shell, mic, engine) =>
        {
            var s = CoreSettings.Current;
            s.SpeechWakeWordEnabled = false;             // Takeover's mantra only while the user is not driving the mic
            shell.RefreshVoiceInputModes();
            await Until(() => !engine.IsListening, 5);
            var dir = System.IO.Directory.CreateTempSubdirectory("ccp-mantras-").FullName;
            var audio = System.IO.Path.Combine(dir, "resources", "sounds", "companion_audio");
            System.IO.Directory.CreateDirectory(audio);
            var (pkg, id) = (CoreMods.ActiveModPackageProvider, CoreMods.ActiveModIdProvider);
            var (addXp, track) = (CoreProgression.AddXPProvider, CoreProgression.TrackMantraCompletedProvider);
            var xp = new System.Collections.Generic.List<double>();
            var shotId = "spoken-mantra-test-" + Guid.NewGuid().ToString("N");
            CoreMods.ActiveModIdProvider = () => shotId;
            CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = shotId }, dir, isBuiltIn: false);
            s.AutonomyConsentGiven = true;
            try
            {
                Assert.False(shell.Autonomy.CanPerform(AutonomyActionType.SpokenMantra));   // no mantras.json: never picked
                System.IO.File.WriteAllText(System.IO.Path.Combine(audio, "mantras.json"),
                    "{\"mantras\":[{\"id\":\"m1\",\"phrase\":\"" + WavPhrase + "\",\"promptText\":\"Say it\"}]}");
                shotId += "-2";                              // a fresh mod id reloads the set
                Assert.True(shell.Autonomy.CanPerform(AutonomyActionType.SpokenMantra));
                Assert.True(shell.SetAutonomyEnabled(true));
                Dispatcher.UIThread.RunJobs();
                CoreProgression.AddXPProvider = (amount, _) => { lock (xp) xp.Add(amount); };
                await body(shell, mic, engine, xp);
            }
            finally
            {
                (CoreProgression.AddXPProvider, CoreProgression.TrackMantraCompletedProvider) = (addXp, track);
                shell.Autonomy.Stop();
                (CoreMods.ActiveModPackageProvider, CoreMods.ActiveModIdProvider) = (pkg, id);
                (s.AutonomyModeEnabled, s.AutonomyConsentGiven) = (false, false);
                System.IO.Directory.Delete(dir, true);
            }
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
