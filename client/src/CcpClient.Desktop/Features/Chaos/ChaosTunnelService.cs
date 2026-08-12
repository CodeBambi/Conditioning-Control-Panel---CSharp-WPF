using Avalonia.Controls;
using Avalonia.Threading;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Chaos;

/// <summary>
/// The tunnel backdrop's own-lifecycle service (WPF `ChaosTunnelService` port — the
/// behavioral truth is ConditioningControlPanel/Chaos/ChaosTunnelService.cs; the first
/// Avalonia attempt is lessons-only, record.md Step 1). Owns the loopback origin, the
/// window, the exit watchdog, and the z-guard; drives the page through
/// <see cref="ChaosTunnelCore"/>.
///
/// Layering contract (the load-bearing part): the window is opaque, non-topmost,
/// non-activating, and sunk to the bottom of the z-order at show; while a run is live the
/// z-guard keeps the DASHBOARD below the tunnel (WPF :444-539 semantics — the first
/// attempt's blind re-sink of the tunnel itself is REJECTED: it lets the launcher cover
/// the backdrop). WPF's preventive WM_WINDOWPOSCHANGING rewrite hook is deliberately NOT
/// ported (no-flash polish; the timer corrects within one cadence) — rejected-for-now
/// alternative, filed for the orchestrator.
///
/// Never a silent fallback: the window is created ONLY when the probed
/// <see cref="ChaosTunnelCapabilityProbes.EmbeddedCapability"/> state is Available; any
/// other state = one typed log line and no window (consult ruling 3).
/// </summary>
public sealed class ChaosTunnelService
{
    /// <summary>Exit watchdog (WPF :153/:880 parity — "ask the page to wind down,
    /// watchdog-force after 1200ms"). A product timer; tests invoke the elapsed path
    /// directly, never wait on it (SP-059 timing discipline).</summary>
    private static readonly TimeSpan ExitWatchdogInterval = TimeSpan.FromMilliseconds(1200);

    /// <summary>Z-guard cadence (WPF :448 parity).</summary>
    private static readonly TimeSpan ZGuardInterval = TimeSpan.FromMilliseconds(1500);

    private readonly ApplicationHost _host;
    private readonly Window _dashboard;
    private ChaosTunnelCore _core = new();
    private ChaosTunnelWindow? _window;
    private ChaosTunnelLoopback? _server;
    private DispatcherTimer? _exitWatchdog;
    private DispatcherTimer? _zGuard;
    private bool _unavailableLogged;

    public ChaosTunnelService(ApplicationHost host, Window dashboard)
    {
        _host = host;
        _dashboard = dashboard;
    }

    /// <summary>The tunnel data root (rides CCP_DATA_ROOT through the SP-057 choke point —
    /// never SpecialFolder here, DataRootChokePointGuardTests).</summary>
    public static string DataRoot() =>
        Path.Combine(Path.GetDirectoryName(CompositionRoot.DefaultSettingsPath())!, "chaos", "tunnel");

    /// <summary>Per-surface WebView2 profile (WPF browser_data_chaos_tunnel parity, :218;
    /// the SP-049 named-per-surface discipline).</summary>
    public static string ProfileDir() => Path.Combine(DataRoot(), "wv2-profile");

    /// <summary>Output-relative payload roots (the SP-009 copied-asset convention).</summary>
    public static string TunnelPayloadRoot() => Path.Combine(AppContext.BaseDirectory, "payload", "tunnel");

    public static string VendorPayloadRoot() => Path.Combine(AppContext.BaseDirectory, "payload", "vendor");

    /// <summary>The probed surface state (never probed = Unavailable is impossible here —
    /// probes run in the CapabilityProbes phase before any window exists; a missing
    /// registry entry is typed, never guessed).</summary>
    public CapabilityState? SurfaceState =>
        _host.Capabilities?.GetState(ChaosTunnelCapabilityProbes.EmbeddedCapability);

    public bool WindowLive => _window is not null;

    /// <summary>Preload (WPF :66-80): build the window + start the page boot WITHOUT
    /// run-start; the page idles behind its curtain until <see cref="Show"/>.</summary>
    public void Preload()
    {
        if (!EnsureBuilt())
        {
            return;
        }

        CancelExitWatchdog();
        _core.Preload();
    }

    /// <summary>Show (WPF :82-99): kick off the run; run-start is queued until the page
    /// says ready. No-op (typed) when the surface is unavailable.</summary>
    public void Show()
    {
        if (!EnsureBuilt())
        {
            return;
        }

        CancelExitWatchdog();
        var json = _core.Show();
        if (json is not null)
        {
            _window?.SendToPage(json);
        }

        StartZGuard();
    }

    public void SendZoneHint(int depth, double intensity) => Send(_core.SendZoneHint(depth, intensity));

