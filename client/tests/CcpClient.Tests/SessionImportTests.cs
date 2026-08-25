using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Storage;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// IMPORTING A SESSION FILE — the first time this build takes a document it did not write and turns
/// it into one of the user's own sessions.
///
/// <para><b>What makes this a different input class from everything else the port reads.</b> The
/// eleven preset documents and the four shipped sessions were written by this application or
/// shipped beside it; an imported file is arbitrary bytes a user pointed a dialog at. So the facts
/// below are not only "a good file works": they pin what a file is NOT allowed to decide — where it
/// lands, what provenance it claims, what the file it lands in is called, and whether it can
/// overwrite something the user already had.</para>
///
/// <para>Upstream is <c>Services/Session/SessionManager.cs:99-142</c> (validate, copy, add) and
/// <c>Services/Session/SessionFileService.cs:118-174</c> (the content checks). No Avalonia
/// anywhere: the picker is a seam and the store is a folder. The BUTTON, its wiring and the
/// sentence the rack shows are the headless suite's
/// (<c>CcpClient.HeadlessTests/SessionImportHeadlessTests.cs</c>).</para>
/// </summary>
public class SessionImportTests
{
    private sealed class Sink : ILogSink
    {
        public List<string> Lines { get; } = [];

        public void Log(string message) => Lines.Add(message);
    }

    /// <summary>The seam, scripted. Records what the consumer asked the dialog for; hands back
    /// whatever the fact scripted.</summary>
    private sealed class FakePicker : IUserFilePicker
    {
        public UserFileOpen OpenOutcome { get; set; } = UserFileOpen.Cancelled.Instance;

        public string? OpenTitle { get; private set; }

        public UserFileKind? OpenKind { get; private set; }

        public int OpenCalls { get; private set; }

        public Task<UserFileSave> SaveTextAsync(
            string title, UserFileKind kind, string suggestedFileName, string contents) =>
            throw new InvalidOperationException("import never saves through the picker");

