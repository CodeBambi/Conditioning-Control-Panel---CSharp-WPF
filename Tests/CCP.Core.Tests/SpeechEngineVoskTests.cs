using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The Core speech engine end to end: a real Vosk model, a real grammar session, fed by a WAV
/// replayed in 50 ms chunks the way a mic delivers them. The model is not in the repo; fetch it
/// with .github/scripts/fetch-vosk-model.sh and point CCP_VOSK_MODEL at it (default
/// ~/.cache/ccp-test/vosk/vosk-model-small-en-us-0.15). With CCP_REQUIRE_VOSK=1 a missing model
/// fails instead of skipping, so CI cannot go green without running these.
/// </summary>
public class SpeechEngineVoskTests
{
    // Fixtures/vosk-test.wav: Vosk's own Apache-2.0 test.wav, trimmed to its first utterance.
    private const string Spoken = "one zero zero zero one";

    private static string Fixture([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "vosk-test.wav");

    private static SpeechEngine Engine(byte[] pcm)
    {
        var model = Environment.GetEnvironmentVariable("CCP_VOSK_MODEL") is { Length: > 0 } m ? m
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "ccp-test", "vosk", "vosk-model-small-en-us-0.15");
        if (SpeechEngine.ResolveModelDir(model) == null)
        {
            var msg = $"No Vosk model at {model} (run .github/scripts/fetch-vosk-model.sh)";
            if (Environment.GetEnvironmentVariable("CCP_REQUIRE_VOSK") == "1") Assert.Fail(msg);
            Assert.Skip(msg);
        }
        var engine = new SpeechEngine(new WavMicSource(pcm), new[] { model });
        Assert.True(engine.IsAvailable, $"Vosk refused the model: {engine.ModelStatus}");
        return engine;
    }

    private static byte[] WavPcm()
    {
        var b = File.ReadAllBytes(Fixture());
        for (int i = 12; i + 8 <= b.Length; i += 8 + BitConverter.ToInt32(b, i + 4))
            if (b[i] == 'd' && b[i + 1] == 'a' && b[i + 2] == 't' && b[i + 3] == 'a')
                return b.AsSpan(i + 8, BitConverter.ToInt32(b, i + 4)).ToArray();
        throw new InvalidDataException("no data chunk");
    }

    [Fact]
    public async Task Matches_the_spoken_phrase()
    {
        using var engine = Engine(WavPcm());
        var r = await engine.RecognizePhraseAsync(Spoken, new RecognizeOptions { Timeout = TimeSpan.FromSeconds(20) }, TestContext.Current.CancellationToken);
        Assert.True(r.Matched, $"heard '{r.Transcript}' score {r.Score} loud {r.LoudEnough} timedOut {r.TimedOut}");
        Assert.Equal(Spoken, SpeechEngine.Normalize(r.Transcript));
    }

    [Fact]
    public async Task Rejects_a_different_phrase()
    {
        using var engine = Engine(WavPcm());
        var r = await engine.RecognizePhraseAsync("hello my darling", new RecognizeOptions { Timeout = TimeSpan.FromSeconds(20) }, TestContext.Current.CancellationToken);
        Assert.False(r.Matched, $"matched on '{r.Transcript}' score {r.Score}");
        Assert.False(r.Unavailable);
        Assert.False(r.TimedOut);
        Assert.NotEmpty(r.Transcript);
    }

    [Fact]
    public async Task Times_out_on_silence()
    {
        using var engine = Engine(Array.Empty<byte>());
        var r = await engine.RecognizePhraseAsync(Spoken, new RecognizeOptions { Timeout = TimeSpan.FromSeconds(1.5) }, TestContext.Current.CancellationToken);
        Assert.True(r.TimedOut);
        Assert.False(r.Matched);
        Assert.False(r.Unavailable);
    }

    [Fact]
    public async Task Wakes_on_the_first_configured_phrase_from_the_she_listening_rules()
    {
        using var engine = Engine(WavPcm());
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        var words = VoiceInputRules.ExpandWakeVariants(VoiceInputRules.WakeWords($" {Spoken} ;hey bambi,HEY BAMBI"));
        Assert.Equal(SpeechEngine.Normalize(Spoken), SpeechEngine.Normalize(await engine.WaitForWakeWordAsync(words, cts.Token)));
    }

    /// <summary>Replays PCM in 50 ms chunks (10 ms apart, 5x real time), then silence until stopped.</summary>
    private sealed class WavMicSource(byte[] pcm) : IMicSource
    {
        public bool HasDevice => true;
        public IReadOnlyList<SpeechInputDevice> ListDevices() => new[] { new SpeechInputDevice(-1, "wav") };

        public IDisposable Start(Action<byte[], int> onPcm)
        {
            var cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                const int Chunk = 1600; // 50 ms of 16 kHz s16 mono
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
