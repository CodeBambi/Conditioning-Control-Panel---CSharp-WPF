using Avalonia.Platform.Storage;

namespace CcpClient.Desktop.Storage;

/// <summary>
/// The Avalonia picker options this product asks for, built in one place so the constraints in
/// <see cref="IUserFilePicker"/> are a property of the OPTIONS rather than of each call site.
///
/// <para>Pure: it constructs option objects and touches no window, no provider and no disk, which
/// is what lets constraint 1 (<b>no default directory that is not the user's own choice</b>) be
/// pinned by a fact on a machine with no desktop at all.</para>
/// </summary>
public static class StoragePickerOptions
{
    /// <summary>
    /// Save options. <c>ShowOverwritePrompt</c> is on because the user is choosing a destination
    /// and may land on a file they still want; Avalonia documents it as supported on Windows and
    /// Linux and ignored on macOS (<i>File Picker Options → Platform compatibility</i>).
    ///
    /// <para><b>SuggestedStartLocation is deliberately absent</b> — see constraint 1. It is also
    /// the one option Avalonia documents as unreliable on Linux anyway ("On Linux some DBus file
    /// picker don't support start location"), so the strict answer is the portable one too.</para>
    /// </summary>
    public static FilePickerSaveOptions ForSave(string title, UserFileKind kind, string suggestedFileName) => new()
    {
        Title = title,
        SuggestedFileName = suggestedFileName,
        DefaultExtension = kind.DefaultExtension,
        ShowOverwritePrompt = true,
        FileTypeChoices = [TypeOf(kind)],
    };

    /// <summary>
    /// Open options. <c>AllowMultiple</c> is false: every consumer of this seam opens ONE document,
    /// and a picker that can return a set is a picker whose extra results have to go somewhere.
    ///
    /// <para>The second filter is Avalonia's built-in "all files", which is upstream's own open
    /// filter for this document class (<c>MainWindow/MainWindow.PresetIO.cs:102</c> —
    /// <c>"...|All files (*.*)|*.*"</c>): a backup that was renamed is still a backup, and the file
    /// is validated by its CONTENT rather than by its name.</para>
    /// </summary>
    public static FilePickerOpenOptions ForOpen(string title, UserFileKind kind) => new()
    {
        Title = title,
        AllowMultiple = false,
        FileTypeFilter = [TypeOf(kind), FilePickerFileTypes.All],
    };

    private static FilePickerFileType TypeOf(UserFileKind kind) => new(kind.Label)
    {
        Patterns = [.. kind.Patterns],
        MimeTypes = [.. kind.MimeTypes],
    };
}
