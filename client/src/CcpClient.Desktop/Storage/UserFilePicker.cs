namespace CcpClient.Desktop.Storage;

/// <summary>
/// Why a picked file is refused. A CODE, never a message: an <see cref="IOException"/>'s text
/// carries the full path of the file that failed, so returning <c>ex.Message</c> would defeat the
/// whole point of this seam by the shortest possible route.
/// </summary>
public enum UserFileRefusal
{
    /// <summary>This platform/window has no file picker at all, so nothing was asked and nothing
    /// happened. Avalonia's <c>TopLevel.StorageProvider</c> is never null — it degrades to a
    /// <c>NoopStorageProvider</c> whose pickers return empty (Avalonia 12.1.1
    /// <c>TopLevel.cs:521-524</c>) — so without this probe a missing backend is a dead button.</summary>
    NoPicker,

    /// <summary>The user chose a file and it could not be read.</summary>
    ReadFailed,

    /// <summary>The user chose a destination and it could not be written.</summary>
    WriteFailed,

    /// <summary>The chosen file is larger than <see cref="UserFilePicker.MaxTextBytes"/>. Refused
    /// BEFORE the bytes are held, so picking a disc image where a small document was meant costs a
    /// message rather than the process.</summary>
    TooLarge,
}

/// <summary>What a save attempt did.</summary>
public abstract record UserFileSave
{
    private UserFileSave() { }

    /// <summary>The user chose a destination and the bytes are on disk.</summary>
    public sealed record Saved : UserFileSave
    {
        public static readonly Saved Instance = new();
    }

    /// <summary>The user closed the picker without choosing. Not an error.</summary>
    public sealed record Cancelled : UserFileSave
    {
        public static readonly Cancelled Instance = new();
    }

    /// <summary>Nothing was written, for the named reason.</summary>
    public sealed record Refused(UserFileRefusal Reason) : UserFileSave;
}

/// <summary>What an open attempt did.</summary>
public abstract record UserFileOpen
{
    private UserFileOpen() { }

    /// <summary>
    /// The user chose a file and here is its text. <see cref="Text"/> is the ONLY string this
    /// seam ever hands back, and it is the file's CONTENT — pinned by
    /// <c>PhraseBackupTests.NoOutcomeOfTheSeamCarriesAPathOrAFileName</c>.
    /// </summary>
    public sealed record Opened(string Text) : UserFileOpen;

    /// <summary>The user closed the picker without choosing. Not an error.</summary>
    public sealed record Cancelled : UserFileOpen
    {
        public static readonly Cancelled Instance = new();
    }

    /// <summary>Nothing was read, for the named reason.</summary>
    public sealed record Refused(UserFileRefusal Reason) : UserFileOpen;
}

/// <summary>
/// One document kind, as the OS dialog needs to describe it.
/// </summary>
/// <param name="Label">What the filter row says, e.g. "Phrase backup".</param>
/// <param name="Patterns">GLOB patterns. "Patterns are used by most Windows, Linux and Browser
/// platforms" (Avalonia docs, <i>File Picker Options → Defining custom file types</i>).</param>
/// <param name="MimeTypes">"a web identifier for the files used on most platforms, but not Windows
/// and iOS" (same page) — this is the hint the Linux xdg-desktop-portal picker uses.</param>
/// <param name="DefaultExtension">Appended by the save dialog when the user types a bare name.
/// NO LEADING DOT: Avalonia hands this straight to <c>IFileSaveDialog::SetDefaultExtension</c>
/// (12.1.1 <c>Win32StorageProvider.cs:142-147</c>), which documents the period as excluded.
/// Apple's uniform type identifiers are deliberately NOT modelled: this port ships Windows and
/// Linux, and the same page says an unknown hint must be left null rather than guessed.</param>
public sealed record UserFileKind(
    string Label,
    IReadOnlyList<string> Patterns,
    IReadOnlyList<string> MimeTypes,
    string DefaultExtension);

