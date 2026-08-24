namespace CcpClient.Desktop.Session;

/// <summary>
/// The user's OWN scripted sessions on disk — upstream's <c>CustomSessions</c> folder
/// (<c>Services/Session/SessionFileService.cs:27-35</c>) and the three things it does to it: read
/// the folder (<c>:180-200</c>), write a session into it (<c>:226-247</c>), and delete one
/// (<c>:279-300</c>).
///
/// <para><b>This is the persistence decision the editor forced, made deliberately.</b> Until this
/// file, nothing in the port wrote a <c>.session.json</c> at all: the four built-ins are content
/// linked read-only beside the binary and the rack could only ever read them. An editor changes
/// that by existing — a user who edits a built-in session has made something that is no longer
/// built-in — so the question "where does it go" has to be answered before the first byte is
/// written, because a file in the wrong place is far harder to take back than one never written.
/// The answer is upstream's answer in this port's own idiom:</para>
/// <list type="number">
/// <item><b>Under the data root, never beside the binary.</b> Upstream puts custom sessions in
/// <c>%APPDATA%</c> and built-ins in <c>BaseDirectory/assets/sessions</c>
/// (<c>Services/Session/SessionFileService.cs:27-49</c>), and the split is not decoration: the
/// program directory can be read-only, is wiped by a reinstall, and is shared between users. The
/// port's data root is the one the whole product already funnels through
/// (<c>Lifecycle/CompositionRoot.cs:175</c>), so a harness that isolates the root isolates this
/// folder too, and no test can write into a developer's real profile by forgetting to.</item>
/// <item><b>Beside the session logs, not among the settings documents.</b>
/// <see cref="ScriptedSessionLogStore"/> already owns a folder in exactly this position
/// (<c>&lt;data root&gt;/session_logs</c>), and it is the right precedent rather than the eleven
/// preset documents: those are ONE known file each, loaded at boot and flushed at teardown by the
/// persistence store. These are an open-ended set of user documents, written on an explicit save
/// gesture and never on a flush.</item>
/// <item><b>Editing a built-in makes a COPY and never touches the original.</b> Upstream's own
/// rule, stated in its own words at the call site — "Editing a built-in session creates a new
/// custom session" (<c>MainWindow/MainWindow.SessionIO.cs:1834-1836</c>) — with a fresh id, so
/// the shipped file the user started from is still there, unmodified, in the rack beside it. See
/// <see cref="SessionEditorRules.Apply"/>.</item>
/// <item><b>The folder appears the day a session is really saved.</b> Upstream creates it eagerly
/// (<c>Services/Session/SessionFileService.cs:51-58</c>, called from load); this store creates it
/// inside <see cref="Save"/> only — the same call <see cref="ScriptedSessionLogStore.Folder"/>
/// makes, and the same user outcome with one less boot-time write.</item>
/// </list>
///
/// <para><b>What this changes about a claim the rack made yesterday.</b>
/// <see cref="ScriptedSessionSort"/> refused to port upstream's <c>recent</c> order on the ground
/// that "nothing in this build writes a <c>.session.json</c>, so every stamp here is the moment the
/// install laid the file down". That sentence stops being true here, and the enum says so.</para>
/// </summary>
public sealed class CustomSessionStore
{
    /// <summary>The folder's name under the data root. Upstream's is <c>CustomSessions</c>
    /// (<c>Services/Session/SessionFileService.cs:33</c>); this one follows the lower-cased,
    /// underscore-separated shape the port's own data root already uses for
    /// <see cref="ScriptedSessionLogStore.FolderName"/> and its preset documents.</summary>
    public const string FolderName = "custom_sessions";

