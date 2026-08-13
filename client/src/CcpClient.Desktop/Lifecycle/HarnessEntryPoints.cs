namespace CcpClient.Desktop.Lifecycle;

/// <summary>The launch-intent class of a <c>--flag</c> literal found under client/src.
/// The verdict order is deliberate: only <see cref="Harness"/> refuses to run unsealed.</summary>
public enum EntryPointDisposition
{
    /// <summary>Automated evidence capture or failure injection — REFUSE when CCP_DATA_ROOT is unset (SP-064).</summary>
    Harness,

    /// <summary>Human demonstrator / inspection — never refuse (task-board decree).</summary>
    Demo,

    /// <summary>Pre-phase bounded self-check — returns before any phase, never builds the composition root.</summary>
    SelfCheck,

    /// <summary>Modifies another entry point; cannot launch anything alone; no independent verdict.</summary>
    Modifier,

    /// <summary>A --flag literal that is not a program arg at all (LibVLC / WebView2 / secret-tool argv).
    /// Classified so the unclassified-literal guard binds it without giving it a startup verdict.</summary>
    NotAStartupFlag,
}

/// <summary>
/// SP-064: the ONE registry classifying every <c>--flag</c> literal reachable from the
/// startup dispatch surface (plus the non-startup literals the guard also binds).
/// The gate in <see cref="Program.Main"/> consumes ONLY this registry — there is no
/// second copy of the classification anywhere. A new harness flag added here as
/// <see cref="EntryPointDisposition.Harness"/> inherits the refusal, the table-driven
/// pin, and the guard automatically; a new flag NOT added here fails
/// HarnessEntryPointGuardTests with its file:line.
/// </summary>
public static class HarnessEntryPoints
{
    /// <summary>Process exit code when a harness entry point is refused (1 = usage/startup
    /// failure, 2 = panic — both taken; grep-pinned unique at introduction, SP-064 record Step 1).</summary>
    public const int RefusalExitCode = 3;

    private static readonly KeyValuePair<string, EntryPointDisposition>[] Entries =
    [
        // Class 1 — HARNESS: automated evidence capture / failure injection (refuse unsealed).
        new("--dtrh-m2test", EntryPointDisposition.Harness),
        new("--dtrh-fx-drive", EntryPointDisposition.Harness),
        new("--loom-drive", EntryPointDisposition.Harness),
        new("--intake-drive", EntryPointDisposition.Harness),
        new("--tunnel-drive", EntryPointDisposition.Harness),
        new("--dtrh-kill-renderers", EntryPointDisposition.Harness),
        new("--dtrh-block-route", EntryPointDisposition.Harness),
        new("--intake-kill-renderers", EntryPointDisposition.Harness),

        // Class 2 — DEMO / INSPECTION: a human may run these against their real profile (decree).
        new("--popup-demo", EntryPointDisposition.Demo),
        new("--avatartube-demo", EntryPointDisposition.Demo),
        // In-memory pack corruption only (typed undecodable-asset path) — fabricates nothing
        // persisted; demo decree stands (SP-064 record, consult-checked boundary call).
        new("--avatar-corrupt-demo", EntryPointDisposition.Demo),
        new("--dtrh-demo", EntryPointDisposition.Demo),
        new("--dtrh-quick", EntryPointDisposition.Demo),
        new("--loom-demo", EntryPointDisposition.Demo),
        new("--intake-demo", EntryPointDisposition.Demo),
        new("--tunnel-demo", EntryPointDisposition.Demo),

        // Class 3 — PRE-PHASE SELF-CHECK: return before any phase; never touch the profile.
        new("--verify-assets", EntryPointDisposition.SelfCheck),
        new("--version", EntryPointDisposition.SelfCheck),
        new("--generate-avatar-packs", EntryPointDisposition.SelfCheck),
        new("--avatar-strip-decode", EntryPointDisposition.SelfCheck),
        new("--avatar-sequence", EntryPointDisposition.SelfCheck),

        // Class 4 — MODIFIER: no independent verdict.
        new("--capture", EntryPointDisposition.Modifier),
        new("--pack", EntryPointDisposition.Modifier),
        new("--trace", EntryPointDisposition.Modifier),
        new("--scan", EntryPointDisposition.Modifier),
        new("--ai-ollama-host", EntryPointDisposition.Modifier),
        new("--avatar-animate", EntryPointDisposition.Modifier),
        new("--avatar-trace", EntryPointDisposition.Modifier),
        new("--dtrh-page", EntryPointDisposition.Modifier),
        new("--dtrh-picker-timeout", EntryPointDisposition.Modifier),
        new("--dtrh-auto-close", EntryPointDisposition.Modifier),
        new("--loom-auto-close", EntryPointDisposition.Modifier),
        new("--intake-auto-close", EntryPointDisposition.Modifier),
        new("--tunnel-auto-close", EntryPointDisposition.Modifier),

        // Class 5 — NOT a startup flag (LibVLC / WebView2 / secret-tool argv literals under
        // client/src). Bound by the guard so the literal surface is fully classified.
        new("--no-video-title-show", EntryPointDisposition.NotAStartupFlag),
        new("--avcodec-hw=none", EntryPointDisposition.NotAStartupFlag),
        new("--autoplay-policy=no-user-gesture-required", EntryPointDisposition.NotAStartupFlag),
        new("--disable-direct-composition-video-overlays --disable-features=CalculateNativeWinOcclusion", EntryPointDisposition.NotAStartupFlag),
        new("--label=ccp-client", EntryPointDisposition.NotAStartupFlag),
    ];

    /// <summary>Every classified literal (Ordinal — flag matching is case-sensitive by convention).</summary>
    public static IReadOnlyDictionary<string, EntryPointDisposition> All { get; } =
        new Dictionary<string, EntryPointDisposition>(Entries, StringComparer.Ordinal);

    /// <summary>The harness-class flags present in <paramref name="args"/>, in argument
    /// order, de-duplicated. Pure: no environment read — Program.Main reads
    /// <see cref="CompositionRoot.ActiveDataRootOverride"/> itself so this stays
    /// unit-testable without process-env mutation (SP-062 discipline).</summary>
    public static IReadOnlyList<string> HarnessFlagsIn(string[] args)
    {
        var found = new List<string>();
        foreach (var arg in args)
        {
            if (All.TryGetValue(arg, out var disposition)
                && disposition == EntryPointDisposition.Harness
                && !found.Contains(arg, StringComparer.Ordinal))
            {
                found.Add(arg);
            }
        }

        return found;
    }

    /// <summary>The refusal text. Names every offending flag AND the override variable
    /// (via <see cref="CompositionRoot.DataRootOverrideVariable"/> — never a retyped string).</summary>
    public static string RefusalMessage(IReadOnlyList<string> harnessFlags) =>
        $"refusing to start: {string.Join(", ", harnessFlags)} " +
        (harnessFlags.Count == 1 ? "is a HARNESS-ONLY entry point" : "are HARNESS-ONLY entry points") +
        $" (automated evidence capture / failure injection) and {CompositionRoot.DataRootOverrideVariable} is not set — " +
        $"set {CompositionRoot.DataRootOverrideVariable}=<fully-qualified absolute directory> so the run is isolated " +
        $"from the real user profile (SP-057 seam; SP-064 makes it mandatory for harness entries)";
}
