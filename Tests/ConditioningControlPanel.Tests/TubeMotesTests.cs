using System.IO;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Controls;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Polish wave 13 (owner, 2026-10-07): the smoke is gone from the tube art and the app draws its
/// own motes inside the glass. These pin where the motes go (the chamber's inside, attached and
/// detached), that the canvas keeps its focus gate for everyone else, and that the tube's canvas
/// can never take a click.
/// </summary>
public class TubeMotesTests
{
    [Fact]
    public void TheMotesSitInsideTheChamberAttachedAndDetached()
    {
        var a = AvatarTubeWindow.TubeMotesBox(detached: false);
        Assert.Equal(273.1, a.X, 1);
        Assert.Equal(552.6, a.Y, 1);
        Assert.Equal(125.7, a.Width, 1);
        Assert.Equal(269.3, a.Height, 1);

        var d = AvatarTubeWindow.TubeMotesBox(detached: true);
        Assert.Equal(121.9, d.X, 1);
        Assert.Equal(540.4, d.Y, 1);
        Assert.True(d.Right < a.X, "the detached chamber sits left of the attached one");

        // inside the 780x1080 design canvas
        foreach (var r in new[] { a, d })
            Assert.True(r.X >= 0 && r.Y >= 0 && r.Right <= 780 && r.Bottom <= 1080);
    }

    [Fact]
    public void OnlyTheTubeRunsWhileItsWindowIsInactive()
    {
        Assert.False(new AmbientFxConfig().RunWhileInactive);

        var root = RepoRoot();
        var windowing = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "AvatarTube", "AvatarTubeWindow.Windowing.cs"));
        Assert.Contains("RunWhileInactive = true", windowing);
        Assert.Contains("ApplyTubeMotes(useAlternative", windowing);

        // MainWindow surfaces keep the focus gate (#550 idle parking).
        foreach (var f in Directory.GetFiles(Path.Combine(root, "ConditioningControlPanel", "MainWindow"), "*.cs"))
            Assert.DoesNotContain("RunWhileInactive", File.ReadAllText(f));

        var xaml = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "AvatarTube", "AvatarTubeWindow.xaml"));
        var tag = Regex.Match(xaml, "<fx:AmbientFxCanvas x:Name=\"TubeMotes\"[^>]*>", RegexOptions.Singleline).Value;
        Assert.Contains("IsHitTestVisible=\"False\"", tag);
        Assert.Contains("ClipToBounds=\"True\"", tag);
        // behind her: declared after the tube art and before the avatar
        Assert.True(xaml.IndexOf("x:Name=\"ImgTubeFrame\"") < xaml.IndexOf("x:Name=\"TubeMotes\""));
        Assert.True(xaml.IndexOf("x:Name=\"TubeMotes\"") < xaml.IndexOf("x:Name=\"AvatarBorder\""));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }
}
