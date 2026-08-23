using System.Reflection;
using System.Text;
using Avalonia.Platform.Storage;
using CcpClient.Desktop.Storage;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The client's open-or-save SEAM, held to the four constraints its board row admitted it under.
///
/// <para><b>A CHECKED API CONSTRAINT SHAPES THIS SUITE.</b> Avalonia 12 marks
/// <c>IStorageProvider</c>, <c>IStorageItem</c> and <c>IStorageFile</c>
/// <c>[NotClientImplementable]</c> and enforces it with an interface member user code cannot write
/// (<c>error CS0535: does not implement 'IStorageProvider.(This interface or abstract class is
/// -not- implementable by user code !)'</c>), so a fake storage provider genuinely does not
/// compile. The seam is therefore split: everything that happens to the BYTES lives in
/// <see cref="UserFileTransfer"/> and is proved here over plain streams, and the part that only a
/// human at a dialog can exercise is reduced to the capability probe and one picker call.</para>
///
/// <para><b>What these facts do NOT prove.</b> No dialog is opened, so nothing here says a window
/// appears, is modal, is placed, returns a file, or maps cancellation on any platform. That half
/// needs a human on a desktop and is recorded as the outstanding gate.</para>
/// </summary>
public class UserFilePickerTests
{
    private static readonly UserFileKind Kind = new(
        Label: "Phrase backup",
        Patterns: ["*.ccpphrases.json"],
        MimeTypes: ["application/json"],
        DefaultExtension: "ccpphrases.json");

    /// <summary>Members of the Avalonia storage API that would look past the one file the user
    /// picked, or would pick a directory on the user's behalf. Split from their own spelling so
    /// this list is not itself a hit when the guard scans the tree.</summary>
    private static readonly string[] ForbiddenStorageMembers =
    [
        "Suggested" + "StartLocation",
        "OpenFolder" + "PickerAsync",
        "TryGetWellKnown" + "FolderAsync",
        "TryGetFolderFrom" + "PathAsync",
        "TryGetFileFrom" + "PathAsync",
        "SaveBook" + "markAsync",
        "OpenFileBook" + "markAsync",
        "OpenFolderBook" + "markAsync",
        "GetItems" + "Async",
        "GetParent" + "Async",
    ];

    // ======================================================================================
    // Constraint 1: no default directory that is not the user's own choice.
    // ======================================================================================

    [Fact]
    public void NeitherHalfOfThePickerNamesAStartingDirectory()
    {
        var save = StoragePickerOptions.ForSave("Export Phrases", Kind, "backup.ccpphrases.json");
        var open = StoragePickerOptions.ForOpen("Import Phrases", Kind);

        Assert.Null(save.SuggestedStartLocation);
        Assert.Null(open.SuggestedStartLocation);
    }

    [Fact]
    public void SaveOptionsCarryTheNameTheTypeAndTheOverwritePrompt()
    {
        var save = StoragePickerOptions.ForSave("Export Phrases", Kind, "ccp-phrases-20260824.ccpphrases.json");

        Assert.Equal("Export Phrases", save.Title);
        Assert.Equal("ccp-phrases-20260824.ccpphrases.json", save.SuggestedFileName);
        // No leading period: Avalonia hands this straight to IFileSaveDialog::SetDefaultExtension
        // (Win32StorageProvider.cs:142-147), which documents the period as excluded.
        Assert.Equal("ccpphrases.json", save.DefaultExtension);
        Assert.True(save.ShowOverwritePrompt);
        var type = Assert.Single(save.FileTypeChoices!);
        // Patterns are what Windows and Linux filter on; the MIME type is what the Linux portal
        // uses (Avalonia docs, File Picker Options → Defining custom file types).
        Assert.Equal(["*.ccpphrases.json"], type.Patterns);
        Assert.Equal(["application/json"], type.MimeTypes);
        // "If a specific hint is not known, don't set random values" — this port has no Apple leg.
        Assert.Null(type.AppleUniformTypeIdentifiers);
    }