        public Task<UserFileOpen> OpenTextAsync(string title, UserFileKind kind)
        {
            OpenCalls++;
            OpenTitle = title;
            OpenKind = kind;
            return Task.FromResult(OpenOutcome);
        }
    }

    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), "ccp-session-import-" + Guid.NewGuid().ToString("N"));

    /// <summary>A minimal valid session document, as a file's text.</summary>
    private static string FileText(
        string id = "shared_from_a_friend",
        string name = "Shared From A Friend",
        int durationMinutes = 45,
        string description = "Someone else made this.\nSecond line.") =>
        new ScriptedSession
        {
            Id = id,
            Name = name,
            DurationMinutes = durationMinutes,
            Description = description,
            Difficulty = ScriptedSessionDifficulty.Hard,
            BonusXP = 1200,
        }.ToJson();

    private static (SessionImport Import, FakePicker Picker, CustomSessionStore Store, Sink Log)
        Rig(string root, string text, Func<string>? newId = null)
    {
        var picker = new FakePicker { OpenOutcome = new UserFileOpen.Opened(text) };
        var log = new Sink();
        var store = new CustomSessionStore(root, log);
        return (new SessionImport(picker, store, newId), picker, store, log);
    }

    // =====================================================================================
    //  a good file
    // =====================================================================================

    /// <summary>
    /// The whole outcome, once: a file the user pointed at becomes a session in THEIR folder,
    /// stamped as theirs, with the authored name, duration, difficulty and description intact —
    /// upstream's import (<c>Services/Session/SessionManager.cs:129-141</c>).
    ///
    /// <para>Read back off the DISK rather than off the returned instance, because the instance is
    /// what this code just built and the file is what the rack will read tomorrow.</para>
    /// </summary>
    [Fact]
    public async Task AGoodFileBecomesTheUsersOwnSession_OnDiskAndInTheirCatalogue()
    {
        var root = TempRoot();
        var (import, _, store, log) = Rig(root, FileText(), () => "minted");

        var outcome = Assert.IsType<SessionImportOutcome.Imported>(await import.RunAsync());
        Assert.Equal("Shared From A Friend", outcome.Session.Name);

        var landed = Assert.Single(store.Read());
        Assert.Equal("Shared From A Friend", landed.Name);
        Assert.Equal(45, landed.DurationMinutes);
        Assert.Equal(ScriptedSessionDifficulty.Hard, landed.Difficulty);
        Assert.Equal(1200, landed.BonusXP);
        Assert.Equal("Someone else made this.\nSecond line.", landed.Description);

        // Provenance is a property of the folder that read it, so it says YOURS in the rack.
        Assert.Equal(ScriptedSessionOrigin.Custom, landed.Origin);
        Assert.Equal("YOURS", SessionRackNotices.RowProvenance(ScriptedSessionOrigin.Custom));

        // Beside the built-ins, never among them, and the shipped folder is not even touched.
        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, CustomSessionStore.FolderName)),
            Path.GetDirectoryName(Path.GetFullPath(landed.SourceFilePath)));

        // Nothing content-bearing was logged: no name, no description, no path.
        Assert.Empty(log.Lines);

        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// The three things an imported file does not get to choose. Each is structural rather than a
    /// check that could be forgotten, and the document below tries all three at once: it claims to
    /// be a BUILT-IN, names a <c>sourceFilePath</c> outside the data root entirely, and carries an
    /// id built to escape the folder.
    ///
    /// <para>Provenance and path are <c>[JsonIgnore]</c> — upstream's decision, under its own
    /// comment "Source Tracking (not serialized to file)"
    /// (<c>Models/SessionDefinition.cs:64-69</c>) — so neither reaches the model at all, and the
    /// id is replaced before it can name a file.</para>
    /// </summary>
    [Fact]
    public async Task AnImportedFileChoosesNeitherItsProvenance_ItsPath_NorItsFileName()
    {
        var root = TempRoot();
        var hostile = """
            {
              "id": "../../../pwned",
              "name": "Trust Me",
              "durationMinutes": 30,
              "origin": "builtIn",
              "sourceFilePath": "C:\\Windows\\System32\\drivers\\etc\\hosts",
              "settings": {},
              "phases": []
            }
            """;
        var (import, _, store, _) = Rig(root, hostile, () => "minted-id");

        var outcome = Assert.IsType<SessionImportOutcome.Imported>(await import.RunAsync());

        // The id the file asked for never named anything.
        Assert.Equal("minted-id", outcome.Session.Id);

        var landed = Assert.Single(store.Read());
        Assert.Equal("minted-id", landed.Id);
        Assert.Equal(ScriptedSessionOrigin.Custom, landed.Origin);

        var folder = Path.GetFullPath(Path.Combine(root, CustomSessionStore.FolderName));
        Assert.Equal(folder, Path.GetDirectoryName(Path.GetFullPath(landed.SourceFilePath)));
        Assert.Equal("minted-id" + ScriptedSession.FileExtension, Path.GetFileName(landed.SourceFilePath));

        // Exactly one file, and it is inside the folder — nothing was written beside or above it.
        Assert.Single(Directory.GetFiles(folder));
        Assert.Empty(Directory.GetDirectories(folder));

        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// <b>The reason the id is minted rather than kept.</b> Upstream needs TWO counter loops here —
    /// a unique-id loop (<c>Services/Session/SessionManager.cs:117-127</c>) and a unique-filename
    /// loop (<c>Services/Session/SessionFileService.cs:256-266</c>) — because its import keeps the
    /// authored id and the id is what names the file. Without either, an imported document whose id
    /// matches a session the user already has silently overwrites it.
    ///
    /// <para>This is the fact that fails if the mint is removed: the user's own session, saved
    /// first, is still on disk and still says what they wrote.</para>
    /// </summary>
    [Fact]
    public async Task AnImportNeverOverwritesASessionTheUserAlreadyHad()
    {
        var root = TempRoot();
        var log = new Sink();
        var store = new CustomSessionStore(root, log);

        var mine = new ScriptedSession { Id = "mine", Name = "My Own Work", DurationMinutes = 60 };
        Assert.NotNull(store.Save(mine));

        var picker = new FakePicker
        {
            OpenOutcome = new UserFileOpen.Opened(FileText(id: "mine", name: "Not Yours")),
        };
        Assert.IsType<SessionImportOutcome.Imported>(
            await new SessionImport(picker, store, () => "minted").RunAsync());

        var all = store.Read();
        Assert.Equal(2, all.Count);
        var kept = Assert.Single(all, s => s.Name == "My Own Work");
        Assert.Equal(60, kept.DurationMinutes);
        Assert.Equal("mine" + ScriptedSession.FileExtension, Path.GetFileName(kept.SourceFilePath));
        Assert.Single(all, s => s.Name == "Not Yours");

        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// The same file imported twice is two sessions, as it is upstream (its unique-id loop makes a
    /// second row rather than refusing, <c>Services/Session/SessionManager.cs:117-127</c>). A
    /// re-import that silently replaced the first would be a data loss nobody asked for.
    /// </summary>
    [Fact]
    public async Task ImportingTheSameFileTwiceMakesTwoSessions()
    {
        var root = TempRoot();
        var log = new Sink();
        var store = new CustomSessionStore(root, log);
        var picker = new FakePicker { OpenOutcome = new UserFileOpen.Opened(FileText()) };
        var ids = new Queue<string>(["first", "second"]);
        var import = new SessionImport(picker, store, ids.Dequeue);

        Assert.IsType<SessionImportOutcome.Imported>(await import.RunAsync());
        Assert.IsType<SessionImportOutcome.Imported>(await import.RunAsync());

        var all = store.Read();
        Assert.Equal(2, all.Count);
        Assert.All(all, s => Assert.Equal("Shared From A Friend", s.Name));
        Assert.Equal(
            ["first", "second"],
            all.Select(s => s.Id).OrderBy(id => id, StringComparer.Ordinal));

        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// Keys this build has never heard of survive an import and reach the disk — the persistence
    /// contract's §6 rule, which is what lets a file authored by a newer build be imported here
    /// without being truncated by the import.
    /// </summary>
    [Fact]
    public async Task KeysThisBuildDoesNotModelSurviveAnImportOntoDisk()
    {
        var root = TempRoot();
        var text = """
            {
              "id": "from_the_future",
              "name": "From The Future",
              "durationMinutes": 20,
              "somethingThisBuildHasNeverHeardOf": { "nested": [1, 2, 3] }
            }
            """;
        var (import, _, store, _) = Rig(root, text, () => "minted");

        Assert.IsType<SessionImportOutcome.Imported>(await import.RunAsync());

        var landed = Assert.Single(store.Read());
        var extension = Assert.Single(landed.ExtensionData!);
        Assert.Equal("somethingThisBuildHasNeverHeardOf", extension.Key);
        Assert.Equal(3, extension.Value.GetProperty("nested").GetArrayLength());

        // And it is in the BYTES, not just in the instance the import returned.
        var onDisk = File.ReadAllText(landed.SourceFilePath);
        Assert.Contains("somethingThisBuildHasNeverHeardOf", onDisk, StringComparison.Ordinal);

        Directory.Delete(root, recursive: true);
    }

    // =====================================================================================
    //  refusals
    // =====================================================================================

    /// <summary>
    /// Everything that is not a session document is one refusal with one code — upstream's "Failed
    /// to parse session file" (<c>Services/Session/SessionFileService.cs:139-143</c>) and its
    /// <c>JsonException</c> arm (<c>:165-169</c>), which <see cref="ScriptedSession.Parse"/> already
    /// answers as null. NOTHING is written: the folder does not even come into existence.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("{\"id\": \"x\", \"name\": \"y\", \"durationMinutes\": ")]
    [InlineData("{\"id\": \"x\", \"name\": \"y\", \"difficulty\": \"impossible\"}")]
    public async Task AFileThatIsNotASessionDocumentIsRefused_AndNothingIsWritten(string text)
    {
        var root = TempRoot();
        var (import, _, store, _) = Rig(root, text);

        var refused = Assert.IsType<SessionImportOutcome.RefusedFile>(await import.RunAsync());
        Assert.Equal(SessionFileRefusal.NotJson, refused.Reason);
        Assert.Empty(store.Read());
        Assert.False(Directory.Exists(Path.Combine(root, CustomSessionStore.FolderName)));
    }

    /// <summary>
    /// Upstream's three CONTENT checks, in upstream's order and with upstream's meanings
    /// (<c>Services/Session/SessionFileService.cs:145-158</c>). The empty object is the one that
    /// shows why all three are needed: every member of <see cref="ScriptedSession"/> has a default,
    /// so <c>{}</c> is a structurally valid nameless thirty-minute session and only these checks
    /// stop it becoming a row.
    /// </summary>
    [Theory]
    [InlineData("{}", SessionFileRefusal.NoId)]
    [InlineData("{\"name\": \"No Id\"}", SessionFileRefusal.NoId)]
    [InlineData("{\"id\": \"  \", \"name\": \"Blank Id\"}", SessionFileRefusal.NoId)]
    [InlineData("{\"id\": \"x\"}", SessionFileRefusal.NoName)]
    [InlineData("{\"id\": \"x\", \"name\": \"   \"}", SessionFileRefusal.NoName)]
    [InlineData("{\"id\": \"x\", \"name\": \"y\", \"durationMinutes\": 0}", SessionFileRefusal.NoDuration)]
    [InlineData("{\"id\": \"x\", \"name\": \"y\", \"durationMinutes\": -30}", SessionFileRefusal.NoDuration)]
    public async Task UpstreamsThreeContentChecksEachRefuse_AndNothingIsWritten(
        string text, SessionFileRefusal expected)
    {
        var root = TempRoot();
        var (import, _, store, _) = Rig(root, text);

        var refused = Assert.IsType<SessionImportOutcome.RefusedFile>(await import.RunAsync());
        Assert.Equal(expected, refused.Reason);
        Assert.Empty(store.Read());
        Assert.False(Directory.Exists(Path.Combine(root, CustomSessionStore.FolderName)));
    }

    /// <summary>
    /// A duration outside the EDITOR's slider range is imported as authored. Upstream's import
    /// checks only that it is positive (<c>Services/Session/SessionFileService.cs:155-158</c>); the
    /// 5..181 clamp belongs to the editor's control (<see cref="SessionEditorRules.ClampDuration"/>)
    /// and is deliberately not imposed on a file this port did not author, for the same reason
    /// difficulty and XP are carried through unchanged.
    /// </summary>
    [Fact]
    public async Task ADurationOutsideTheEditorsSliderIsImportedAsAuthored()
    {
        var root = TempRoot();
        var (import, _, store, _) = Rig(root, FileText(durationMinutes: 3), () => "minted");

        Assert.IsType<SessionImportOutcome.Imported>(await import.RunAsync());
        Assert.Equal(3, Assert.Single(store.Read()).DurationMinutes);
        Assert.NotEqual(3, SessionEditorRules.ClampDuration(3));

        Directory.Delete(root, recursive: true);
    }

    /// <summary>A closed dialog is not an error and not a change — nothing is read, nothing is
    /// written, and the folder is not created.</summary>
    [Fact]
    public async Task CancellingThePickerChangesNothing()
    {
        var root = TempRoot();
        var picker = new FakePicker { OpenOutcome = UserFileOpen.Cancelled.Instance };
        var store = new CustomSessionStore(root, new Sink());

        Assert.IsType<SessionImportOutcome.Cancelled>(
            await new SessionImport(picker, store).RunAsync());
        Assert.Equal(1, picker.OpenCalls);
        Assert.Empty(store.Read());
        Assert.False(Directory.Exists(root));
    }

    /// <summary>
    /// The picker's own refusals come through as the picker's own typed codes, never as a file
    /// refusal — a desktop with no picker at all and a file too large to hold are different
    /// problems from a bad session document, and the sentence a user reads has to say which.
    /// </summary>
    [Theory]
    [InlineData(UserFileRefusal.NoPicker)]
    [InlineData(UserFileRefusal.ReadFailed)]
    [InlineData(UserFileRefusal.TooLarge)]
    public async Task APickerRefusalCarriesThePickersOwnReason_AndNothingIsWritten(
        UserFileRefusal reason)
    {
        var root = TempRoot();
        var picker = new FakePicker { OpenOutcome = new UserFileOpen.Refused(reason) };
        var store = new CustomSessionStore(root, new Sink());

        var refused = Assert.IsType<SessionImportOutcome.RefusedPicker>(
            await new SessionImport(picker, store).RunAsync());
        Assert.Equal(reason, refused.Reason);
        Assert.Empty(store.Read());
        Assert.False(Directory.Exists(root));
    }

    /// <summary>
    /// A write that cannot happen is an answer, not an exception —
    /// <see cref="CustomSessionStore"/>'s own persist shape, reproduced by putting a FILE where the
    /// folder has to be so <c>Directory.CreateDirectory</c> fails for a real reason on every
    /// platform. Upstream lets the <c>IOException</c> out of its click handler
    /// (<c>Services/Session/SessionFileService.cs:62-66</c>).
    /// </summary>
    [Fact]
    public async Task AWriteThatCannotHappenIsAnAnswer_NotAnException()
    {
        var root = TempRoot();
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, CustomSessionStore.FolderName), "not a folder");

        var (import, _, _, log) = Rig(root, FileText(), () => "minted");

        Assert.IsType<SessionImportOutcome.SaveFailed>(await import.RunAsync());
        var line = Assert.Single(log.Lines);
        Assert.StartsWith("custom session: could not be saved (", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Shared From A Friend", line, StringComparison.Ordinal);

        Directory.Delete(root, recursive: true);
    }

    // =====================================================================================
    //  what the dialog is asked for
    // =====================================================================================

    /// <summary>
    /// The dialog is asked for upstream's own document kind and upstream's own title
    /// (<c>Windows/SessionEditorWindow.xaml.cs:1064-1065</c>,
    /// <c>Localization/Languages/en.json:3277</c>), with the MIME hint the Linux portal picker
    /// filters on added.
    /// </summary>
    [Fact]
    public async Task TheDialogIsAskedForUpstreamsOwnDocumentKindAndTitle()
    {
        var root = TempRoot();
        var (import, picker, _, _) = Rig(root, FileText(), () => "minted");

        await import.RunAsync();

        Assert.Equal("Import Session", picker.OpenTitle);
        Assert.Equal(SessionImport.Title, picker.OpenTitle);
        Assert.Equal("Session Files", picker.OpenKind!.Label);
        Assert.Equal(["*.session.json"], picker.OpenKind.Patterns);
        Assert.Equal(["application/json"], picker.OpenKind.MimeTypes);
        Assert.Equal("session.json", picker.OpenKind.DefaultExtension);

        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// <see cref="SessionImport.Validate"/> answers null for a document that may be imported, which
    /// is what makes every refusal above a decision rather than an accident of ordering.
    /// </summary>
    [Fact]
    public void ValidateAnswersNullForADocumentThatMayBeImported()
    {
        Assert.Null(SessionImport.Validate(ScriptedSession.Parse(FileText())));
        Assert.Equal(SessionFileRefusal.NotJson, SessionImport.Validate(null));
    }

    // =====================================================================================
    //  the sentences
    // =====================================================================================

    /// <summary>
    /// Every refusal a user can reach says what went wrong AND that nothing was added — upstream's
    /// "Invalid session file: {0}" (<c>Localization/Languages/en.json:3300</c>) with a typed reason
    /// where upstream substitutes a message the FILE can author
    /// (<c>Services/Session/SessionFileService.cs:167</c>).
    /// </summary>
    [Fact]
    public void EveryImportRefusalSaysWhatWentWrong_AndThatNothingWasAdded()
    {
        var sentences = new List<string>();
        foreach (var reason in Enum.GetValues<SessionFileRefusal>())
        {
            var line = SessionRackNotices.ImportRefusedFile(reason);
            Assert.StartsWith("That file isn't a session this build can import: ", line, StringComparison.Ordinal);
            Assert.EndsWith("Nothing was added.", line, StringComparison.Ordinal);
            sentences.Add(line);
        }

        foreach (var reason in Enum.GetValues<UserFileRefusal>())
        {
            var line = SessionRackNotices.ImportRefusedPicker(reason);
            Assert.StartsWith("Could not read that file: ", line, StringComparison.Ordinal);
            sentences.Add(line);
        }

        sentences.Add(SessionRackNotices.ImportFaulted("IOException"));

        // Distinct, so no two different problems read as the same problem.
        Assert.Equal(sentences.Count, sentences.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// <b>No import sentence carries a path or a file name.</b> The picker seam's fourth constraint
    /// (<see cref="IUserFilePicker"/>), on the surface where it is easiest to break: upstream puts
    /// the full path in its own confirmation and its log
    /// (<c>MainWindow/MainWindow.SessionIO.cs:2097</c>).
    ///
    /// <para>The success line names the FOLDER — a constant this application chose, not anything
    /// the user's file system told it — and that is the one thing a user can act on.</para>
    /// </summary>
    [Fact]
    public void NoImportSentenceCarriesAPathOrAFileName()
    {
        var session = new ScriptedSession { Id = "x", Name = "Shared", DurationMinutes = 30 };
        var lines = new List<string> { SessionRackNotices.Imported(session), SessionRackNotices.ImportFaulted("IOException") };
        lines.AddRange(Enum.GetValues<SessionFileRefusal>().Select(SessionRackNotices.ImportRefusedFile));
        lines.AddRange(Enum.GetValues<UserFileRefusal>().Select(SessionRackNotices.ImportRefusedPicker));

        foreach (var line in lines)
        {
            Assert.DoesNotContain("\\", line, StringComparison.Ordinal);
            Assert.DoesNotContain("/", line, StringComparison.Ordinal);
            Assert.DoesNotContain(ScriptedSession.FileExtension, line, StringComparison.Ordinal);
        }

        Assert.Contains(CustomSessionStore.FolderName, lines[0], StringComparison.Ordinal);
        Assert.Contains("Shared", lines[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// The rack's absence notice no longer claims import is missing, and it does name the corner-GIF
    /// overlay it really is missing. A sentence that goes on listing something the build now has is
    /// the rot this row has corrected twice already.
    /// </summary>
    [Fact]
    public void TheAbsenceNoticeStoppedClaimingImportIsMissing_AndNamesTheCornerGifInstead()
    {
        Assert.DoesNotContain("importing", SessionRackNotices.Absences, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("corner-GIF", SessionRackNotices.Absences, StringComparison.Ordinal);
        Assert.Contains("timeline", SessionRackNotices.Absences, StringComparison.Ordinal);
        Assert.Contains("XP award", SessionRackNotices.Absences, StringComparison.Ordinal);
    }

    /// <summary>
    /// The corner-GIF flag is still bound off the file and still applied by nothing — the refusal
    /// recorded on <see cref="ScriptedSession.HasCornerGifOption"/>. One shipped session sets it,
    /// so this fact would notice the day something started reading it.
    /// </summary>
    [Fact]
    public void TheCornerGifFlagIsCarriedByTheFileAndAppliedByNothing()
    {
        var session = ScriptedSession.Parse(
            """{"id":"gamer_girl","name":"g","hasCornerGifOption":true,"cornerGifDescription":"a gif"}""");

        Assert.NotNull(session);
        Assert.True(session.HasCornerGifOption);
        Assert.Equal("a gif", session.CornerGifDescription);

        // Round-trips unchanged, which is all this build promises about it.
        var again = ScriptedSession.Parse(session.ToJson())!;
        Assert.True(again.HasCornerGifOption);
        Assert.Equal("a gif", again.CornerGifDescription);
    }
}
