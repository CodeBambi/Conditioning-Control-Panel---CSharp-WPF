using Avalonia;
using CcpClient.Desktop.Lifecycle;
using Keincheck;
using CcpClient.Desktop.Manifest;

namespace CcpClient.Desktop;

/// <summary>
/// Entry point. Runs startup phases 1–3 as plain C# before the Avalonia lifetime
/// (phase 4) starts, per client/docs/startup-shutdown-contract.md §1. Explicit manual
/// construction only — no DI container, no static service locator (contract §7).
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Diagnostic self-check (asset-manifest.md §--verify-assets self-check contract):
        // a bounded path BEFORE any phase — no window, no lifetime, no participants, no
        // startup side effects (SP-003 phase discipline). The normal path below is
        // byte-identical to before this flag existed.
        if (args.Contains(AssetSelfCheck.Flag, StringComparer.Ordinal))
        {
            return AssetSelfCheck.Run(typeof(Program).Assembly, Console.Out);
        }

        // Version surface (release-publish-gates.md §2): same bounded self-check shape —
        // reads the InformationalVersion ATTRIBUTE, never a path (single-file safe).
        if (args.Contains(VersionSelfCheck.Flag, StringComparer.Ordinal))
        {
            return VersionSelfCheck.Run(typeof(Program).Assembly, Console.Out);
        }

        // SP-015 bounded diagnostics (same discipline: pre-phase, no window, no participants).
        // --generate-avatar-packs <dir>: deterministic synthetic pack regeneration.
        if (args.Contains(Features.AvatarTube.AvatarEvidence.GenerateFlag, StringComparer.Ordinal))
        {
            var index = Array.IndexOf(args, Features.AvatarTube.AvatarEvidence.GenerateFlag);
            if (index + 1 >= args.Length)
            {
                Console.Error.WriteLine("usage: --generate-avatar-packs <directory>");
                return 1;
            }

            var written = Features.AvatarTube.SyntheticAvatarPacks.WriteAll(args[index + 1]);
            foreach (var file in written)
            {
                Console.Out.WriteLine($"generated: {file}");
            }

            return 0;
        }

        // --avatar-strip-decode --capture <bmp>: one capture's strip + content fraction as JSON.
        if (args.Contains(Features.AvatarTube.AvatarEvidence.StripDecodeFlag, StringComparer.Ordinal))
        {
            var capture = ArgValue(args, "--capture");
            if (capture is null)
            {
                Console.Error.WriteLine("usage: --avatar-strip-decode --capture <file.bmp>");
                return 1;
            }

            return Features.AvatarTube.AvatarEvidence.StripDecode(
                capture, Console.Out,
                fullWindow: args.Contains(Features.AvatarTube.AvatarEvidence.ScanFlag, StringComparer.Ordinal));
        }

        // --avatar-sequence <samples.jsonl> --pack <pack.json> [--trace <trace.jsonl>]: named verdicts.
        if (args.Contains(Features.AvatarTube.AvatarEvidence.SequenceFlag, StringComparer.Ordinal))
        {
            var samples = ArgValue(args, Features.AvatarTube.AvatarEvidence.SequenceFlag);
            var pack = ArgValue(args, "--pack");
            if (samples is null || pack is null)
            {
                Console.Error.WriteLine("usage: --avatar-sequence <samples.jsonl> --pack <pack.json> [--trace <trace.jsonl>]");
                return 1;
            }

            return Features.AvatarTube.AvatarEvidence.RunSequence(samples, pack, ArgValue(args, "--trace"), Console.Out);
        }

        // Phase 1 (Bootstrap) actions must exist before anything can fail: panic hooks
        // and the minimal logger seam (contract §1, §9).
        ILogSink log = new DebugLogSink();
        InstallPanicHooks(log);

        // SP-057 HARNESS-ONLY profile isolation seam: validate CCP_DATA_ROOT pre-phase so a
        // bad override fails HERE (bounded, exit 1) instead of mid-composition — and NEVER
        // falls back to the real profile (framing d). The active override is logged once
        // (the harness's own path; the real profile path is never newly logged) — the line
        // is headed-run evidence that the seam was live (record.md Step 3 positive control).
        var dataRootOverride = CompositionRoot.ActiveDataRootOverride();
        if (dataRootOverride is not null)
        {
            string resolved;
            try
            {
                resolved = CompositionRoot.ResolveDataRoot(dataRootOverride);
            }
            catch (DataRootOverrideException ex)
            {
                Console.Error.WriteLine($"usage: {CompositionRoot.DataRootOverrideVariable}=<fully-qualified absolute directory> — {ex.Message}");
                return 1;
            }

            log.Log($"data-root override active: {CompositionRoot.DataRootOverrideVariable} -> {resolved} (HARNESS-ONLY isolation seam, SP-057)");
        }

        // SP-027 slice b5 HARNESS-ONLY failure injection (parsed EARLY — the blocked-route
        // prefix threads into the composition root below; the kill flag rides into App):
        // --dtrh-kill-renderers kills the profile-matched WebView2 children once the
        // engine is live (and again on the relaunched instance → exhaustion);
        // --dtrh-block-route <prefix> makes the loopback answer 403 for the prefix (W18).
        var dtrhKillRenderers = args.Contains("--dtrh-kill-renderers", StringComparer.Ordinal);
        var dtrhBlockRoute = ArgValue(args, "--dtrh-block-route");

        // SP-046 c7 product config seam: --ai-ollama-host <url> points the companion's
        // loopback provider at a specific host (headed-evidence lab, alternate local port).
        // Loopback-only boundary: a non-loopback host classifies remote and is rejected
        // pre-socket (admission policy + provider defense in depth) — the flag can NEVER
        // widen the network boundary. Malformed URI = bounded startup failure.
        var aiOllamaHost = ArgValue(args, "--ai-ollama-host");
        if (aiOllamaHost is not null &&
            (!Uri.TryCreate(aiOllamaHost, UriKind.Absolute, out var aiHostUri) ||
             (aiHostUri.Scheme != "http" && aiHostUri.Scheme != "https")))
        {
            Console.Error.WriteLine("usage: --ai-ollama-host <absolute http(s) URI — loopback only; remote hosts are rejected pre-socket>");
            return 1;
        }

        var trace = new StartupTrace();
        var root = new CompositionRoot { LogSinkFactory = () => log, DtrhBlockedRoutePrefixHarness = dtrhBlockRoute, AiOllamaHostOverride = aiOllamaHost };
        ApplicationHost? host = null;
        using var cts = new CancellationTokenSource();

        var outcome = StartupPhaseRunner
            .RunAsync(CreateStartupPhases(root, trace, h => host = h), trace, cts.Token)
            .GetAwaiter().GetResult();

        switch (outcome)
        {
            // Startup-failure path (contract §6): teardown of completed phases only;
            // the window never exists; StartWithClassicDesktopLifetime is never called.
            case StartupOutcome.Failed failed:
                log.Log($"startup failed ({failed.Failure.Kind}) in phase {failed.Failure.Phase}: {failed.Failure.Reason}");
                host?.ShutdownAsync().GetAwaiter().GetResult();
                return 1;
            case StartupOutcome.Cancelled:
                host?.ShutdownAsync().GetAwaiter().GetResult();
                return 0;
        }

        // --popup-demo (SP-013 WSLg evidence): opens the demonstrator popup at startup.
        // WSLg has no input automation (SP-008 named limit) — the popup cannot be
        // left-clicked there, so it must open itself; probe facts go to stderr.
        var popupDemo = args.Contains("--popup-demo", StringComparer.Ordinal);

        // SP-015 demonstrator flags: open the AvatarTube tube at startup (same WSLg
        // no-input-automation reasoning), corrupt the pulse pack in-memory (typed
        // undecodable-asset path evidence), and/or mirror the engine trace to a JSONL file.
        var avatarDemo = args.Contains("--avatartube-demo", StringComparer.Ordinal);
        var avatarCorrupt = args.Contains("--avatar-corrupt-demo", StringComparer.Ordinal);
        // WSLg has no input automation (SP-008 limit): the demo can open already animated.
        var avatarAnimate = args.Contains("--avatar-animate", StringComparer.Ordinal);
        var avatarTrace = ArgValue(args, "--avatar-trace");

        // SP-023 DTRH host slice b1: --dtrh-demo [page] opens the DTRH flow at startup
        // (same WSLg no-input-automation reasoning). b2 (SP-024): the default path opens
        // the save picker first (hero-card outcome); --dtrh-quick skips it (Quick Start
        // outcome). --dtrh-picker-timeout <seconds> auto-commits the picker's current
        // selection (timed drive for no-input platforms — never an input claim).
        // --dtrh-auto-close <seconds> closes the host window on a timer (WSLg exit
        // evidence without input automation).
        var dtrhDemo = args.Contains("--dtrh-demo", StringComparer.Ordinal);
        var dtrhPage = ArgValue(args, "--dtrh-page") ?? "index.html";
        var dtrhQuick = args.Contains("--dtrh-quick", StringComparer.Ordinal);
        var dtrhPickerTimeout = int.TryParse(ArgValue(args, "--dtrh-picker-timeout"), out var pickerSeconds)
            ? pickerSeconds
            : 0;
        var dtrhAutoClose = int.TryParse(ArgValue(args, "--dtrh-auto-close"), out var closeSeconds)
            ? closeSeconds
            : 0;
        // SP-025 slice b3: --dtrh-fx-drive "<steps>" — HARNESS-ONLY timed injection of raw
        // page JSON through the real dispatch path (headed/WX native-effects evidence
        // without gameplay; runs are b4-gated).
        var dtrhFxDrive = ArgValue(args, "--dtrh-fx-drive");
        // SP-026 slice b4: --dtrh-m2test — HARNESS-ONLY meta test mode (init m2Test:true +
        // in-memory clone; the payload's m2test.js drives the full meta vocabulary +
        // payout round-trip page-originated; the real save is never touched).
        var dtrhM2Test = args.Contains("--dtrh-m2test", StringComparer.Ordinal);

        // SP-049 THE LOOM studio (--loom-demo): the v6.6.3 standalone studio window at
        // startup (the --dtrh-demo demonstrator class — the greenfield dashboard has no
        // Spiral Overlay card yet; typed named limit, record.md Step 1). --loom-drive
        // "<steps>" is HARNESS-ONLY (scripted pointer through the engine's own
        // InvokeScript — encoder/messages/store/serving all real). --loom-auto-close
        // <seconds> closes the window on a timer (WSLg exit evidence without input).
        var loomDemo = args.Contains("--loom-demo", StringComparer.Ordinal);
        var loomDrive = ArgValue(args, "--loom-drive");
        var loomAutoClose = int.TryParse(ArgValue(args, "--loom-auto-close"), out var loomCloseSeconds)
            ? loomCloseSeconds
            : 0;

        // SP-054 Graded Intake host (--intake-demo): the demonstrator class (the
        // --loom-demo precedent; the dashboard flip-tile/nudge is the BLOCKED inventory's
        // item — flagged scope expansion, record.md glue bundle). --intake-drive "<steps>"
        // is HARNESS-ONLY (raw page JSON through the real dispatch path: quiz-result /
        // intake-close / fullscreen-set / exit / loom-file). --intake-kill-renderers arms
        // the W17 watchdog-relaunch injection on the intake profile. --intake-auto-close
        // <seconds> closes on a timer (SP-008 no-input exit evidence).
        var intakeDemo = args.Contains("--intake-demo", StringComparer.Ordinal);
        var intakeDrive = ArgValue(args, "--intake-drive");
        var intakeKillRenderers = args.Contains("--intake-kill-renderers", StringComparer.Ordinal);
        var intakeAutoClose = int.TryParse(ArgValue(args, "--intake-auto-close"), out var intakeCloseSeconds)
            ? intakeCloseSeconds
            : 0;

        // SP-061 Chaos tunnel backdrop DEMONSTRATOR (--tunnel-demo): the opaque
        // below-Topmost surface at startup (the --loom-demo demonstrator class; no
        // dashboard entry point exists — typed named limit). --tunnel-drive "<steps>" is
        // HARNESS-ONLY (timed steps: topmost-show/topmost-hide over the REAL DtrhVideoWindow
        // Topmost surface, tunnel-close/tunnel-show cycles, finish). --tunnel-auto-close
        // <seconds> closes on a timer (SP-008 no-input exit evidence).
        var tunnelDemo = args.Contains("--tunnel-demo", StringComparer.Ordinal);
        var tunnelDrive = ArgValue(args, "--tunnel-drive");
        var tunnelAutoClose = int.TryParse(ArgValue(args, "--tunnel-auto-close"), out var tunnelCloseSeconds)
            ? tunnelCloseSeconds
            : 0;

        try
        {
            // Phase 4 (UserInterface): the Avalonia lifetime itself.
            return BuildAvaloniaApp(host!, popupDemo, avatarDemo, avatarCorrupt, avatarTrace, avatarAnimate,
                dtrhDemo, dtrhPage, dtrhAutoClose, dtrhQuick, dtrhPickerTimeout, dtrhFxDrive, dtrhM2Test,
                dtrhKillRenderers, loomDemo, loomDrive, loomAutoClose,
                intakeDemo, intakeDrive, intakeKillRenderers, intakeAutoClose,
                tunnelDemo, tunnelDrive, tunnelAutoClose).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Panic path (contract §6): the lifetime Exit event does NOT fire here.
            // Log, best-effort guarded teardown, non-zero exit. No dialog, no swallow.
            log.Log($"panic: unhandled exception escaped the UI lifetime: {ex}");
            host!.ShutdownAsync().GetAwaiter().GetResult();
            return 2;
        }
    }

    /// <summary>
    /// Phases 1–3 (contract §1). Extracted from <see cref="Main"/> so tests can walk the
    /// real composition root through the real phase runner (contract §10.2).
    /// </summary>
    public static StartupPhase[] CreateStartupPhases(
        CompositionRoot root, StartupTrace trace, Action<ApplicationHost> onHostBuilt)
    {
        ApplicationHost? host = null;
        return
        [
            StartupPhase.FromSync("Bootstrap", _ => StartupOutcome.Success.Instance),
            StartupPhase.FromSync("CompositionRoot", _ =>
            {
                if (!root.Validate(out var failure))
                {
                    return new StartupOutcome.Failed(failure!);
                }

                host = root.Build(trace);
                onHostBuilt(host);
                return StartupOutcome.Success.Instance;
            }),
            new StartupPhase("CoreServices", ct => host!.StartParticipantsAsync(ct)),
            // Capability contract §3 rule 2: probes run as owned operations in this named
            // phase, after CoreServices (store load cannot race probe files) and before the
            // window exists. Capability states never fail startup — a Faulted/Degraded state
            // is truthful information, so the phase returns Success once probes complete.
            new StartupPhase("CapabilityProbes", async ct =>
            {
                if (host!.ProbeRunner is not null)
                {
                    await host.ProbeRunner.RunAllAsync(ct).ConfigureAwait(false);
                }

                return ct.IsCancellationRequested
                    ? StartupOutcome.Cancelled.Instance
                    : StartupOutcome.Success.Instance;
            }),
        ];
    }

    private static void InstallPanicHooks(ILogSink log)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.Log($"panic: unhandled exception: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Log($"panic: unobserved task exception: {e.Exception}");
            e.SetObserved();
        };
    }

    public static AppBuilder BuildAvaloniaApp(
        ApplicationHost host, bool popupDemo = false,
        bool avatarDemo = false, bool avatarCorrupt = false, string? avatarTracePath = null,
        bool avatarAnimate = false, bool dtrhDemo = false, string dtrhPage = "index.html",
        int dtrhAutoCloseSeconds = 0, bool dtrhQuick = false, int dtrhPickerTimeoutSeconds = 0,
        string? dtrhFxDrive = null, bool dtrhM2Test = false, bool dtrhKillRenderers = false,
        bool loomDemo = false, string? loomDrive = null, int loomAutoCloseSeconds = 0,
        bool intakeDemo = false, string? intakeDrive = null, bool intakeKillRenderers = false,
        int intakeAutoCloseSeconds = 0,
        bool tunnelDemo = false, string? tunnelDrive = null, int tunnelAutoCloseSeconds = 0)
    {
        var builder = AppBuilder
            .Configure<App>(() => new App(host, popupDemo, avatarDemo, avatarCorrupt, avatarTracePath, avatarAnimate,
                dtrhDemo, dtrhPage, dtrhAutoCloseSeconds, dtrhQuick, dtrhPickerTimeoutSeconds, dtrhFxDrive, dtrhM2Test,
                dtrhKillRenderers, loomDemo, loomDrive, loomAutoCloseSeconds,
                intakeDemo, intakeDrive, intakeKillRenderers, intakeAutoCloseSeconds,
                tunnelDemo, tunnelDrive, tunnelAutoCloseSeconds))
            .UsePlatformDetect();
        // Live-UI MCP seat (avalonia-live, Keincheck embedded server on http://127.0.0.1:3001).
        // Opt-in per run (CCP_MCP=1) so tests and normal runs never bind the port.
        if (Environment.GetEnvironmentVariable("CCP_MCP") == "1")
        {
            builder = builder.UseMcpServer();
        }

        return builder;
    }

    private static string? ArgValue(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
