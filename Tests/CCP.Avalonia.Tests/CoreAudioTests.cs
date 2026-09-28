using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>LibVlcAudio's Linux ducking with pactl stubbed: WPF's ref count, generation and
/// force-unduck rules, and that exit restores what it ducked.</summary>
public sealed class CoreAudioTests
{
    private const string OneStream =
        "[{\"index\":7,\"volume\":{\"front-left\":{\"value\":65536},\"front-right\":{\"value\":65536}},\"properties\":{\"application.process.id\":\"1\"}}]";

    private static (LibVlcAudio audio, List<string> sets) Make()
    {
        var sets = new List<string>();
        var audio = new LibVlcAudio(args =>
        {
            if (args.StartsWith("-f json")) return OneStream;
            lock (sets) sets.Add(args);
            return "";
        });
        return (audio, sets);
    }

    [Fact]
    public void DuckUnduckRefCountsAndRestores()
    {
        var (audio, sets) = Make();
        audio.Duck(80);
        audio.Duck(80);
        audio.Drain();
        audio.Unduck(-1);
        Assert.True(audio.IsDucked);          // one ref still held
        audio.Unduck(-1);
        audio.Shutdown();                      // drains the queued pactl work
        Assert.False(audio.IsDucked);
        Assert.Equal(new[] { "set-sink-input-volume 7 13107 13107", "set-sink-input-volume 7 65536 65536" }, sets);
    }

    [Fact]
    public void ForceUnduckMakesOldGenerationStale()
    {
        var (audio, sets) = Make();
        audio.Duck(80);
        audio.ForceUnduck();                   // what the 5-minute watchdog calls
        Assert.False(audio.IsDucked);
        audio.Duck(80);
        audio.Unduck(0);                       // generation 0 predates the force: ignored
        Assert.True(audio.IsDucked);
        audio.Unduck(1);
        Assert.False(audio.IsDucked);
    }

    [Fact]
    public void ShutdownRestoresDuckedApps()
    {
        var (audio, sets) = Make();
        audio.Duck(50);
        audio.Drain();
        audio.Shutdown();
        Assert.False(audio.IsDucked);
        Assert.Equal(new[] { "set-sink-input-volume 7 32768 32768", "set-sink-input-volume 7 65536 65536" }, sets);
    }
}
