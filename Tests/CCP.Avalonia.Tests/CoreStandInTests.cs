using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
