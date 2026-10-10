using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Views.Controls.Companion.V2;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.1.5 lane C (small UI fixes): #1386 Shatter vs the "when clicked" picker, the pop-up
/// question card's own off switch, #1379 TubeFit preview NRE + the perk picker's locked rows.
/// Pure rules where there is one; a source scan for the wiring (these windows need a real app).
/// </summary>
public class Fix715UiTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Src(params string[] parts) =>
        SourceRoots.ReadProductFile(parts);

    // ---------------------------------------------------------------- #1386

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]   // switch left on but the prize is not owned: the picker still works
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Shatter_decides_the_click_only_when_on_and_owned(bool shatterOn, bool owns, bool expected)
        => Assert.Equal(expected, FlashFeatureControl.ShatterDecidesClick(shatterOn, owns));

    [Fact]
    public void Exit_picker_greys_out_with_a_reason_while_Shatter_decides()
    {
        var xaml = Src("Features", "FlashFeatureControl.xaml");
        Assert.Contains("x:Name=\"TxtExitShatterNote\"", xaml);
        Assert.Contains("flash_exit_shatter_note", xaml);

        var cs = Src("Features", "FlashFeatureControl.xaml.cs");
        Assert.Contains("CmbExit.IsEnabled = !shatter;", cs);
        // Re-evaluated when the switch flips, when settings reload and when grants change.
        Assert.True(Regex.Matches(cs, @"RefreshExitRow\(\);").Count >= 3);
    }

    // ---------------------------------------------------------------- pop-up questions off switch

    [Fact]
    public void Turn_off_flips_the_Graded_Intake_setting_once()
    {
        var s = new AppSettings { PopQuizEnabled = true };
        Assert.True(PopQuizWindow.TurnOffPopQuestions(s));
        Assert.False(s.PopQuizEnabled);
        Assert.False(PopQuizWindow.TurnOffPopQuestions(s));
        Assert.False(s.PopQuizEnabled);
    }

    [Fact]
    public void Turn_off_link_is_never_an_answer()
    {
        var xaml = Src("Windows", "PopQuizWindow.xaml");
        Assert.Contains("MouseLeftButtonUp=\"TurnOff_Click\"", xaml);
        Assert.Contains("intake_popquiz_turn_off", xaml);

        var cs = Src("Windows", "PopQuizWindow.xaml.cs");
        var start = cs.IndexOf("private void TurnOff_Click", StringComparison.Ordinal);
        Assert.True(start > 0);
        var end = cs.IndexOf("\n        }", start, StringComparison.Ordinal);
        var body = cs[start..end];
        Assert.DoesNotContain("AddXP", body);
        Assert.DoesNotContain("Answer_Click", body);
        Assert.Contains("CleanupAndClose()", body);
    }

    // ---------------------------------------------------------------- #1379

    [Fact]
    public void TubeFit_slider_events_are_suppressed_until_the_ctor_wires_them()
    {
        var cs = Src("Dialogs", "TubeFitDialog.xaml.cs");
        Assert.Contains("private bool _suppressSliderEvents = true;", cs);
    }

    [Fact]
    public void Locked_perk_rows_sit_back()
    {
        Assert.Equal(1.0, PerkPicker.RowOpacity(true));
        Assert.True(PerkPicker.RowOpacity(false) < 0.6);
    }

    // ---------------------------------------------------------------- loc

    [Theory]
    [InlineData("flash_exit_shatter_note")]
    [InlineData("intake_popquiz_turn_off")]
    public void New_keys_ship_in_every_language(string key)
    {
        var dir = SourceRoots.LanguagesDirectory;
        foreach (var file in Directory.GetFiles(dir, "*.json"))
            Assert.Contains($"\"{key}\":", File.ReadAllText(file));
    }
}
