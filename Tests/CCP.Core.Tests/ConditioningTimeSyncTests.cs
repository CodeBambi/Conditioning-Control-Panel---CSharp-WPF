using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>hunt3 IC5 (WPF SyncConditioningTimeToServerAsync, ProfileSyncService.cs:1820 / :2296 /
/// :3417): the total reaches the server through the ordinary push, asked for every 15 minutes of a run
/// and once on its stop, never per second; a higher account total is adopted, never a lower one.</summary>
[Collection(SessionStatics.Name)]   // ConditioningTime is static and reads CoreSettings.Current
public sealed class ConditioningTimeSyncTests
{
    private static void WithTracker(Action<Func<int>> body)
    {
        var s = CoreSettings.Current;
        var (oldMinutes, oldSync) = (s.TotalConditioningMinutes, ConditioningTime.SyncRequested);
        var t0 = new DateTime(2026, 10, 10, 12, 0, 0);
        ConditioningTime.OnEngineStopped(t0);   // whatever an earlier test left running
        var asks = 0;
        ConditioningTime.SyncRequested = () => asks++;
        try { body(() => asks); }
        finally
        {
            ConditioningTime.SyncRequested = null;
            ConditioningTime.OnEngineStopped(t0);
            ConditioningTime.SyncRequested = oldSync;
            s.TotalConditioningMinutes = oldMinutes;
        }
    }

    [Fact]
    public void TheBodyCarriesTheTotal()
    {
        var body = JObject.Parse(JsonConvert.SerializeObject(SyncPush.Body(new AppSettings { UnifiedId = "u_1", TotalConditioningMinutes = 90.5 }, null)));
        Assert.Equal(90.5, body.Value<double>("total_conditioning_minutes"));
        Assert.True((SyncPush.Sent & SyncBody.Field.TotalConditioningMinutes) != 0);
    }

    [Fact]
    public void ARunAsksEveryFifteenMinutes_AndOnceOnStop_NeverPerSecond() => WithTracker(asks =>
    {
        var t0 = new DateTime(2026, 10, 10, 12, 0, 0);
        CoreSettings.Current.TotalConditioningMinutes = 100;
        ConditioningTime.OnEngineStarted(t0);
        for (var i = 1; i < ConditioningTime.SyncEverySeconds; i++) ConditioningTime.Tick(t0.AddSeconds(i));
        Assert.Equal(0, asks());                                        // 14:59 in: nothing sent yet
        ConditioningTime.Tick(t0.AddSeconds(ConditioningTime.SyncEverySeconds));
        Assert.Equal(1, asks());
        Assert.Equal(115, CoreSettings.Current.TotalConditioningMinutes, 3);   // the 15th minute landed before the ask
        for (var i = 1; i <= ConditioningTime.SyncEverySeconds; i++) ConditioningTime.Tick(t0.AddSeconds(900 + i));
        Assert.Equal(2, asks());
        ConditioningTime.OnEngineStopped(t0.AddSeconds(1800));
        Assert.Equal(3, asks());                                        // the stop
        ConditioningTime.Tick(t0.AddSeconds(1801));
        ConditioningTime.OnEngineStopped(t0.AddSeconds(1802));
        Assert.Equal(3, asks());                                        // not running: nothing more
    });

    [Fact]
    public void AThrowingSenderNeverBreaksTheTracker() => WithTracker(_ =>
    {
        var t0 = new DateTime(2026, 10, 10, 12, 0, 0);
        CoreSettings.Current.TotalConditioningMinutes = 10;
        ConditioningTime.SyncRequested = () => throw new InvalidOperationException("offline");
        ConditioningTime.OnEngineStarted(t0);
        ConditioningTime.OnEngineStopped(t0.AddMinutes(2));
        Assert.Equal(12, CoreSettings.Current.TotalConditioningMinutes, 3);
        Assert.False(ConditioningTime.IsTracking);
    });

    [Fact]
    public void AHigherAccountTotalIsAdopted_ALowerOneNever()
    {
        var s = new AppSettings { TotalConditioningMinutes = 50 };
        Assert.True(ProfileAdopt.AdoptConditioningMinutes(s, new JValue(480.5), "test"));
        Assert.Equal(480.5, s.TotalConditioningMinutes);
        Assert.False(ProfileAdopt.AdoptConditioningMinutes(s, new JValue(20), "test"));
        Assert.False(ProfileAdopt.AdoptConditioningMinutes(s, new JValue("9999"), "test"));
        Assert.False(ProfileAdopt.AdoptConditioningMinutes(s, null, "test"));
        Assert.Equal(480.5, s.TotalConditioningMinutes);
        ProfileAdopt.ApplySyncResponse(s, JObject.Parse("{\"total_conditioning_minutes\":600}"), DateTime.UtcNow);
        Assert.Equal(600, s.TotalConditioningMinutes);
    }

    [Fact]
    public void ALiftMidRunMovesTheBaseline_SoTheStopCreditsOnlyThisRun() => WithTracker(_ =>
    {
        var t0 = new DateTime(2026, 10, 10, 12, 0, 0);
        var s = CoreSettings.Current;
        s.TotalConditioningMinutes = 10;
        ConditioningTime.OnEngineStarted(t0);
        Assert.True(ProfileAdopt.AdoptConditioningMinutes(s, new JValue(500), "test"));   // a sync reply mid-run
        ConditioningTime.OnEngineStopped(t0.AddMinutes(3));
        Assert.Equal(503, s.TotalConditioningMinutes, 3);
    });
}
