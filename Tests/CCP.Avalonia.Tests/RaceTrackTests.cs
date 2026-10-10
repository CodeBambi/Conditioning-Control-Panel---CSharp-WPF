using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Racing Thoughts, the player's own tracks (ledger P5): a pick is charted and answered with
/// progress and two charts, the transport frames drive the player and the 250 ms clock, the file
/// running out is track-ended, and every failure reaches the page as track-error.</summary>
public sealed class RaceTrackTests
{
    private sealed class FakePlayer : IRaceTrackPlayer
    {
        public event Action? Ended;
        public string? Loaded;
        public int Plays, Stops;
        public bool Paused, Disposed, FailLoad;
        public double PositionSec { get; set; }
        public double DurationSec => 30;
        public bool IsPlaying { get; private set; }
        public void Load(string path)
        {
            if (FailLoad) throw new InvalidOperationException(RaceTrackPlayer.NoAudioMessage);
            Loaded = path;
        }
        public void RefreshVolume() { }
        public void Play() { Plays++; IsPlaying = true; Paused = false; }
        public void Pause() { Paused = true; IsPlaying = false; }
        public void Resume() { Paused = false; IsPlaying = true; }
        public void Stop() { Stops++; IsPlaying = false; }
        public void Dispose() => Disposed = true;
        public void RunOut() { IsPlaying = false; Ended?.Invoke(); }
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static (GameWindow Window, List<JObject> Posted, FakePlayer Player) Open()
    {
        EnsureApp();
        var w = new GameWindow(RaceWindow.Spec);
        w.RaceOwnedTracks = () => new[] { 0 };
        w.RaceCanLaunch = () => true;
        var player = new FakePlayer();
        w.RaceNewPlayer = () => player;
        var posted = new List<JObject>();
        w.Posted += json => { lock (posted) posted.Add(JObject.Parse(json)); };
        w.StartRace();
        w.Show();
        w.HandleMessage("{\"type\":\"ready\"}");
        return (w, posted, player);
    }

    private static List<JObject> Of(List<JObject> posted, string type)
    {
        lock (posted) return posted.Where(p => (string?)p["type"] == type).ToList();
    }

    /// <summary>A 16 kHz mono WAV (the shape Core reads without a transcoder), unique per call.</summary>
    private static string WriteWav(double seconds)
    {
        var path = Path.Combine(Path.GetTempPath(), "ccp-race-" + Guid.NewGuid().ToString("N") + ".wav");
        int frames = (int)(16000 * seconds);
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8.ToArray()); w.Write(36 + frames * 2); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(frames * 2);
        var rng = new Random();
        for (int i = 0; i < frames; i++)
        {
            double t = i / 16000.0;
            double env = 0.15 + 0.8 * Math.Pow(Math.Sin(Math.PI * t / 2.0), 2);
            w.Write((short)(env * 20000 * Math.Sin(2 * Math.PI * 220 * t) + rng.Next(-200, 200)));
        }
        return path;
    }

    private static async Task Settle(GameWindow w)
    {
        await w.RaceAnalysis;
        await Task.Delay(150);   // the worker's posts hop to the UI thread
    }

