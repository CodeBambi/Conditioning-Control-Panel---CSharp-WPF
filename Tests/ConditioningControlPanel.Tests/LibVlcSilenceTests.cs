using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1260: the Bubble Count video lost its sound part way through while the bubble pops
/// kept playing. LibVLC keeps Mute and Volume on one audio session per process, so a silent
/// preview player (a picture clip on a flash or bubble) that set <c>Mute = true</c> muted the game
/// video too. A source scan, because the bug is one line anywhere in the app, not one class.
/// </summary>
public sealed class LibVlcSilenceTests
{
    // Mute = true anywhere in a LibVLC file (WPF's MediaElement says IsMuted), and Volume = 0 in a
    // player initializer or on a player field (a MediaElement's media.Volume = 0 is its own element).
    private static readonly Regex SilencingWrite = new(
        @"\bMute\s*=\s*true\b|[{,]\s*Volume\s*=\s*0\s*[,}]|[Pp]layer!?\.Volume\s*=\s*0\s*;",
        RegexOptions.Compiled);

    [Fact]
    public void NoLibVlcPlayerIsSilencedThroughMuteOrVolume()
    {
        var offenders = Directory
            .EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(f => (File: f, Text: File.ReadAllText(f)))
            .Where(x => x.Text.Contains("LibVLCSharp"))
            .SelectMany(x => x.Text.Split('\n')
                .Select((line, i) => (x.File, Line: i + 1, Code: line.Trim()))
                .Where(l => !l.Code.StartsWith("//") && SilencingWrite.IsMatch(l.Code)))
            .Select(l => $"{Path.GetFileName(l.File)}:{l.Line}: {l.Code}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Silence a LibVLC player with LibVlcSilence.NoAudioOption on its Media, never Mute/Volume:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void ScannerCatchesTheShapesThatBrokeBubbleCount()
    {
        Assert.Matches(SilencingWrite, "_player = new MediaPlayer(vlc) { Mute = true, EnableHardwareDecoding = false };");
        Assert.Matches(SilencingWrite, "var player = new VlcMediaPlayer(libvlc) { Volume = 0 };");
        Assert.Matches(SilencingWrite, "_mediaPlayer.Mute = true; // muted tutorial loop");
        Assert.DoesNotMatch(SilencingWrite, "mediaPlayer.Mute = effective <= 0;");
        Assert.Matches(SilencingWrite, "_mediaPlayer.Volume = 0;");
        Assert.DoesNotMatch(SilencingWrite, "_mediaPlayer.Volume = Math.Clamp(volume, 0, 100);");
        Assert.DoesNotMatch(SilencingWrite, "media.Volume = 0; // a WPF MediaElement");
    }

    [Fact]
    public void TheSilentOptionIsLibVlcsNoAudio()
    {
        Assert.Equal(":no-audio", LibVlcSilence.NoAudioOption);
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "ConditioningControlPanel project not found above " + AppContext.BaseDirectory);
    }
}
