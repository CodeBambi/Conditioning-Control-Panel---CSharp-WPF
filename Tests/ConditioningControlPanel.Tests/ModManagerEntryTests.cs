using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The header demotion, 0911 ("New UI Feedback" thread): the Mod Manager capsule and the language
/// pill left the top bar. The manager is now the last row of the mod drop-down plus the Library >
/// Mods rail entry; the language picker is Settings > General plus the first-run Welcome step,
/// which now opens on the OS display language.
///
/// <para>Source-text assertions, as in <see cref="HeaderBannerTests"/>: MainWindow cannot be
/// instantiated without the whole service graph, and every failure guarded here still compiles. A
/// capsule or pill that creeps back into the header, a drop-down verb row that ActivateMod gets to
/// see, or a loc key that misses a language file would all ship clean.</para>
/// </summary>
public class ModManagerEntryTests
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

    private static string MainWindowXaml() => ReadSource("MainWindow", "MainWindow.xaml");
    private static string MainWindowCode() => ReadSource("MainWindow", "MainWindow.xaml.cs");

    private static string Method(string source, string name)
    {
        var m = Regex.Match(source, @"void " + name + @"\(.*?\n        \}", RegexOptions.Singleline);
        Assert.True(m.Success, name + " has moved or changed shape");
        return m.Value;
    }

    // =====================================================================================
    //  1. the header
    // =====================================================================================

    [Fact]
    public void TheHeaderHoldsNeitherTheCapsuleNorThePill()
    {
        var xaml = MainWindowXaml();

        Assert.DoesNotContain("x:Name=\"BtnManageMods\"", xaml);
        Assert.DoesNotContain("x:Name=\"CmbLanguagePill\"", xaml);
        Assert.DoesNotContain("D MANAGER", xaml);

        // The rail entry is the other door to the same dialog, and it keeps the same handler.
        var rail = Regex.Match(xaml, "<Button x:Name=\"BtnNavMods\".*?>", RegexOptions.Singleline);
        Assert.True(rail.Success, "BtnNavMods is gone from the rail");
        Assert.Contains("Click=\"BtnManageMods_Click\"", rail.Value);

        // No partial still reaches for the pill by name. Agent worktrees under .claude are other
        // branches' checkouts and are excluded, as in HeaderBannerTests.
        var stragglers = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "ConditioningControlPanel"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("CmbLanguagePill", StringComparison.Ordinal))
            .ToArray();
        Assert.True(stragglers.Length == 0,
            "CmbLanguagePill is gone from the XAML but still referenced in: " + string.Join(", ", stragglers));
    }

    // =====================================================================================
    //  2. the drop-down row
    // =====================================================================================

    [Fact]
    public void TheDropDownEndsWithTheManagerRow()
    {
        var code = MainWindowCode();
        Assert.Contains("internal const string ModManagerEntryId", code);

        var init = Method(code, "InitializeModSelector");
        var userMods = init.IndexOf("!m.IsBuiltIn", StringComparison.Ordinal);
        var entry = init.IndexOf("isAction: true", StringComparison.Ordinal);
        Assert.True(userMods >= 0, "InitializeModSelector no longer lists user mods");
        Assert.True(entry > userMods, "the manager row is not the LAST row of the mod drop-down");
        Assert.Contains("ModManagerEntryId", init);
        Assert.Contains("label_open_mod_manager", init);

        // The template draws the row as a footer off IsAction: hairline above, gear for the dot.
        var template = Regex.Match(MainWindowXaml(), "<ComboBox x:Name=\"ModSelectorCombo\".*?</ComboBox>", RegexOptions.Singleline);
        Assert.True(template.Success, "ModSelectorCombo has moved");
        Assert.Contains("<DataTrigger Binding=\"{Binding IsAction}\" Value=\"True\">", template.Value);
        Assert.Contains("TargetName=\"Rule\" Property=\"Visibility\" Value=\"Visible\"", template.Value);
        Assert.Contains("TargetName=\"Dot\" Property=\"Visibility\" Value=\"Collapsed\"", template.Value);
    }

    [Fact]
    public void TheManagerRowNeverBecomesTheSelection()
    {
        var code = MainWindowCode();

        // The guard sits BEFORE ActivateMod could see the sentinel id.
        var handler = Method(code, "ModSelectorCombo_SelectionChanged");
        var guard = handler.IndexOf("ModManagerEntryId", StringComparison.Ordinal);
        var activate = handler.IndexOf("ActivateMod(", StringComparison.Ordinal);
        Assert.True(guard >= 0, "ModSelectorCombo_SelectionChanged no longer checks for the manager row");
        Assert.True(activate > guard, "the manager row reaches ActivateMod before the guard");

        // Restore under the suppress flag, close the list, then the one launcher the rail uses.
        var open = Method(code, "OpenModManagerFromSelector");
        Assert.Contains("_suppressModSelectorChange = true;", open);
        Assert.Contains("SelectedValue = App.Mods?.ActiveModId;", open);
        Assert.Contains("IsDropDownOpen = false;", open);
        Assert.Contains("_suppressModSelectorChange = false;", open);
        Assert.Contains("BtnManageMods_Click(", open);
        Assert.DoesNotContain("new ModManagerDialog", open);
    }

    // =====================================================================================
    //  3. the language picker
    // =====================================================================================

    [Fact]
    public void TheLanguagePickerLivesInSettingsAndOnTheFirstScreen()
    {
        var general = ReadSource("Views", "Controls", "AppSettings", "GeneralSettingsSection.xaml");
        Assert.Contains("x:Name=\"CmbLanguageSetting\"", general);
        Assert.Contains("SelectionChanged=\"CmbLanguageSetting_SelectionChanged\"", general);

        // The wizard's picker is on the Welcome screen, above the gate - not a screen of its own.
        var wizard = ReadSource("Windows", "FirstRunWizard.xaml");
        var picker = wizard.IndexOf("x:Name=\"CmbWizardLanguage\"", StringComparison.Ordinal);
        var gate = wizard.IndexOf("x:Name=\"AgeRow\"", StringComparison.Ordinal);
        Assert.True(picker >= 0, "CmbWizardLanguage is gone from the first-run wizard");
        Assert.True(gate > picker, "the wizard's language picker no longer sits on the Welcome screen above the gate");

        // It opens on the OS display language: detection runs before the static text is applied,
        // through the same writer the picker itself uses.
        var code = ReadSource("Windows", "FirstRunWizard.xaml.cs");
        var detect = code.IndexOf("DetectSystemLanguage();", StringComparison.Ordinal);
        var text = code.IndexOf("ApplyStaticText();", StringComparison.Ordinal);
        Assert.True(detect >= 0, "the wizard no longer detects the OS display language");
        Assert.True(detect < text, "DetectSystemLanguage runs after ApplyStaticText, so the first screen paints in English");

        var body = Method(code, "DetectSystemLanguage");
        Assert.Contains("CultureInfo.InstalledUICulture", body);
        Assert.Contains("LocalizationManager.AvailableLanguages", body);
        Assert.Contains("ApplyLanguageSelection(", body);
        Assert.Contains("SetApplicationLanguage(", body);
    }

    // =====================================================================================
    //  4. the copy
    // =====================================================================================

    [Fact]
    public void TheNewCopyShipsInEveryLanguageAndNoLongerPointsAtTheTitleBar()
    {
        var langDir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        var files = Directory.GetFiles(langDir, "*.json");
        Assert.Equal(9, files.Length);

        foreach (var file in files)
        {
            var json = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            Assert.True(Regex.IsMatch(json, "\"label_open_mod_manager\"\\s*:\\s*\"[^\"]+\""), "label_open_mod_manager is missing from " + name);
            Assert.False(json.Contains("\"label_mod_manager\"", StringComparison.Ordinal), "label_mod_manager is an orphan in " + name);
            Assert.False(json.Contains("\"tooltip_change_language\"", StringComparison.Ordinal), "tooltip_change_language is an orphan in " + name);
        }

        var en = File.ReadAllText(Path.Combine(langDir, "en.json"));
        foreach (var key in new[] { "fr8_welcome_language_hint", "set2_general_language_hint" })
        {
            var value = Regex.Match(en, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            Assert.True(value.Success, key + " is gone from en.json");
            Assert.DoesNotContain("title bar", value.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
