using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Intensity Ramp's "Link to Ramp" chips against the dials they really move.
///
/// <para>Two of those six chips used to read "Master" and "Sub" - two words with no noun on them,
/// sitting in a row of four visual effects (general chat 2026-09-16, and four people lost their
/// audio the same week with "are you using the ramp" as the standing first question). The labels
/// now name the volumes, which is only true while the ramp's apply step still writes
/// <c>MasterVolume</c> and <c>SubAudioVolume</c>. This pins the pair together: relabel the chip
/// and this test says which branch has to move with it.</para>
///
/// <para>Source text, because the chips are XAML and the apply step lives in MainWindow, which no
/// unit test can instantiate.</para>
/// </summary>
public class RampLinkLabelTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    /// <summary>The chip's own StackPanel, from its ToolTip through to its label.</summary>
    private static string ChipBlock(string checkBoxName)
    {
        var xaml = ReadSource("Features", "IntensityRampFeatureControl.xaml");
        var at = xaml.IndexOf("x:Name=\"" + checkBoxName + "\"", StringComparison.Ordinal);
        Assert.True(at > 0, checkBoxName + " is no longer in IntensityRampFeatureControl.xaml");
        var from = xaml.LastIndexOf("<StackPanel", at, StringComparison.Ordinal);
        var to = xaml.IndexOf("</StackPanel>", at, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "could not read the chip around " + checkBoxName);
        return xaml.Substring(from, to - from);
    }

    [Theory]
    [InlineData("ChkLinkFlash", "label_flash", "tooltip_ramp_link_flash")]
    [InlineData("ChkLinkSpiral", "label_spiral", "tooltip_ramp_link_spiral")]
    [InlineData("ChkLinkPink", "label_pink", "tooltip_ramp_link_pink")]
    [InlineData("ChkLinkMaster", "label_ramp_link_master", "tooltip_ramp_link_master")]
    [InlineData("ChkLinkSub", "label_ramp_link_sub", "tooltip_ramp_link_sub")]
    [InlineData("ChkLinkBrainDrain", "label_braindrain", "tooltip_ramp_link_braindrain")]
    public void EveryRampChipIsLabelledAndExplained(string checkBox, string labelKey, string tipKey)
    {
        var chip = ChipBlock(checkBox);
        Assert.Contains("loc:Str " + labelKey + "}", chip, StringComparison.Ordinal);
        Assert.Contains("loc:Str " + tipKey + "}", chip, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two audio chips say "volume" because they move a volume. If the apply step ever links
    /// something else to those settings, the labels are a lie and this is where it shows.
    /// </summary>
    [Theory]
    [InlineData("RampLinkMasterAudio", "MasterVolume")]
    [InlineData("RampLinkSubliminalAudio", "SubAudioVolume")]
    public void TheAudioLinksStillDriveTheirVolume(string setting, string dial)
    {
        var src = ReadSource("MainWindow", "MainWindow.StartStop.cs");
        var at = src.IndexOf("settings." + setting, StringComparison.Ordinal);
        Assert.True(at > 0, setting + " no longer appears in the ramp's apply step");

        // The branch that reads the link flag must also write the dial the label promises.
        var branch = src.Substring(at, Math.Min(600, src.Length - at));
        Assert.Contains("settings." + dial + " =", branch, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Sub" is gone from every language file, not just English: an untranslated leftover would
    /// render the old mystery word for eight locales out of nine.
    /// </summary>
    [Fact]
    public void TheOldMysteryLabelIsGoneEverywhere()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.True(files.Length == 9, "expected 9 language files, found " + files.Length);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("\"label_sub\"", text, StringComparison.Ordinal);
            foreach (var key in new[] { "label_ramp_link_master", "label_ramp_link_sub",
                                        "tooltip_ramp_link_flash", "tooltip_ramp_link_spiral",
                                        "tooltip_ramp_link_pink", "tooltip_ramp_link_master",
                                        "tooltip_ramp_link_sub", "tooltip_ramp_link_braindrain" })
                Assert.True(Regex.IsMatch(text, "\"" + key + "\"\\s*:\\s*\"[^\"]+\""),
                    Path.GetFileName(file) + " has no value for " + key);
        }
    }
}
