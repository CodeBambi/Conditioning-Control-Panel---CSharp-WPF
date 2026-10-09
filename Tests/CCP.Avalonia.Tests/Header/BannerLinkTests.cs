using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests.Header;

/// <summary>
/// The header banner's links (WPF Hyperlinks to linktr.ee and app.cclabs.app) rendered as dead text
/// in the port. MainShellWindow.BannerLinks.cs hit-tests the TextBlock's last run; these pin that a
/// press on the link run counts and a press on the lead-in copy does not.
/// </summary>
public sealed class BannerLinkTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task OnlyTheLastRunIsTheLink() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var tb = new TextBlock { FontSize = 14 };
        tb.Inlines!.Add(new Run("If you love the project please consider supporting it"));
        tb.Inlines.Add(new Run(" "));
        tb.Inlines.Add(new Run(MainShellWindow.BannerSupportUrl));
        var w = new Window { Width = 900, Height = 60, Background = Brushes.Black, Content = tb };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var mid = tb.TextLayout.Height / 2;
            Assert.False(MainShellWindow.IsOnBannerLink(tb, new Point(4, mid)));
            Assert.True(MainShellWindow.IsOnBannerLink(tb, new Point(tb.TextLayout.WidthIncludingTrailingWhitespace - 4, mid)));
            Assert.False(MainShellWindow.IsOnBannerLink(tb, new Point(tb.TextLayout.WidthIncludingTrailingWhitespace + 40, mid)));
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });
}
