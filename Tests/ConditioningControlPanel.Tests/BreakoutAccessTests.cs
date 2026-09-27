using System;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

[Collection("LauncherSignIn")]
public sealed class BreakoutAccessTests : IDisposable
{
    private readonly Action<bool> _open = BreakoutHostService.OpenHost;
    private readonly Func<bool> _demand = BreakoutHostService.DemandFull;
    public void Dispose()
    {
        BreakoutHostService.OpenHost = _open;
        BreakoutHostService.DemandFull = _demand;
    }

    [Theory]
    [InlineData(false, false, 3, false, true)]
    [InlineData(false, true, 3, false, true)]
    [InlineData(true, true, 3, false, true)]
    [InlineData(true, false, 8, true, false)]
    public void Projection_caps_demo_and_only_full_entitlement_enables_Endless(
        bool tier2, bool explicitDemo, int limit, bool endless, bool demo)
    {
        var wire = JObject.FromObject(BreakoutAccess.Project(tier2, explicitDemo));
        Assert.Equal(limit, (int)wire["storyLimit"]!);
        Assert.Equal(endless, (bool)wire["endless"]!);
        Assert.Equal(demo, (bool)wire["demo"]!);
        Assert.Equal(3, wire.Count);
    }

    [Fact]
    public void Demo_launch_never_queries_a_paid_or_sign_in_gate()
    {
        bool? opened = null;
        BreakoutHostService.DemandFull = () => throw new InvalidOperationException("demo must be free");
        BreakoutHostService.OpenHost = demo => opened = demo;
        BreakoutHostService.LaunchDemo();
        Assert.True(opened);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Full_launch_rechecks_tier_and_never_opens_when_denied(bool allowed)
    {
        int checks = 0, opens = 0;
        BreakoutHostService.DemandFull = () => { checks++; return allowed; };
        BreakoutHostService.OpenHost = demo => { Assert.False(demo); opens++; };
        BreakoutHostService.LaunchFull();
        Assert.Equal(1, checks);
        Assert.Equal(allowed ? 1 : 0, opens);
    }

    [Fact]
    public void Lost_entitlement_projects_demo_instead_of_preserving_full_access()
    {
        Assert.Equal(8, BreakoutAccess.Project(true).StoryLimit);
        var revoked = BreakoutAccess.Project(false);
        Assert.Equal(3, revoked.StoryLimit);
        Assert.False(revoked.Endless);
        Assert.True(revoked.Demo);
    }
}
