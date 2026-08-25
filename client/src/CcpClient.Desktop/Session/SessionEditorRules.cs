namespace CcpClient.Desktop.Session;

/// <summary>
/// What the session editor DOES to a session, as arithmetic — upstream's
/// <c>SessionEditorWindow.BtnSave_Click</c> (<c>Windows/SessionEditorWindow.xaml.cs:1138-1153</c>)
/// and the branch that receives its result (<c>MainWindow/MainWindow.SessionIO.cs:1833-1864</c>),
/// with no window anywhere near it.
///
/// <para><b>The rule this file exists for is the one the persistence decision turns on:</b> editing
/// a BUILT-IN session produces a new custom session and leaves the shipped file alone, while
/// editing a CUSTOM one overwrites its own file. Upstream states it in its own words at the branch
/// — "Editing a built-in session creates a new custom session" — and implements it as a fresh
/// <c>Guid</c> plus a save into the custom folder (<c>:1835-1852</c>); its custom branch
/// deliberately restores the original id, source and path first, with its own comment saying why:
/// "Preserve original ID, source, and file path to save over existing file" (<c>:1856-1859</c>).
/// Both halves are here, and <see cref="Apply"/> is a pure function so both are unit facts rather
/// than things a window would have to be mounted to see.</para>
///
/// <para><b>Three fields, not upstream's timeline.</b> Upstream's editor binds the name, the
/// duration and the description to controls directly
/// (<c>Windows/SessionEditorWindow.xaml.cs:64-66</c> loads exactly those three,
/// <c>:1141-1142</c> plus <c>:1027</c> reads them back); everything else on that window is the
/// drag-and-drop TIMELINE, which authors <c>TimelineEvents</c> and re-flattens
/// <c>Settings</c> from them. Those three are what this port's editor edits, and what it leaves is
/// named on the window itself rather than only here.</para>
///
/// <para><b>Difficulty and XP are NOT recomputed, and that is a divergence rather than an
/// oversight.</b> Upstream derives both from the timeline it just authored —
/// <c>CalculateXP</c> is 10/minute plus a per-feature bonus rounded to the nearest 50
/// (<c>Models/TimelineSession.cs:123-145</c>) and <c>CalculateDifficulty</c> is a score over
/// duration, distinct feature count, per-feature weights and three intensity thresholds
/// (<c>:150-189</c>) — and BOTH read <c>FeatureDefinition</c>'s weight table
/// (<c>Models/FeatureDefinition.cs:56-57</c>, twenty entries) through a rebuild of the timeline
/// from <c>Settings</c> (<c>Models/TimelineSession.cs:592-751</c>). None of that is ported: this
/// port has no timeline model, no feature-weight table and no settings-to-events rebuild, so a
/// derivation from duration alone would produce a band upstream would never produce. The authored
/// values are carried through unchanged and the editor SAYS SO on screen, which is the port's
/// standing rule for a refusal (§9 D7) rather than a number invented to fill a cell.</para>
/// </summary>
public static class SessionEditorRules
{
    /// <summary>The shortest session the editor will author — upstream's duration slider
    /// <c>Minimum</c> (<c>Windows/SessionEditorWindow.xaml:110</c>).</summary>
    public const int MinDurationMinutes = 5;

    /// <summary>The longest — upstream's slider <c>Maximum</c> (<c>:110</c>). Odd on purpose:
    /// it is upstream's number, and three hours plus a minute is what its rack really offers.
    /// </summary>
    public const int MaxDurationMinutes = 181;

    /// <summary>Upstream's snap interval on the same slider (<c>:111</c>,
    /// <c>TickFrequency="5" IsSnapToTickEnabled="True"</c>).</summary>
    public const int DurationStepMinutes = 5;

    /// <summary>Upstream's only validation refusal, in upstream's own words
    /// (<c>Windows/SessionEditorWindow.xaml.cs:1144-1148</c>,
    /// <c>msg_please_enter_a_session_name</c>).</summary>
    public const string NameRequired = "Please enter a session name.";

