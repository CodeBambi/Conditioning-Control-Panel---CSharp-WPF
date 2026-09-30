using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Twin of the WPF PromptTwoZoneTests legacy case. In the head, App.Personality was null
/// under test, so BuildSystemPrompt fell to the default prompt, whose example title is shuffled.
/// Core reads the stateless PersonalityService.Shared, which always yields a preset (neutral
/// default, no {{VIDEO}} tokens), so the shuffle has to be reached through a preset that has them.</summary>
public sealed class BambiSpriteLegacyShuffleTests
{
    [Fact]
    public void LegacyBuild_ShufflesPerCall_OnAPresetWithVideoTokens()
    {
        var settings = new AppSettings { ActivePersonalityPresetId = PersonalityPresets.SlutModeId };
        var service = (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
        typeof(SettingsService).GetField("<Current>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, settings);
        var priorProvider = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        BambiSprite.VideoPoolProvider = () => new Dictionary<string, string>
        {
            ["Yes Brain Loop"] = "u1", ["Bambi Bae"] = "u2", ["Naughty Bambi"] = "u3",
            ["Overload"] = "u4", ["Day 1"] = "u5", ["Bambi Slay"] = "u6", ["TikTok Loop"] = "u7"
        };
        try
        {
            var legacy = new BambiSprite();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 30; i++) seen.Add(legacy.BuildSystemPrompt());
            Assert.True(seen.Count > 1, "legacy build produced 30 byte-identical prompts - the per-call sampling is gone");
        }
        finally
        {
            BambiSprite.VideoPoolProvider = null;
            CoreSettings.ServiceProvider = priorProvider;
        }
    }
}
