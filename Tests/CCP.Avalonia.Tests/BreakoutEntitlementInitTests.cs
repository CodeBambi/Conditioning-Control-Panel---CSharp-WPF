using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>play#25 (WPF BackRoomHostService.BuildInit :575 + BreakoutAccess.Project): the init frame carries
/// the Breakout entitlement, so Prime gets the full game on Windows and the demo door stays a demo on Linux.</summary>
public sealed class BreakoutEntitlementInitTests
{
    private static JObject? Of(string id, bool full) =>
        GameWindow.BreakoutEntitlementFor(id, full) is { } o ? JObject.FromObject(o) : null;

    [Fact]
    public void FullDoorFollowsTheTierAndTheDemoDoorNeverExpands()
    {
        Assert.Equal(JObject.Parse("{storyLimit:8,endless:true,demo:false}"), Of("breakout", true), JToken.EqualityComparer);
        Assert.Equal(JObject.Parse("{storyLimit:3,endless:false,demo:true}"), Of("breakout", false), JToken.EqualityComparer);
        Assert.Equal(JObject.Parse("{storyLimit:3,endless:false,demo:true}"), Of("breakoutdemo", true), JToken.EqualityComparer);
        Assert.Equal(JObject.Parse("{storyLimit:8,endless:true,demo:false}"), Of("backroom", true), JToken.EqualityComparer);
        Assert.Null(Of("goon", true));
    }
}
