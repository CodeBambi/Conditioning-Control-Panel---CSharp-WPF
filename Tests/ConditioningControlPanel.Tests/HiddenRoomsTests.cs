using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Racing Thoughts and Piece by Piece are on main but not ready for a release. Their Play wall
/// surfaces are collapsed in PlayTabView.xaml; this tripwire fails the build the moment one of
/// them is revealed, so the reveal is a deliberate commit and not a stray reflow.
/// </summary>
public class HiddenRoomsTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Views")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found above " + AppContext.BaseDirectory);
    }

    private static string PlayXaml() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Views", "Tabs", "PlayTabView.xaml"));

    private static string OpeningTag(string xaml, string name)
    {
        var m = Regex.Match(xaml, @"<\w+[^>]*x:Name=""" + Regex.Escape(name) + @"""[^>]*>", RegexOptions.Singleline);
        Assert.True(m.Success, name + " is missing from PlayTabView.xaml");
        return m.Value;
    }

    [Theory]
    [InlineData("BtnPlayRace")]
    [InlineData("SlotPieceByPiece")]
    public void UnreleasedRoomStaysCollapsed(string name)
    {
        var tag = OpeningTag(PlayXaml(), name);
        Assert.Contains(@"Visibility=""Collapsed""", tag);
    }
}
