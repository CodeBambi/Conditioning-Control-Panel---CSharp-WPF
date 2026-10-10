using System;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF AwarenessPrivacyRuntimeVm: both awareness list pickers offer AwarenessAppCandidates.Gather
/// (the window in front, the ledger, then the trigger ring), minus what is already listed.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class AwarenessPickerCandidatesTests
{
    [Fact]
    public void The_picker_offers_the_window_in_front_then_the_trigger_ring_and_never_a_listed_app()
    {
        var (oldCurrent, oldRecent) = (AwarenessHost.CurrentServiceName, AwarenessHost.RecentForegroundApps);
        try
        {
            AwarenessHost.CurrentServiceName = () => "firefox";
            AwarenessHost.RecentForegroundApps = () => new List<string> { "steam", "Firefox", "discord" };
            var offered = AwarenessPrivacyViewModel.Candidates(new[] { "Discord" });
            Assert.Equal("firefox", offered[0]);
            Assert.Contains("steam", offered);
            Assert.DoesNotContain(offered, a => string.Equals(a, "discord", StringComparison.OrdinalIgnoreCase));
            Assert.Single(offered, a => string.Equals(a, "firefox", StringComparison.OrdinalIgnoreCase));
        }
        finally { (AwarenessHost.CurrentServiceName, AwarenessHost.RecentForegroundApps) = (oldCurrent, oldRecent); }
    }
}