/// <summary>
/// <b>The client's one open-or-save mechanism, as a seam.</b> Every future file dialog in this
/// product goes through here.
///
/// <para><b>WHY IT IS SHAPED LIKE THIS — text in, text out.</b> <c>client/port.txt</c> reserves the
/// PATH boundary to the owner, and this seam is admitted on the reading that a USER-INITIATED
/// picker does not broaden it: the user chooses the file, in a system dialog, once per operation.
/// The rule exists to stop the app reading or writing paths <i>of its own accord</i>, so the seam is
/// built so that it CANNOT:</para>
///
/// <list type="number">
/// <item><description><b>No default directory that is not the user's own choice.</b>
/// <c>PickerOptions.SuggestedStartLocation</c> is never set, and there is no parameter through
/// which a caller could set it. The OS opens wherever the OS opens.</description></item>
/// <item><description><b>No remembered path used without a fresh gesture.</b> This interface has no
/// "again", no handle, no token: the only way to reach a byte is to open a picker, so a second
/// operation is a second dialog. Remembering something for DISPLAY would be fine; silently reusing
/// a path is exactly the defect, and there is nothing here to reuse.</description></item>
/// <item><description><b>No enumeration beyond what the picker returns.</b> The implementation
/// touches the single <c>IStorageFile</c> the dialog handed back. It never calls
/// <c>OpenFolderPickerAsync</c>, <c>GetItemsAsync</c>, <c>GetParentAsync</c>,
/// <c>TryGetWellKnownFolderAsync</c> or the bookmark API — no globbing a chosen file's
/// siblings.</description></item>
/// <item><description><b>No path or file name reaches a log, a diagnostic, a bark, or a saved
/// document.</b> The seam returns TEXT and TYPED CODES; the path stays inside the method that
/// received it and dies with it. This port already holds that line for the user's own media
/// (<c>Effects/MandatoryVideoEffect.cs:9-10</c>, <c>Effects/FlashImagesEffect.cs:8-10</c>) and this
/// is at least as strict, because those two at least pass a path to their own surface and this
/// passes one nowhere at all. It is a deliberate DIVERGENCE from upstream, which puts the full path
/// in the confirmation dialog and in the log on both paths
/// (<c>MainWindow/MainWindow.PresetIO.cs:82-83</c>, <c>:127</c>).</description></item>
/// </list>
///
/// <para>The API is Avalonia 12's <c>IStorageProvider</c>, reached from
/// <c>TopLevel.StorageProvider</c> — v12 removed the v11 <c>OpenFileDialog</c>/<c>SaveFileDialog</c>
/// classes in favour of it (<c>client/docs/row-1-research-inputs.md</c>: "file dialogs →
/// IStorageProvider pickers"), and the current official documentation for the shape used here is
/// <c>docs.avaloniaui.net/docs/services/file-dialogs</c> plus
/// <c>/docs/services/storage/storage-provider</c>.</para>
/// </summary>
public interface IUserFilePicker
{
    /// <summary>
    /// Asks the user where to put a document, then writes <paramref name="contents"/> there as
    /// UTF-8. One gesture, one write, no path returned.
    /// </summary>
    Task<UserFileSave> SaveTextAsync(string title, UserFileKind kind, string suggestedFileName, string contents);

    /// <summary>
    /// Asks the user which document to read, then returns its text. One gesture, one read, no path
    /// returned.
    /// </summary>
    Task<UserFileOpen> OpenTextAsync(string title, UserFileKind kind);
}

/// <summary>Limits shared by every implementation of <see cref="IUserFilePicker"/>.</summary>
public static class UserFilePicker
{
    /// <summary>
    /// The most text this seam will read out of a chosen file, 8 MiB. Every document class this
    /// port opens is a small settings file; the cap exists because the picker lets a user choose
    /// ANY file, and reading a video into a string is an out-of-memory crash rather than an error
    /// message. Refused as <see cref="UserFileRefusal.TooLarge"/> without holding the bytes.
    /// </summary>
    public const int MaxTextBytes = 8 * 1024 * 1024;
}
