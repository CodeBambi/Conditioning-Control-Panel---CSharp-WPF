using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF EmiCodexWindow.Build/ChapterList_SelectionChanged: the plain reader lists the
/// shipped chapters, falls open at the bookmark, and turning a page moves the bookmark.</summary>
public sealed class EmiCodexReaderTests
{
    [Fact]
    public Task ReadsShippedChaptersAndKeepsTheBookmark() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = EmiState.Current.CodexChapter;
        try
        {
            EmiState.Current.CodexChapter = "flashes";
            var win = new EmiCodexWindow("bundle");
            var list = win.FindControl<ListBox>("ChapterList")!;
            var body = win.FindControl<StackPanel>("PageBody")!;

            Assert.True(list.ItemCount > 10);   // the real chapters/*.json, not placeholders
            Assert.Equal("flashes", ((TextBlock)list.SelectedItem!).Text);
            Assert.Equal("flashes", ((TextBlock)body.Children[0]).Text);

            list.SelectedIndex++;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("videos", ((TextBlock)body.Children[0]).Text);   // volume 2, order 2
            Assert.Equal("videos", EmiState.Current.CodexChapter);
            win.Close();
        }
        finally { EmiState.Current.CodexChapter = old; }
        return Task.CompletedTask;
    });
}
