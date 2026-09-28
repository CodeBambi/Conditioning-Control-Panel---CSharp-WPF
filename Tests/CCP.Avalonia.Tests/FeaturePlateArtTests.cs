using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>ModArt.BindFeaturePlates on a real feature card: shipped art, a CoreMods.ModChanged
/// subscription while on screen, and the mod's art on a mod switch.</summary>
public sealed class FeaturePlateArtTests
{
    [Fact]
    public async Task LockCardPlatesRepaintOnModSwitch()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            LocalizationManager.Instance.SetLanguage("en");

            int before = ModChangedCount();
            var card = new LockCardFeatureControl();
            var window = new Window { Content = card, Width = 1100, Height = 780 };
            window.Show();
            Assert.Equal(before + 1, ModChangedCount());

            var hero = Assert.IsType<ImageBrush>(card.FindControl<Border>("HeroArt")!.Background);
            var side = Assert.IsType<ImageBrush>(card.FindControl<Border>("SideArt")!.Background);
            Assert.Equal(0.9, hero.Opacity);
            Assert.Equal(AlignmentX.Right, hero.AlignmentX);
            Assert.Equal(Stretch.UniformToFill, side.Stretch);
            Assert.NotEqual(3, Width(hero));

            var dir = Directory.CreateTempSubdirectory();
            var png = Path.Combine(dir.FullName, "mod.png");
            using (var bmp = new WriteableBitmap(new PixelSize(3, 3), new Vector(96, 96)))
                bmp.Save(png);
            var previous = CoreModArt.OverridePathProvider;
            CoreModArt.OverridePathProvider = p => p == "features/Phrase_Lock.png" ? png : null;
            try
            {
                CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(3, Width((ImageBrush)card.FindControl<Border>("HeroArt")!.Background!));
                Assert.Equal(3, Width((ImageBrush)card.FindControl<Border>("SideArt")!.Background!));

                window.Content = null; // off screen: unsubscribed
                Assert.Equal(before, ModChangedCount());
            }
            finally
            {
                CoreModArt.OverridePathProvider = previous;
                window.Close();
                dir.Delete(true);
            }
            return Task.CompletedTask;
        });
    }

    private static int Width(ImageBrush brush) => ((Bitmap)brush.Source!).PixelSize.Width;

    private static int ModChangedCount()
        => (typeof(CoreMods).GetField(nameof(CoreMods.ModChanged), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
}
