using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Speech;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The intake page's slice-3 requests, driven by fake page messages through the same
/// <see cref="IntakeHostWindow.HandleMessage"/> the web carrier feeds (WPF IntakeHostService.OnPageMessage).
/// No web engine and never a real mic: speech replays Vosk's test WAV.</summary>
public sealed class IntakeBridgeTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static List<JObject> Capture(IntakeHostWindow host)
    {
        var sent = new List<JObject>();
        host.Posted += json => sent.Add(JObject.Parse(json));
        return sent;
    }

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
    private static readonly byte[] Gif = "GIF89a"u8.ToArray().Concat(new byte[10]).Append((byte)0x3B).ToArray();

    [Fact]
    public Task KeptSpiralsLandInTheLibraryAndPngsStayInTheirFolder() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("intake-png-").FullName;
        var host = new IntakeHostWindow { SpiralImageFolder = dir };
        var sent = Capture(host);
        var loom = Path.Combine(DtrhLoomStore.SpiralsFolder, "loom_evil.gif");
        try
        {
            // loom-save: the page's name is slugged, so "../../evil" cannot leave Spirals/.
            host.HandleMessage(new JObject { ["type"] = "loom-save", ["name"] = "../../evil",
                ["gifBase64"] = Convert.ToBase64String(Gif), ["params"] = new JObject() }.ToString());
            Assert.Equal("loom-result", (string?)sent[^1]["type"]);
            Assert.True((bool)sent[^1]["ok"]!);
            Assert.True(File.Exists(loom));
            host.HandleMessage(new JObject { ["type"] = "loom-save", ["name"] = "x", ["gifBase64"] = Convert.ToBase64String(Png) }.ToString());
            Assert.Equal("bad-gif", (string?)sent[^1]["error"]);

            // intake-save-image: not a PNG is refused; the index is clamped and the name is the host's.
            host.HandleMessage(new JObject { ["type"] = "intake-save-image", ["pngBase64"] = Convert.ToBase64String(Gif) }.ToString());
            Assert.Equal("bad-image", (string?)sent[^1]["error"]);
            Assert.Empty(Directory.GetFiles(dir));
            host.HandleMessage(new JObject { ["type"] = "intake-save-image", ["pngBase64"] = Convert.ToBase64String(Png), ["index"] = 500 }.ToString());
            Assert.Equal("intake-save-image-result", (string?)sent[^1]["type"]);
            var path = (string)sent[^1]["path"]!;
            Assert.Equal(dir, Path.GetDirectoryName(path));
            Assert.EndsWith("-99.png", path);
            Assert.Equal(Png, File.ReadAllBytes(path));
        }
        finally
        {
            host.Close();
            Directory.Delete(dir, true);
            File.Delete(loom);
            File.Delete(Path.ChangeExtension(loom, ".json"));
        }
        return Task.CompletedTask;
    });

    private static List<FypAssetManifest.Entry> Batch() => new()
    {
        new() { Id = "scrolller/a/1", Url = "https://cdn.example/a.jpg", Type = "image" },
        new() { Id = "scrolller/a/2", Url = "https://cdn.example/b.mp4", Type = "video" },
    };

    [Fact]
    public Task NeedRemoteAppendsOnlyStillsAndAlwaysClearsTheLatch() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (source, consent) = (s.MediaSource, s.RemoteMediaConsented);
        var asked = 0;
        var host = new IntakeHostWindow { FetchRemote = () => { asked++; return Task.FromResult((Batch(), (string?)null)); } };
        var sent = Capture(host);
        void Ask() { host.HandleMessage("{\"type\":\"need-remote\"}"); Dispatcher.UIThread.RunJobs(); }
        try
        {
            // Local only, or mixed without consent: nothing is fetched and init says so.
            (s.MediaSource, s.RemoteMediaConsented) = ("local", true);
            Ask();
            (s.MediaSource, s.RemoteMediaConsented) = ("mixed", false);
            Ask();
            Assert.Equal(0, asked);
            Assert.Empty(sent);
            Assert.False((bool)JObject.FromObject(host.InitMessage())["config"]!["remoteMedia"]!);

            s.RemoteMediaConsented = true;
            Assert.True((bool)JObject.FromObject(host.InitMessage())["config"]!["remoteMedia"]!);
            Ask();
            Assert.Equal(1, asked);
            Assert.Equal("assets-append", (string?)sent[0]["type"]);
            Assert.Equal(new[] { "https://cdn.example/a.jpg" }, sent[0]["images"]!.Values<string>());
            Assert.Equal("online-status", (string?)sent[1]["type"]);
            Assert.Equal(1, (int)sent[1]["added"]!);
        }
        finally
        {
            host.Close();
            (s.MediaSource, s.RemoteMediaConsented) = (source, consent);
        }
        return Task.CompletedTask;
    });

    /// <summary>The say-it beat's engine with the clock in the test's hand: each listen window stays
    /// open until the test ends it (or the bridge cancels it, which reads as an empty timeout, as an
    /// aborted capture does). Continuations run inline, so whether the loop listens again is known the
    /// moment a window ends.</summary>
    private sealed class SteppedSpeech : SpeechEngine
    {
        private sealed class NoMic : IMicSource
        {
            public bool HasDevice => true;
            public IReadOnlyList<SpeechInputDevice> ListDevices() => Array.Empty<SpeechInputDevice>();
            public IDisposable Start(Action<byte[], int> onPcm) => throw new InvalidOperationException("never a real mic");
        }

        public SteppedSpeech() : base(new NoMic(), Array.Empty<string>()) { }
        public int Listens;
        public readonly TaskCompletionSource FirstListen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<PhraseResult>? _window;
        public bool MicOpen => _window is { Task.IsCompleted: false };
        public override bool IsAvailable => true;

        public override Task<PhraseResult> RecognizePhraseAsync(string target, RecognizeOptions? options = null, CancellationToken ct = default)
        {
            Listens++;
            var w = _window = new TaskCompletionSource<PhraseResult>();
            ct.Register(() => w.TrySetResult(new PhraseResult { TimedOut = true }));
            FirstListen.TrySetResult();
            return w.Task;
        }

        /// <summary>End the open window: an empty timeout is what an aborted capture returns.</summary>
        public void End(PhraseResult r) => _window?.TrySetResult(r);
    }

    private static async Task WithSpeech(Func<IntakeHostWindow, SteppedSpeech, List<JObject>, Task> body)
    {
        Setup();
        var engine = new SteppedSpeech();
        var host = new IntakeHostWindow { Speech = () => engine };
        var sent = Capture(host);
        var savedConsent = CoreSettings.Current.MicConsentGiven;
        CoreSpeech.HasCaptureDeviceProvider = () => true;
        try { await body(host, engine, sent); }
        finally
        {
            host.Close();
            CoreSettings.Current.MicConsentGiven = savedConsent;
            CoreSpeech.HasCaptureDeviceProvider = null;
        }
    }

    private static Task Listen(IntakeHostWindow host, SteppedSpeech engine, int id)
    {
        host.HandleMessage($"{{\"type\":\"speech-start\",\"id\":{id},\"phrase\":\"good girls obey\"}}");
        return engine.FirstListen.Task.WaitAsync(TimeSpan.FromSeconds(10));   // the loop runs off the UI thread
    }

    [Fact]
    public Task SpeakingThePhraseMatchesAndClosingStopsTheMic() => AvaloniaTestDispatcher.RunAsync(() => WithSpeech(async (host, engine, sent) =>
    {
        // No consent: refused with the wire reason, the mic never opens.
        CoreSettings.Current.MicConsentGiven = false;
        Assert.Equal("consent", (string?)JObject.FromObject(host.InitMessage())["config"]!["speech"]!["reason"]);
        host.HandleMessage("{\"type\":\"speech-start\",\"id\":1,\"phrase\":\"good girls obey\"}");
        Assert.Equal("consent", (string?)sent[^1]["reason"]);
        Assert.Equal(0, engine.Listens);

        CoreSettings.Current.MicConsentGiven = true;
        await Listen(host, engine, 2);
        engine.End(new PhraseResult { Matched = true, Transcript = "good girls obey", Score = 1, LoudEnough = true });
        Dispatcher.UIThread.RunJobs();
        var final = sent.Single(f => (string?)f["kind"] == "final");
        Assert.Equal(2, (int)final["id"]!);
        Assert.True((bool)final["matched"]!);
        Assert.Contains(sent, f => (string?)f["kind"] == "listening");
        Assert.Equal(1, engine.Listens);

        // A closed window never leaves the mic open (WPF DisposeAll -> StopSpeechBridge).
        var quiet = new SteppedSpeech();
        var quietHost = new IntakeHostWindow { Speech = () => quiet };
        await Listen(quietHost, quiet, 3);
        quietHost.Close();
        Assert.False(quiet.MicOpen);
        Assert.Equal(1, quiet.Listens);
    }));

    /// <summary>WPF GameSurfaces 'intake' -> CloseActive -> DisposeAll -> StopSpeechBridge, on every panic
    /// route. The route's own capture abort reaches the loop as an empty timeout; without the stop it
    /// read that as silence and reopened the mic for up to three more windows.</summary>
    [Theory]
    [InlineData("key")]
    [InlineData("tray")]
    [InlineData("voice")]
    public Task PanicClosesTheIntakeAndTheMicNeverReopens(string route) => AvaloniaTestDispatcher.RunAsync(() => WithSpeech(async (host, engine, sent) =>
    {
        var s = CoreSettings.Current;
        var (enabled, key) = (s.PanicKeyEnabled, s.PanicKey);
        (s.PanicKeyEnabled, s.PanicKey) = (true, "F8");
        s.MicConsentGiven = true;
        var shell = new MainShellWindow();
        var exited = false;
        shell.Closed += (_, _) => exited = true;
        try
        {
            host.Show();
            await Listen(host, engine, 7);
            Assert.True(engine.MicOpen);
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
            if (route == "key") shell.HandlePanicKeyPress(t0);
            else if (route == "tray") MainShellWindow.StopEverything();
            else shell.VoicePanic();
            engine.End(new PhraseResult { TimedOut = true });   // the route's abort, if it outran the stop
            Dispatcher.UIThread.RunJobs();

            Assert.False(engine.MicOpen);
            Assert.Equal(1, engine.Listens);
            Assert.False(host.IsVisible);
            Assert.DoesNotContain(sent, f => (string?)f["kind"] == "silence");
            if (route == "key")
            {
                // Closing the intake does not arm the exit ladder (WPF, while a game owns the screen).
                shell.HandlePanicKeyPress(t0.AddSeconds(1));
                Assert.False(exited);
            }
        }
        finally
        {
            if (!exited) shell.Close();
            (s.PanicKeyEnabled, s.PanicKey) = (enabled, key);
        }
    }));

    /// <summary>A panic rung that keeps the intake open (palette Escape, lock-card dismiss) still stops
    /// its mic, and tells the page so it stops showing "listening".</summary>
    [Fact]
    public Task PanicThatKeepsTheIntakeOpenTellsThePageTheMicStopped() => AvaloniaTestDispatcher.RunAsync(() => WithSpeech(async (host, engine, sent) =>
    {
        CoreSettings.Current.MicConsentGiven = true;
        host.Show();
        await Listen(host, engine, 9);
        IntakeHostWindow.StopMicsForPanic();
        Dispatcher.UIThread.RunJobs();
        Assert.False(engine.MicOpen);
        Assert.Equal(1, engine.Listens);
        Assert.True(host.IsVisible);
        Assert.Contains(sent, f => (string?)f["kind"] == "stopped" && (int)f["id"]! == 9);
    }));
}
