using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Each Avalonia surface shows exactly what its Core type produces, so a local
/// stand-in with drifted text or order fails here.</summary>
public sealed class CoreStandInTests
{
    [Fact]
    public Task DescentFuseSurfacesShowCoreDescentFuseCopy() => Run(() =>
    {
        var spiral = new SpiralTabView();
        Assert.Equal(DescentFuseCopy.FogEyebrow, Text(spiral, "FogEyebrow"));
        Assert.Equal(DescentFuseCopy.FogLine, Text(spiral, "FogLine"));
        Assert.Equal(DescentFuseCopy.FogTail, Text(spiral, "FogTail"));
        Assert.Equal(DescentFuseCopy.WaitingLine, Text(spiral, "WaitingLine"));

        var fuse = new DescentFuseWindow();
        Assert.Equal(DescentFuseCopy.ShowAwaits, Text(fuse, "ShowLine"));
        fuse.Close();

        // A private copy of any Core line (ShowAwaits, IgnitionLine, the fog lines...) is a stand-in.
        var core = typeof(DescentFuseCopy).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!).ToHashSet();
        Assert.Contains(DescentFuseCopy.IgnitionLine, core);
        foreach (var type in new[] { typeof(DescentFuseWindow), typeof(SpiralTabView) })
        {
            var copies = type.GetFields(BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string)
                            && core.Contains((string)f.GetRawConstantValue()!))
                .Select(f => f.Name).ToArray();
            Assert.True(copies.Length == 0, $"{type.Name} re-declares DescentFuseCopy: {string.Join(", ", copies)}");
        }
    });

    [Fact]
    public Task DescentCeremonyShowsCoreDescentCeremonyCopy() => Run(() =>
    {
        var window = new DescentCeremonyWindow();
        Assert.Equal(DescentCeremonyCopy.IntroBody, Text(window, "IntroBody"));
        Assert.Equal(DescentCeremonyCopy.CycleBody(), Text(window, "CycleBody"));
        Assert.Equal(DescentCeremonyCopy.CycleBonusLine(), Text(window, "CycleBonus"));
        Assert.Equal(DescentCeremonyCopy.BothDoorsFooter, Text(window, "BothDoorsFooter"));
        Assert.Null(typeof(DescentCeremonyWindow).GetNestedType("Copy", BindingFlags.NonPublic));
        Assert.Null(typeof(DescentCeremonyWindow).GetNestedType("Choices", BindingFlags.NonPublic));
    });

    [Theory]
    [InlineData("volume")]
    [InlineData("")]
    public Task SettingsPaletteListsSettingsPaletteIndexSearchInOrder(string query) => Run(() =>
    {
        var window = new SettingsPaletteWindow();
        window.Show();
        window.FindControl<TextBox>("TxtQuery")!.Text = query;
        Dispatcher.UIThread.RunJobs();

        var shown = window.FindControl<ListBox>("ListResults")!.Items
            .Cast<PaletteRow>().Select(r => r.Entry.Id).ToArray();
        var expected = SettingsPaletteIndex.Search(query).Select(e => e.Id).ToArray();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, shown);
        window.Close();
    });

    [Fact]
    public Task EnhancementPlayerTakesEnhancementResolverLibraryTier() => Run(() =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-enh-");
        var media = Path.Combine(dir.FullName, "track.mp3");
        File.WriteAllBytes(media, Array.Empty<byte>());
        EnhancementResolver.LibraryMatchProvider = (_, _) =>
            new EnhancementLibraryEntry { FilePath = Path.Combine(dir.FullName, "lib.ccpenh.json") };
        try
        {
            var window = new EnhancementPlayerWindow();
            typeof(EnhancementPlayerWindow)
                .GetMethod("TryAutoLoadEnhancement", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, new object[] { media });
            var source = typeof(EnhancementPlayerWindow)
                .GetField("_lastDiscoverySource", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(window);
            Assert.Equal("Library", source!.ToString());
        }
        finally
        {
            EnhancementResolver.LibraryMatchProvider = null;
            dir.Delete(recursive: true);
        }
    });

    [Fact]
    public Task PresetsTabRailListsCorePresetsAndSelectionEnablesExport() => Run(() =>
    {
        var tab = new PresetsTabView();
        var panel = tab.FindControl<WrapPanel>("PresetCardsPanel")!;
        var shown = panel.Children.OfType<Border>().Where(b => b.Tag is string).Select(b => (string)b.Tag!).ToArray();
        var expected = ConditioningControlPanel.Models.Preset.GetDefaultPresets()
            .Concat(ConditioningControlPanel.CoreSettings.Current.UserPresets).Select(p => p.Id).ToArray();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, shown);

        var export = tab.FindControl<Button>("BtnExportPreset")!;
        Assert.False(export.IsEnabled);
        var first = ConditioningControlPanel.Models.Preset.GetDefaultPresets()[0];
        typeof(PresetsTabView).GetMethod("SelectPreset", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(tab, new object[] { first });
        Assert.True(export.IsEnabled);
        Assert.Equal(ConditioningControlPanel.CoreMods.MakeModAware(first.Name), Text(tab, "TxtDetailTitle"));
    });

    [Fact]
    public Task EnhancementsTabDrawsCoreSkillDefinitionCatalogue() => Run(() =>
    {
        var tab = new EnhancementsTabView();
        var nodes = tab.FindControl<Canvas>("SkillTreeCanvas")!.Children.OfType<Border>()
            .Where(b => b.Tag is string).ToArray();
        var expected = ConditioningControlPanel.Models.SkillDefinition.All.Where(s => !s.IsSecret).ToArray();
        Assert.Equal(expected.Select(s => s.Id), nodes.Select(b => (string)b.Tag!));

        var texts = nodes[0].GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
        Assert.Contains(expected[0].LocalizedName, texts);
        Assert.Contains(texts, t => t!.EndsWith(" " + expected[0].Cost));

        Assert.Equal(ConditioningControlPanel.Models.SkillDefinition.All.Count(s => s.IsSecret),
            tab.FindControl<Panel>("SecretSkills")!.Children.Count);
    });

    [Fact]
    public Task SessionEditorDrivesCoreTimelineSession() => Run(() =>
    {
        Assert.Null(typeof(SessionEditorWindow).GetNestedType("EditorSession", BindingFlags.NonPublic));
        var session = new ConditioningControlPanel.Models.TimelineSession { Name = "T", DurationMinutes = 60 };
        session.AddStopEvent(session.AddStartEvent("spiral", 5), 25);
        session.AddStopEvent(session.AddStartEvent("flash", 30), 50);

        var window = new SessionEditorWindow(session);
        Assert.Equal(session.GetDifficultyText(), Text(window, "TxtDifficulty"));
        Assert.Equal(Loc.GetF("session_xp_amount", session.CalculateXP()), Text(window, "TxtXP"));
        window.Close();
    });

    [Fact]
    public Task QuizResultOffersQuizSessionGeneratorSession() => Run(() =>
    {
        var window = new QuizWindow();
        var result = new QuizResult { Category = QuizCategory.Bambi, TotalScore = 30, MaxScore = 40, ProfileText = "p" };
        typeof(QuizWindow).GetMethod("ShowResult", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, new object[] { result });

        var id = result.Category.ToString();
        var expected = QuizSessionGenerator.GenerateSession(30, 40, id, id,
            QuizSessionGenerator.GetFallbackContent(id, 75));
        var name = expected.Name.Length > 30 ? expected.Name.Substring(0, 30) + "..." : expected.Name;
        Assert.Equal(Loc.GetF("quiz_save_session", name), Text(window, "TxtTrySessionLabel"));
        Assert.True(window.FindControl<Border>("BtnTrySession")!.IsHitTestVisible);
        window.Close();
    });

    private static string? Text(Control root, string name) => root.FindControl<TextBlock>(name)!.Text;

    private static Task Run(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
        LocalizationManager.Instance.SetLanguage("en");
        body();
        return Task.CompletedTask;
    });
}
