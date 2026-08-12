using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features.Dtrh;

namespace CcpClient.Desktop.Features.Chaos;

/// <summary>
/// The tunnel surface's capability probe (SP-023 capability-honesty pattern). ONE capability
/// only — deliberately NO dialog capability (pre-approach consult ruling 3b): a NativeWebDialog
/// tunnel on Linux could only render the page's black curtain (the page's sole host transport
/// is window.chrome.webview, absent on WebKitGTK) and the dialog toplevel exposes no
/// keep-below control, so the below-Topmost contract cannot be honored — an unfalsifiable
/// surface is not admitted (the SP-054 intake precedent). A green dialog capability row for
/// an unadmitted surface is exactly the divergence the capability contract bans.
/// </summary>
public static class ChaosTunnelCapabilityProbes
{
    /// <summary>Embedded WebView surface (Windows = WebView2 NativeWebView).</summary>
    public const string EmbeddedCapability = "chaos-tunnel-webview-embedded";

    public static CapabilityState ProbeEmbedded()
    {
        if (!OperatingSystem.IsWindows())
        {
            // The tunnel's OWN Linux reasons (never DTRH's): no page-side bridge transport
            // + no keep-below control on the dialog toplevel.
            return new CapabilityState.Unavailable(new CapabilityReason(
                "unsupported-platform",
                "the tunnel page's only host transport is window.chrome.webview "
                + "(Resources/web/tunnel/main.js:20-21,40) — absent on WebKitGTK; and the "
                + "NativeWebDialog toplevel exposes no keep-below control, so below-Topmost "
                + "cannot be honoured (SP-025 b3 Linux-toplevel-z-order precedent). "
                + "Linux tunnel surface not admitted."));
        }

        // The dependency being probed is literally the same engine load the DTRH embedded
        // surface performs (registry-located EmbeddedBrowserWebView.dll + factory export) —
        // delegate; the returned detail names what was confirmed there. Rendering is claimed
        // only by headed evidence, never by this probe.
        return DtrhCapabilityProbes.ProbeEmbedded();
    }
}
