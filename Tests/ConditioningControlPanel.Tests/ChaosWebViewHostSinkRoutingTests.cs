using ConditioningControlPanel;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #938 host-wide half — audio-output routing for every page ChaosWebViewHost serves from the
/// local ccp.game virtual origin (Arcademy, DtRH, FYP, Loom, Graded Intake, EMI desk...). Tester
/// reports 0831: these surfaces always played on the Windows default no matter what the output
/// picker said.
///
/// <para>The injected script cannot run under xunit, so its contract is pinned the way the
/// player-page suite pins its half: static reads of the template, asserting the pieces the design
/// depends on. The C#-side pins are the origin gate (remote first-party sites must NEVER get the
/// script or the mic grant) and the JSON embedding of the label (a device name cannot break out of
/// the script).</para>
/// </summary>
public class ChaosWebViewHostSinkRoutingTests
{
    // =====================================================================================
    //  origin gate
    // =====================================================================================

    [Fact]
    public void SinkRouting_OnlyForTheLocalVirtualOrigin()
    {
        Assert.True(ChaosWebViewHost.IsSinkRoutingHost("ccp.game"));
        Assert.True(ChaosWebViewHost.IsSinkRoutingHost("CCP.GAME"));
    }

    [Fact]
    public void SinkRouting_RemoteFirstPartyHostsStayOnTheDefault()
    {
        // Bureau / Just Drop navigate to remote sites; granting mic permission to remote content
        // is the trade the player-page fix refused for third parties — refuse it here too.
        Assert.False(ChaosWebViewHost.IsSinkRoutingHost("cclabs.app"));
        Assert.False(ChaosWebViewHost.IsSinkRoutingHost("www.cclabs.app"));
        Assert.False(ChaosWebViewHost.IsSinkRoutingHost(null));
        Assert.False(ChaosWebViewHost.IsSinkRoutingHost(""));
    }

    // =====================================================================================
    //  label embedding
    // =====================================================================================

    [Fact]
    public void BuildScript_EmbedsLabelAsJsonStringLiteral()
    {
        var script = ChaosWebViewHost.BuildSinkRoutingScript("Headphones (USB Audio)");
        Assert.Contains("var WANTED = \"Headphones (USB Audio)\";", script);
        Assert.DoesNotContain("__CCP_SINK_LABEL__", script);
    }

    [Fact]
    public void BuildScript_HostileLabelCannotEscapeTheLiteral()
    {
        // A device name is attacker-ish input only in the sense that Windows lets it hold
        // anything; quotes and backslashes must come out JSON-escaped, not raw.
        var script = ChaosWebViewHost.BuildSinkRoutingScript("evil\" };alert(1);//\\");
        Assert.Contains("var WANTED = \"evil\\\" };alert(1);//\\\\\";", script);
    }

    // =====================================================================================
    //  script contract pins
    // =====================================================================================

    [Fact]
    public void Script_ProbeIsLazyAndStopsItsTracks()
    {
        var script = ChaosWebViewHost.BuildSinkRoutingScript("X");
        // Probe only runs when enumerate comes back label-less...
        Assert.Contains("d.kind === 'audiooutput' && d.label", script);
        // ...and the getUserMedia stream is stopped immediately — nothing records anything.
        Assert.Contains("md.getUserMedia({ audio: true })", script);
        Assert.Contains("t.stop()", script);
    }

    [Fact]
    public void Script_PatchesBothAudioExits()
    {
        var script = ChaosWebViewHost.BuildSinkRoutingScript("X");
        // Media elements played directly (FYP, DtRH clips)...
        Assert.Contains("HTMLMediaElement.prototype.play", script);
        // ...and the WebAudio graph the Arcademy shell mixes everything through.
        Assert.Contains("AudioContext.prototype.setSinkId", script);
        Assert.Contains("class extends OrigCtx", script);
    }

    [Fact]
    public void Script_MatchesTheSameTolerantHeuristicAsThePlayerPage()
    {
        var script = ChaosWebViewHost.BuildSinkRoutingScript("X");
        // Exact → bracketed-driver → bidirectional prefix/contains, and the synthetic Chromium
        // ids never win over a concrete device.
        Assert.Contains("d.deviceId !== 'default' && d.deviceId !== 'communications'", script);
        Assert.Contains("w.lastIndexOf(')')", script);
    }

    [Fact]
    public void Script_ReportsThroughThePageLogBridge()
    {
        var script = ChaosWebViewHost.BuildSinkRoutingScript("X");
        // Same log lines as the player-page host contract, riding the existing 'log' envelope —
        // 'info' and 'warn' are levels PageLogLevel routes to Information/Warning, never Debug.
        Assert.Contains("type: 'log'", script);
        Assert.Contains("via setSinkId", script);
        Assert.Contains("staying on the Windows default output", script);
        Assert.Equal(Serilog.Events.LogEventLevel.Information, ChaosWebViewHost.PageLogLevel("info"));
        Assert.Equal(Serilog.Events.LogEventLevel.Warning, ChaosWebViewHost.PageLogLevel("warn"));
    }
}