    /// <summary>
    /// Upstream's save-time check and no more (<c>:1144-1148</c>): a name that is empty or nothing
    /// but whitespace is refused and NOTHING is written. Null when the edit may proceed.
    ///
    /// <para>Upstream's <c>ValidateSessionFile</c> is stricter — it also demands an id and a
    /// positive duration (<c>Services/Session/SessionFileService.cs:145-161</c>). This build DOES
    /// now have the import path that guard belongs to — <see cref="SessionImport.Validate"/> is all
    /// three of upstream's content checks — and it is still not owed HERE: the two extra things it
    /// checks cannot fail on this path, because <see cref="Apply"/> gives every session an id and
    /// the duration is clamped into
    /// <see cref="MinDurationMinutes"/>..<see cref="MaxDurationMinutes"/> before it is stored.</para>
    /// </summary>
    public static string? Validate(string? name) =>
        string.IsNullOrWhiteSpace(name) ? NameRequired : null;

    /// <summary>The duration slider's own range, applied to a number that did not come from the
    /// slider. Upstream's clamp is the control's <c>Minimum</c>/<c>Maximum</c>; a session file
    /// authored elsewhere can carry any integer, and one that has been through this editor
    /// carries one in range.</summary>
    public static int ClampDuration(int minutes) =>
        Math.Clamp(minutes, MinDurationMinutes, MaxDurationMinutes);

    /// <summary>
    /// The edit, applied. Returns a NEW session — <paramref name="original"/> is never touched, so
    /// a save that is refused downstream (a read-only volume, say) leaves the rack's own instance
    /// exactly as it found it.
    /// </summary>
    /// <param name="original">The session the user opened. Its
    /// <see cref="ScriptedSession.Origin"/> is what decides which of upstream's two branches
    /// runs.</param>
    /// <param name="name">The name box. Trimmed, because a name is a file's display text and a
    /// trailing space in a rack row is a defect nobody can see the cause of. Upstream does not trim
    /// (<c>:1141</c>) and its rack shows the space.</param>
    /// <param name="description">The description box. NOT trimmed: it is multi-line prose whose
    /// first line is the rack's blurb cell (<see cref="Views.Pages.SessionRackNotices.RowBlurb"/>),
    /// and trimming would silently eat an authored leading blank line.</param>
    /// <param name="durationMinutes">The duration slider, clamped by
    /// <see cref="ClampDuration"/>.</param>
    /// <param name="newId">Where a fresh id comes from on the built-in branch. Injected so a fact
    /// can pin the branch rather than pin a GUID; production passes nothing and gets upstream's
    /// <c>Guid.NewGuid().ToString()</c> (<c>MainWindow/MainWindow.SessionIO.cs:1838</c>).</param>
    public static ScriptedSession Apply(
        ScriptedSession original,
        string? name,
        string? description,
        int durationMinutes,
        Func<string>? newId = null)
    {
        ArgumentNullException.ThrowIfNull(original);

        // THROUGH THE FILE FORMAT, so that everything this port does not model — an unknown key in
        // ExtensionData above all — survives an edit instead of being dropped by it (persistence
        // contract §6). Upstream cannot do this: its editor round-trips through TimelineSession,
        // which rebuilds Settings from events and keeps only what that model has members for.
        var edited = original.Copy();
        edited.Name = (name ?? string.Empty).Trim();
        edited.Description = description ?? string.Empty;
        edited.DurationMinutes = ClampDuration(durationMinutes);

        if (original.Origin == ScriptedSessionOrigin.BuiltIn)
        {
            // Upstream's built-in branch (:1835-1838): a new id, so the copy is a second row rather
            // than a shadow of the shipped file. The path is cleared as well, which upstream gets
            // for free from its Save-As dialog (:1840-1850) and this port has to do explicitly —
            // without it CustomSessionStore.Save would be handed a path inside the BUILT-IN folder,
            // and its containment guard would be the only thing standing between the user and an
            // overwritten shipped session.
            edited.Id = (newId ?? DefaultId)();
            edited.SourceFilePath = "";
        }
        else
        {
            // Upstream's custom branch (:1856-1859), including its comment's reason: the id and the
            // path are restored from the ORIGINAL so the save lands on the file this session came
            // out of. Copy() already carries both; they are re-stated here because this is the
            // half of the branch that has to be true, not an accident of how the copy was made.
            edited.Id = original.Id;
            edited.SourceFilePath = original.SourceFilePath;
        }

        // Both branches, upstream's (:1835 sets it via AddNewSession -> SessionFileService:229;
        // :1858 preserves a Source that is already Custom): what comes out of the editor is the
        // user's, wherever it came from. The store stamps it again after a successful write, which
        // is belt-and-braces rather than duplication — a session that was never written must not
        // be able to claim it is on disk.
        edited.Origin = ScriptedSessionOrigin.Custom;
        return edited;
    }

    private static string DefaultId() => Guid.NewGuid().ToString();
}
