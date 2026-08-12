using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features.Chaos;
using CcpClient.Desktop.Features.Dtrh;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-061: the tunnel surface's capability typing — ONE capability, never a silent
/// fallback. Windows delegates to the DTRH embedded probe (literally the same engine load);
/// Linux carries the tunnel's OWN unavailability reasons (never DTRH's text, never a green
/// dialog row for an unadmitted surface — consult ruling 3b).
/// </summary>
public sealed class ChaosTunnelCapabilityTests
{
    [Fact]
    public void Probe_AlwaysReturnsATypedState_NeverThrows()
    {
        var state = ChaosTunnelCapabilityProbes.ProbeEmbedded();
        Assert.True(
            state is CapabilityState.Available or CapabilityState.Unavailable
                or CapabilityState.DependencyMissing or CapabilityState.Faulted,
            $"unexpected state: {state}");
    }

    [Fact]
    public void Windows_DelegatesToTheSameEngineLoadAsDtrh()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // the delegation branch is Windows-only; the Linux branch has its own test below
        }

        var tunnel = ChaosTunnelCapabilityProbes.ProbeEmbedded();
        var dtrh = DtrhCapabilityProbes.ProbeEmbedded();
        Assert.Equal(dtrh.GetType(), tunnel.GetType());
    }

    [Fact]
    public void Linux_UnavailableNamesTheTunnelsOwnTwoGaps()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // exercised on Linux runs (the WSL gate); skipped honestly here
        }

        var state = Assert.IsType<CapabilityState.Unavailable>(ChaosTunnelCapabilityProbes.ProbeEmbedded());
        Assert.Equal("unsupported-platform", state.Reason.Code);
        Assert.Contains("chrome.webview", state.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("keep-below", state.Reason.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyOneTunnelCapabilityExists_NeverADialogRow()
    {
        // The consult's divergence guard: no chaos-tunnel-web-dialog capability may appear
        // (a green row for an unadmitted surface). Pinned by the registered-names assertion
        // in CapabilityTests (the exact-name list) — this test pins the constant set.
        Assert.Equal("chaos-tunnel-webview-embedded", ChaosTunnelCapabilityProbes.EmbeddedCapability);
        Assert.Null(typeof(ChaosTunnelCapabilityProbes).GetField("DialogCapability"));
        Assert.Null(typeof(ChaosTunnelCapabilityProbes).GetMethod("ProbeDialog"));
    }
}
