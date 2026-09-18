using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Play wall's release gate for the two rooms that shipped hidden at the 6.9.5 cut.
///
/// <para>Piece by Piece is still behind it: its strip is collapsed in PlayTabView.xaml and this
/// tripwire fails the build the moment it is revealed, so the reveal is a deliberate commit and
/// not a stray reflow.</para>
///
/// <para>Racing Thoughts came out from behind it on 2026-09-17, and the assertion INVERTED rather
/// than being deleted. That button is the only door the game has ever had - no Lab entry, no hub
/// tile, nothing but the two dev args - so a reflow that re-collapses it takes the whole game off
/// the wall with no other way in. The tripwire is worth as much pointing this way as it was
/// pointing the other.</para>
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
    [InlineData("SlotPieceByPiece")]
    public void UnreleasedRoomStaysCollapsed(string name)
    {
        var tag = OpeningTag(PlayXaml(), name);
        Assert.Contains(@"Visibility=""Collapsed""", tag);
    }

    [Fact]
    public void RacingThoughtsHasItsDoorBack()
    {
        // Not just "not Collapsed": an explicit Visibility of any kind on this button means someone
        // has started driving it from XAML again, and the last time that happened the game vanished.
        var tag = OpeningTag(PlayXaml(), "BtnPlayRace");
        Assert.DoesNotContain("Visibility=", tag);
        Assert.Contains("Click=\"BtnStartRace_Click\"", tag);
    }
}
