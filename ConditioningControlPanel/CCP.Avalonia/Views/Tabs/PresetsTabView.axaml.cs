using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.ViewModels.Tabs;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class PresetsTabView : UserControl
{
    public PresetsTabView()
    {
        AvaloniaXamlLoader.Load(this);

        if (this.FindControl<Border>("SessionDropZone") is { } dropZone)
        {
            DragDrop.SetAllowDrop(dropZone, true);
            dropZone.AddHandler(DragDrop.DragOverEvent, SessionDropZone_DragOver);
            dropZone.AddHandler(DragDrop.DragLeaveEvent, SessionDropZone_DragLeave);
            dropZone.AddHandler(DragDrop.DropEvent, SessionDropZone_Drop);
        }
    }

    private PresetsTabViewModel? ViewModel => DataContext as PresetsTabViewModel;

    private void SessionRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: Session session })
        {
            ViewModel?.SelectSessionCommand.Execute(session);
        }
    }

    private void PresetCard_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: Preset preset })
        {
            ViewModel?.SelectPresetCommand.Execute(preset);
        }
    }

    private void EditSessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: Session session })
        {
            ViewModel?.EditSessionCommand.Execute(session);
        }
    }

    private void ExportSessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: Session session })
        {
            ViewModel?.ExportSessionCommand.Execute(session);
        }
    }

    private void ShareSessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: Session session })
        {
            ViewModel?.ShareSessionCommand.Execute(session);
        }
    }

    private void DeleteSessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: Session session })
        {
            ViewModel?.DeleteSessionCommand.Execute(session);
        }
    }

    private void SessionDropZone_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        ViewModel?.SetDropZoneActiveCommand.Execute(true);
        e.Handled = true;
    }

    private void SessionDropZone_DragLeave(object? sender, DragEventArgs e)
    {
        ViewModel?.SetDropZoneActiveCommand.Execute(false);
        e.Handled = true;
    }

    private void SessionDropZone_Drop(object? sender, DragEventArgs e)
    {
        ViewModel?.SetDropZoneActiveCommand.Execute(false);

        if (!e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.Handled = true;
            return;
        }

        var files = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().Select(f => f.Path.LocalPath).ToArray();
        if (files?.Length > 0)
        {
            ViewModel?.HandleSessionDropCommand.Execute(files);
        }

        e.Handled = true;
    }
}
