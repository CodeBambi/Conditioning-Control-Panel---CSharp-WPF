using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Views.Controls.Companion.Pages;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Every option the v2 chat page hid when it collapsed the companion room (7043dfe85, see the
/// nav rework's LOST-OPTIONS list) has a visible control on a Companion page. The pages host
/// the live room zones and Workshop cells, so each check follows that chain: the page adopts
/// the zone or cell, and the zone or cell carries the control.
/// </summary>
public class CompanionPagesTests
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
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    private static string Page(string name) => Src("Views", "Controls", "Companion", "Pages", name);
    private static string Cell(string name) => Src("Views", "Controls", "Companion", "Runtime", name);

    [Theory]
    [InlineData("personality", "PersonalityPage")]
    [InlineData("permissions", "PermissionsPage")]
    [InlineData("companionlinks", "LinksPage")]
    public void EachPageIsRegisteredThroughTheTabRegistry(string key, string page)
    {
        var src = Src("MainWindow", "MainWindow.CompanionTabs.cs");
        Assert.Contains($"new NavTabHost(\"{key}\", () => new {page}(this)", src);
        Assert.Contains($"(({page})v).OnShown()", src);
    }

    [Fact]
    public void LinksPage_CarriesTheVideoPoolAndTheKnowledgeLinks()
    {
        var xaml = Page("LinksPage.xaml");
        var cs = Page("LinksPage.xaml.cs");
        Assert.Contains("x:Name=\"VideosHost\"", xaml);
        Assert.Contains("Adopt(tab.Vm.Shelf.Library, VideosHost)", cs);
        Assert.Contains("x:Name=\"BtnAddVideoLink\"", Cell("WorkshopLibraryCell.xaml"));

        Assert.Contains("<companion:KnowledgeLinksEditor x:Name=\"KnowledgeLinks\" SaveImmediately=\"True\"/>", xaml);
        Assert.Contains("KnowledgeLinks.Load()", cs);
        Assert.Contains("companion_page_links_known_note", xaml);
    }

    [Fact]
    public void KnowledgeLinksEditor_IsOneImplementationHostedTwice()
    {
        var editor = Src("Views", "Controls", "Companion", "KnowledgeLinksEditor.xaml.cs");
        Assert.Contains("GlobalKnowledgeBaseLinks", editor);
        Assert.Contains("new KnowledgeLinkEditorDialog", editor);
        var editorXaml = Src("Views", "Controls", "Companion", "KnowledgeLinksEditor.xaml");
        Assert.Contains("x:Name=\"LstKnowledgeLinks\"", editorXaml);
        Assert.Contains("x:Name=\"BtnAddKnowledgeLink\"", editorXaml);
        Assert.Contains("x:Name=\"BtnRemoveKnowledgeLink\"", editorXaml);

        var dialog = Src("Dialogs", "CompanionPromptEditorDialog.xaml");
        Assert.Contains("<companion:KnowledgeLinksEditor x:Name=\"KnowledgeLinks\"/>", dialog);
        Assert.DoesNotContain("<ListBox x:Name=\"LstKnowledgeLinks\"", dialog);
        var dialogCs = Src("Dialogs", "CompanionPromptEditorDialog.xaml.cs");
        Assert.DoesNotContain("AddKnowledgeLink_Click", dialogCs);
        Assert.Contains("KnowledgeLinks.SaveToSettings()", dialogCs);
    }

    [Fact]
    public void PersonalityPage_IsTheOneHomeForThePick_Presets_Editor_AndCommunity()
    {
        var xaml = Page("PersonalityPage.xaml");
        var cs = Page("PersonalityPage.xaml.cs");
        Assert.Contains("x:Name=\"PickerHost\"", xaml);
        Assert.Contains("new CompanionPickerCard", cs);
        Assert.Contains("FindName(\"PersonalityZone\")", cs);
        Assert.Contains("Adopt(presets, PresetsHost)", cs);
        Assert.Contains("x:Name=\"BtnOpenPromptEditor\"", xaml);
        Assert.Contains("BtnCustomizeCompanion_Click", cs);
        Assert.Contains("Adopt(tab.Vm.Shelf.Community, CommunityHost)", cs);
        var community = Cell("WorkshopCommunityCell.xaml");
        foreach (var name in new[] { "BtnBrowsePrompts", "BtnImportPrompt", "BtnExportPrompt", "BtnRefreshPrompts" })
            Assert.Contains($"x:Name=\"{name}\"", community);
    }

    [Theory]
    [InlineData("WorkshopBehaviorCell.xaml", "SliderBubbleDurationCompanion")]   // BubbleDurationSeconds
    [InlineData("WorkshopBehaviorCell.xaml", "SliderIdleIntervalCompanion")]     // IdleGiggleIntervalSeconds
    [InlineData("WorkshopBehaviorCell.xaml", "ChkVoiceLinesCompanion")]          // CompanionVoiceLinesMuted
    [InlineData("WorkshopBehaviorCell.xaml", "ChkTubeMidnightGlass")]            // TubeMidnightGlass
    [InlineData("WorkshopBehaviorCell.xaml", "ChkMuteWhispersCompanion")]        // SubAudioMuted
    [InlineData("WorkshopBehaviorCell.xaml", "ChkPauseBrowserCompanion")]        // pause browser
    [InlineData("WorkshopTriggersCell.xaml", "ChkTriggerModeCompanion")]         // TriggerModeEnabled
    [InlineData("WorkshopTriggersCell.xaml", "SliderTriggerIntervalCompanion")]  // TriggerIntervalSeconds
    public void PersonalityPage_BehaviourFold_CarriesEveryLostKnob(string cell, string control)
    {
        var xaml = Page("PersonalityPage.xaml");
        var cs = Page("PersonalityPage.xaml.cs");
        var fold = xaml.Substring(xaml.IndexOf("<feat:MoreFold x:Name=\"BehaviourFold\">", StringComparison.Ordinal));
        fold = fold.Substring(0, fold.IndexOf("</feat:MoreFold>", StringComparison.Ordinal));
        Assert.Contains("x:Name=\"BehaviorHost\"", fold);
        Assert.Contains("x:Name=\"TriggersHost\"", fold);
        Assert.Contains("Adopt(tab.Vm.Shelf.Behavior, BehaviorHost)", cs);
        Assert.Contains("Adopt(tab.Vm.Shelf.Triggers, TriggersHost)", cs);
        Assert.Contains($"x:Name=\"{control}\"", Cell(cell));
    }

    [Fact]
    public void BehaviourFold_OpensItselfWhenAKnobIsOffDefault()
    {
        Assert.False(PersonalityPage.BehaviourOffDefault(null));
        Assert.False(PersonalityPage.BehaviourOffDefault(new AppSettings()));
        var s = new AppSettings();
        s.TubeMidnightGlass = !s.TubeMidnightGlass;
        Assert.True(PersonalityPage.BehaviourOffDefault(s));
        s = new AppSettings();
        s.TriggerIntervalSeconds += 7;
        Assert.True(PersonalityPage.BehaviourOffDefault(s));
    }

    [Fact]
    public void PermissionsPage_HostsTheOneAiPermissionsList_AndTakeoverLinksToIt()
    {
        Assert.Contains("FindName(\"PermissionsZone\") is AiPermissionsGrid", Page("PermissionsPage.xaml.cs"));
        Assert.Contains("x:Name=\"PermissionsHost\"", Page("PermissionsPage.xaml"));
        Assert.Contains("x:Name=\"ChkAllowLockCard\"", Src("Views", "Controls", "Companion", "AiPermissionsGrid.xaml"));
        Assert.Contains("ShowTab(\"permissions\")", Src("Views", "Tabs", "BambiTakeoverTabView.xaml.cs"));
        Assert.Contains("companion_page_permissions_live_here", Src("Views", "Tabs", "BambiTakeoverTabView.xaml"));
    }

    [Fact]
    public void ChatSettingsEntries_OpenThePages()
    {
        var cs = Src("Views", "Controls", "Companion", "V2", "ConversationPage.xaml.cs");
        Assert.Contains("\"who\" or \"personality\" => \"personality\"", cs);
        Assert.Contains("\"permissions\" => \"permissions\"", cs);
        Assert.Contains("\"videos\" => \"companionlinks\"", cs);
    }

    [Theory]
    [InlineData(Views.Controls.Companion.CompanionRoomAnchors.WorkshopRosterCell, "personality")]
    [InlineData(Views.Controls.Companion.CompanionRoomAnchors.WorkshopBehaviorCell, "personality")]
    [InlineData(Views.Controls.Companion.CompanionRoomAnchors.WorkshopTriggersCell, "personality")]
    [InlineData(Views.Controls.Companion.CompanionRoomAnchors.WorkshopCommunityCell, "personality")]
    [InlineData(Views.Controls.Companion.CompanionRoomAnchors.WorkshopLibraryCell, "companionlinks")]
    [InlineData(Views.Controls.Companion.CompanionRoomAnchors.WorkshopAwarenessCell, null)]
    public void WorkshopDeepLinks_LandOnThePages(string cell, string? page) =>
        Assert.Equal(page, MainWindow.CompanionPageForWorkshopCell(cell));

    [Fact]
    public void NewPageCopy_NeverSaysSheOrHer()
    {
        var en = Src("Localization", "Languages", "en.json");
        foreach (var line in en.Split('\n').Where(l => l.TrimStart().StartsWith("\"companion_page_", StringComparison.Ordinal)))
        {
            var value = line.Substring(line.IndexOf(':') + 1).ToLowerInvariant();
            Assert.DoesNotMatch(@"\b(she|her|hers|herself)\b", value);
            Assert.DoesNotContain("—", value);
        }
    }
}
