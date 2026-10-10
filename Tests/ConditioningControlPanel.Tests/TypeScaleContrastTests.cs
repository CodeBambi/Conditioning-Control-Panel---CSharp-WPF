using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readability pass (polish wave 8, 2026-10-06): the text tokens and the section ink must read
/// on every page the section wash can paint, on the lifted card surface, and on the Bambi and
/// Sissy mod grounds; the six type-scale sizes are pinned so a drive-by edit cannot drift them.
/// Source test: Colors.xaml and the theme MainWindow.xaml are read as text (the
/// <c>WindowDefaultSizeTests</c> idiom), so a hex change in the dictionary is what is measured.
/// </summary>
public class TypeScaleContrastTests
{
    private static readonly string[] Sections =
    {
        NavSections.Home, NavSections.Studio, NavSections.Companion, NavSections.Play,
        NavSections.Social, NavSections.You, NavSections.Library, NavSections.Settings,
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Theme(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "Theme", file));

    private static Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        Assert.Equal(8, hex.Length);
        byte B(int i) => byte.Parse(hex.Substring(i, 2), NumberStyles.HexNumber);
        return Color.FromArgb(B(0), B(2), B(4), B(6));
    }

    private static Color Token(string key)
    {
        var m = Regex.Match(Theme("Colors.xaml"),
            "<Color\\s+x:Key=\"" + Regex.Escape(key) + "\">\\s*(#[0-9A-Fa-f]{6,8})\\s*</Color>");
        Assert.True(m.Success, $"Colors.xaml has no <Color x:Key=\"{key}\">");
        return Hex(m.Groups[1].Value);
    }

    /// <summary>The page ground a section paints: its hue at the wash alpha over the panel.</summary>
    private static Color Washed(string section, Color ground) =>
        NavStripRules.Over(Color.FromArgb(MainWindow.SectionWashAlpha,
            NavStripRules.Accent(section).R, NavStripRules.Accent(section).G, NavStripRules.Accent(section).B), ground);

    /// <summary>Every ground a line of text can land on: each washed page plus the card.</summary>
    private static IEnumerable<(string Name, Color Ground)> Grounds()
    {
        var panel = Token("PanelBg");
        foreach (var s in Sections) yield return ($"{s} page", Washed(s, panel));
        yield return ("card", Token("ElevatedSurface"));
    }

    [Theory]
    [InlineData("TextLight", 10.0)]
    [InlineData("TextSecondary", 7.5)]
    [InlineData("TextMuted", 5.0)]
    public void EveryTextTierReadsOnEveryWashedPageAndOnTheCard(string token, double floor)
    {
        var ink = Token(token);
        foreach (var (name, ground) in Grounds())
        {
            var c = NavStripRules.Contrast(ink, ground);
            Assert.True(c >= floor, $"{token} on {name} is {c:F2}:1, floor {floor}");
        }
    }

    [Fact]
    public void SectionInkReadsAndTintStaysNearWhiteForEverySection()
    {
        foreach (var s in Sections)
        {
            foreach (var (name, ground) in Grounds())
            {
                var ink = NavStripRules.Contrast(NavStripRules.Ink(s), ground);
                Assert.True(ink >= 4.5, $"Ink({s}) on {name} is {ink:F2}:1");
                var tint = NavStripRules.Contrast(NavStripRules.Tint(s), ground);
                Assert.True(tint >= 9.0, $"Tint({s}) on {name} is {tint:F2}:1");
            }
        }
    }

    [Fact]
    public void TheSectionHelpersFollowTheirRecipe()
    {
        // Lilac is the static default in Colors.xaml: the painted resources start equal to it.
        Assert.Equal(Token("SectionInk"), NavStripRules.Ink(NavSections.Home));
        Assert.Equal(Token("SectionTint"), NavStripRules.Tint(NavSections.Home));
        Assert.Equal(Token("SectionRule"), NavStripRules.Rule(NavSections.Home));
        Assert.Equal(Token("SectionOutline"), NavStripRules.Outline(NavSections.Home));

        // The proposal's worked values: VioletBlue ink #A3ABFC, Sage ink #C1E0BE.
        Assert.Equal(Hex("#A3ABFC"), NavStripRules.Ink(NavSections.Play));
        Assert.Equal(Hex("#C1E0BE"), NavStripRules.Ink(NavSections.Library));
        foreach (var s in Sections)
        {
            var hue = NavStripRules.Accent(s);
            Assert.Equal(Color.FromArgb(0x59, hue.R, hue.G, hue.B), NavStripRules.Rule(s));
            Assert.Equal(Color.FromArgb(0x40, hue.R, hue.G, hue.B), NavStripRules.Outline(s));
            Assert.Equal(255, NavStripRules.Ink(s).A);
            Assert.Equal(255, NavStripRules.Tint(s).A);
        }
    }

    [Fact]
    public void TheCardLiftsOffThePageWithoutFlatteningTheInset()
    {
        var lift = NavStripRules.Contrast(Token("ElevatedSurface"), Token("PanelBg"));
        Assert.InRange(lift, 1.15, 1.25);
    }

    [Fact]
    public void TheSixTypeScaleSizesAreExact()
    {
        var xaml = Theme("MainWindow.xaml");
        var expected = new (string Key, double Size)[]
        {
            ("Type.PageTitle", 26), ("Type.CardTitle", 18), ("Type.PageSubtitle", 13.5),
            ("Type.Body", 13), ("Type.Label", 11.5), ("Type.Caption", 11), ("Type.SectionHeader", 11),
        };
        foreach (var (key, size) in expected)
        {
            var m = Regex.Match(xaml,
                "<Style\\s+x:Key=\"" + Regex.Escape(key) + "\"[^>]*>(.*?)</Style>", RegexOptions.Singleline);
            Assert.True(m.Success, $"no style {key}");
            var fs = Regex.Match(m.Groups[1].Value, "Property=\"FontSize\"\\s+Value=\"([0-9.]+)\"");
            Assert.True(fs.Success, $"{key} sets no FontSize");
            Assert.Equal(size, double.Parse(fs.Groups[1].Value, CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void TheSharedHeaderAndLabelStylesSitOnTheScale()
    {
        var xaml = Theme("MainWindow.xaml");
        Assert.Matches("<Style x:Key=\"SectionHeader\" TargetType=\"TextBlock\" BasedOn=\"\\{StaticResource Type.SectionHeader\\}\">", xaml);
        Assert.Matches("<Style x:Key=\"SettingLabel\" TargetType=\"TextBlock\" BasedOn=\"\\{StaticResource Type.Body\\}\">", xaml);
        // Type.* must be declared before the styles based on them (BasedOn is a StaticResource).
        Assert.True(xaml.IndexOf("x:Key=\"Type.SectionHeader\"", StringComparison.Ordinal)
                    < xaml.IndexOf("x:Key=\"SectionHeader\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Key=\"Type.Body\"", StringComparison.Ordinal)
                    < xaml.IndexOf("x:Key=\"SettingLabel\"", StringComparison.Ordinal));
    }

    /// <summary>The mods repaint PanelBg (their PanelColor) and DarkerBg (their BackgroundColor)
    /// but never the text tokens: TextMuted must still read on Bambi and Sissy, washed. Their
    /// panel #252542 is lighter than the default #1C1C35, so the floor here is WCAG AA (4.5);
    /// 5.0 on these grounds would take TextMuted to about #B0B0C7, close to TextSecondary.</summary>
    [Theory]
    [InlineData("bambi")]
    [InlineData("sissy")]
    public void TextMutedReadsOnTheBambiAndSissyGrounds(string mod)
    {
        var m = mod == "bambi" ? BuiltInMods.BambiSleep : BuiltInMods.SissyHypno;
        var muted = Token("TextMuted");
        foreach (var hex in new[] { m.Theme!.PanelColor!, m.Theme!.BackgroundColor!, m.Theme!.SurfaceColor! })
        {
            var ground = Hex(hex);
            foreach (var s in Sections)
            {
                var c = NavStripRules.Contrast(muted, Washed(s, ground));
                Assert.True(c >= 4.5, $"TextMuted on {mod} {hex} washed {s} is {c:F2}:1");
                var ink = NavStripRules.Contrast(NavStripRules.Ink(s), Washed(s, ground));
                Assert.True(ink >= 4.5, $"Ink({s}) on {mod} {hex} washed is {ink:F2}:1");
            }
        }
    }
}
