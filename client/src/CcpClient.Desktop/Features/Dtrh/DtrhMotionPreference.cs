using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// SP-053: the reduced-motion inheritance probe seam (task-board P1 row "Webview
/// prefers-reduced-motion inheritance probe" — a PRE-EXISTING DTRH host obligation
/// surfaced by the SP-050 audit, range-proven NOT a v6.6.3 delta). The DTRH page probes
/// `matchMedia('(prefers-reduced-motion: reduce)')` itself (payload
/// `shared/capability.js:35` — READ-ONLY; reduced → 2D mode, `canTry3d:false` at :57).
/// This seam measures what the EMBEDDED engine answers for that exact query against the
/// OS animation state, read with the exact API WPF's own OS cap wraps
/// (`SystemParameters.ClientAreaAnimation`, MotionFx.cs:37-54 →
/// `SystemParametersInfo(SPI_GETCLIENTAREAANIMATION)`).
///
/// Probe-first: the verdict is whatever the engine reports — never assumed. The OS can
/// only REMOVE motion (MotionFx.cs:37-54 parity), so inheritance HOLDS iff the engine's
/// reduced answer equals OS-animations-off. GET-only: the OS toggle (SET + restore) is
/// harness-side PowerShell with try/finally (consult correction 2 — product code never
/// flips a user setting). GET failure → null → Unknown, never a defaulted boolean
/// (consult correction 3). Linux half: named limit (WSL zero-distros; WebKitGTK
/// inheritance unknown) — null, never faked.
/// </summary>
public static class DtrhMotionPreference
{
    /// <summary>The exact media query the page's own probe evaluates
    /// (`shared/capability.js:35`). The measurement PATH — pinned by unit test, never
    /// paraphrased.</summary>
    public const string ProbeQuery = "(prefers-reduced-motion: reduce)";

    /// <summary>SPI_GETCLIENTAREAANIMATION — the uiAction WPF's
    /// `SystemParameters.ClientAreaAnimation` reads (MotionFx.cs:37-54 parity).</summary>
    private const uint SpiGetClientAreaAnimation = 0x1042;

    public enum MotionInheritanceVerdict
    {
        /// <summary>Engine answer matches OS-animations-off — the embedded engine inherits
        /// the OS/user motion preference for this query.</summary>
        Holds,

        /// <summary>Engine answer contradicts the OS state. Direction matters (recorded in
        /// prose, not the enum): OS-OFF + engine reduce=false is the user-observable
        /// betrayal (a reduced-motion user silently gets the 3D descent); OS-ON + engine
        /// reduce=true is conservative-safe (the OS can only remove motion — no forcing
        /// mechanism can or should fix that direction).</summary>
        Fails,

        /// <summary>One side could not be read (GET failure, non-Windows named limit, or
        /// the engine never answered). Never a defaulted boolean.</summary>
        Unknown,
    }

    /// <summary>The pure verdict seam (unit-testable — asserts the measurement PATH,
    /// never the OS inheritance itself). Holds iff both sides are known and
    /// <paramref name="engineReduced"/> equals OS-animations-off.</summary>
    public static MotionInheritanceVerdict Evaluate(bool? osClientAreaAnimation, bool? engineReduced) =>
        (osClientAreaAnimation, engineReduced) switch
        {
            (null, _) or (_, null) => MotionInheritanceVerdict.Unknown,
            (var osAnimation, var reduced) => reduced == !osAnimation
                ? MotionInheritanceVerdict.Holds
                : MotionInheritanceVerdict.Fails,
        };

    /// <summary>Host-side OS read: Windows → SystemParametersInfo(GET) (the exact API
    /// WPF's cap wraps); anything else → null (named limit — Linux WebKitGTK inheritance
    /// unproven, never faked). Null also on GET failure (consult: never default true).</summary>
    public static bool? ReadOsClientAreaAnimation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            if (!SystemParametersInfoGet(SpiGetClientAreaAnimation, 0, out var enabled, 0))
            {
                // GET failed (Marshal.GetLastWin32Error available to the caller of the
                // P/Invoke; here failure → Unknown, never a defaulted boolean).
                return null;
            }

            return enabled != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    // GET shape only (consult correction 3): BOOL* out-param. The SET shape (by-value
    // pvParam, SPIF_UPDATEINIFILE|SPIF_SENDCHANGE) lives in the harness toggle script
    // under spine-tasks/SP-053-reduced-motion-probe/evidence/ — never in product code.
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoGet(uint uiAction, uint uiParam, out int pvParam, uint fWinIni);
}
