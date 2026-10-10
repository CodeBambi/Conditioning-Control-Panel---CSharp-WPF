using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Polish wave 8, the readability pass (2026-10-06). Text colours come from the token tiers
/// (TextLight / TextSecondary / TextMuted) and the section ink, never from a private grey
/// someone picked by eye. These five hexes were the ad-hoc greys and the private link lilac
/// the survey found across the views; the sweep moved every TextBlock / Run using them onto a
/// token. A source scan (same idiom as <c>WindowDefaultSizeTests</c>), because most of these
/// views cannot be constructed off a real app.
///
/// <para>Buttons are out of scope (the readability pass does not touch buttons), so only
/// TextBlock and Run elements are read. The files the "pages" lane owns are allowlisted until
/// that lane lands.</para>
/// </summary>
public class AdHocGreyTests
{
    private static readonly string[] AdHocHexes = { "#B8B8C8", "#B8B1CC", "#9A93B8", "#8079A3", "#B084DC" };

    // Owned by the pages lane of the same wave (Play > Games, Companion > Personality).
    private static readonly string[] Allowlist =
    {
        "PlayTabView.xaml", "PersonalityPage.xaml", "CompanionPickerCard.xaml", "CompanionTheme.xaml",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static IEnumerable<string> ViewFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Views"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !Allowlist.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));

    private static readonly Regex TextElement = new(@"<(TextBlock|Run)\b[^>]*>", RegexOptions.Singleline);

    [Fact]
    public void No_text_in_the_views_uses_an_ad_hoc_grey()
    {
        var hits = new List<string>();
        foreach (var file in ViewFiles())
        {
            var xaml = File.ReadAllText(file);
            foreach (Match m in TextElement.Matches(xaml))
                foreach (var hex in AdHocHexes)
                    if (m.Value.IndexOf("Foreground=\"" + hex + "\"", StringComparison.OrdinalIgnoreCase) >= 0)
                        hits.Add($"{Path.GetFileName(file)}:{xaml[..m.Index].Count(c => c == '\n') + 1} {hex}");
        }
        Assert.True(hits.Count == 0, "ad-hoc text greys (use TextSecondaryBrush / TextMutedBrush / SectionInkBrush):\n" + string.Join("\n", hits));
    }

    // One title per page, on the shared scale. Each entry: file, the loc key of its page title.
    public static TheoryData<string, string> PageTitles => new()
    {
        { "Tabs/AppSettingsTabView.xaml", "set2_settings_title" },
        { "Tabs/StudioTabView.xaml", "st4_studio_title" },
        { "Tabs/PresetsTabView.xaml", "sd_page_title" },
        { "Tabs/DiscordTabView.xaml", "profile_tab_heading" },
        { "Tabs/AvailableSubjectsTabView.xaml", "tab_available_subjects" },
        { "Controls/Companion/Pages/LinksPage.xaml", "nav_tab_companionlinks" },
        { "Controls/Companion/Pages/PermissionsPage.xaml", "nav_tab_permissions" },
    };

    [Theory]
    [MemberData(nameof(PageTitles))]
    public void Page_titles_use_the_shared_page_title_style(string relative, string key)
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Views", relative));
        var title = TextElement.Matches(xaml).Cast<Match>()
            .FirstOrDefault(m => m.Value.Contains("{loc:Str " + key + "}", StringComparison.Ordinal));
        Assert.True(title != null, $"{relative}: no TextBlock carries {key}");
        Assert.Contains("Style=\"{StaticResource Type.PageTitle}\"", title!.Value);
        // A local size, weight or colour would override the style and undo the scale.
        Assert.DoesNotMatch(@"\s(FontSize|FontWeight|Foreground)=", title.Value);
    }
}
