using System.Linq;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1147: "Brain drain not offered as option when creating a new session". The runtime model has
/// carried BrainDrainEnabled / StartMinute / Start+End intensity all along, and TimelineSession
/// converts them in both directions - what was missing was the palette icon (skipped outright in
/// SessionEditorWindow, #430) and the SessionEngine apply path (fully commented out, so a block on
/// the timeline was a no-op). These tests pin the data half: the feature is still in the palette
/// catalogue, and a brain-drain block survives create -> save -> reload with its numbers intact.
/// Pure data classes, so no WPF Application is required.
/// </summary>
public class SessionBrainDrainRoundTripTests
{
    [Fact]
    public void BrainDrain_IsInTheCreatorFeatureCatalogue()
    {
        var feature = FeatureDefinition.GetAllFeatures().SingleOrDefault(f => f.Id == "brain_drain");

        Assert.NotNull(feature);
        Assert.Equal(FeatureCategory.Overlays, feature!.Category);
        Assert.True(feature.SupportsRamping);
        Assert.Contains(feature.Settings, s => s.Key == "intensity" && s.SupportsRamp);
    }

    [Fact]
    public void ABrainDrainBlock_SurvivesSaveAndReload()
    {
        // Create: drop a ramping brain-drain block on the timeline from minute 10 to minute 40.
        var authored = new TimelineSession { Name = "Drain Test", DurationMinutes = 60 };
        var start = authored.AddStartEvent("brain_drain", 10);
        start.SetSetting("intensity", 4);
        start.StartValue = 4;
        start.EndValue = 18;
        authored.AddStopEvent(start, 40);

        // Save: this is what the engine actually runs.
        var settings = authored.ToSessionSettings();

        Assert.True(settings.BrainDrainEnabled);
        Assert.Equal(10, settings.BrainDrainStartMinute);
        Assert.Equal(40, settings.BrainDrainEndMinute);
        Assert.Equal(4, settings.BrainDrainStartIntensity);
        Assert.Equal(18, settings.BrainDrainEndIntensity);

        // Reload: reopening the saved session in the editor has to show the same block.
        var reopened = TimelineSession.FromSession(new Session
        {
            Name = authored.Name,
            DurationMinutes = authored.DurationMinutes,
            Settings = settings
        });

        var reloaded = Assert.Single(reopened.GetStartEvents("brain_drain"));
        Assert.Equal(10, reloaded.Minute);
        Assert.Equal(4, reloaded.StartValue);
        Assert.Equal(18, reloaded.EndValue);
        Assert.Equal(40, reopened.GetPairedStopEvent(reloaded)?.Minute);
    }

    [Fact]
    public void ATimelineWithoutBrainDrain_LeavesTheFeatureOff()
    {
        var authored = new TimelineSession { Name = "No Drain", DurationMinutes = 30 };
        authored.AddStartEvent("flash", 0);

        var settings = authored.ToSessionSettings();

        Assert.False(settings.BrainDrainEnabled);
    }
}
