using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace CcpClient.Desktop.Storage;

/// <summary>
/// The real <see cref="IUserFilePicker"/>: Avalonia 12's <c>IStorageProvider</c>, reached from the
/// <c>TopLevel</c> the gesture came from.
///
/// <para><b>Cross-platform by construction.</b> Nothing here is Windows-specific; the backend is
/// whatever the platform supplies. On Windows that is <c>Win32StorageProvider</c> (IFileDialog).
/// On Linux <c>UsePlatformDetect()</c> selects X11 (<c>AppBuilderDesktopExtensions.cs:30-34</c>,
/// tag 12.1.1) and <c>X11Window.cs:280-292</c> composes a <c>FallbackStorageProvider</c> over, in
/// order, the xdg-desktop-portal FileChooser (<c>DBusSystemDialog</c>), the GTK3 dialog
/// (<c>GtkSystemDialog</c>) and finally Avalonia's own in-app <c>ManagedStorageProvider</c> — so an
/// X11 or XWayland desktop always has a picker, portal or not. The native Wayland backend is opt-in
/// (<c>UseWayland()</c>) and exposes no storage provider at 12.1.1
/// (<c>Avalonia.Wayland/WindowImplBase.cs</c> <c>TryGetFeature</c> answers screens, clipboard and
/// launcher only), so a build that opted into it would get <c>NoopStorageProvider</c> — which is
/// exactly why the capability probe below exists rather than a hopeful call.</para>
///
/// <para><b>The provider is resolved per operation</b>, not captured at construction: a picker
/// bound to a window that has since closed would silently target a dead top level. This class holds
/// no other state at all — see constraint 2 on <see cref="IUserFilePicker"/> — which is pinned by
/// a fact that counts its fields.</para>
///
/// <para><b>What cannot be proved without a human at a desktop.</b> Avalonia marks
/// <c>IStorageProvider</c> and <c>IStorageFile</c> <c>[NotClientImplementable]</c> and enforces it
/// with a member user code cannot write, so there is no fake provider and the six lines below that
/// probe, call and dispose are exercised only by opening a real dialog. Everything that happens to
/// the bytes afterwards lives in <see cref="UserFileTransfer"/> and is proved headlessly.</para>
/// </summary>
public sealed class AvaloniaUserFilePicker : IUserFilePicker
{
    private readonly Func<IStorageProvider?> _provider;

    public AvaloniaUserFilePicker(Func<IStorageProvider?> provider) => _provider = provider;

    /// <summary>
    /// The production binding: the top level of the control the user acted on.
    /// <c>TopLevel.GetTopLevel</c> is v12's required accessor — a <c>TopLevel</c> is no longer
    /// necessarily the visual-tree root (<c>client/docs/row-1-research-inputs.md</c>, v12 windowing
    /// breaking changes) — and it is null for a control that is not attached, which lands as
    /// <see cref="UserFileRefusal.NoPicker"/> rather than as a null-reference.
    /// </summary>
    public static AvaloniaUserFilePicker For(Visual visual) =>
        new(() => TopLevel.GetTopLevel(visual)?.StorageProvider);

    /// <inheritdoc />
    public async Task<UserFileSave> SaveTextAsync(
        string title, UserFileKind kind, string suggestedFileName, string contents)
    {
        // Constraint 2: a fresh gesture every time. The provider is asked again, the dialog is
        // opened again, and nothing from the previous call survives to be reused.
        var provider = _provider();
        if (provider is not { CanSave: true })
        {
            return new UserFileSave.Refused(UserFileRefusal.NoPicker);
        }

        IStorageFile? file;
        try
        {
            file = await provider.SaveFilePickerAsync(
                StoragePickerOptions.ForSave(title, kind, suggestedFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new UserFileSave.Refused(UserFileRefusal.WriteFailed);
        }

        if (file is null)
        {
            return UserFileSave.Cancelled.Instance; // "null if the user canceled the dialog"
        }

        try
        {
            return await UserFileTransfer.WriteTextAsync(file.OpenWriteAsync, contents);
        }
        finally
        {
            file.Dispose(); // IStorageItem is IDisposable and documents that it should be disposed
        }
    }

    /// <inheritdoc />
    public async Task<UserFileOpen> OpenTextAsync(string title, UserFileKind kind)
    {
        var provider = _provider();
        if (provider is not { CanOpen: true })
        {
            return new UserFileOpen.Refused(UserFileRefusal.NoPicker);
        }

        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await provider.OpenFilePickerAsync(StoragePickerOptions.ForOpen(title, kind));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new UserFileOpen.Refused(UserFileRefusal.ReadFailed);
        }

        // "an empty collection if the user canceled the dialog" (Avalonia docs, Storage Provider).
        if (files.Count == 0)
        {
            return UserFileOpen.Cancelled.Instance;
        }

        // Constraint 3: exactly the one file the dialog returned. Nothing walks to its folder, its
        // siblings or its parent, and nothing here reads its name or its path.
        try
        {
            return await UserFileTransfer.ReadTextAsync(files[0].OpenReadAsync);
        }
        finally
        {
            foreach (var picked in files)
            {
                picked.Dispose();
            }
        }
    }
}
