using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    [Fact]
    public Task NeedRemoteAppendsOnlyStillsAndAlwaysClearsTheLatch() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (source, consent) = (s.MediaSource, s.RemoteMediaConsented);
        var asked = 0;
        var host = new IntakeHostWindow
        {
            FetchRemote = () =>
            {
                asked++;
                return Task.FromResult((new List<FypAssetManifest.Entry>
                {
                    new() { Id = "scrolller/a/1", Url = "https://cdn.example/a.jpg", Type = "image" },
                    new() { Id = "scrolller/a/2", Url = "https://cdn.example/b.mp4", Type = "video" },
                }, (string?)null));
            },
        };
        var sent = Capture(host);
        try
        {
            s.MediaSource = "local";
            host.HandleMessage("{\"type\":\"need-remote\"}");
            Assert.Equal(0, asked);   // no consent / local only: nothing is fetched
            Assert.False((bool)JObject.FromObject(host.InitMessage())["config"]!["remoteMedia"]!);

            s.MediaSource = "mixed";
            s.RemoteMediaConsented = true;
            Assert.True((bool)JObject.FromObject(host.InitMessage())["config"]!["remoteMedia"]!);
            await host.ServeRemoteBatchAsync();
            Dispatcher.UIThread.RunJobs();
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
    });

    [Fact]
    public Task SpeakingThePhraseMatchesAndClosingStopsTheMic() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var mic = new LockCardVoiceTests.WavMicSource(LockCardVoiceTests.Wav());
        using var engine = new SpeechEngine(mic, new[] { LockCardVoiceTests.Model() });
        var host = new IntakeHostWindow { Speech = () => engine };
        var sent = Capture(host);
        var savedConsent = CoreSettings.Current.MicConsentGiven;
        CoreSpeech.HasCaptureDeviceProvider = () => true;
        try
        {
            // No consent: refused with the wire reason, the mic never opens.
            CoreSettings.Current.MicConsentGiven = false;
            Assert.Equal("consent", (string?)JObject.FromObject(host.InitMessage())["config"]!["speech"]!["reason"]);
            host.HandleMessage("{\"type\":\"speech-start\",\"id\":1,\"phrase\":\"one zero zero zero one\"}");
            Assert.Equal("consent", (string?)sent[^1]["reason"]);
            Assert.Equal(0, mic.Starts);

            CoreSettings.Current.MicConsentGiven = true;
            host.HandleMessage("{\"type\":\"speech-start\",\"id\":2,\"phrase\":\"one zero zero zero one\"}");
            await LockCardVoiceTests.Until(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return sent.Any(f => (string?)f["kind"] == "final");
            }, 30);
            var final = sent.First(f => (string?)f["kind"] == "final");
            Assert.Equal(2, (int)final["id"]!);
            Assert.True((bool)final["matched"]!);
            Assert.Contains(sent, f => (string?)f["kind"] == "listening");

            // A closed window never leaves the mic open (WPF DisposeAll -> StopSpeechBridge).
            // A silent mic holds its 10 s window open, so only the close can cut it inside 5 s.
            var quiet = new LockCardVoiceTests.WavMicSource(Array.Empty<byte>());
            using var quietEngine = new SpeechEngine(quiet, new[] { LockCardVoiceTests.Model() });
            var quietHost = new IntakeHostWindow { Speech = () => quietEngine };
            quietHost.HandleMessage("{\"type\":\"speech-start\",\"id\":3,\"phrase\":\"hello my darling\"}");
            await LockCardVoiceTests.Until(() => quietEngine.IsListening, 10);
            quietHost.Close();
            await LockCardVoiceTests.Until(() => quiet.Starts == quiet.Stops && !quietEngine.IsListening, 5);
        }
        finally
        {
            host.Close();
            CoreSettings.Current.MicConsentGiven = savedConsent;
            CoreSpeech.HasCaptureDeviceProvider = null;
        }
    });
}
