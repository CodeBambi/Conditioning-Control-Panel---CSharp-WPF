using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Avalonia port of the update-available dialog.
/// </summary>
public partial class UpdateNotificationDialog : Window
{
    /// <summary>
    /// Whether the user chose to install the update.
    /// </summary>
    public bool InstallRequested { get; private set; }

    public UpdateNotificationDialog()
    {
        InitializeComponent();
    }

    public UpdateNotificationDialog(UpdateInfo updateInfo) : this()
    {
        if (updateInfo is null) return;

        TxtVersionInfo.Text = Loc.GetF("update_version_available_fmt", updateInfo.Version, GetCurrentVersion());

        TxtFileSize.Text = Loc.GetF("update_download_size_fmt", updateInfo.FormattedFileSize);

        if (!string.IsNullOrWhiteSpace(updateInfo.ReleaseNotes))
        {
            TxtReleaseNotes.Text = ConvertMarkdownToPlainText(updateInfo.ReleaseNotes);
        }
        else
        {
            TxtReleaseNotes.Text = Loc.GetF("update_no_release_notes_fmt", updateInfo.Version);
        }
    }

    private static string GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Convert GitHub markdown release notes to readable plain text for the TextBlock.
    /// </summary>
    private static string ConvertMarkdownToPlainText(string markdown)
    {
        var text = markdown;

        // Remove horizontal rules
        text = Regex.Replace(text, @"^---+\s*$", "", RegexOptions.Multiline);

        // Convert ### headers to uppercase with newline
        text = Regex.Replace(text, @"^###\s*(.+)$", "\n$1", RegexOptions.Multiline);

        // Convert ## headers to uppercase with newline
        text = Regex.Replace(text, @"^##\s*(.+)$", "\n$1", RegexOptions.Multiline);

        // Remove bold markers **text** -> text
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");

        // Convert markdown list items to bullet points
        text = Regex.Replace(text, @"^- ", "• ", RegexOptions.Multiline);

        // Remove markdown links [text](url) -> text
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");

        // Collapse excessive newlines
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }

    private void BtnLater_Click(object? sender, RoutedEventArgs e)
    {
        InstallRequested = false;
        Close(false);
    }

    private void BtnInstall_Click(object? sender, RoutedEventArgs e)
    {
        InstallRequested = true;
        Close(true);
    }
}