    [Fact]
    public void OpenOptionsTakeOneFileAndOfferUpstreamsAllFilesRowBesideTheType()
    {
        var open = StoragePickerOptions.ForOpen("Import Phrases", Kind);

        Assert.Equal("Import Phrases", open.Title);
        Assert.False(open.AllowMultiple);
        Assert.Equal(2, open.FileTypeFilter!.Count);
        Assert.Equal("Phrase backup", open.FileTypeFilter[0].Name);
        // Upstream's open filter ends in "All files (*.*)" (MainWindow/MainWindow.PresetIO.cs:102):
        // a backup that was renamed is still a backup, and the file is judged by its content.
        Assert.Same(FilePickerFileTypes.All, open.FileTypeFilter[1]);
    }

    // ======================================================================================
    // Constraint 2: nothing is remembered between operations.
    // ======================================================================================

    [Fact]
    public void ThePickerHoldsNothingItCouldReuseWithoutAFreshGesture()
    {
        var fields = typeof(AvaloniaUserFilePicker)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        // One field, readonly, and it is the way BACK to the platform rather than a way back to a
        // file: a remembered IStorageFile, path, bookmark or folder would show up here.
        var only = Assert.Single(fields);
        Assert.True(only.IsInitOnly);
        Assert.Equal(typeof(Func<IStorageProvider?>), only.FieldType);
    }

    [Fact]
    public async Task AControlWithNoTopLevelIsATypedRefusalRatherThanACrash()
    {
        // The one path through the real picker that needs no dialog: TopLevel.GetTopLevel returns
        // null for a control that is not attached, so the factory answers null.
        var picker = new AvaloniaUserFilePicker(() => null);

        var saved = await picker.SaveTextAsync("t", Kind, "a.json", "x");
        var opened = await picker.OpenTextAsync("t", Kind);

        Assert.Equal(new UserFileSave.Refused(UserFileRefusal.NoPicker), saved);
        Assert.Equal(new UserFileOpen.Refused(UserFileRefusal.NoPicker), opened);
    }

    // ======================================================================================
    // Constraint 3: no enumeration beyond what the picker returned.
    // ======================================================================================

    [Fact]
    public void NothingInTheProductReachesForAFolderABookmarkOrAStartLocation()
    {
        // The scan-the-source guard idiom this repository already uses (PathPortabilityGuardTests,
        // DataRootChokePointGuardTests). It binds the whole product rather than this seam, so the
        // nine consumers still to be built inherit the constraint instead of re-arguing it.
        var source = Path.Combine([FindRepoRoot(), "client", "src"]);
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var code = line.TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith("///", StringComparison.Ordinal)
                    || code.StartsWith("*", StringComparison.Ordinal))
                {
                    continue; // the paragraph explaining the rule is not a breach of it
                }

