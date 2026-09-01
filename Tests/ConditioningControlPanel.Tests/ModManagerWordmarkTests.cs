using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The header's Mod Manager button, 0813: the stacked gear-over-caption became a one-line
/// wordmark where the gear glyph IS the "O" of "MOD".
///
/// <para>Source-text assertions for the same reason <see cref="HeaderBannerTests"/> uses them -
/// MainWindow cannot be instantiated in a unit test without the whole service graph - and every
/// failure guarded here compiles and renders cleanly, so nothing else would catch it:</para>
/// <list type="bullet">
/// <item>whitespace between the three inlines is collapsed to a real space by the XAML parser, so
/// a well-meaning reformat turns the wordmark into "M (gear) D MANAGER";</item>
/// <item>the letters are hardcoded on the strength of <c>label_mod_manager</c> being the literal
/// string "MOD MANAGER" in all nine language files - if a translator ever localizes it, the split
/// is wrong and this has to go back to glyph + full loc'd string;</item>
/// <item>a gear painted with a literal brush stops following the mod accent, which every other
/// pixel of this capsule does;</item>
/// <item>anything that reintroduces wrapping or a second line grows the header row.</item>
/// </list>
/// </summary>
public class ModManagerWordmarkTests
{
    private static string MainWindowXaml() => SourceRoots.ReadProductFile("MainWindow", "MainWindow.xaml");

    /// <summary>The whole &lt;Button x:Name="BtnManageMods"&gt; element, template included.</summary>
    private static string ButtonBlock()
    {
        var xaml = MainWindowXaml();
        var block = Regex.Match(xaml, "<Button x:Name=\"BtnManageMods\".*?</Button>", RegexOptions.Singleline);
        Assert.True(block.Success, "BtnManageMods is gone from the header");
        return block.Value;
    }

    [Fact]
    public void TheGearTakesTheOAndTheThreeInlinesTouch()
    {
        var block = ButtonBlock();

        // M, then the gear, then the rest of the word - in that order, and adjacent. The
        // adjacency is the whole trick: XAML collapses any whitespace between two inlines into a
        // single rendered space, so a line break after the "M" run prints "M (gear) D MANAGER".
        Assert.Matches(new Regex("<Run Text=\"M\"/><InlineUIContainer\\b"), block);
        Assert.Matches(new Regex("</InlineUIContainer><Run\\s+Text=\"D MANAGER\"/>", RegexOptions.Singleline), block);

        // ...and the thing between them is the gear glyph, not a stand-in.
        var gear = Regex.Match(block, "<InlineUIContainer.*?</InlineUIContainer>", RegexOptions.Singleline);
        Assert.True(gear.Success, "the gear no longer sits in an InlineUIContainer - it has lost its baseline");
        Assert.Contains("&#xE713;", gear.Value);
        Assert.Contains("Segoe MDL2 Assets", gear.Value);

        // Baseline-aligned by the text engine and nudged down onto the cap band, not floated.
        Assert.Contains("BaselineAlignment=\"Baseline\"", gear.Value);
        var margin = Regex.Match(gear.Value, "Margin=\"[^\"]*,(-[\\d.]+)\"");
        Assert.True(margin.Success, "the gear lost its negative bottom margin - it now rides above the baseline");
    }

    [Fact]
    public void TheGearStillTintsWithTheModAccent()
    {
        var gear = Regex.Match(ButtonBlock(), "<InlineUIContainer.*?</InlineUIContainer>", RegexOptions.Singleline).Value;

        // A glyph, not an image or an emoji, precisely so Foreground can carry the accent - and a
        // DynamicResource, so a mod switch repaints it along with the letters and the capsule.
        Assert.Contains("Foreground=\"{DynamicResource PinkBrush}\"", gear);
    }

    [Fact]
    public void ItIsOneLineInsideTheSame28pxCapsule()
    {
        var block = ButtonBlock();

        Assert.Contains("Height=\"28\"", block);
        Assert.Contains("TextWrapping=\"NoWrap\"", block);
        Assert.DoesNotContain("TextWrapping=\"Wrap\"", block);
        Assert.DoesNotContain("TextWrapping=\"WrapWithOverflow\"", block);

        // The old shape was a vertical StackPanel stacking gear over caption. One line means the
        // capsule holds exactly one TextBlock of letters plus the one nested inside the container.
        Assert.DoesNotContain("<StackPanel", block);
        Assert.Equal(2, Regex.Matches(block, "<TextBlock\\b").Count);

        // Click target, tooltip and the hover repaint all survive the restructure.
        Assert.Contains("Click=\"BtnManageMods_Click\"", block);
        Assert.Contains("ToolTip=\"{loc:Str tooltip_manage_mods}\"", block);
        Assert.Contains("Property=\"IsMouseOver\"", block);
    }

    [Fact]
    public void TheLocKeyIsStillShippedAndStillReads_MOD_MANAGER()
    {
        // Hardcoding the M / D MANAGER split is only safe while every language file agrees the
        // string is untranslated. The key is not orphaned: it is the button's automation name.
        Assert.Contains("AutomationProperties.Name=\"{loc:Str label_mod_manager}\"", ButtonBlock());

        var langDir = SourceRoots.LanguagesDirectory;
        var files = Directory.GetFiles(langDir, "*.json");
        Assert.Equal(9, files.Length);

        foreach (var file in files)
        {
            var match = Regex.Match(File.ReadAllText(file), "\"label_mod_manager\"\\s*:\\s*\"([^\"]*)\"");
            Assert.True(match.Success, "label_mod_manager was deleted from " + Path.GetFileName(file));
            Assert.True(match.Groups[1].Value == "MOD MANAGER",
                Path.GetFileName(file) + " localizes label_mod_manager to \"" + match.Groups[1].Value +
                "\" - the hardcoded M / D MANAGER split in MainWindow.xaml no longer spells it");
        }
    }
}
