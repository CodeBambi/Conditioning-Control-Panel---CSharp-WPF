using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core.Platform;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia dialog service using <see cref="IStorageProvider"/> for file/folder pickers
/// and MessageBox.Avalonia for message and confirmation boxes.
/// </summary>
public sealed class AvaloniaDialogService : IDialogService
{
    private readonly Func<TopLevel?> _getTopLevel;

    public AvaloniaDialogService(Func<TopLevel?> getTopLevel)
    {
        _getTopLevel = getTopLevel;
    }

    public async Task ShowMessageAsync(string title, string message, DialogSeverity severity = DialogSeverity.Info)
    {
        var top = _getTopLevel();
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, MapIcon(severity));

        switch (top)
        {
            case Window window:
                await box.ShowWindowDialogAsync(window);
                break;
            case ContentControl control:
                await box.ShowAsPopupAsync(control);
                break;
            default:
                await box.ShowAsync();
                break;
        }
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var top = _getTopLevel();
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.YesNo, Icon.Question);

        var result = top switch
        {
            Window window => await box.ShowWindowDialogAsync(window),
            ContentControl control => await box.ShowAsPopupAsync(control),
            _ => await box.ShowAsync()
        };

        return result == ButtonResult.Yes;
    }

    public async Task<string?> ShowInputDialogAsync(string title, string message, string? defaultValue = null)
    {
        var top = _getTopLevel();
        var dialog = new InputDialog(title, message, defaultValue ?? "");

        if (top is Window window)
        {
            var accepted = await dialog.ShowDialog<bool>(window);
            return accepted ? dialog.ResultText : null;
        }

        dialog.Show();
        var tcs = new TaskCompletionSource<string?>();
        dialog.Closed += (_, _) =>
        {
            tcs.TrySetResult(dialog.ResultText);
        };
        return await tcs.Task;
    }

    public async Task<IReadOnlyList<string>> ShowOpenFileDialogAsync(
        string title,
        IReadOnlyList<FileFilter> filters,
        bool allowMultiple = false,
        string? initialDirectory = null)
    {
        var top = _getTopLevel();
        if (top is null) return Array.Empty<string>();

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = MapFilters(filters)
        };

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            try
            {
                var startFolder = await top.StorageProvider.TryGetFolderFromPathAsync(initialDirectory).ConfigureAwait(false);
                if (startFolder != null)
                {
                    options.SuggestedStartLocation = startFolder;
                }
            }
            catch { /* best effort */ }
        }

        var result = await top.StorageProvider.OpenFilePickerAsync(options);
        return result.Select(r => r.Path.LocalPath).ToList();
    }

    public async Task<string?> ShowSaveFileDialogAsync(
        string title,
        IReadOnlyList<FileFilter> filters,
        string? defaultFileName = null)
    {
        var top = _getTopLevel();
        if (top is null) return null;

        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            FileTypeChoices = MapFilters(filters)
        };

        var result = await top.StorageProvider.SaveFilePickerAsync(options);
        return result?.Path.LocalPath;
    }

    public async Task<string?> ShowOpenFolderDialogAsync(string title)
    {
        var top = _getTopLevel();
        if (top is null) return null;

        var options = new FolderPickerOpenOptions { Title = title };

        var result = await top.StorageProvider.OpenFolderPickerAsync(options);
        return result.FirstOrDefault()?.Path.LocalPath;
    }

    private static IReadOnlyList<FilePickerFileType> MapFilters(IReadOnlyList<FileFilter> filters)
    {
        return filters
            .Select(f => new FilePickerFileType(f.Name)
            {
                Patterns = f.Extensions.Select(e => $"*.{e}").ToList()
            })
            .ToList();
    }

    private static Icon MapIcon(DialogSeverity severity)
    {
        return severity switch
        {
            DialogSeverity.Warning => Icon.Warning,
            DialogSeverity.Error => Icon.Error,
            _ => Icon.Info
        };
    }
}