                foreach (var member in ForbiddenStorageMembers)
                {
                    if (line.Contains(member, StringComparison.Ordinal))
                    {
                        hits.Add($"{Path.GetRelativePath(source, file)}:{index + 1}: {member}");
                    }
                }
            }
        }

        Assert.Empty(hits);
    }

    // ======================================================================================
    // Constraint 4: no path and no file name crosses the boundary.
    // ======================================================================================

    [Fact]
    public void NoOutcomeOfTheSeamCarriesAPathOrAFileName()
    {
        Type[] outcomes =
        [
            typeof(UserFileSave.Saved), typeof(UserFileSave.Cancelled), typeof(UserFileSave.Refused),
            typeof(UserFileOpen.Opened), typeof(UserFileOpen.Cancelled), typeof(UserFileOpen.Refused),
        ];

        static bool CarriesText(Type type) =>
            type == typeof(string) || type.GetGenericArguments().Contains(typeof(string));

        var strings = outcomes
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => CarriesText(property.PropertyType))
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToArray();

        // Exactly one string leaves this seam and it is the file's CONTENT. A path, a name, a
        // directory or an exception message would show up here as a second entry.
        Assert.Equal(["Opened.Text"], strings);
    }

    [Fact]
    public void TheSeamsWholePublicSurfaceHandsBackNoFileHandleAndNoUri()
    {
        // A caller cannot be handed something it could ask a path of. Every public method and
        // property of the Storage namespace is checked, not just the outcome records.
        var leaks = typeof(IUserFilePicker).Assembly.GetTypes()
            .Where(type => type.IsPublic && type.Namespace == "CcpClient.Desktop.Storage")
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(member => (Type: type, Member: member, Returned: ReturnedType(member)))
                .Where(entry => entry.Returned is not null && IsFileHandle(entry.Returned))
                .Select(entry => $"{entry.Type.Name}.{entry.Member.Name}"))
            .ToArray();

        Assert.Empty(leaks);
    }

    // ======================================================================================
    // The bytes: what happens after the user has chosen.
    // ======================================================================================

    [Fact]
    public async Task SavingTruncatesWhatWasAlreadyThereSoAShorterDocumentLeavesNoTail()
    {
        // A backend whose write stream does NOT truncate. The desktop one does
        // (BclStorageItem.OpenWriteCore is FileMode.Create) but the interface promises only "opens
        // stream for writing", and a backup with a corrupt tail is worse than no backup.
        var file = new FakeFile(Encoding.UTF8.GetBytes("{\"old\":\"a much longer previous document\"}"));

        var saved = await UserFileTransfer.WriteTextAsync(file.OpenWrite, "{\"new\":1}");

        Assert.Equal(UserFileSave.Saved.Instance, saved);
        Assert.Equal("{\"new\":1}", file.Text());
    }

    [Fact]
    public async Task AStreamThatCannotSeekIsStillWrittenRatherThanFailedOn()
    {
        // The other side of the truncate-if-you-can branch: SetLength would throw here, so it must
        // not be attempted. Forward-only is the shape a portal-backed or piped stream can have.
        var file = new FakeFile(seekable: false);

        var saved = await UserFileTransfer.WriteTextAsync(file.OpenWrite, "forward only");

        Assert.Equal(UserFileSave.Saved.Instance, saved);
        Assert.Equal("forward only", file.Text());
    }

    [Fact]
    public async Task SavedTextIsUtf8WithNoByteOrderMark()
    {
        var file = new FakeFile();

        await UserFileTransfer.WriteTextAsync(file.OpenWrite, "héllo");

        Assert.Equal(Encoding.UTF8.GetBytes("héllo"), file.Bytes());
    }

    [Fact]
    public async Task OpeningStripsAByteOrderMarkSoAFileWrittenByAnotherToolStillParses()
    {
        // Upstream reads with File.ReadAllText, which detects and drops the mark
        // (Services/PhraseBackupService.cs:96). System.Text.Json refuses a string that starts with
        // U+FEFF, so a backup saved by Notepad would be "malformed" without this.
        var marked = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("{\"schema\":\"ccp-phrases/v1\"}")).ToArray();
        var file = new FakeFile(marked);

        var opened = await UserFileTransfer.ReadTextAsync(file.OpenRead);

        Assert.Equal("{\"schema\":\"ccp-phrases/v1\"}", Assert.IsType<UserFileOpen.Opened>(opened).Text);
    }

    [Fact]
    public async Task AFileTooLargeToBeADocumentIsRefusedRatherThanReadIntoMemory()
    {
        var opened = await UserFileTransfer.ReadTextAsync(
            () => Task.FromResult<Stream>(new ZeroStream(UserFilePicker.MaxTextBytes + 1)));

        Assert.Equal(new UserFileOpen.Refused(UserFileRefusal.TooLarge), opened);
    }

    [Fact]
    public async Task AFileExactlyAtTheCapIsStillRead()
    {
        // The cap is a limit, not a fence one byte early. Without this, narrowing it by one would
        // pass unnoticed.
        var opened = await UserFileTransfer.ReadTextAsync(
            () => Task.FromResult<Stream>(new ZeroStream(UserFilePicker.MaxTextBytes)));

        Assert.Equal(UserFilePicker.MaxTextBytes, Assert.IsType<UserFileOpen.Opened>(opened).Text.Length);
    }

    [Fact]
    public async Task AFailedWriteIsATypedCodeAndTheExceptionsTextNeverEscapes()
    {
        var denied = new UnauthorizedAccessException(@"Access to C:\Users\someone\phrases.json is denied");

        var saved = await UserFileTransfer.WriteTextAsync(() => Task.FromException<Stream>(denied), "x");

        Assert.Equal(new UserFileSave.Refused(UserFileRefusal.WriteFailed), saved);
    }

    [Fact]
    public async Task AFailedReadIsATypedCodeToo()
    {
        var removed = new IOException("the volume for /media/usb/backups was removed");

        var opened = await UserFileTransfer.ReadTextAsync(() => Task.FromException<Stream>(removed));

        Assert.Equal(new UserFileOpen.Refused(UserFileRefusal.ReadFailed), opened);
    }

    [Fact]
    public async Task AFaultPartWayThroughAWriteIsARefusalRatherThanAnEscapingException()
    {
        var opened = await UserFileTransfer.WriteTextAsync(
            () => Task.FromResult<Stream>(new FailingStream()), "never lands");

        Assert.Equal(new UserFileSave.Refused(UserFileRefusal.WriteFailed), opened);
    }

    // ======================================================================================
    // Helpers.
    // ======================================================================================

    private static Type? ReturnedType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        MethodInfo method => method.ReturnType,
        FieldInfo field => field.FieldType,
        _ => null,
    };

    private static bool IsFileHandle(Type type)
    {
        var candidates = new[] { type }.Concat(type.GetGenericArguments());
        return candidates.Any(candidate =>
            candidate == typeof(Uri)
            || candidate == typeof(FileInfo)
            || candidate == typeof(DirectoryInfo)
            || typeof(IStorageItem).IsAssignableFrom(candidate));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine([directory.FullName, "client", "CcpClient.sln"])))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("repository root not found from " + AppContext.BaseDirectory);
    }

    /// <summary>A file whose bytes survive the stream that wrote them, as a real one does.</summary>
    private sealed class FakeFile
    {
        private readonly MemoryStream _bytes = new();
        private readonly bool _seekable;

        public FakeFile(byte[]? initial = null, bool seekable = true)
        {
            _seekable = seekable;
            if (initial is { Length: > 0 })
            {
                _bytes.Write(initial, 0, initial.Length);
            }

            _bytes.Position = 0;
        }

        public byte[] Bytes() => _bytes.ToArray();

        public string Text() => Encoding.UTF8.GetString(_bytes.ToArray());

        public Task<Stream> OpenWrite()
        {
            _bytes.Position = 0;
            return Task.FromResult<Stream>(new SurvivingStream(_bytes, _seekable));
        }

        public Task<Stream> OpenRead() => Task.FromResult<Stream>(new MemoryStream(_bytes.ToArray(), writable: false));
    }

    /// <summary>A stream over a buffer that outlives it. <see cref="CanSeek"/> is switchable so
    /// the truncate-if-you-can branch has both sides exercised.</summary>
    private sealed class SurvivingStream(MemoryStream inner, bool seekable) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => seekable;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => seekable
            ? inner.Seek(offset, origin)
            : throw new NotSupportedException("stream is forward-only");

        public override void SetLength(long value)
        {
            if (!seekable)
            {
                throw new NotSupportedException("stream is forward-only");
            }

            inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            // deliberately does NOT dispose the buffer: the file survives its writer
        }
    }

    /// <summary>A stream that accepts the open and then fails on the write — the shape of a volume
    /// that goes away mid-save.</summary>
    private sealed class FailingStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new IOException(@"C:\Users\someone\phrases.json: device not ready");

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new IOException(@"C:\Users\someone\phrases.json: device not ready");
    }

    /// <summary>A read-only stream of N zero bytes, so the size cap can be exercised without
    /// allocating the file twice.</summary>
    private sealed class ZeroStream(long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = (int)Math.Min(count, length - _position);
            Array.Clear(buffer, offset, take);
            _position += take;
            return take;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
