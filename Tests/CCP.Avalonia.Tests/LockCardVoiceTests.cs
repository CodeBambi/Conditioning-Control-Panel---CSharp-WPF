using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF LockCardWindow.xaml.cs:956-1089 on this head: a voice card is solved by speaking,
/// fed by a WAV (Vosk's test.wav, never a real mic). CCP_LOCKCARD_FRAMES=&lt;dir&gt; saves each
/// voice-panel state as a PNG.</summary>
public sealed class LockCardVoiceTests
{
    private const string Spoken = "one zero zero zero one";   // what the fixture says

    [Fact]
    public Task SpeakingThePhraseCompletesTheCard() => Run(Wav(), Spoken, async card =>
    {
        Snap(card, "voice-listening");
        await Until(() => card.IsCompleted, 30);
        Snap(card, "voice-yes");
    });

    [Fact]
    public Task AWrongPhraseKeepsTheCardOpen() => Run(Wav(), "hello my darling", async card =>
    {
        await Until(() => card.VoiceState.StartsWith("✗"), 30);
        Snap(card, "voice-again");
        Assert.False(card.IsCompleted);
        Assert.True(LockCardWindow.IsAnyOpen());
    });

    [Fact]
    public Task SixUnavailableAttemptsFallBackToTyping() => Run(Array.Empty<byte>(), Spoken, async card =>
    {
        await Until(() => !card.VoiceMode, 15);
        Snap(card, "voice-fallback-typing");
        Assert.False(card.IsCompleted);
    }, holdMic: true);

    private static Task Run(byte[] pcm, string phrase, Func<LockCardWindow, Task> body, bool holdMic = false) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var mic = new WavMicSource(pcm);
            using var engine = new SpeechEngine(mic, new[] { Model() });
            var consent = CoreSettings.Current.MicConsentGiven;
            using var hold = new CancellationTokenSource();
            try
            {
                PulseMicSource.Use(engine, mic);
                CoreSettings.Current.MicConsentGiven = true;
                // Another owner holding the mic: every card listen comes back Unavailable.
                if (holdMic) _ = engine.RecognizePhraseAsync(Spoken, new RecognizeOptions { Timeout = TimeSpan.FromSeconds(60) }, hold.Token);
                LockCardWindow.ShowOnAllMonitors(phrase, 1, strictMode: false, isTest: true, voiceMode: true);
                Dispatcher.UIThread.RunJobs();
                var card = LockCardWindow.Primary!;
                Assert.True(card.VoiceMode, "voice was requested with engine + consent but the card is typing");
                await body(card);
            }
            finally
            {
                hold.Cancel();
                LockCardWindow.ForceCloseAll();
                CoreSettings.Current.MicConsentGiven = consent;
                CoreSpeech.IsAvailableProvider = null;
                CoreSpeech.HasCaptureDeviceProvider = null;
                CoreSpeech.ModelStatusProvider = null;
                CoreSpeech.EnumerateInputDevicesProvider = null;
            }
        });

    private static async Task Until(Func<bool> cond, int seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (!cond())
        {
            Assert.True(DateTime.UtcNow < end, "timed out waiting for the card");
            await Task.Delay(50);
        }
    }

    private static void Snap(LockCardWindow card, string name)
    {
        if (Environment.GetEnvironmentVariable("CCP_LOCKCARD_FRAMES") is not { Length: > 0 } dir) return;
        Dispatcher.UIThread.RunJobs();
        Directory.CreateDirectory(dir);
        card.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
    }

    private static string Model()
    {
        var model = Environment.GetEnvironmentVariable("CCP_VOSK_MODEL") is { Length: > 0 } m ? m
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "ccp-test", "vosk", "vosk-model-small-en-us-0.15");
        if (!Directory.Exists(model))
        {
            var msg = $"No Vosk model at {model} (run .github/scripts/fetch-vosk-model.sh)";
            if (Environment.GetEnvironmentVariable("CCP_REQUIRE_VOSK") == "1") Assert.Fail(msg);
            Assert.Skip(msg);
        }
        return model;
    }

    private static byte[] Wav([CallerFilePath] string here = "")
    {
        var b = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(here)!, "..", "CCP.Core.Tests", "Fixtures", "vosk-test.wav"));
        for (int i = 12; i + 8 <= b.Length; i += 8 + BitConverter.ToInt32(b, i + 4))
            if (b[i] == 'd' && b[i + 1] == 'a' && b[i + 2] == 't' && b[i + 3] == 'a')
                return b.AsSpan(i + 8, BitConverter.ToInt32(b, i + 4)).ToArray();
        throw new InvalidDataException("no data chunk");
    }

    /// <summary>Replays PCM in 50 ms chunks (5x real time), then silence until stopped.</summary>
    private sealed class WavMicSource(byte[] pcm) : IMicSource
    {
        public bool HasDevice => true;
        public IReadOnlyList<SpeechInputDevice> ListDevices() => new[] { new SpeechInputDevice(-1, "wav") };

        public IDisposable Start(Action<byte[], int> onPcm)
        {
            var cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                const int Chunk = 1600;
                for (int off = 0; !cts.IsCancellationRequested; off += Chunk)
                {
                    var buf = new byte[Chunk];
                    if (off < pcm.Length) Array.Copy(pcm, off, buf, 0, Math.Min(Chunk, pcm.Length - off));
                    onPcm(buf, Chunk);
                    try { await Task.Delay(10, cts.Token); } catch (OperationCanceledException) { }
                }
            });
            return cts;
        }
    }
}
