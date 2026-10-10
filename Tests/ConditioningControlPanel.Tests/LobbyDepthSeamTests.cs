using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Polish wave 10 (depth), the integrator's seam: the lobby page binds the depth paint that
/// LobbyRowView carries (lane D could only give the row its properties), and the wash hands
/// the section hue to every depth painter. LobbyRenderTests proves the page still builds.
/// </summary>
public class LobbyDepthSeamTests
{
    [Fact]
    public void TheLobbyPageWearsTheRowsDepthPaint()
    {
        var xaml = Read("ConditioningControlPanel", "Views", "Tabs", "AvailableSubjectsTabView.xaml");
        // columns are wells
        Assert.Contains("Background\" Value=\"{DynamicResource DepthWellFloorBrush}\"", xaml);
        Assert.Contains("BorderBrush\" Value=\"{DynamicResource DepthPressedBevel}\"", xaml);
        // rows float: a band under the card, the float rim on it
        Assert.Contains("Tag=\"lobby-float\" Fill=\"{Binding CardShadow}\" Height=\"{Binding CardShadowPx}\"", xaml);
        Assert.Contains("BorderBrush=\"{Binding CardRim}\"", xaml);
        // the join key wears the plank bevel and sheen
        Assert.Contains("BorderBrush=\"{Binding ButtonBevel}\"", xaml);
        Assert.Contains("Background=\"{Binding ButtonSheen}\"", xaml);
    }

    [Fact]
    public void TheWashHandsTheHueToEveryDepthPainter()
    {
        var cs = Read("ConditioningControlPanel", "MainWindow", "MainWindow.SectionChrome.cs");
        int edge = cs.IndexOf("PaintSectionEdge(hue, ms);");
        Assert.True(edge > 0);
        foreach (var call in new[] { "PaintDepthRail(hue);", "PaintDepthHud(hue);", "SettingsTab?.PaintDepthHome(hue);", "QuestsTab?.PaintDepthQuests(hue);" })
            Assert.True(cs.IndexOf(call, edge) > edge, call);
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, Path.Combine(parts));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
