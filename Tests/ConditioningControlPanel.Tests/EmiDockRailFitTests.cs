using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The EMI dock chip at the bottom of the nav rail must be the same height whether EMI is out or
/// not. The shut rail is 56px wide, so the chip's text column is 0px wide most of the time; a
/// wrapping TextBlock measured at width 0 breaks after every character, and the "avatar muted"
/// pill that appears while she is out stood ~260px tall, squeezing the doors above until one was
/// clipped to an arc (owner report, 2026-09-15). Source read, same reason as NavRailFlyoutTests.
/// </summary>
public class EmiDockRailFitTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument DockXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return XDocument.Load(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Controls", "EmiDock.xaml"));
    }

    [Fact]
    public void RootGridIsPinnedToTheRingHeight()
    {
        var root = DockXaml().Root!.Elements().First(e => e.Name.LocalName == "Grid");
        Assert.Equal("40", (string?)root.Attribute("Height"));
    }

    [Fact]
    public void NoTextInTheChipWraps()
    {
        var texts = DockXaml().Descendants().Where(e => e.Name.LocalName == "TextBlock").ToList();
        Assert.Contains(texts, t => (string?)t.Attribute(X + "Name") == "TxtMuted");
        foreach (var t in texts)
        {
            var wrap = (string?)t.Attribute("TextWrapping");
            Assert.True(wrap == null || wrap == "NoWrap",
                $"{(string?)t.Attribute(X + "Name")} wraps: at the shut rail's 0px text column that is one line per character");
        }
    }
}
