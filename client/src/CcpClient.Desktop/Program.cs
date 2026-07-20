using Avalonia;
using CcpClient.Desktop.Lifecycle;
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

        // Phase 1 (Bootstrap) actions must exist before anything can fail: panic hooks
        // and the minimal logger seam (contract §1, §9).
        ILogSink log = new DebugLogSink();
        InstallPanicHooks(log);

        var trace = new StartupTrace();
        var root = new CompositionRoot { LogSinkFactory = () => log };
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

        try
        {
            // Phase 4 (UserInterface): the Avalonia lifetime itself.
            return BuildAvaloniaApp(host!, popupDemo).StartWithClassicDesktopLifetime(args);
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

    public static AppBuilder BuildAvaloniaApp(ApplicationHost host, bool popupDemo = false) => AppBuilder
        .Configure<App>(() => new App(host, popupDemo))
        .UsePlatformDetect();
}
