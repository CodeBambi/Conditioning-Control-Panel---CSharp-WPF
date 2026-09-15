using System.Linq;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Split-accounts contract A: /v2/auth/device/initiate carries the desktop's own unified id and
/// auth token when the app holds BOTH, so the server can bind the pairing to this account instead
/// of minting a twin. With either missing the request is exactly what it was before the contract.
/// </summary>
public class DeviceCodeInitiatePayloadTests
{
    private const string Uid = "u_mfk3q9x1a2b3c4d5e6f7";
    private const string Token = "tok_live_do_not_log";

    private static void AssertLegacyShape(V2DeviceCodeService.InitiatePayload payload)
    {
        Assert.Null(payload.AuthToken);
        Assert.Equal(new[] { "client", "version" }, payload.Body.Properties().Select(p => p.Name).ToArray());
        Assert.Equal("ccp-desktop", payload.Body["client"]!.Value<string>());
        Assert.Equal(UpdateService.AppVersion, payload.Body["version"]!.Value<string>());
    }

    [Fact]
    public void NoSessionSendsExactlyWhatItSentBefore()
    {
        AssertLegacyShape(V2DeviceCodeService.BuildInitiatePayload(null, null));
    }

    [Fact]
    public void AnIdWithoutATokenIsNotAHint()
    {
        AssertLegacyShape(V2DeviceCodeService.BuildInitiatePayload(Uid, null));
        AssertLegacyShape(V2DeviceCodeService.BuildInitiatePayload(Uid, "  "));
    }

    [Fact]
    public void ATokenWithoutAnIdIsNotAHint()
    {
        AssertLegacyShape(V2DeviceCodeService.BuildInitiatePayload(null, Token));
        AssertLegacyShape(V2DeviceCodeService.BuildInitiatePayload("", Token));
    }

    [Fact]
    public void AFullSessionRidesAlong()
    {
        var payload = V2DeviceCodeService.BuildInitiatePayload(Uid, Token);

        Assert.Equal(Token, payload.AuthToken);
        Assert.Equal(Uid, payload.Body["unified_id"]!.Value<string>());
        Assert.Equal("ccp-desktop", payload.Body["client"]!.Value<string>());
        Assert.Equal(UpdateService.AppVersion, payload.Body["version"]!.Value<string>());
        Assert.Equal(3, payload.Body.Count);
    }

    [Fact]
    public void TheTokenNeverEntersTheBody()
    {
        var payload = V2DeviceCodeService.BuildInitiatePayload(Uid, Token);

        Assert.DoesNotContain(Token, payload.Body.ToString());
    }
}
