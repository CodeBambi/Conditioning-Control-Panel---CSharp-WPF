using System.Text.Json;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// THE SESSION EDITOR'S ARITHMETIC AND ITS FOLDER — the two halves a window is not needed to see.
///
/// <para>What these facts are about is the decision the editor forced and nothing else could:
/// a user who edits a built-in session has made something that is no longer built-in, so where the
/// bytes go and which file they land on are behaviour rather than plumbing. Upstream's answers are
/// in <c>Services/Session/SessionFileService.cs</c> and at the call site in
/// <c>MainWindow/MainWindow.SessionIO.cs:1833-1864</c>; these pin the port's against them.</para>
///
/// <para>No Avalonia anywhere: <see cref="SessionEditorRules"/> is pure and
/// <see cref="CustomSessionStore"/> is a folder. The window, its boxes and the rack's repaint are
/// the headless suite's (<c>CcpClient.HeadlessTests/SessionEditorHeadlessTests.cs</c>).</para>
/// </summary>
public class SessionEditorTests
{
    private sealed class Sink : ILogSink
    {
        public List<string> Lines { get; } = [];

        public void Log(string message) => Lines.Add(message);
    }

    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), "ccp-session-editor-" + Guid.NewGuid().ToString("N"));

    private static ScriptedSession BuiltIn(string id = "morning_drift", string name = "Morning Drift") =>
        new()
        {
            Id = id,
            Name = name,
            Description = "A gentle start.\nSecond line.",
            DurationMinutes = 30,
            Difficulty = ScriptedSessionDifficulty.Easy,
            BonusXP = 400,
            Origin = ScriptedSessionOrigin.BuiltIn,
            SourceFilePath = Path.Combine("shipped", id + ScriptedSession.FileExtension),
        };

    // =====================================================================================
    //  the copy rule
    // =====================================================================================

    /// <summary>
    /// Upstream's built-in branch, whole: a NEW id, the shipped session untouched, and what comes
    /// out is the user's (<c>MainWindow/MainWindow.SessionIO.cs:1835-1838</c> — "Editing a built-in
    /// session creates a new custom session" — with <c>Source = Custom</c> arriving through
    /// <c>AddNewSession</c>, <c>Services/Session/SessionManager.cs:174-179</c>).
    ///
    /// <para>The cleared path is this port's own and it is the load-bearing half: without it the
    /// session would carry a path INSIDE the shipped folder into the store, and only the store's
    /// containment guard would stand between the user and an overwritten built-in.</para>
    /// </summary>
    [Fact]
    public void EditingABuiltIn_MakesACopyWithANewIdAndLeavesTheShippedSessionAlone()
    {
        var original = BuiltIn();
        var edited = SessionEditorRules.Apply(
            original, "My Drift", "Mine now.", 45, () => "fresh-id");

        Assert.Equal("fresh-id", edited.Id);
        Assert.Equal(ScriptedSessionOrigin.Custom, edited.Origin);
        Assert.Equal(string.Empty, edited.SourceFilePath);
        Assert.Equal("My Drift", edited.Name);
        Assert.Equal("Mine now.", edited.Description);
        Assert.Equal(45, edited.DurationMinutes);

        // The rack's own instance, after the edit: every field exactly as it was.
        Assert.Equal("morning_drift", original.Id);
        Assert.Equal("Morning Drift", original.Name);
        Assert.Equal("A gentle start.\nSecond line.", original.Description);
        Assert.Equal(30, original.DurationMinutes);
        Assert.Equal(ScriptedSessionOrigin.BuiltIn, original.Origin);
        Assert.Equal(
            Path.Combine("shipped", "morning_drift" + ScriptedSession.FileExtension),
            original.SourceFilePath);
    }

    /// <summary>
    /// Upstream's custom branch and its comment's reason: "Preserve original ID, source, and file
    /// path to save over existing file" (<c>MainWindow/MainWindow.SessionIO.cs:1856-1859</c>). This
    /// is what makes a second edit an OVERWRITE rather than a third row in the rack, and the id
    /// factory is proved unreachable on this branch by handing it one that would be obvious.
    /// </summary>
    [Fact]
    public void EditingACustom_KeepsItsIdAndItsFile_SoASecondEditOverwritesRatherThanMultiplies()
    {
        var mine = BuiltIn("kept-id", "Mine");
        mine.Origin = ScriptedSessionOrigin.Custom;
        mine.SourceFilePath = Path.Combine("custom_sessions", "kept-id.session.json");

        var edited = SessionEditorRules.Apply(
            mine, "Mine, longer", "Now with more.", 60, () => "SHOULD-NOT-BE-USED");

        Assert.Equal("kept-id", edited.Id);
        Assert.Equal(mine.SourceFilePath, edited.SourceFilePath);
        Assert.Equal(ScriptedSessionOrigin.Custom, edited.Origin);
        Assert.Equal(60, edited.DurationMinutes);
    }

    /// <summary>
    /// The edit goes THROUGH THE FILE FORMAT, so a key this port has never heard of survives being
    /// read, edited and written — the persistence contract's §6 rule, on the one document a user
    /// can now rewrite by hand. Upstream cannot do this: its editor round-trips through
    /// <c>TimelineSession</c> (<c>Windows/SessionEditorWindow.xaml.cs:63</c>, <c>:1150</c>), which
    /// keeps only what that model has members for.
    /// </summary>
    [Fact]
    public void AnUnknownKeySurvivesAnEdit_RatherThanBeingDroppedByIt()
    {
        var parsed = ScriptedSession.Parse(
            """
            {
              "id": "future",
              "name": "From a newer build",
              "durationMinutes": 30,
              "moodEngineVersion": 7
            }
            """);
        Assert.NotNull(parsed);
        parsed.Origin = ScriptedSessionOrigin.Custom;

        var written = SessionEditorRules.Apply(parsed, "Renamed", "", 35).ToJson();

        using var document = JsonDocument.Parse(written);
        Assert.Equal("Renamed", document.RootElement.GetProperty("name").GetString());
        Assert.Equal(7, document.RootElement.GetProperty("moodEngineVersion").GetInt32());
    }

    /// <summary>Upstream's only save-time refusal, and the thing it protects: an empty name writes
    /// NOTHING (<c>Windows/SessionEditorWindow.xaml.cs:1144-1148</c>). Whitespace is empty, which is
    /// upstream's <c>IsNullOrWhiteSpace</c>.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void AnEmptyNameIsRefused_InUpstreamsOwnWords(string? name) =>
        Assert.Equal(SessionEditorRules.NameRequired, SessionEditorRules.Validate(name));

    /// <summary>A real name passes, and the sentence the refusal carries is upstream's
    /// (<c>msg_please_enter_a_session_name</c>).</summary>
    [Fact]
    public void ARealNamePasses_AndTheRefusalIsUpstreamsSentence()
    {
        Assert.Null(SessionEditorRules.Validate("Morning Drift"));
        Assert.Equal("Please enter a session name.", SessionEditorRules.NameRequired);
    }

    /// <summary>
    /// The duration slider's own bounds, applied to a number that did not come from the slider —
    /// upstream's <c>Minimum="5" Maximum="181"</c> (<c>Windows/SessionEditorWindow.xaml:110</c>).
    /// Both ends, and the boundary values themselves, which is where an off-by-one would live.
    /// </summary>
    [Theory]
    [InlineData(0, 5)]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    [InlineData(45, 45)]
    [InlineData(181, 181)]
    [InlineData(182, 181)]
    [InlineData(100000, 181)]
    [InlineData(-30, 5)]
    public void TheDurationIsClampedToTheSlidersRange(int asked, int expected)
    {
        Assert.Equal(expected, SessionEditorRules.ClampDuration(asked));
        Assert.Equal(expected, SessionEditorRules.Apply(BuiltIn(), "n", "", asked).DurationMinutes);
    }

    /// <summary>
    /// The name is trimmed and the description is NOT, and both halves are deliberate: a trailing
    /// space in a rack row is a defect nobody can see the cause of, and a description's leading
    /// blank line is authored content whose first line is the rack's blurb cell
    /// (<see cref="SessionRackNotices.RowBlurb"/>). Upstream trims neither
    /// (<c>Windows/SessionEditorWindow.xaml.cs:1141-1142</c>).
    /// </summary>
    [Fact]
    public void TheNameIsTrimmedAndTheDescriptionIsNot()
    {
        var edited = SessionEditorRules.Apply(BuiltIn(), "  Padded  ", "  keep me  ", 30);
        Assert.Equal("Padded", edited.Name);
        Assert.Equal("  keep me  ", edited.Description);
    }

    /// <summary>
    /// What the editor does NOT touch, pinned so a later slice that starts deriving them has to
    /// come here first: upstream recomputes difficulty and XP from the timeline it just authored
    /// (<c>Models/TimelineSession.cs:123-145</c>, <c>:150-189</c>) and this port models no timeline,
    /// so the AUTHORED values are carried through rather than a number invented from duration
    /// alone. A 30-minute Easy session stretched to three hours is still Easy here and would not be
    /// upstream — a recorded divergence, and the editor says so on screen.
    /// </summary>
    [Fact]
    public void DifficultyAndXpAreCarriedThroughUnchanged_AndTheEditorSaysSo()
    {
        var edited = SessionEditorRules.Apply(BuiltIn(), "Much longer", "", 180);
        Assert.Equal(ScriptedSessionDifficulty.Easy, edited.Difficulty);
        Assert.Equal(400, edited.BonusXP);

        Assert.Contains(
            "Difficulty and XP stay as authored",
            SessionRackNotices.EditorAbsences,
            StringComparison.Ordinal);
    }

    // =====================================================================================
    //  the folder
    // =====================================================================================

    /// <summary>
    /// Where a custom session lives, proved as a PATH rather than asserted in prose: under the data
    /// root the composition root resolved, in its own folder, beside the session logs — never
    /// beside the binary, where upstream's built-ins are and where a reinstall would wipe it
    /// (<c>Services/Session/SessionFileService.cs:27-46</c>).
    ///
    /// <para>The folder does not exist until a session is really saved, which is one less boot-time
    /// write than upstream's eager create (<c>:51-57</c>) for the same user outcome.</para>
    /// </summary>
    [Fact]
    public void ACustomSessionLandsUnderTheDataRoot_AndTheFolderAppearsOnlyWhenOneIsSaved()
    {
        var root = TempRoot();
        Directory.CreateDirectory(root);
        var store = new CustomSessionStore(root, new Sink());

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "custom_sessions")), store.Folder);

        // Nothing under the data root yet — enumerated rather than asked about, so an absent folder
        // fails this line instead of quietly satisfying a predicate.
        Assert.Empty(Directory.GetDirectories(root));
        Assert.Empty(store.Read());

        var path = store.Save(SessionEditorRules.Apply(BuiltIn(), "Mine", "", 45, () => "abc"));

        Assert.NotNull(path);
        Assert.Equal([store.Folder], Directory.GetDirectories(root).Select(Path.GetFullPath));
        Assert.Equal(store.Folder, Path.GetDirectoryName(path));
        Assert.Equal("abc.session.json", Path.GetFileName(path));

        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// The whole round trip a user makes: edit a built-in, save it, read the folder back — and what
    /// comes back is the EDIT, stamped as the user's, with the shipped file it came from still
    /// where it was. Provenance is read off the FOLDER and never out of the file, which is
    /// upstream's <c>[JsonIgnore]</c> rule (<c>Models/SessionDefinition.cs:64-69</c>) and the reason
    /// a copied file cannot go on claiming to be built-in.
    /// </summary>
    [Fact]
    public void ASavedEditReadsBackAsTheUsersOwn_AndCarriesNoProvenanceInItsBytes()
    {
        var root = TempRoot();
        var store = new CustomSessionStore(root, new Sink());
        var edited = SessionEditorRules.Apply(BuiltIn(), "My Drift", "Mine.", 45, () => "abc");
        var path = store.Save(edited);
        Assert.NotNull(path);

        var back = Assert.Single(store.Read());
        Assert.Equal("My Drift", back.Name);
        Assert.Equal(45, back.DurationMinutes);
        Assert.Equal(ScriptedSessionOrigin.Custom, back.Origin);
        Assert.Equal(path, back.SourceFilePath);

        // The bytes: no origin, no path. Upstream marks both members [JsonIgnore] and this is the
        // same fact read off the file rather than off the attribute.
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.False(document.RootElement.TryGetProperty("origin", out _));
        Assert.False(document.RootElement.TryGetProperty("sourceFilePath", out _));
        Assert.False(document.RootElement.TryGetProperty("source", out _));

        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// A second edit of the same custom session OVERWRITES its own file — upstream's
    /// "use existing file path if set and valid" (<c>Services/Session/SessionFileService.cs:231-242</c>).
    /// One file after two saves, and it holds the second name.
    /// </summary>
    [Fact]
    public void ASecondEditOverwritesItsOwnFile_RatherThanLayingASecondOneBesideIt()
    {
        var root = TempRoot();
        var store = new CustomSessionStore(root, new Sink());

        var first = SessionEditorRules.Apply(BuiltIn(), "Mine", "", 45, () => "abc");
        var firstPath = store.Save(first);

        var second = SessionEditorRules.Apply(store.Read()[0], "Mine, again", "", 60);
        var secondPath = store.Save(second);

        Assert.Equal(firstPath, secondPath);
        var only = Assert.Single(store.Read());
        Assert.Equal("Mine, again", only.Name);
        Assert.Equal(60, only.DurationMinutes);
        Assert.Single(Directory.GetFiles(store.Folder, "*" + ScriptedSession.FileExtension));

        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// A SESSION WHOSE FILE IS NOT NAMED AFTER ITS ID IS STILL OVERWRITTEN, and this fact exists
    /// because the mutation check found that nothing else could tell. Upstream's save prefers the
    /// session's OWN file over a name derived from its id
    /// (<c>Services/Session/SessionFileService.cs:231-242</c>); every session this port has written
    /// so far is named for its id, so deleting that preference changed no answer and every fact
    /// stayed green (mutation M19).
    ///
    /// <para>The scenario that makes it load-bearing is not hypothetical: the custom folder is a
    /// folder, and a user who renames <c>abc.session.json</c> to something they can read owns a
    /// session whose file name and id no longer agree. Without the preference their next edit
    /// writes a SECOND file and the rack grows a duplicate row they never asked for.</para>
    /// </summary>
    [Fact]
    public void AFileTheUserRenamedIsStillOverwritten_RatherThanDuplicatedUnderItsId()
    {
        var root = TempRoot();
        var store = new CustomSessionStore(root, new Sink());
        var mine = SessionEditorRules.Apply(BuiltIn(), "Mine", "", 45, () => "abc");
        var first = store.Save(mine);
        Assert.NotNull(first);

        // The user renames it in their file manager. Nothing else about it changes.
        var renamed = Path.Combine(store.Folder, "my favourite.session.json");
        File.Move(first, renamed);

        var reread = Assert.Single(store.Read());
        Assert.Equal(renamed, reread.SourceFilePath);
        Assert.Equal("abc", reread.Id);

        var again = store.Save(SessionEditorRules.Apply(reread, "Mine, edited", "", 60));

        Assert.Equal(renamed, again);
        Assert.Equal(
            ["my favourite.session.json"],
            Directory.GetFiles(store.Folder, "*" + ScriptedSession.FileExtension)
                .Select(Path.GetFileName));
        Assert.Equal("Mine, edited", Assert.Single(store.Read()).Name);

        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// THE GUARD THAT DOES NOT DEPEND ON THE EDITOR BEING RIGHT. A session still carrying a path
    /// outside this folder is written into the folder under its own id, never back over the file it
    /// names. Upstream's save trusts <c>File.Exists</c> alone (<c>:233</c>), so the same call there
    /// would overwrite the shipped built-in.
    /// </summary>
    [Fact]
    public void ASessionPointingOutsideTheFolderIsWrittenInsideIt_NeverBackOverTheFileItNames()
    {
        var root = TempRoot();
        var elsewhere = Path.Combine(root, "shipped");
        Directory.CreateDirectory(elsewhere);
        var shipped = Path.Combine(elsewhere, "morning_drift.session.json");
        File.WriteAllText(shipped, """{"id":"morning_drift","name":"Morning Drift"}""");

        var store = new CustomSessionStore(root, new Sink());
        var smuggled = BuiltIn();
        smuggled.Origin = ScriptedSessionOrigin.Custom;   // as if a caller had stamped it
        smuggled.SourceFilePath = shipped;
        smuggled.Name = "Overwritten";

        var path = store.Save(smuggled);

        Assert.NotNull(path);
        Assert.Equal(store.Folder, Path.GetDirectoryName(path));
        Assert.Equal("""{"id":"morning_drift","name":"Morning Drift"}""", File.ReadAllText(shipped));

        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// A sibling folder whose NAME merely begins with this one's is outside it. Upstream's
    /// <c>StartsWith</c> (<c>Services/Session/SessionFileService.cs:285</c>) admits
    /// <c>custom_sessions.bak/…</c>; comparing resolved parent directories admits exactly the set
    /// upstream meant to admit.
    /// </summary>
    [Fact]
    public void ASiblingFolderWithAPrefixNameIsNotThisFolder()
    {
        var root = TempRoot();
        var store = new CustomSessionStore(root, new Sink());
        var lookalike = Path.Combine(root, "custom_sessions.bak");
        Directory.CreateDirectory(lookalike);
        var victim = Path.Combine(lookalike, "abc.session.json");
        File.WriteAllText(victim, "{}");

        var session = BuiltIn("abc", "Backed up");
        session.Origin = ScriptedSessionOrigin.Custom;
        session.SourceFilePath = victim;

        // The refused delete left the file where it was — read back rather than asked about, so a
        // file that HAS gone fails here with a missing-file throw instead of a silent predicate.
        Assert.False(store.Delete(session));
        Assert.Equal("{}", File.ReadAllText(victim));

        var path = store.Save(session);
        Assert.Equal(store.Folder, Path.GetDirectoryName(path));
        Assert.Equal("{}", File.ReadAllText(victim));

        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// Delete refuses a built-in outright — upstream's first guard and its own comment, "Can't
    /// delete built-in sessions" (<c>Services/Session/SessionManager.cs:203-205</c>) — and removes
    /// one of the user's own. The persistence contract permits the second: §5 rule 5, "preserved,
    /// never deleted" binds the STORE and not the USER.
    /// </summary>
    [Fact]
    public void DeleteRefusesABuiltIn_AndRemovesOneOfTheUsersOwn()
    {
        var root = TempRoot();
        var store = new CustomSessionStore(root, new Sink());
        var mine = SessionEditorRules.Apply(BuiltIn(), "Mine", "", 45, () => "abc");
        store.Save(mine);

        // A built-in that has somehow been handed this store's own path is STILL refused: the
        // origin check runs first, so provenance alone is enough.
        var pretender = BuiltIn();
        pretender.SourceFilePath = mine.SourceFilePath;
        Assert.False(store.Delete(pretender));
        Assert.Single(store.Read());

        Assert.True(store.Delete(store.Read()[0]));
        Assert.Empty(store.Read());
        Assert.False(store.Delete(mine));   // gone already: false, never a throw

        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// The catalogue the rack draws: the shipped sessions first, then the user's — upstream's
    /// registry order (<c>Services/Session/SessionManager.cs:61-66</c> then <c>:80-87</c>). The four
    /// built-ins are content beside the test binary, so this reads the real files.
    /// </summary>
    [Fact]
    public void TheCatalogueIsBuiltInsThenTheUsersOwn()
    {
        var root = TempRoot();
        var store = new CustomSessionStore(root, new Sink());
        var shipped = ScriptedSession.ReadBuiltIns();
        Assert.NotEmpty(shipped);

        store.Save(SessionEditorRules.Apply(shipped[0], "Mine", "", 45, () => "zzz"));

        var catalogue = store.Catalogue();
        Assert.Equal(shipped.Count + 1, catalogue.Count);
        Assert.Equal(
            shipped.Select(s => s.Id),
            catalogue.Take(shipped.Count).Select(s => s.Id));
        Assert.All(
            catalogue.Take(shipped.Count),
            s => Assert.Equal(ScriptedSessionOrigin.BuiltIn, s.Origin));
        Assert.Equal("Mine", catalogue[^1].Name);
        Assert.Equal(ScriptedSessionOrigin.Custom, catalogue[^1].Origin);

        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// A write that cannot happen is a null and a content-free log line, never an exception out of
    /// a click handler — <see cref="ScriptedSessionLogStore"/>'s own persist shape. Reproduced by
    /// putting a FILE where the folder has to be, so <c>Directory.CreateDirectory</c> fails for a
    /// real reason on every platform.
    /// </summary>
    [Fact]
    public void AWriteThatCannotHappenIsAnAnswer_NotAnException()
    {
        var root = TempRoot();
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "custom_sessions"), "not a folder");

        var sink = new Sink();
        var store = new CustomSessionStore(root, sink);

        Assert.Null(store.Save(SessionEditorRules.Apply(BuiltIn(), "Mine", "", 45, () => "abc")));
        var line = Assert.Single(sink.Lines);
        Assert.StartsWith("custom session: could not be saved (", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Mine", line, StringComparison.Ordinal);

        Directory.Delete(root, recursive: true);
    }

    // =====================================================================================
    //  the sentences
    // =====================================================================================

    /// <summary>
    /// The badge, and why it exists now when it was refused before: two sessions of the SAME NAME
    /// can sit in the rack together the moment a built-in is edited, and this is the only cell that
    /// tells them apart. Upstream's own words and upstream's own colours
    /// (<c>MainWindow/MainWindow.SessionIO.cs:588-597</c>,
    /// <c>Resources/Theme/Colors.xaml:200</c>, <c>:202</c>).
    /// </summary>
    [Fact]
    public void TheRowsProvenanceBadgeCarriesUpstreamsWordsAndUpstreamsColours()
    {
        Assert.Equal("BUILT-IN", SessionRackNotices.RowProvenance(ScriptedSessionOrigin.BuiltIn));
        Assert.Equal("YOURS", SessionRackNotices.RowProvenance(ScriptedSessionOrigin.Custom));
        Assert.Equal(
            "#FF5CC8FF", SessionRackNotices.RowProvenanceColour(ScriptedSessionOrigin.BuiltIn));
        Assert.Equal(
            "#FFFF69B4", SessionRackNotices.RowProvenanceColour(ScriptedSessionOrigin.Custom));
        Assert.NotEqual(
            SessionRackNotices.RowProvenanceColour(ScriptedSessionOrigin.BuiltIn),
            SessionRackNotices.RowProvenanceColour(ScriptedSessionOrigin.Custom));
    }

    /// <summary>
    /// The editor tells the user WHICH of the two things the Save button is about to do, before
    /// they type. Upstream never has to: its built-in branch ends on a Save-As dialog
    /// (<c>MainWindow/MainWindow.SessionIO.cs:1840-1850</c>), which announces the copy by asking
    /// where to put it. This port saves without asking, so the sentence has to be on the window.
    /// </summary>
    [Fact]
    public void TheEditorSaysWhetherSavingCopiesOrOverwrites()
    {
        var builtIn = SessionRackNotices.EditorProvenance(ScriptedSessionOrigin.BuiltIn);
        var custom = SessionRackNotices.EditorProvenance(ScriptedSessionOrigin.Custom);

        Assert.Contains("your own copy", builtIn, StringComparison.Ordinal);
        Assert.Contains("leaves this one alone", builtIn, StringComparison.Ordinal);
        Assert.Contains("overwrites", custom, StringComparison.Ordinal);
        Assert.NotEqual(builtIn, custom);
    }

    /// <summary>
    /// The rack's absence notice stops claiming the editor and custom sessions are missing, because
    /// they are not — and goes on naming what still is. §9 D7: named rather than silently missing,
    /// and never a claim that has stopped being true.
    ///
    /// <para><b>IMPORTING left this list when <see cref="SessionImport"/> landed</b>, and this fact
    /// was updated rather than the notice being left to rot — the same correction the editor made
    /// to it. What it still may not do is drop the two that ARE absent, which is what the
    /// <c>Contains</c> pair below is for. The corner-GIF half of the notice is
    /// <c>SessionImportTests</c>'s.</para>
    /// </summary>
    [Fact]
    public void TheRackNoLongerClaimsTheEditorAndCustomSessionsAreMissing()
    {
        Assert.DoesNotContain(
            "session editor", SessionRackNotices.Absences, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "custom", SessionRackNotices.Absences, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "importing a session file", SessionRackNotices.Absences, StringComparison.Ordinal);

        Assert.Contains("authoring a session's timeline", SessionRackNotices.Absences, StringComparison.Ordinal);
        Assert.Contains("the XP award", SessionRackNotices.Absences, StringComparison.Ordinal);
    }
}