    [Fact]
    public async Task APick_IsLoaded_Charted_AndAnsweredWithProgressAndTwoCharts()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (w, posted, player) = Open();
            var path = WriteWav(30);
            try
            {
                w.RacePickFile = () => Task.FromResult<string?>(path);
                w.HandleMessage("{\"type\":\"track-pick\"}");
                await Task.Delay(100);   // the pick is queued behind the dialog
                await Settle(w);

                Assert.Equal(path, player.Loaded);
                var progress = Of(posted, "track-progress");
                Assert.Contains(progress, p => (string?)p["stage"] == "decode" && (string?)p["name"] == Path.GetFileName(path));
                Assert.DoesNotContain(Of(posted, "track-error"), _ => true);

                var charts = Of(posted, "track-chart");
                Assert.Equal(2, charts.Count);
                Assert.True((bool)charts[0]["partial"]!);
                Assert.False((bool)charts[1]["partial"]!);
                Assert.False((bool)charts[1]["authored"]!);
                var chart = (JObject)charts[1]["chart"]!;
                Assert.Equal(Path.GetFileName(path), (string?)chart["source"]!["name"]);
                Assert.Equal(30.0, (double)chart["source"]!["durationSec"]!, 1);
                Assert.NotEmpty((JArray)chart["acts"]!);
                Assert.False((bool)chart["analysis"]!["partial"]!);

                // The same file again is a cache hit: one whole chart, nothing analysed.
                int before = charts.Count;
                w.RaceBeginTrack(path);
                await Settle(w);
                var again = Of(posted, "track-chart");
                Assert.Equal(before + 1, again.Count);
                Assert.False((bool)again[^1]["partial"]!);
            }
            finally { w.Close(); File.Delete(path); }
        });
    }

    [Fact]
    public async Task Transport_PlayPauseStop_DriveThePlayerAndTheClock_AndTheEndIsTrackEnded()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (w, posted, player) = Open();
            var path = WriteWav(4);
            try
            {
                // Nothing loaded: the transport frames are quiet, not errors.
                w.HandleMessage("{\"type\":\"track-play\"}");
                Assert.Empty(Of(posted, "track-clock"));

                w.RaceBeginTrack(path);
                await Settle(w);

                w.HandleMessage("{\"type\":\"track-play\"}");
                Assert.Equal(1, player.Plays);
                var clock = Of(posted, "track-clock")[^1];
                Assert.True((bool)clock["playing"]!);
                Assert.Equal(30.0, (double)clock["durationSec"]!);

                player.PositionSec = 1.23456;
                w.HandleMessage("{\"type\":\"track-pause\",\"on\":true}");
                Assert.True(player.Paused);
                clock = Of(posted, "track-clock")[^1];
                Assert.False((bool)clock["playing"]!);
                Assert.Equal(1.235, (double)clock["t"]!, 3);

                w.HandleMessage("{\"type\":\"track-pause\",\"on\":false}");
                Assert.False(player.Paused);
                Assert.True((bool)Of(posted, "track-clock")[^1]["playing"]!);

                player.RunOut();
                Assert.Single(Of(posted, "track-ended"));

                int stops = player.Stops;
                w.HandleMessage("{\"type\":\"track-stop\"}");
                Assert.True(player.Stops > stops);
                Assert.Single(Of(posted, "track-ended"));   // a stop the run asked for is not an ending
            }
            finally { w.Close(); File.Delete(path); }
            Assert.True(player.Disposed);   // the window closing (panic included) ends the audio
        });
    }

    [Fact]
    public async Task Errors_ReachThePageAsTrackError_AndACancelledPickIsACancelledPlate()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (w, posted, player) = Open();
            var junk = Path.Combine(Path.GetTempPath(), "ccp-race-" + Guid.NewGuid().ToString("N") + ".mp3");
            File.WriteAllText(junk, "this is not audio");
            try
            {
                // The dialog closed with nothing picked.
                w.RacePickFile = () => Task.FromResult<string?>(null);
                w.HandleMessage("{\"type\":\"track-pick\"}");
                await Task.Delay(100);
                Assert.Contains(Of(posted, "track-progress"), p => (string?)p["stage"] == "cancelled");
                Assert.Empty(Of(posted, "track-error"));

                // track-cancel with nothing running is answered by the host itself.
                int cancelled = Of(posted, "track-progress").Count(p => (string?)p["stage"] == "cancelled");
                w.HandleMessage("{\"type\":\"track-cancel\"}");
                Assert.Equal(cancelled + 1, Of(posted, "track-progress").Count(p => (string?)p["stage"] == "cancelled"));

                // A player that cannot open the file says so with its own message.
                player.FailLoad = true;
                w.RaceBeginTrack(junk);
                Assert.Contains(Of(posted, "track-error"), p => (string?)p["message"] == RaceTrackPlayer.NoAudioMessage);

                // And a file that will not decode is an error from the analysis, never a crash.
                await Settle(w);
                Assert.True(Of(posted, "track-error").Count >= 2);
                Assert.Empty(Of(posted, "track-chart"));

                // A dialog that throws is an error too.
                w.RacePickFile = () => throw new IOException("the dialog would not open");
                w.HandleMessage("{\"type\":\"track-pick\"}");
                await Task.Delay(100);
                Assert.Contains(Of(posted, "track-error"), p => (string?)p["message"] == "the dialog would not open");
            }
            finally { w.Close(); File.Delete(junk); }
        });
    }

    [Fact]
    public void Volume_IsTheMasterTimesTheGain_OnLibVlcsCubicScale()
    {
        Assert.Equal(100, RaceTrackPlayer.VlcVolume(100, 1f));
        Assert.Equal(0, RaceTrackPlayer.VlcVolume(0, 1f));
        Assert.Equal(79, RaceTrackPlayer.VlcVolume(50, 1f));    // -6 dB, not -18
        Assert.Equal(79, RaceTrackPlayer.VlcVolume(100, 0.5f));
        Assert.Equal(100, RaceTrackPlayer.VlcVolume(250, 3f));
    }

    [Fact]
    public void TheSoutChain_WritesA16kMonoWav_AndSurvivesAWindowsPath()
    {
        var sout = RaceTrackPlayer.SoutFor(@"C:\Users\O'Neil\race\decode\x.wav");
        Assert.Contains("acodec=s16l,channels=1,samplerate=16000", sout);
        Assert.Contains("mux=wav", sout);
        Assert.Contains(@"dst='C:/Users/O\'Neil/race/decode/x.wav'", sout);
    }

    /// <summary>The real thing, where libvlc is beside the tests: an mp3 goes through the sout chain to
    /// a WAV Core reads, and the player plays it out to Ended. Quiet on a machine with no libvlc.</summary>
    [Fact]
    public async Task LibVlc_DecodesARealMp3_AndPlaysItOutToEnded()
    {
        try { _ = new ConditioningControlPanel.Avalonia.Platform.LibVlcAudio("--aout=dummy"); }
        catch { }
        if (ConditioningControlPanel.Avalonia.Platform.LibVlcAudio.Shared == null) return;
        var mp3 = Path.Combine(AppContext.BaseDirectory, "Resources", "AwarenessPresets", "audio", "clicker.mp3");
        if (!File.Exists(mp3) || new FileInfo(mp3).Length < 4096) return;   // an LFS pointer is not audio

        var wav = Path.Combine(Path.GetTempPath(), "ccp-race-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            await Task.Run(() => RaceTrackPlayer.TranscodeToWav(mp3, wav, null, System.Threading.CancellationToken.None));
            var pcm = ConditioningControlPanel.Services.Race.TrackDecoder.TryReadWav16kMono(wav, System.Threading.CancellationToken.None);
            Assert.NotNull(pcm);
            Assert.True(pcm!.Length > 1600, "decoded " + pcm.Length + " samples");
            Assert.Contains(pcm, v => Math.Abs(v) > 0.01f);

            var junk = wav + ".mp3";
            File.WriteAllText(junk, "this is not audio");
            try
            {
                var bad = wav + ".out.wav";
                bool threw = false, empty = false;
                try { await Task.Run(() => RaceTrackPlayer.TranscodeToWav(junk, bad, null, System.Threading.CancellationToken.None)); }
                catch (InvalidDataException) { threw = true; }
                if (!threw)
                {
                    var none = File.Exists(bad) ? ConditioningControlPanel.Services.Race.TrackDecoder.TryReadWav16kMono(bad, System.Threading.CancellationToken.None) : null;
                    empty = none == null || none.Length == 0;
                }
                try { File.Delete(bad); } catch { }
                Assert.True(threw || empty, "a file that is not audio must not decode to samples");
            }
            finally { File.Delete(junk); }
        }
        finally { try { File.Delete(wav); } catch { } }

        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            using var player = new RaceTrackPlayer();
            bool ended = false;
            player.Ended += () => ended = true;
            player.Load(mp3);
            player.Play();
            for (int i = 0; i < 100 && !ended; i++) await Task.Delay(100);
            Assert.True(ended, "the file running out raises Ended");
            Assert.True(player.DurationSec > 0);

            // A Stop the run asked for never reads as an ending.
            ended = false;
            player.Play();
            player.Stop();
            await Task.Delay(400);
            Assert.False(ended);
        });
    }
}
