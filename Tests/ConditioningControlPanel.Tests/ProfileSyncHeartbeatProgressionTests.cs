using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Redis bandwidth pass (2026-09-15): the desktop's ungated 60s GET /v2/user/profile poll was
/// retired and the cross-device XP adopt now reads level/xp/current_season off the heartbeat
/// response's <c>progression</c> block. These pin the parser between the wire and the adopt:
/// nothing is offered unless the block is present and well-typed, and a server that predates
/// the block (no <c>progression</c> at all) is a silent no-op, never an exception.
/// </summary>
public class ProfileSyncHeartbeatProgressionTests
{
    [Fact]
    public void AWellFormedBlockIsHandedThrough()
    {
        var p = ProfileSyncService.ParseHeartbeatProgression(
            "{\"success\":true,\"last_seen\":\"2026-09-15T00:00:00.000Z\",\"progression\":{\"level\":47,\"xp\":250000.5,\"current_season\":\"2026-09\"}}");
        Assert.NotNull(p);
        Assert.Equal(47, p!.Value.level);
        Assert.Equal(250000.5, p.Value.totalXp);
        Assert.Equal("2026-09", p.Value.season);
    }

    [Fact]
    public void AnIntegerXpAndAMissingSeasonAreStillAnOffer()
    {
        var p = ProfileSyncService.ParseHeartbeatProgression("{\"success\":true,\"progression\":{\"level\":3,\"xp\":1200}}");
        Assert.NotNull(p);
        Assert.Equal(3, p!.Value.level);
        Assert.Equal(1200d, p.Value.totalXp);
        Assert.Null(p.Value.season);
    }

    [Theory]
    [InlineData(null)]                                                           // no body
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"success\":true,\"last_seen\":\"2026-09-15T00:00:00.000Z\"}")]  // a pre-pass server: no block
    [InlineData("{\"success\":true,\"progression\":null}")]                        // server had no numbers to give
    [InlineData("{\"success\":true,\"progression\":{\"level\":null,\"xp\":null,\"current_season\":null}}")]
    [InlineData("{\"success\":true,\"progression\":{\"level\":0,\"xp\":500}}")]      // level 0 is "unknown", never adopt
    [InlineData("{\"success\":true,\"progression\":{\"level\":\"5\",\"xp\":500}}")]  // strings are not numbers
    [InlineData("{\"success\":true,\"progression\":{\"level\":5,\"xp\":\"500\"}}")]
    [InlineData("{\"success\":true,\"progression\":{\"level\":5}}")]                 // no xp at all
    [InlineData("{\"success\":true,\"progression\":[]}")]                          // wrong shape
    public void AnythingLessThanAWellTypedBlockIsNoOffer(string? body)
        => Assert.Null(ProfileSyncService.ParseHeartbeatProgression(body));

    [Fact]
    public void TheWholeHandOffNeverThrowsOnGarbage()
    {
        // TryAdoptFromHeartbeatBody runs inside the heartbeat tick. With no App.Settings in a
        // test process the adopt bails at its first guard, so this exercises parse + call.
        var svc = new ProfileSyncService();
        svc.TryAdoptFromHeartbeatBody("{\"progression\":{\"level\":9,\"xp\":99}}");
        svc.TryAdoptFromHeartbeatBody("{{{{");
        svc.TryAdoptFromHeartbeatBody(null);
    }
}
