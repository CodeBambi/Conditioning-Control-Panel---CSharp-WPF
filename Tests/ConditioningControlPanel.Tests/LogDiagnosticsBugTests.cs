using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1202 / #1216 (Lovense never connects) and #1210 (local AI answers with the fallback
/// line every time). The logs showed the symptom but not the cause; these pin the diagnostics
/// that now say which of the causes it was.
/// </summary>
public class LogDiagnosticsBugTests
{
    private static Exception Refused() =>
        new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused));

    [Fact]
    public void RefusedSocket_IsRefused()
    {
        Assert.Equal(LovenseProviderV2.ProbeFailure.Refused, LovenseProviderV2.ClassifyProbeFailure(Refused()));
    }

    [Fact]
    public void CancelledProbe_IsTimeout()
    {
        Assert.Equal(LovenseProviderV2.ProbeFailure.TimedOut,
            LovenseProviderV2.ClassifyProbeFailure(new TaskCanceledException("A task was canceled.")));
    }

    [Fact]
    public void AllRefused_SaysGameModeIsNotListening()
    {
        var hint = LovenseProviderV2.UnreachableHint(
            new[] { LovenseProviderV2.ProbeFailure.Refused, LovenseProviderV2.ProbeFailure.Refused }, true);
        Assert.Contains("not listening", hint);
    }

    [Fact]
    public void TimeoutsOffSubnet_SaysDifferentNetwork()
    {
        var hint = LovenseProviderV2.UnreachableHint(new[] { LovenseProviderV2.ProbeFailure.TimedOut }, false);
        Assert.Contains("not on the phone's network", hint);
    }

    [Fact]
    public void TimeoutsOnSubnet_SaysNothingAnswered()
    {
        var hint = LovenseProviderV2.UnreachableHint(new[] { LovenseProviderV2.ProbeFailure.TimedOut }, true);
        Assert.Contains("Nothing answered", hint);
    }

    [Fact]
    public void SharesSubnet_MatchesOnlyInsideTheMask()
    {
        var locals = new[] { (IPAddress.Parse("192.168.4.10"), IPAddress.Parse("255.255.255.0")) };
        Assert.True(LovenseProviderV2.SharesSubnet(IPAddress.Parse("192.168.4.25"), locals));
        Assert.False(LovenseProviderV2.SharesSubnet(IPAddress.Parse("192.168.1.25"), locals));
    }

    [Fact]
    public void ReplyShape_ReportsThinkingAndDoneReason_NeverText()
    {
        var body = "{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"thinking\":\"secret plan\"},\"done_reason\":\"length\"}";
        var shape = LocalAiService.DescribeReplyShape(body);
        Assert.Contains("done_reason=length", shape);
        Assert.Contains("content=0 chars", shape);
        Assert.Contains("thinking=11 chars", shape);
        Assert.DoesNotContain("secret", shape);
    }

    [Fact]
    public void ReplyShape_SurvivesJunk()
    {
        Assert.Contains("not JSON", LocalAiService.DescribeReplyShape("<html>"));
        Assert.Equal("body 0 bytes", LocalAiService.DescribeReplyShape(null));
    }
}
