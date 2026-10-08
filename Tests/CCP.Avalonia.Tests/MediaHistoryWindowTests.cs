using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using App = ConditioningControlPanel.Avalonia.App;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>win-media-history: the Media Log reads the real shared log (not placeholders), filters
/// audio, plans each preview like WPF, follows live entries and clears behind a confirm.</summary>
public sealed class MediaHistoryWindowTests
{
    [Fact]
    public Task MediaLog_shows_the_real_log_filters_previews_follows_and_clears() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-mhw-").FullName;
        var png = Path.Combine(dir, "still.png");
        using (var rt = new RenderTargetBitmap(new PixelSize(8, 8))) rt.Save(png);
        File.WriteAllBytes(Path.Combine(dir, "drain.mp3"), new byte[] { 0xFF, 0xFB });   // present: a missing clip is "file not found" (WPF order)
        var history = new MediaHistoryService(Path.Combine(dir, "media_history.json"));
        var before = App.MediaHistory;
        MediaHistoryWindow? w = null;
        try
        {
            App.MediaHistory = history;
            history.RecordImages(new[] { png });
            history.RecordVideo(Path.Combine(dir, "gone.mp4"));
            history.RecordAudio(Path.Combine(dir, "drain.mp3"));
            history.RecordImages(new[] { "https://cdn.example.com/x.jpg" });

            w = new MediaHistoryWindow();
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var list = w.FindControl<ListBox>("MediaList")!;
            MediaHistoryRow Row(int i) => (MediaHistoryRow)list.Items[i]!;
            string Text(string name) => w.FindControl<TextBlock>(name)!.Text ?? "";
            bool Shown(string name) => w.FindControl<Control>(name)!.IsVisible;
            void Click(string name) { w.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

            Assert.Equal(4, list.ItemCount);                                  // the log, newest first
            Assert.Equal("https://cdn.example.com/x.jpg", Row(0).Entry.FilePath);
            Assert.Equal(Loc.GetF("label_media_entry_count", 4), Text("TxtCount"));

            Click("BtnFilterAudio");
            Assert.Equal(1, list.ItemCount);
            Assert.Equal(MediaType.Audio, Row(0).Entry.Type);
            Assert.Equal(Loc.GetF("label_media_entry_count_filtered", 1, 4), Text("TxtCount"));
            list.SelectedIndex = 0;
            Assert.Equal(Loc.Get("label_media_audio_preview"), Text("PreviewMissing"));   // never decoded as a picture

            Click("BtnFilterAll");
            list.SelectedIndex = 0;                                          // online entry
            Assert.Equal(Loc.Get("label_media_streamed_only"), Text("PreviewMissing"));
            Assert.True(Shown("BtnPreviewCopyLink"));
            Assert.True(Shown("BtnPreviewOpenSource"));
            Assert.False(Shown("BtnPreviewOpenFolder"));
            Assert.False(Row(0).FolderButtonVisible);

            list.SelectedIndex = 2;                                          // a video whose file is gone
            Assert.Equal(Loc.Get("label_file_not_found"), Text("PreviewMissing"));
            Assert.False(Shown("BtnPreviewCopyLink"));
            Assert.True(Shown("BtnPreviewOpenFolder"));

            list.SelectedIndex = 3;                                          // the real still
            Assert.True(Shown("PreviewImage"));
            Assert.NotNull(w.FindControl<Image>("PreviewImage")!.Source);
            Assert.NotNull(Row(3).Thumb);
            Assert.Null(Row(2).Thumb);                                       // videos: glyph, no decode

            history.RecordVideo(Path.Combine(dir, "live.mp4"));              // a clip plays while open
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(5, list.ItemCount);
            Assert.EndsWith("live.mp4", Row(0).Entry.FilePath);

            Click("BtnClear");
            var dialog = Assert.Single(w.OwnedWindows.OfType<MessageDialog>());
            dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, history.Count);
            Assert.True(Shown("TxtEmpty"));
            Assert.False(Shown("MediaList"));

            w.Close();
            Dispatcher.UIThread.RunJobs();
            history.RecordAudio(Path.Combine(dir, "after.mp3"));              // a closed window no longer listens
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, list.ItemCount);
        }
        finally
        {
            w?.Close();
            App.MediaHistory = before;
            history.Dispose();
            Dispatcher.UIThread.RunJobs();
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });
}
