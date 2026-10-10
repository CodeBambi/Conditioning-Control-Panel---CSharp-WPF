using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Home slideshow (Tonight Board) plays no sound (owner, 2026-10-07: a note out of nowhere on
/// every card change confused people). All four board cues return before playing anything.
/// </summary>
public class BoardSilentTests
{
    [Fact]
    public void TheBoardIsSilent() => Assert.False(LauncherSfx.BoardSoundOn);

    [Theory]
    [InlineData("BoardCardChange")]
    [InlineData("BoardPress")]
    [InlineData("BoardSnooze")]
    [InlineData("BoardTouch")]
    public void EveryBoardCue_ReturnsBeforePlaying(string cue)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services", "Launcher", "LauncherSfx.cs"));
        var m = Regex.Match(src, $@"public static void {cue}\([^)]*\)\s*\{{\s*if \(!BoardSoundOn\) return;");
        Assert.True(m.Success, $"{cue} does not start with the BoardSoundOn guard");
    }
}
