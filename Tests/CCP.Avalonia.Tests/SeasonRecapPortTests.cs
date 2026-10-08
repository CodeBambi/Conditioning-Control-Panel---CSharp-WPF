using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>rows-season-recap: the mod palette (WPF RecapTheme), the foil shimmer and the
/// clipboard copy, each through the window a user opens.</summary>
public sealed class SeasonRecapPortTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static SeasonRecapCard CardOf(Window w) => w.GetVisualDescendants().OfType<SeasonRecapCard>().First();

    private static GradientStop FoilStop(SeasonRecapCard card, int i) =>
        ((LinearGradientBrush)card.FindControl<Border>("OuterFoil")!.Background!).GradientStops[i];

    [Fact]
    public Task CardTakesTheActiveModAccentAndFollowsModChanged() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var old = CoreMods.AccentColorHexProvider;
        var w = new SeasonRecapWindow();
        try
        {
            CoreMods.AccentColorHexProvider = () => "#00C850";
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var card = CardOf(w);
            // RecapMagenta = the accent; RecapVioletLite = accent lightened 45% (WPF RecapTheme).
            Assert.Equal(Color.FromRgb(0x00, 0xC8, 0x50), FoilStop(card, 1).Color);
            Assert.Equal(Color.FromRgb(114, 224, 158), FoilStop(card, 2).Color);

            CoreMods.AccentColorHexProvider = () => "#2040FF";
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.FromRgb(0x20, 0x40, 0xFF), FoilStop(card, 1).Color);
        }
        finally { CoreMods.AccentColorHexProvider = old; w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task WindowBackdropAndContinuePillFollowTheModToo() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var old = CoreMods.AccentColorHexProvider;
        CoreMods.AccentColorHexProvider = () => "#00C850";
        var w = new SeasonRecapWindow();
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var pill = (LinearGradientBrush)w.FindControl<Button>("BtnContinue")!
                .GetVisualDescendants().OfType<Border>().First().Background!;
            // RecapVoid = accent x0.06; the pill runs accent -> accent lightened 45%.
            Assert.Equal(Color.FromRgb(0, 12, 4), ((SolidColorBrush)w.Background!).Color);
            Assert.Equal(Color.FromRgb(0x00, 0xC8, 0x50), pill.GradientStops[0].Color);

            CoreMods.AccentColorHexProvider = () => "#2040FF";
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.FromRgb(0x20, 0x40, 0xFF), pill.GradientStops[0].Color);
            Assert.Equal(Color.FromRgb(1, 3, 15), ((SolidColorBrush)w.Background!).Color);
        }
        finally { CoreMods.AccentColorHexProvider = old; w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FoilShimmerDriftsOnTheWpfCurveOnlyWhileVisible() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var w = new SeasonRecapWindow();
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var card = CardOf(w);
            var clock = new SteppedClock();
            card.Clock = clock;
            card.IsVisible = false;   // restart under the stepped clock
            Assert.False(card.FoilRunning);
            card.IsVisible = true;
            Assert.True(card.FoilRunning);

            card.TickFoil();
            Assert.Equal(0.20, FoilStop(card, 1).Offset, 3);
            Assert.Equal(0.55, FoilStop(card, 2).Offset, 3);
            clock.Now += TimeSpan.FromSeconds(1.75);  // quarter: sine in-out = (1 - cos(pi/4)) / 2
            card.TickFoil();
            Assert.Equal(0.20 + 0.30 * (1 - Math.Cos(Math.PI / 4)) / 2, FoilStop(card, 1).Offset, 4);
            clock.Now += TimeSpan.FromSeconds(1.75);  // half way, sine in-out = 0.5
            card.TickFoil();
            Assert.Equal(0.35, FoilStop(card, 1).Offset, 3);
            clock.Now += TimeSpan.FromSeconds(3.5);   // the far end
            card.TickFoil();
            Assert.Equal(0.50, FoilStop(card, 1).Offset, 3);
            Assert.Equal(0.85, FoilStop(card, 2).Offset, 3);
            clock.Now += TimeSpan.FromSeconds(7);     // autoreversed home
            card.TickFoil();
            Assert.Equal(0.20, FoilStop(card, 1).Offset, 3);

            w.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(card.FoilRunning);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task CopyPutsTheCardPngOnTheClipboardAndSaysSo() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var w = new SeasonRecapWindow();
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            w.FindControl<Button>("BtnCopy")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 50 && w.FindControl<TextBlock>("PART_Status")!.Text != Loc.Get("recap_toast_copied"); i++)
            {
                await Task.Yield();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(Loc.Get("recap_toast_copied"), w.FindControl<TextBlock>("PART_Status")!.Text);
            var bmp = await TopLevel.GetTopLevel(w)!.Clipboard!.TryGetBitmapAsync();
            Assert.NotNull(bmp);
            Assert.True(bmp!.PixelSize.Width > 2000); // the 2x export, not a placeholder
        }
        finally { w.Close(); }
    });
}
