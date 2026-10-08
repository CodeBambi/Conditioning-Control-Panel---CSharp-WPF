using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>MiniPlayerWindow's non-video paths through LoadFile -> Show, as WPF OpenAssetPreview
/// drives them: a GIF loops (WPF AnimationBehavior RepeatBehavior Forever), and a file that will
/// not load tells the user (WPF MessageBox) and closes. The GIF loop is stepped by hand.</summary>
public sealed class MiniPlayerPreviewTests
{
    // 1x1, two frames (palette index 0 red, then 1 blue), 100 ms each, NETSCAPE loop forever.
    private static readonly byte[] TwoFrameGif =
    {
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
        0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF,
        0x21, 0xFF, 0x0B, 0x4E, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2E, 0x30, 0x03, 0x01, 0x00, 0x00, 0x00,
        0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00,
        0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x4C, 0x01, 0x00,
        0x3B,
    };

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task GifLoopsItsFramesAndStopsOnClose()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-gif-").FullName;
        try
        {
            var gif = Path.Combine(dir, "loop.gif");
            File.WriteAllBytes(gif, TwoFrameGif);
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                EnsureApp();
                var w = new MiniPlayerWindow();
                w.LoadFile(gif);
                w.Show();
                var image = w.FindControl<Image>("ImagePreview")!;
                Assert.True(image.IsVisible);
                Assert.NotNull(image.Source);   // the still first frame, before the decode lands
                Assert.False(w.FindControl<Grid>("VideoControls")!.IsVisible);

                await w.GifDecoding!;
                Assert.Equal(2, w.GifFrameCount);
                Assert.True(w.GifAnimating);
                var first = image.Source;
                w.NextGifFrame();
                var second = image.Source;
                Assert.NotSame(first, second);
                w.NextGifFrame();
                Assert.Same(first, image.Source);   // loops forever

                w.Close();
                Assert.False(w.GifAnimating);
                Assert.Equal(0, w.GifFrameCount);
                Assert.Null(image.Source);
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task UnreadableImageTellsTheUserThenCloses()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-badimg-").FullName;
        try
        {
            var bad = Path.Combine(dir, "broken.png");
            File.WriteAllText(bad, "not a picture");
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                EnsureApp();
                var w = new MiniPlayerWindow();
                var closed = false;
                w.Closed += (_, _) => closed = true;
                w.LoadFile(bad);
                Assert.False(closed);   // nothing to own a notice yet: it waits for Show
                w.Show();
                Dispatcher.UIThread.RunJobs();

                var notice = w.OwnedWindows.OfType<MessageDialog>().Single();
                Assert.False(closed);
                notice.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await w.NoticeShowing!;
                Dispatcher.UIThread.RunJobs();
                Assert.True(closed);
            });
        }
        finally { Directory.Delete(dir, true); }
    }
}
