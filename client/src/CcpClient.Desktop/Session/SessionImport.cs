using CcpClient.Desktop.Storage;

namespace CcpClient.Desktop.Session;

/// <summary>
/// Why a chosen file is not a session this build can import — upstream's
/// <c>ValidateSessionFile</c> refusals (<c>Services/Session/SessionFileService.cs:118-174</c>) as
/// CODES rather than as messages.
///
/// <para>The code/message split is <see cref="UserFileRefusal"/>'s and it matters more here than
/// anywhere else in the port: upstream builds its refusal out of the file's own contents
/// (<c>:167</c> puts <c>ex.Message</c> from the JSON reader in front of the user, and
/// <c>Windows/SessionEditorWindow.xaml.cs:1072</c> shows it in a dialog), so a crafted file can
/// choose the sentence this application displays. A code cannot be authored.</para>
///
/// <para><b>Two of upstream's six refusals are absent, and neither is a gap.</b> "File not found"
/// (<c>:122-126</c>) and "File must be a .session.json file" (<c>:128-132</c>) are both checks on a
/// PATH, and no path reaches this side of the picker seam — the seam hands back text and typed
/// codes and nothing else (<see cref="IUserFilePicker"/>, constraint 4). Neither is owed: a file
/// that could not be read never becomes <see cref="UserFileOpen.Opened"/> at all, and the extension
/// check is subsumed by the four content checks below AND weaker than them in both directions —
/// upstream accepts a hostile document named <c>evil.session.json</c> and refuses a perfectly good
/// session a user saved as <c>mine.json</c>. What the file IS decides here; what it is called does
/// not.</para>
/// </summary>
public enum SessionFileRefusal
{
    /// <summary>The bytes did not deserialize into a session — upstream's "Failed to parse session
    /// file" (<c>Services/Session/SessionFileService.cs:139-143</c>) and its <c>JsonException</c>
    /// arm (<c>:165-169</c>), which <see cref="ScriptedSession.Parse"/> already answers as null.
    /// It covers everything that is not a JSON object as well: an array, a number, a bare
    /// <c>null</c>, a truncated file, and a document nested past
    /// <c>JsonSerializerOptions.MaxDepth</c>.</summary>
    NotJson,

    /// <summary>Upstream's "Session must have an ID" (<c>:145-148</c>).</summary>
    NoId,

    /// <summary>Upstream's "Session must have a name" (<c>:150-153</c>).</summary>
    NoName,

    /// <summary>Upstream's "Session duration must be greater than 0" (<c>:155-158</c>).</summary>
    NoDuration,
}

/// <summary>What an import attempt did.</summary>
public abstract record SessionImportOutcome
{
    private SessionImportOutcome() { }

    /// <summary>The file was good and the session is now the user's, on disk and in the rack —
    /// upstream's <c>(true, $"Imported '{session.Name}'", session)</c>
    /// (<c>Services/Session/SessionManager.cs:141</c>).</summary>
    /// <param name="Session">What landed, stamped with where it landed. Its
    /// <see cref="ScriptedSession.Id"/> is the one this port minted, never the file's.</param>
    public sealed record Imported(ScriptedSession Session) : SessionImportOutcome;

    /// <summary>The user closed the picker without choosing. Nothing was read and nothing
    /// changed.</summary>
    public sealed record Cancelled : SessionImportOutcome
    {
        public static readonly Cancelled Instance = new();
    }

    /// <summary>The chosen file is not a session this build can import. Nothing was written —
    /// upstream's <c>(false, errorMessage, null)</c> (<c>Services/Session/SessionManager.cs:106</c>).
    /// </summary>
    public sealed record RefusedFile(SessionFileRefusal Reason) : SessionImportOutcome;

    /// <summary>The file was never read, for the picker's own reason.</summary>
    public sealed record RefusedPicker(UserFileRefusal Reason) : SessionImportOutcome;

    /// <summary>The file was good and the copy could not be written — a full or read-only volume,
    /// a locked file. Nothing changed.</summary>
    public sealed record SaveFailed : SessionImportOutcome
    {
        public static readonly SaveFailed Instance = new();
    }
}

