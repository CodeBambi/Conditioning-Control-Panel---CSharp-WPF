using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using CCP.Avalonia.Tests.Board;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>k4 HA5: a mod switch repaints the Home tile art and the nav door art without a restart
/// (WPF ApplyActiveModChange: LoadFeatureImages + ApplyDoorArt).</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ModArtRepaintTests
{
    [Theory]
    [InlineData("mysterybox", BuiltInMods.BambiSleepId, "features/mysterybox_bambi.png")]
    [InlineData("vault", BuiltInMods.SissyHypnoId, "features/vault_sissy.png")]
    [InlineData("vault", BuiltInMods.DronificationId, "features/vault_drone.png")]
    [InlineData("mysterybox", BuiltInMods.LockedId, "features/mysterybox_locked.png")]
    [InlineData("vault", BuiltInMods.CCPDefaultId, null)]
    [InlineData("vault", "someone-elses-mod", null)]
    public void ThemedTiles_AreNamedByTheBuiltInMod(string baseName, string modId, string? expected)
        => Assert.Equal(expected, SettingsTabView.TileVariantPath(baseName, modId));

    [Theory]
    [InlineData("nav/door_social.png", "drone-mode", "nav/mods/drone-mode/door_social.png")]
    [InlineData("nav/door_social.png", null, null)]
    [InlineData("nav/door_social.png", "../evil", null)]
    [InlineData("nav/mods/drone-mode/door_social.png", "drone-mode", null)]
    [InlineData("features/flash.png", "drone-mode", null)]
    public void DoorArt_HasAnEmbeddedTwinPerMod(string path, string? modId, string? expected)
        => Assert.Equal(expected, MainShellWindow.EmbeddedModTwin(path, modId));

    [Fact]
    public Task ASwitch_RepaintsTheTilesAndTheDoors() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BoardHeadTests.EnsureApp();
        BoardHeadTests.Pin();
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow { Width = 1600, Height = 1000 };
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            var tab = shell.Named<SettingsTabView>("SettingsTab")!;
            var flash = tab.FindControl<FeatureCard>("CardFlash")!;
            var mystery = tab.FindControl<FeatureCard>("CardMystery")!;
            var split = tab.FindControl<SplitFeatureCard>("ComboMindDrain")!;
            var door = shell.Named<Image>("ImgDoorHome")!;
            var (flashBefore, mysteryBefore, splitBefore, doorBefore) = (flash.Icon, mystery.Icon, split.IconA, door.Source);

            shell.RefreshModArt();

            // Fresh decodes, not the XAML's authored pictures: the switch reached every surface.
            Assert.IsType<Bitmap>(flash.Icon);
            Assert.NotSame(flashBefore, flash.Icon);
            Assert.NotSame(mysteryBefore, mystery.Icon);
            Assert.NotSame(splitBefore, split.IconA);
            Assert.IsType<Bitmap>(door.Source);
            Assert.NotSame(doorBefore, door.Source);

            // The door resolver falls back to our own art when a mod ships no twin.
            Assert.NotNull(MainShellWindow.ResolveDoorArt("nav/door_home.png", "no-such-mod"));
        }
        finally
        {
            shell?.Close();
            BoardHeadTests.Unpin();
        }
        return Task.CompletedTask;
    });
}