    public void SetIntensity(double value) => Send(_core.SetIntensity(value));

    public void SetStreak(int combo, double mult) => Send(_core.SetStreak(combo, mult));

    public void SetVideoPlaying(bool on) => Send(_core.SetVideoPlaying(on));

    public void SpawnPowerup(string? id = null, double ahead = 90) => Send(_core.SpawnPowerup(id, ahead));

    /// <summary>CloseActive (WPF :141-163, idempotent): run-end + bounded exit-done wait,
    /// watchdog-forced teardown, or immediate teardown when the page never booted.</summary>
    public void CloseActive()
    {
        var (plan, runEndJson) = _core.CloseActive();
        if (plan == ChaosTunnelCore.ClosePlan.DisposeImmediately)
        {
            Teardown();
            return;
        }

        if (runEndJson is not null)
        {
            _window?.SendToPage(runEndJson);
        }

        CancelExitWatchdog();
        _exitWatchdog = new DispatcherTimer { Interval = ExitWatchdogInterval };
        _exitWatchdog.Tick += (_, _) =>
        {
            _host.LogDiagnostic("chaos-tunnel: exit-done wait elapsed — watchdog-FORCED teardown (page wedged mid-exit; WPF :881 parity)");
            Teardown();
        };
        _exitWatchdog.Start();
    }

    // ---------------- internals ----------------

    private void Send(string? json)
    {
        if (json is not null && _window is not null)
        {
            _window.SendToPage(json);
        }
    }

    private bool EnsureBuilt()
    {
        if (_window is not null)
        {
            return true;
        }

        if (SurfaceState is not CapabilityState.Available)
        {
            if (!_unavailableLogged)
            {
                _unavailableLogged = true;
                _host.LogDiagnostic(
                    "chaos-tunnel: surface NOT available — no window created (typed, never a silent fallback). "
                    + $"capability {ChaosTunnelCapabilityProbes.EmbeddedCapability}: {Describe(SurfaceState)}");
            }

            return false;
        }

        try
        {
            _core = new ChaosTunnelCore();
            _server = new ChaosTunnelLoopback(TunnelPayloadRoot(), VendorPayloadRoot(),
                msg => _host.LogDiagnostic(msg));
            _server.Start();

            _window = new ChaosTunnelWindow(msg => _host.LogDiagnostic(msg), ProfileDir());
            _window.PageMessage += OnPageMessage;
            _window.NavigationFinished += success =>
                _host.LogDiagnostic($"chaos-tunnel: NavigationCompleted success={success}");
            _window.Closed += (_, _) => { /* teardown handled in Teardown (WPF :200 parity) */ };
            _window.AttachEmbeddedSurface(new Uri($"{_server.Origin}/tunnel/index.html"));
            _window.Show();
            _window.SinkToBottom();
            _host.LogDiagnostic("chaos-tunnel: window up (non-topmost, opaque, embedded WebView2 tunnel)");
            return true;
        }
        catch (Exception ex)
        {
            _host.LogDiagnostic($"chaos-tunnel: build failed ({ex.GetType().Name}: {ex.Message}) — typed, no window");
            Teardown();
            return false;
        }
    }

    private void OnPageMessage(string body)
    {
        switch (_core.HandlePageMessage(body))
        {
            case ChaosTunnelCore.PageOutcome.Ready ready:
                _host.LogDiagnostic($"chaos-tunnel: page ready (flushing {ready.Flush.Count} queued frame(s))");
                foreach (var json in ready.Flush)
                {
                    _window?.SendToPage(json);
                }

                break;
            case ChaosTunnelCore.PageOutcome.ExitDone exitDone:
                if (exitDone.CloseNow)
                {
                    _host.LogDiagnostic("chaos-tunnel: exit-done received — closing (graceful fast path)");
                    Teardown();
                }
                else
                {
                    // RunAgain re-armed inside the exit window (WPF :289-294 parity).
                    CancelExitWatchdog();
                    _host.LogDiagnostic("chaos-tunnel: exit-done after RunAgain re-arm — window kept");
                }

                break;
            case ChaosTunnelCore.PageOutcome.SfxCue:
                // HANDLED TYPED (consult ruling 4): the cues exist upstream but the client
                // has no chaos sound-library port — a content gap owned by that row, never
                // claimed as WPF parity (SP-051's falsified framing). Presence+shape only.
                _host.LogDiagnostic("chaos-tunnel: sfx cue received (unresolvable here: no chaos sound-library port — greenfield content gap)");
                break;
            case ChaosTunnelCore.PageOutcome.PowerupClick:
                _host.LogDiagnostic("chaos-tunnel: powerup-click received");
                break;
            case ChaosTunnelCore.PageOutcome.PageLog pageLog:
                _host.LogDiagnostic($"chaos-tunnel[page]: {pageLog.Message}");
                break;
            case ChaosTunnelCore.PageOutcome.Unknown unknown:
                _host.LogDiagnostic($"chaos-tunnel: unknown page frame type '{unknown.Type}' (typed, tolerated)");
                break;
            case ChaosTunnelCore.PageOutcome.Malformed:
                _host.LogDiagnostic("chaos-tunnel: malformed page frame (typed, tolerated)");
                break;
        }
    }