/// <summary>
/// <b>Importing a session file</b> — bytes from OUTSIDE this application becoming one of the user's
/// own sessions.
///
/// <para><b>Upstream has two import paths and this ports the outcome of one through the gesture of
/// the other.</b> The OUTCOME is <c>SessionManager.ImportSession</c>
/// (<c>Services/Session/SessionManager.cs:99-142</c>): validate, copy into the custom-sessions
/// folder, and the session joins the rack as the user's own. Upstream reaches it by DRAG AND DROP
/// onto the rack (<c>MainWindow/MainWindow.SessionIO.cs:2054-2102</c>), and this port has no
/// external-file drop surface — so the GESTURE is upstream's other one, the Import button on its
/// session editor, which opens an <c>OpenFileDialog</c> filtered to <c>*.session.json</c> and
/// titled "Import Session" (<c>Windows/SessionEditorWindow.xaml.cs:1060-1066</c>). That button's
/// own outcome is deliberately NOT ported: it loads the file into the editor's TIMELINE
/// (<c>:1083-1096</c>), which is the half of upstream's editor this port models nothing for
/// (<see cref="SessionEditorRules"/>).</para>
///
/// <para><b>The boundary is the one the port already opened, not a new one.</b>
/// <see cref="IUserFilePicker"/> exists precisely so that a user-initiated, one-file-per-gesture
/// system dialog is the only way arbitrary bytes enter this application: no default directory, no
/// remembered path, no enumeration, and no path or file name leaving the seam. Import needs exactly
/// that and nothing more, so nothing here widens it.</para>
///
/// <para><b>An imported file cannot choose where it lands, what it claims to be, or what it is
/// called.</b> Three properties, and each is structural rather than a check that could be
/// forgotten:</para>
/// <list type="number">
/// <item><b>Provenance is not in the file.</b> <see cref="ScriptedSession.Origin"/> and
/// <see cref="ScriptedSession.SourceFilePath"/> are <c>[JsonIgnore]</c> — upstream's own decision,
/// under its own comment "Source Tracking (not serialized to file)"
/// (<c>Models/SessionDefinition.cs:64-69</c>) — so a crafted document cannot claim to be a built-in
/// and cannot aim <see cref="CustomSessionStore.Save"/> at a file of its choosing.</item>
/// <item><b>The file name is minted here, never derived from the bytes.</b> The id becomes a fresh
/// <c>Guid</c>, and the id is what names the file (<c>CustomSessionStore.Save</c>). Upstream
/// instead keeps the authored id and defends the two collisions it creates with two separate
/// counter loops — a unique-id loop (<c>Services/Session/SessionManager.cs:117-127</c>) and a
/// unique-filename loop (<c>Services/Session/SessionFileService.cs:256-266</c>) — because without
/// them an imported file silently overwrites a session the user already had. One mint removes both
/// collisions and takes the file name out of the attacker's hands at the same time; it is the same
/// mechanism the editor already uses when it copies a built-in
/// (<see cref="SessionEditorRules.Apply"/>). <b>Divergence:</b> the authored id does not survive an
/// import. Nothing in this build reads a session id for meaning — it names the file, keys the rack
/// row's control name, and is compared for selection — and upstream's own reason for keeping it
/// (localising a name off it, <c>Models/Session.cs:37-39</c>) has no counterpart here.</item>
/// <item><b>The size cap is the seam's.</b> <see cref="UserFilePicker.MaxTextBytes"/> refuses a
/// file larger than 8 MiB before its bytes are held, so pointing the dialog at a disc image costs a
/// sentence rather than the process.</item>
/// </list>
///
/// <para><b>Nothing is replaced, so nothing is asked.</b> <see cref="PhraseBackup.ImportAsync"/>
/// confirms before it acts because an import there REPLACES the user's phrases
/// (<c>Services/PhraseBackupService.cs:110-112</c>); a session import only ADDS a row, and upstream
/// does not ask either (<c>MainWindow/MainWindow.SessionIO.cs:2093</c> imports straight off the
/// drop).</para>
///
/// <para><b>The duration is carried as authored.</b> Upstream's import checks only that it is
/// positive (<c>Services/Session/SessionFileService.cs:155-158</c>); the editor's
/// 5..181 clamp (<see cref="SessionEditorRules.ClampDuration"/>) is a property of that slider and
/// is deliberately not applied to a file this port did not author, for the same reason difficulty
/// and XP are carried through unchanged.</para>
///
/// <para><b>Nothing is logged.</b> Upstream writes the imported session's NAME to its log
/// (<c>MainWindow/MainWindow.SessionIO.cs:2097</c>); <see cref="CustomSessionStore"/>'s standing
/// rule for this folder is outcomes and counts, never a session's name or description, and an
/// import has a typed outcome that reaches the user instead.</para>
/// </summary>
public sealed class SessionImport
{
    /// <summary>The dialog's title — upstream's <c>title_import_session</c>
    /// (<c>Localization/Languages/en.json:3277</c>, used at
    /// <c>Windows/SessionEditorWindow.xaml.cs:1065</c>).</summary>
    public const string Title = "Import Session";

