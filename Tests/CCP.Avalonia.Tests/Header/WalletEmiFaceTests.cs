using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using Xunit;

namespace CCP.Avalonia.Tests.Header;

/// <summary>The wallet croupier wears a real drawn face (WPF EmiFace "^_^"), not a mono TextBlock.</summary>
public sealed class WalletEmiFaceTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task FaceFitsTheGlass() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var ink = WalletEmiFace.FitInk("^_^", out var geo);
        Assert.NotNull(geo);
        Assert.True(ink.Width > 40, $"ink {ink}");
        Assert.True(ink.Width <= WalletEmiFace.VirtualWidth * 0.95 - 10 + 0.5, $"ink {ink}");
        Assert.True(ink.Height <= WalletEmiFace.VirtualHeight * 0.95 - 10 + 0.5, $"ink {ink}");
        return Task.CompletedTask;
    });

    [Fact]
    public Task WalletCarriesTheFace() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var wallet = new SparkleWallet();
        var face = wallet.GetLogicalDescendants().OfType<WalletEmiFace>().FirstOrDefault();
        Assert.Equal("^_^", face?.Face);
        return Task.CompletedTask;
    });
}
