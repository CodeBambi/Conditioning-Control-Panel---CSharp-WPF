using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Launcher;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The web game windows (WPF's per-game WebView2 hosts): every launcher game opens one, the
/// page's exit closes it, and the panic registry sees it while it is up.</summary>
public sealed class GameWindowTests
{
    [Fact]
    public void EveryLauncherGameHasAWindow_ButIntakeAndTheRace()
    {
        var missing = LauncherCards.All.Select(c => c.Id)
            .Where(id => id is not "intake" and not "race" && !GameWindow.Games.ContainsKey(id)).ToList();
        Assert.True(missing.Count == 0, "No game window for: " + string.Join(", ", missing));
        // For You, Just Drop and the Loom are panel doors (Premium shelf, Spiral card), never launcher tiles.
        foreach (var id in GameWindow.Games.Keys.Where(k => k is not "fyp" and not "justdrop" and not "loom"))
            Assert.True(LauncherWindow.Destinations.ContainsKey(id), $"launcher has no destination for {id}");
    }

    [Fact]
    public async Task PageExitClosesTheWindow_AndPanicSeesItWhileUp()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var w = new GameWindow(GameWindow.Games["piecebypiece"]);
            w.Show();
            Assert.True(GameWindow.IsAnyOpen());
            Assert.Contains(PanicSurfaces.All, s => s.Id == "games" && s.OwnsTheScreen?.Invoke() == true);
            w.HandleMessage("{\"type\":\"exit-done\"}");
            Assert.False(w.IsVisible);
            Assert.False(GameWindow.IsAnyOpen());
            return Task.CompletedTask;
        });
    }
}
