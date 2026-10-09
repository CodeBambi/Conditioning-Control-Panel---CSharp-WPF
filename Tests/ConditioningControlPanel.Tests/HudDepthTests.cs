using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 10, lane B (the HUD): the band is a RECESS, the XP track a SUNKEN groove with
/// a TUBE in it, the LVL chip and stat pills RAISED coins, the START row RAISED planks (START the
/// one chunky one) that press through <see cref="HudPlank"/>. These pin which brush each element
/// wears, the travel rule on the planks, and that every other PinkButton draws as before.
/// </summary>
public class HudDepthTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }

    private static string AppFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()))
            .Replace("\r\n", "\n");

    /// <summary>The opening tag (and everything to its matching first close) of a named element.</summary>
    private static string Named(string xaml, string tag, string name, string close)
    {
        var m = Regex.Match(xaml, "<" + tag + " x:Name=\"" + name + "\".*?" + Regex.Escape(close), RegexOptions.Singleline);
        Assert.True(m.Success, name + " is gone");
        return m.Value;
    }

    // ---- the press rule ----------------------------------------------------------------------

    [Fact]
    public void APlankLiftsOnHoverDropsOnPressAndNeverSitsLit()
    {
        Assert.Equal(0, HudPlank.Travel(enabled: true, pressed: false, hovered: false));
        Assert.Equal(-DepthRules.HoverLiftPx, HudPlank.Travel(true, false, true));
        Assert.Equal(DepthRules.PressTravelPx, HudPlank.Travel(true, true, true));
        Assert.Equal(0, HudPlank.Travel(false, true, true));
    }

    [Fact]
    public void TheDropIsRaisedPxAndStartPxForTheChunkyPlankAndGoneWhilePressed()
    {
        Assert.Equal(DepthRules.RaisedPx, HudPlank.DropLength(HudPlankKind.Plank, true, false, false));
        Assert.Equal(DepthRules.StartPx, HudPlank.DropLength(HudPlankKind.Chunky, true, false, false));
        Assert.Equal(DepthRules.RaisedPx + DepthRules.HoverLiftPx, HudPlank.DropLength(HudPlankKind.Plank, true, false, true));
        Assert.Equal(DepthRules.StartPx + DepthRules.HoverLiftPx, HudPlank.DropLength(HudPlankKind.Chunky, true, false, true));
        Assert.Equal(0, HudPlank.DropLength(HudPlankKind.Chunky, true, true, true));
        Assert.Equal(0, HudPlank.DropLength(HudPlankKind.Plank, false, false, false));
        Assert.Equal(0, HudPlank.DropLength(HudPlankKind.None, true, false, false));

        // The band's authored height is its longest (hovered) shadow, so a scale <= 1 covers every state.
        foreach (var kind in new[] { HudPlankKind.Plank, HudPlankKind.Chunky })
            Assert.Equal(HudPlank.DropLength(kind, true, false, true), HudPlank.DropBaseHeight(kind));
    }

    [Fact]
    public void PressIsFastReleaseSpringsAndMotionOffIsStatic()
    {
        Assert.Equal(DepthRules.PressMs, HudPlank.DurationFor(pressed: true, releasing: false, MotionLevel.Full));
        Assert.Equal(DepthRules.ReleaseMs, HudPlank.DurationFor(false, true, MotionLevel.Full));
        Assert.Equal(DepthRules.HoverMs, HudPlank.DurationFor(false, false, MotionLevel.Full));
        Assert.Equal(DepthRules.ReleaseMs / 2, HudPlank.DurationFor(false, true, MotionLevel.Reduced));
        Assert.Equal(0, HudPlank.DurationFor(true, false, MotionLevel.Off));
        Assert.Equal(0, HudPlank.DurationFor(false, true, MotionLevel.Off));
    }

    // ---- the recipe in the XAML --------------------------------------------------------------

    [Fact]
    public void TheHudBandIsARecessWithAWellTopBand()
    {
        var band = Named(AppFile("MainWindow", "MainWindow.xaml"), "Border", "HudBand", "        </Border>");
        Assert.Contains("x:Name=\"HudBandWellTop\" Height=\"" + DepthRules.WellPx + "\"", band);
        Assert.Contains("Fill=\"{DynamicResource DepthWellTop}\"", band);
        Assert.Contains("IsHitTestVisible=\"False\"", band);
    }

    [Fact]
    public void TheXpTrackIsAGrooveAndTheFillIsATubeWithABead()
    {
        var xaml = AppFile("MainWindow", "MainWindow.xaml");
        var track = Named(xaml, "Border", "XPBarTrack", "x:Name=\"XPMeniscus\"");
        Assert.Contains("Background=\"{DynamicResource DepthWellFloorBrush}\"", track);
        Assert.Contains("Background=\"{DynamicResource DepthWellTop}\"", track);
        Assert.Contains("Background=\"{DynamicResource DepthWellLeft}\"", track);
        Assert.Contains("Background=\"{DynamicResource DepthWellFoot}\"", track);
        // the groove bands sit UNDER the fill
        Assert.True(track.IndexOf("x:Name=\"XPGrooveTop\"", StringComparison.Ordinal)
                    < track.IndexOf("x:Name=\"XPBar\"", StringComparison.Ordinal));

        var fill = Named(xaml, "Border", "XPBar", "x:Name=\"XPBarFlashOverlay\"");
        Assert.Contains("Background=\"{DynamicResource DepthTubeGloss}\"", fill);
        Assert.Contains("Fill=\"{DynamicResource DepthTubeBead}\"", fill);
        Assert.Contains("HorizontalAlignment=\"Right\"", fill);        // the bead rides the leading edge
        Assert.Contains("x:Name=\"XPBarSheen\"", fill);                // the sweep stays
        Assert.DoesNotContain("<Border.Effect>", fill);                // no new Effect on the bar
    }

    [Fact]
    public void TheLevelChipAndStatPillsAreRaisedCoins()
    {
        var xaml = AppFile("MainWindow", "MainWindow.xaml");
        var chip = Regex.Match(xaml, "<Border x:Name=\"LevelChip\".*?</Border>\\s*\n", RegexOptions.Singleline).Value;
        Assert.Contains("BorderBrush=\"{DynamicResource DepthRaisedBevel}\" BorderThickness=\"1\"", chip);
        Assert.Contains("Background=\"{DynamicResource DepthRaisedSheen}\"", chip);
        Assert.Contains("x:Name=\"LevelChipDrop\" VerticalAlignment=\"Bottom\" Height=\"" + DepthRules.RaisedPx + "\"", chip);
        Assert.Contains("Height=\"24\"", chip);                       // the HudBand budget

        foreach (var pill in new[] { "PillConditioningTime", "PillOnlineUsers", "PillRankPercentile" })
        {
            var p = Named(xaml, "Border", pill, "</StackPanel>");
            Assert.Contains("BorderBrush=\"{DynamicResource DepthRaisedBevel}\" BorderThickness=\"1\"", p);
            Assert.Contains("Background=\"{DynamicResource DepthRaisedSheen}\"", p);
            Assert.Contains("Background=\"{DynamicResource DepthDropBand}\"", p);
            Assert.Contains("Visibility=\"Collapsed\"", p);           // the pill still hides all of it
        }

        // the chip's shadow takes the chip's own colour wherever mod / Lockdown repaint it
        Assert.Contains("PaintHudChipDepth(accent);", AppFile("MainWindow", "MainWindow.HeroFx.cs"));
    }

    [Fact]
    public void TheStartRowIsPlanksAndStartIsTheOneChunkyOne()
    {
        var xaml = AppFile("MainWindow", "MainWindow.xaml");
        Assert.Single(Regex.Matches(xaml, "controls:HudPlank\\.Kind=\"Chunky\""));
        Assert.Contains("<Button x:Name=\"BtnStart\" Grid.Column=\"1\" Style=\"{StaticResource PinkButton}\" controls:HudPlank.Kind=\"Chunky\"", xaml);
        foreach (var name in new[] { "BtnRemember", "BtnStartMenu", "BtnSaveAll" })
            Assert.Matches(new Regex("<Button x:Name=\"" + name + "\"[^>]*controls:HudPlank\\.Kind=\"Plank\"", RegexOptions.Singleline), xaml);
        Assert.Matches(new Regex("btn_exit}\"[^>]*controls:HudPlank\\.Kind=\"Plank\"", RegexOptions.Singleline), xaml);
        // START keeps its ignition dip: the press moves an inner face, never this transform.
        Assert.Contains("<ScaleTransform x:Name=\"StartPressScale\"/>", xaml);
    }

    [Fact]
    public void ThePinkButtonAndSecondaryButtonCarryOptInDepth()
    {
        var controls = AppFile("Resources", "Theme", "Controls.xaml");
        var pink = Regex.Match(controls, "<Style x:Key=\"PinkButton\".*?</Style>", RegexOptions.Singleline).Value;
        foreach (var part in new[] { "x:Name=\"DepthDrop\"", "x:Name=\"DepthFace\"", "x:Name=\"DepthSheen\"", "x:Name=\"DepthBevel\"",
                                     "{DynamicResource DepthPlankBevel}", "{DynamicResource DepthPlankSheen}",
                                     "{DynamicResource DepthRaisedBevel}", "{DynamicResource DepthRaisedSheen}",
                                     "Property=\"depth:HudPlank.Kind\" Value=\"Chunky\"" })
            Assert.Contains(part, pink);
        // StaticResource would fail: Depth.xaml merges after Controls.xaml.
        Assert.DoesNotContain("StaticResource Depth", controls);

        var secondary = Regex.Match(controls, "<Style x:Key=\"SecondaryButton\".*?</Style>", RegexOptions.Singleline).Value;
        Assert.Contains("x:Name=\"DepthFace\"", secondary);
        Assert.Contains("<Condition Property=\"depth:HudPlank.Kind\" Value=\"None\"/>", secondary); // the squash stands down
    }

    // ---- rendered ----------------------------------------------------------------------------

    [Fact]
    public void APlankRendersItsPartsAndAPlainPinkButtonDoesNot()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var host = new StackPanel();
            host.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/ConditioningControlPanel;component/Resources/Theme/Depth.xaml", UriKind.Absolute)
            });
            var style = (Style)Application.Current.FindResource("PinkButton");
            var start = new Button { Style = style, Height = 50, Width = 240, Content = "START" };
            HudPlank.SetKind(start, HudPlankKind.Chunky);
            var plain = new Button { Style = style, Height = 40, Width = 120, Content = "plain" };
            host.Children.Add(start);
            host.Children.Add(plain);

            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));
            host.UpdateLayout();
            HudPlank.Update(start, releasing: false, animate: false);

            var drop = (FrameworkElement)start.Template.FindName("DepthDrop", start);
            var bevel = (Border)start.Template.FindName("DepthBevel", start);
            var face = (UIElement)start.Template.FindName("DepthFace", start);
            Assert.Equal(Visibility.Visible, drop.Visibility);
            Assert.Equal(HudPlank.DropBaseHeight(HudPlankKind.Chunky), drop.Height);
            Assert.Equal(-HudPlank.DropBaseHeight(HudPlankKind.Chunky), drop.Margin.Bottom);
            Assert.Equal(DepthRules.StartPx / HudPlank.DropBaseHeight(HudPlankKind.Chunky),
                         ((ScaleTransform)drop.RenderTransform).ScaleY, 3);
            Assert.Equal(0, ((TranslateTransform)face.RenderTransform).Y);
            Assert.Equal(Visibility.Visible, bevel.Visibility);
            Assert.Same(host.FindResource("DepthPlankBevel"), bevel.BorderBrush);
            // the plank keeps its button height: the drop hangs below and costs no layout
            Assert.Equal(50, start.ActualHeight);

            var plainDrop = (FrameworkElement)plain.Template.FindName("DepthDrop", plain);
            var plainBevel = (FrameworkElement)plain.Template.FindName("DepthBevel", plain);
            Assert.Equal(Visibility.Collapsed, plainDrop.Visibility);
            Assert.Equal(Visibility.Collapsed, plainBevel.Visibility);
        });
    }
}
