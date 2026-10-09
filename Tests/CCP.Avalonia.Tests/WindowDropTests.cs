using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.SessionIO.cs Window_Drop / ImportDroppedFilesAsync on the Avalonia shell,
/// driven by a real headless drag-and-drop onto the window.</summary>
public sealed class WindowDropTests
{
    [Fact]
    public Task MediaDropConfirmedCopiesIntoTheLibraryAndKeepsTheOriginal() => Run(async (shell, root, assets) =>
    {
        var video = Path.Combine(root, "clip.mp4");
        File.WriteAllBytes(video, new byte[] { 1, 2, 3 });

        Drop(shell, video);
        var ask = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault());
        Assert.Equal(Loc.Get("dlg_media_drop_library"), ButtonText(ask, "BtnOk"));
        Click(ask, "BtnOk");

        var done = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault(d => d != ask));
        Assert.Equal("Imported 1 video", done.FindControl<TextBlock>("TxtMessage")!.Text);
        Assert.True(File.Exists(Path.Combine(assets, "videos", "clip.mp4")));
        Assert.True(File.Exists(video));   // copied, never moved
        Assert.Equal("assets", shell.CurrentTab);
        done.Close();
    });

    [Fact]
    public Task MediaDropCancelledImportsNothing() => Run(async (shell, root, assets) =>
    {
        var video = Path.Combine(root, "clip.mp4");
        File.WriteAllBytes(video, new byte[] { 1, 2, 3 });

        Drop(shell, video);
        var ask = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault());
        Click(ask, "BtnCancel");
        await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().Any() ? null : shell);

        Assert.False(File.Exists(Path.Combine(assets, "videos", "clip.mp4")));
        Assert.True(File.Exists(video));
    });

    [Fact]
    public Task EnhancementDropSaysTheLibraryIsNotReady() => Run(async (shell, root, _) =>
    {
        var enh = Path.Combine(root, "wave.ccpenh.json");
        File.WriteAllText(enh, "{\"$schema\":\"" + ConditioningControlPanel.Models.Deeper.Enhancement.SchemaTag + "\"}");
        Drop(shell, enh);
        await WaitFor(() => Toast(shell, Loc.Get("deeper_import_library_not_ready")));
    });

    [Fact]
    public Task UnrecognisedDropToastsAndShowsNoOverlay() => Run(async (shell, root, _) =>
    {
        var txt = Path.Combine(root, "notes.txt");
        File.WriteAllText(txt, "hello");
        Drop(shell, txt, RawDragEventType.DragEnter);
        Assert.False(shell.Named<Border>("GlobalDropOverlay")!.IsVisible);
        Drop(shell, txt);
        await WaitFor(() => Toast(shell, Loc.Get("drop_not_recognised")));
    });

    [Fact]
    public Task AZipDragShowsTheExtractOverlayAndTheDropHidesIt() => Run((shell, root, _) =>
    {
        var zip = Path.Combine(root, "pack.zip");
        File.WriteAllBytes(zip, Array.Empty<byte>());
        Drop(shell, zip, RawDragEventType.DragEnter);
        Assert.True(shell.Named<Border>("GlobalDropOverlay")!.IsVisible);
        Assert.Equal(Loc.Get("label_drop_to_extract_assets"), shell.Named<TextBlock>("DropOverlayTitle")!.Text);
        Assert.Equal("pack.zip", shell.Named<TextBlock>("DropOverlaySubtitle")!.Text);
        Drop(shell, zip, RawDragEventType.DragLeave);
        Assert.False(shell.Named<Border>("GlobalDropOverlay")!.IsVisible);
        return Task.CompletedTask;
    });

    // ---- harness ----------------------------------------------------------------------

    private static Task Run(Func<MainShellWindow, string, string, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var root = Directory.CreateTempSubdirectory("ccp-window-drop-").FullName;
        var assets = Path.Combine(root, "assets");
        var provider = ConditioningControlPanel.CorePaths.EffectiveAssetsProvider;
        ConditioningControlPanel.CorePaths.EffectiveAssetsProvider = () => assets;
        var shell = new MainShellWindow();
        var host = shell.Named<Panel>("NotificationHost")!;
        global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(host);
        shell.Show();
        try
        {
            await body(shell, root, assets);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close();
            ConditioningControlPanel.CorePaths.EffectiveAssetsProvider = provider;
            try { Directory.Delete(root, true); } catch { }
        }
    });

    private static void Drop(MainShellWindow shell, string path, RawDragEventType type = RawDragEventType.Drop)
    {
        var file = shell.StorageProvider.TryGetFileFromPathAsync(path).GetAwaiter().GetResult()
                   ?? throw new InvalidOperationException("headless storage provider has no file for " + path);
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(file));
        // A real drag always enters before it drops; Avalonia delivers the drop to the entered target.
        if (type == RawDragEventType.Drop)
            shell.DragDrop(new Point(400, 300), RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        shell.DragDrop(new Point(400, 300), type, data, DragDropEffects.Copy, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBlock? Toast(MainShellWindow shell, string text) =>
        shell.Named<Panel>("NotificationHost")!.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == text);

    private static string? ButtonText(Window dialog, string name) =>
        dialog.FindControl<Button>(name)!.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text
        ?? dialog.FindControl<Button>(name)!.Content as string;

    private static void Click(Window dialog, string name) =>
        dialog.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Pumps the UI queue until the side effect exists (the import runs on the pool).</summary>
    private static async Task<T> WaitFor<T>(Func<T?> probe) where T : class
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            if (probe() is { } hit) return hit;
            await Task.Yield();
        }
        throw new TimeoutException("side effect never appeared");
    }
}