    /// <param name="dataDirectory">The user's data directory — the one the session's eleven
    /// documents and its media logs already live in, so a saved session lands beside them and
    /// inside whatever root the composition root resolved.</param>
    public CustomSessionStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        Folder = Path.GetFullPath(Path.Combine(dataDirectory, FolderName));
    }

    /// <summary>Where the user's sessions are. Created at the first <see cref="Save"/>, never at
    /// construction.</summary>
    public string Folder { get; }

    /// <summary>The user's own sessions, in file-name order and stamped
    /// <see cref="ScriptedSessionOrigin.Custom"/> — upstream's <c>LoadCustomSessions</c>
    /// (<c>Services/Session/SessionFileService.cs:180-200</c>). An absent folder is an empty list,
    /// which is every install that has never saved one.</summary>
    public IReadOnlyList<ScriptedSession> Read() =>
        ScriptedSession.ReadFolder(Folder, ScriptedSessionOrigin.Custom);

    /// <summary>
    /// Everything the rack draws: the shipped sessions, then the user's — upstream's registry,
    /// which loads built-ins and customs into one list (<c>Services/Session/SessionManager.cs</c>,
    /// via both <c>Load</c> calls on the file service).
    ///
    /// <para>Built-ins FIRST, and both halves in file-name order, so this is a total order that
    /// does not depend on a clock or a filesystem's enumeration — which is what
    /// <see cref="ScriptedSessionSort.Installed"/> promises and what every other order in
    /// <see cref="ScriptedSessionRack.Arrange"/> falls back to as its tie-break.</para>
    /// </summary>
    public IReadOnlyList<ScriptedSession> Catalogue() => [.. ScriptedSession.ReadBuiltIns(), .. Read()];

    /// <summary>
    /// Write one session into the folder and return the path it landed on — upstream's
    /// <c>SaveCustomSession</c> (<c>Services/Session/SessionFileService.cs:226-247</c>), including
    /// its rule for WHICH file: the session's own file when it already has one, and a fresh name
    /// off its id when it does not (<c>:232-241</c>). That is what makes a second edit of the same
    /// custom session an overwrite rather than a third row in the rack.
    ///
    /// <para><b>The path is re-checked, not trusted.</b> Upstream reuses
    /// <c>session.SourceFilePath</c> on nothing but <c>File.Exists</c>; this one also requires the
    /// file to be IN this folder, so a session read from the built-in folder can never be written
    /// back over its shipped original by a caller that forgot to clear the path. The editor clears
    /// it (<see cref="SessionEditorRules.Apply"/>); this is the guard that does not depend on the
    /// editor being right.</para>
    ///
    /// <para>The session is stamped with where it now lives, so the caller's instance and the file
    /// agree without a re-read.</para>
    /// </summary>
    public string Save(ScriptedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Directory.CreateDirectory(Folder);

        var path = Owns(session.SourceFilePath) && File.Exists(session.SourceFilePath)
            ? session.SourceFilePath
            : Path.Combine(Folder, SanitizeId(session.Id) + ScriptedSession.FileExtension);

        File.WriteAllText(path, session.ToJson());
        session.Origin = ScriptedSessionOrigin.Custom;
        session.SourceFilePath = path;
        return path;
    }

    /// <summary>
    /// Delete one of the user's sessions — upstream's two guards and no more
    /// (<c>Services/Session/SessionManager.cs:201-218</c> and
    /// <c>Services/Session/SessionFileService.cs:279-300</c>): a built-in is refused outright, and
    /// so is any path outside this folder.
    ///
    /// <para><b>The containment guard is stronger than upstream's and the outcome is the same
    /// one.</b> Upstream tests <c>filePath.StartsWith(CustomSessionsFolder)</c> (<c>:285</c>),
    /// which also accepts a SIBLING whose name merely begins with the folder's —
    /// <c>…/CustomSessions.bak/x.session.json</c> passes it. This compares the file's own resolved
    /// parent directory to this folder, which admits exactly the set upstream meant to admit.</para>
    ///
    /// <para>False for every refusal and for every failure, as upstream's is (<c>:281-299</c>): a
    /// delete that cannot happen must not take the page with it.</para>
    /// </summary>
    public bool Delete(ScriptedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Origin == ScriptedSessionOrigin.BuiltIn || !Owns(session.SourceFilePath))
        {
            return false;
        }

        try
        {
            if (!File.Exists(session.SourceFilePath))
            {
                return false;
            }

            File.Delete(session.SourceFilePath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Whether a path names a file that is a DIRECT child of this folder. Ordinal, because
    /// every path this store is ever handed came out of its own enumeration or its own
    /// <see cref="Save"/> — never out of a file, where <see cref="ScriptedSession.SourceFilePath"/>
    /// is deliberately not written.</summary>
    private bool Owns(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(path));
            return string.Equals(parent, Folder, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// A session id as a file name — upstream's <c>SanitizeFileName(session.Id)</c>
    /// (<c>Services/Session/SessionFileService.cs:239</c>, <c>:311-315</c>), over the port's
    /// cross-platform invalid set rather than the running OS's, so a folder synced between a
    /// Windows and a Linux install keeps working (<c>PortablePath.cs:27-35</c> — the same reason
    /// <see cref="ScriptedSessionLogStore"/> sanitises the same way).
    ///
    /// <para>Every id this really sees is a GUID (<see cref="SessionEditorRules.Apply"/>), so the
    /// name cannot collide with another session's; upstream's duplicate-name counter
    /// (<c>:259-266</c>) belongs to its IMPORT path, which this build does not have.</para>
    /// </summary>
    private static string SanitizeId(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "session";
        }

        var invalid = PortablePath.InvalidFileNameChars;
        var chars = id.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }
}
