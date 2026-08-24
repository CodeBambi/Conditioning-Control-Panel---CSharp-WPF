namespace CcpClient.Desktop.Views;

/// <summary>
/// Whether the shell's diagnostic footer is RENDERED to the user.
///
/// <para><b>It used to be rendered unconditionally, and a headed capture is what caught it.</b> The
/// shell docks a two-line footer at the bottom of every page carrying the current route and the
/// rail's layout probe — text like <c>layout-probe: door studio 174.9x44.0 DIP @ scale 1.75 @
/// screen 121,223</c>. That is instrumentation, and a user has no use for it. No test in this
/// repository would ever have found it: every one of them asserts on the PROBE, not on whether
/// somebody should be looking at it. It took opening the app and reading the picture.</para>
///
/// <para><b>The channel itself is deliberate and is NOT being deleted, which is the whole point of
/// gating it here rather than removing the footer.</b> The probe is content-free, it is
/// UIA-readable on Windows, and <see cref="MainWindow"/> logs it to stderr whenever the geometry it
/// describes CHANGES, so it is readable on Linux. (It used to latch on the first layout, and on X11
/// that ran before the scale factor and the window placement landed, so the one logged line
/// described a window that no longer existed.) That log line is how this port read its five rail
/// doors' geometry on
/// a platform where every screen capture comes back black. Delete the channel and the Linux leg
/// loses its only evidence; render it to a user and the product ships its own scaffolding.</para>
///
/// <para><b>Debug renders it, Release does not.</b> The headed harness
/// (<c>client/tools/verify/capture.ps1</c>) drives a Debug build and reads the footer through UIA,
/// so it keeps working unchanged. A published build hides it. The behaviour cannot be inverted
/// from a test — a test run IS a Debug build — so the guard that protects this is a SOURCE guard
/// (<c>DiagnosticFooterGuardTests</c>), the same shape used for the path-portability rules where
/// the platform makes a behavioural inversion impossible.</para>
/// </summary>
internal static class DiagnosticFooterPolicy
{
    /// <summary>Whether the footer is rendered. Debug only.</summary>
    internal static bool Rendered =>
#if DEBUG
        true;
#else
        false;
#endif
}
