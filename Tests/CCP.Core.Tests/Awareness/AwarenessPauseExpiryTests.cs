using System;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The pause button promised an hour and delivered forever.
///
/// <para><b>The defect.</b> The privacy panel enforced "pause her for an hour" by calling
/// <c>App.WindowAwareness.Stop()</c>, which disposed the 1.5s poll timer and the v2 observer with it.
/// <see cref="AwarenessPause"/> expires on its own, but nothing ever called <c>Start()</c> again, so
/// she stayed silent until the app was relaunched. The trap on top: once the hour lapsed
/// <see cref="AwarenessPause.IsPaused"/> read false, so the button relabelled itself back to "pause
/// her for an hour" - and pressing it paused again. There was no path back to running.</para>
///
/// <para><b>The fix.</b> A pause is a check on every tick, never a shutdown. These tests pin the
/// self-expiry that the fix depends on, because if <see cref="AwarenessPause"/> ever stopped expiring
/// on its own the new design would strand her permanently and silently.</para>
///
/// <para>Runs in the awareness statics collection: <see cref="AwarenessPause"/> is process-wide
/// state, so these must not interleave with anything else that reads it.</para>
/// </summary>
[Collection(AwarenessStaticsCollection.Name)]
public class AwarenessPauseExpiryTests : IDisposable
{
    public AwarenessPauseExpiryTests() => AwarenessPause.Resume();
    public void Dispose() => AwarenessPause.Resume();

    [Fact]
    public void APauseLiftsItselfWhenTheHourIsUp()
    {
        var start = new DateTime(2026, 8, 7, 9, 0, 0, DateTimeKind.Local);
        AwarenessPause.Pause(AwarenessPause.DefaultDuration, start);

        Assert.True(AwarenessPause.IsPaused(start.AddMinutes(59)));
        // The whole point: nothing has to call anything for this to become false.
        Assert.False(AwarenessPause.IsPaused(start.AddMinutes(61)));
    }

    [Fact]
    public void TheDefaultIsActuallyAnHour()
    {
        // The button's label says "an hour" in nine languages.
        Assert.Equal(TimeSpan.FromHours(1), AwarenessPause.DefaultDuration);
    }

    [Fact]
    public void RemainingCountsDownAndNeverGoesNegative()
    {
        var start = new DateTime(2026, 8, 7, 9, 0, 0, DateTimeKind.Local);
        AwarenessPause.Pause(AwarenessPause.DefaultDuration, start);

        Assert.Equal(45, (int)Math.Round(AwarenessPause.Remaining(start.AddMinutes(15)).TotalMinutes));

        // The label ceilings this into "paused - {0}m left"; a negative would render as a
        // countdown running backwards rather than as a finished pause.
        Assert.Equal(TimeSpan.Zero, AwarenessPause.Remaining(start.AddHours(3)));
    }

    [Fact]
    public void PressingPauseTwiceExtendsAndNeverShortens()
    {
        var start = new DateTime(2026, 8, 7, 9, 0, 0, DateTimeKind.Local);
        AwarenessPause.Pause(TimeSpan.FromHours(2), start);

        // A second, shorter press must not cut the longer pause short: pressing the button again
        // may never leave the user less protected than not pressing it.
        AwarenessPause.Pause(TimeSpan.FromMinutes(10), start);

        Assert.True(AwarenessPause.IsPaused(start.AddMinutes(30)));
        Assert.True(AwarenessPause.IsPaused(start.AddMinutes(110)));
        Assert.False(AwarenessPause.IsPaused(start.AddMinutes(121)));
    }

    [Fact]
    public void ResumeIsImmediateSoTheButtonStillWorksMidPause()
    {
        var start = new DateTime(2026, 8, 7, 9, 0, 0, DateTimeKind.Local);
        AwarenessPause.Pause(AwarenessPause.DefaultDuration, start);
        Assert.True(AwarenessPause.IsPaused(start.AddMinutes(5)));

        AwarenessPause.Resume();

        Assert.False(AwarenessPause.IsPaused(start.AddMinutes(5)));
        Assert.Equal(TimeSpan.Zero, AwarenessPause.Remaining(start.AddMinutes(5)));
    }

    [Fact]
    public void APauseNeverOutlivesTheProcess()
    {
        // Not persisted, by design: a pause that survived a reboot would be a capability the user
        // believes they switched back on. The panel's copy says restarting resumes her, so nothing
        // may write this to settings.
        var start = new DateTime(2026, 8, 7, 9, 0, 0, DateTimeKind.Local);
        AwarenessPause.Pause(AwarenessPause.DefaultDuration, start);

        var settings = new ConditioningControlPanel.Models.AppSettings();
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(settings);

        Assert.DoesNotContain("PausedUntil", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AwarenessPause", json, StringComparison.OrdinalIgnoreCase);
    }
}