    /// <summary>Idempotent full teardown (WPF DisposeAll :357-369 parity).</summary>
    public void Teardown()
    {
        CancelExitWatchdog();
        StopZGuard();
        try { _window?.Close(); } catch { /* best effort */ }
        try { _server?.Dispose(); } catch { /* best effort */ }
        _window = null;
        _server = null;
        _core = new ChaosTunnelCore();
    }

    private void CancelExitWatchdog()
    {
        try { _exitWatchdog?.Stop(); } catch { /* best effort */ }
        _exitWatchdog = null;
    }

    // ---------------- z-guard (WPF :444-539 semantics) ----------------
    // The tunnel is non-topmost by design (the topmost game windows stack above it), but the
    // DASHBOARD is also non-topmost — re-activating it raises it over the tunnel. While a run
    // is live, demote the dashboard back below the tunnel. Targeted: touches ONLY the
    // dashboard (never arbitrary windows), stops at run end, keeps WPF's 512-iteration walk
    // bound (consult ruling 2 guardrails). The preventive WndProc rewrite hook is NOT ported
    // (rejected-for-now; the timer corrects within one cadence).

    private void StartZGuard()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (_zGuard is null)
        {
            _zGuard = new DispatcherTimer { Interval = ZGuardInterval };
            _zGuard.Tick += (_, _) => EnforceDashboardBelowTunnel();
            _zGuard.Start();
            EnforceDashboardBelowTunnel(); // don't wait a tick — tuck the dashboard under now (WPF :453)
        }
    }

    private void StopZGuard()
    {
        try { _zGuard?.Stop(); } catch { /* best effort */ }
        _zGuard = null;
    }

    private void EnforceDashboardBelowTunnel()
    {
        try
        {
            if (!_core.RunActive || _window is null || !OperatingSystem.IsWindows())
            {
                return;
            }

            var dashboardHandle = _dashboard.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            var tunnelHandle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (dashboardHandle == IntPtr.Zero || tunnelHandle == IntPtr.Zero
                || !ChaosTunnelWin32.IsWindow(tunnelHandle))
            {
                return;
            }

            if (_dashboard.WindowState == WindowState.Minimized || !_dashboard.IsVisible)
            {
                return; // WPF :525 (IsIconic / IsWindowVisible) parity
            }

            // Walk upward from the tunnel; if the dashboard sits anywhere above it, slot it
            // back below (WPF :527-538, 512-iteration bound kept).
            var h = tunnelHandle;
            for (var i = 0; i < 512; i++)
            {
                h = ChaosTunnelWin32.GetWindow(h, ChaosTunnelWin32.GwHwndprev);
                if (h == IntPtr.Zero)
                {
                    return;
                }

                if (h == dashboardHandle)
                {
                    _host.LogDiagnostic("chaos-tunnel z-guard: dashboard was above the tunnel — demoting");
                    ChaosTunnelWin32.SetWindowPos(dashboardHandle, tunnelHandle, 0, 0, 0, 0,
                        ChaosTunnelWin32.SwpNomove | ChaosTunnelWin32.SwpNosize
                        | ChaosTunnelWin32.SwpNoactivate | ChaosTunnelWin32.SwpNoownerzorder);
                    return;
                }
            }
        }
        catch { /* best effort — the guard never faults the run (WPF :538 parity) */ }
    }

    private static string Describe(CapabilityState? state) => state switch
    {
        null => "(capability registry absent)",
        CapabilityState.Available a => $"Available ({a.Detail})",
        CapabilityState.Unavailable u => $"Unavailable ({u.Reason.Code}: {u.Reason.Detail})",
        CapabilityState.DependencyMissing d => $"DependencyMissing ({d.Dependency}: {d.Reason.Detail})",
        CapabilityState.Degraded d => $"Degraded ({d.Reason.Code}: {d.Reason.Detail}; survives: {d.SurvivingSemantics})",
        CapabilityState.PermissionRequired p => $"PermissionRequired ({p.Reason.Code}: {p.Reason.Detail})",
        CapabilityState.Faulted f => $"Faulted ({f.Reason.Code}: {f.Reason.Detail})",
        _ => state.ToString() ?? "(unknown state)",
    };
}