    /// <summary>
    /// The document kind, as the OS dialog describes it. The label and the glob are upstream's
    /// filter (<c>Windows/SessionEditorWindow.xaml.cs:1064</c>,
    /// <c>"Session Files (*.session.json)|*.session.json"</c>); the MIME hint is added because that
    /// — not the glob — is what the Linux xdg-desktop-portal picker filters on
    /// (<see cref="UserFileKind"/>).
    ///
    /// <para>Upstream's second filter row, <c>All Files (*.*)</c>, is deliberately not offered: it
    /// exists there because its refusal is an extension check that a user with a differently-named
    /// session file would otherwise be unable to get past, and this port refuses on CONTENT, so the
    /// escape hatch has nothing to unlock.</para>
    /// </summary>
    public static readonly UserFileKind FileKind = new(
        Label: "Session Files",
        Patterns: ["*" + ScriptedSession.FileExtension],
        MimeTypes: ["application/json"],
        DefaultExtension: ScriptedSession.FileExtension.TrimStart('.'));

    private readonly IUserFilePicker _picker;
    private readonly CustomSessionStore _store;
    private readonly Func<string> _newId;

    /// <param name="picker">The open-or-save seam. The ONLY way bytes from outside get in.</param>
    /// <param name="store">Where the user's own sessions live. The one writer.</param>
    /// <param name="newId">Where the imported session's fresh id comes from. Injected so a fact can
    /// pin the mint rather than pin a GUID; production passes nothing and gets
    /// <c>Guid.NewGuid().ToString()</c>, as <see cref="SessionEditorRules.Apply"/> does.</param>
    public SessionImport(IUserFilePicker picker, CustomSessionStore store, Func<string>? newId = null)
    {
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(store);
        _picker = picker;
        _store = store;
        _newId = newId ?? (() => Guid.NewGuid().ToString());
    }

    /// <summary>
    /// Upstream's <c>ValidateSessionFile</c>, minus its two path checks
    /// (<c>Services/Session/SessionFileService.cs:139-161</c>), in upstream's order. Null when the
    /// document may be imported.
    ///
    /// <para><b><see cref="SessionFileRefusal.NoId"/> stays even though the id is thrown away.</b>
    /// It is not a check on the id this port will use — that one is minted — it is the check that
    /// says these bytes are a SESSION at all. Every member of <see cref="ScriptedSession"/> has a
    /// default, so <c>{}</c> deserializes into a perfectly valid nameless thirty-minute session;
    /// upstream's three content checks are what stop an empty object, a settings file or somebody
    /// else's JSON from becoming a row in the rack, and all three are needed for that.</para>
    /// </summary>
    public static SessionFileRefusal? Validate(ScriptedSession? session)
    {
        if (session is null)
        {
            return SessionFileRefusal.NotJson;
        }

        if (string.IsNullOrWhiteSpace(session.Id))
        {
            return SessionFileRefusal.NoId;
        }

        if (string.IsNullOrWhiteSpace(session.Name))
        {
            return SessionFileRefusal.NoName;
        }

        return session.DurationMinutes <= 0 ? SessionFileRefusal.NoDuration : null;
    }

    /// <summary>
    /// Ask the user for a session file, check it, and — only then — write a copy into their own
    /// sessions folder. Upstream's order exactly (<c>Services/Session/SessionManager.cs:103-132</c>:
    /// validate first, import second, copy third), so a file that is refused is refused before
    /// anything on disk has been touched.
    ///
    /// <para>Every rejection is a value. A malformed or hostile document produces
    /// <see cref="SessionImportOutcome.RefusedFile"/> and never an exception, so a bad file cannot
    /// take the page with it — which is <see cref="ScriptedSession.Parse"/>'s own contract and
    /// upstream's (<c>Services/Session/SessionFileService.cs:109-112</c>).</para>
    /// </summary>
    public async Task<SessionImportOutcome> RunAsync()
    {
        var opened = await _picker.OpenTextAsync(Title, FileKind);
        if (opened is UserFileOpen.Cancelled)
        {
            return SessionImportOutcome.Cancelled.Instance;
        }

        if (opened is UserFileOpen.Refused refused)
        {
            return new SessionImportOutcome.RefusedPicker(refused.Reason);
        }

        var parsed = ScriptedSession.Parse(((UserFileOpen.Opened)opened).Text);
        if (Validate(parsed) is { } refusal)
        {
            return new SessionImportOutcome.RefusedFile(refusal);
        }

        // The mint. Upstream's two collision loops (SessionManager.cs:117-127,
        // SessionFileService.cs:256-266) both exist to stop an imported file landing on a file that
        // is already there; a fresh id cannot collide, and it is also the only thing standing
        // between a crafted id and the name of the file this port is about to write.
        var landing = parsed!;
        landing.Id = _newId();

        // Origin and SourceFilePath are [JsonIgnore], so `landing` carries this port's defaults for
        // both no matter what the document said. Save stamps them from where the bytes really went.
        return _store.Save(landing) is null
            ? SessionImportOutcome.SaveFailed.Instance
            : new SessionImportOutcome.Imported(landing);
    }
}
