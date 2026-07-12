using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Services.Awareness;
using ConditioningControlPanel.Core.Services.Bark;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// BARK-1 slice 3 trigger-wiring tests: the awareness→Raise pair (closes AI-10), Dispose-unsubscribe,
/// and the chaos regression-guard routing helper (<see cref="BarkTriggerRouting"/>). The wiring lives in
/// Core so it is unit-testable without the Avalonia head; the engine is REAL (its gates/conditions apply)
/// and the awareness source is a fake that raises the events. Condition-keyed rules prove the ctx vars
/// were stamped correctly (the rule fires only when the stamped value matches).
/// </summary>
public class BarkTriggerWiringTests
{
    // ---- standalone fakes (mirror BarkEngineTests' harness so this suite needs no shared base) ----

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public bool WasSettingsFileMissing => true;
        public List<string> PendingPresetReinstalls { get; } = new();
        public void Save() { }
        public void Save(bool suppressCloudBackup = false) { }
        public void SaveImmediate(bool suppressCloudBackup = false) { }
        public void RestoreFrom(AppSettings settings) { }
        public void Reset() { }
    }

    private sealed class FakeSpeaker : IBarkSpeaker
    {
        public readonly List<(string Line, string Trigger)> Calls = new();
        public void Speak(string line, string? audioPath, bool priority, string? mood, BarkContext ctx) =>
            Calls.Add((line, ctx.Trigger));
    }

    private sealed class FakeLive : IBarkLiveFields
    {
        public bool TryResolve(string field, out object? value) { value = null; return false; }
    }

    private sealed class FakeGates : IBarkGateSignals
    {
        public bool IsWhisperAudioPlaying => false;
        public bool IsNarratorPlaying => false;
        public bool IsAvatarSpeaking => false;
        public bool IsCompanionBusy(int windowMs) => false;
    }

    /// <summary>Deterministic Random: NextDouble=0 (chance roll always passes), Next(max)=0.</summary>
    private sealed class ScriptedRandom : Random
    {
        public override double NextDouble() => 0;
        public override int Next(int maxValue) => 0;
    }

    private sealed class FakeAwareness : IAwarenessService
    {
        public string CurrentServiceName { get; set; } = "Steam";
        public TimeSpan CurrentActivityDuration { get; set; } = TimeSpan.FromMinutes(6);
        public ActivityCategory CurrentActivity { get; set; } = ActivityCategory.Gaming;
        public string CurrentDetectedName => CurrentServiceName;
        public string CurrentPageTitle => "";
        public bool IsRunning => true;

        public event EventHandler<ActivityChangedEventArgs>? ActivityChanged;
        public event EventHandler<ActivityChangedEventArgs>? StillOnActivity;

        public void Start() { }
        public void Stop() { }
        public bool CanReact() => false;
        public bool CanStillOnReact() => false;
        public void MarkReaction() { }
        public void MarkStillOnReaction() { }

        public void RaiseChanged(ActivityCategory cat, string svc, string cluster = "games", string app = "steam.exe") =>
            ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(cat, ActivityCategory.Unknown, svc, svc, "", false, "", cluster, app));

        public void RaiseStill() =>
            StillOnActivity?.Invoke(this, new ActivityChangedEventArgs(CurrentActivity, ActivityCategory.Unknown, CurrentServiceName, CurrentServiceName));
    }

    private sealed class EngineHarness
    {
        public readonly FakeSpeaker Speaker = new();
        public readonly BarkEngine Engine;

        public EngineHarness(params BarkRule[] rules)
        {
            Engine = new BarkEngine(
                new FakeSettings(), Speaker, new FakeLive(), new FakeGates(),
                mods: null, audioResolver: null,
                ruleLoader: () => new BarkRuleSet(rules),
                logger: null,
                utcNow: () => DateTime.UtcNow,
                rng: new ScriptedRandom());
            Engine.Start();
            Engine.DryRun = false; // deterministic regardless of CCP_BARK_DRYRUN in the environment
        }
    }

    private static BarkRule Rule(string id, string trigger, Dictionary<string, object>? conditions = null) =>
        new()
        {
            Id = id, Trigger = trigger,
            VariantPool = new List<BarkVariant> { new(id + "-line") },
            Priority = 0, CooldownMs = 0, Repeatable = true, ScopeRaw = "session",
            ClassRaw = "normal", Chance = 1.0, Conditions = conditions
        };

    // ------------------------------------------------------------------
    // Awareness pair → engine.Raise (closes AI-10; WPF BarkService.cs:562-577)
    // ------------------------------------------------------------------

    [Fact]
    public void ActivityChanged_RaisesActivityChangedTrigger_WithCategoryCtx()
    {
        // Rule fires only when the stamped category == "Gaming" → proves the ctx var was set.
        var h = new EngineHarness(Rule("a", "ActivityChanged", new() { ["category"] = "Gaming" }));
        var aware = new FakeAwareness();
        using var wiring = new BarkTriggerWiring(h.Engine, aware);
        wiring.Start();

        aware.RaiseChanged(ActivityCategory.Gaming, "Steam");           // category matches → fires
        Assert.Single(h.Speaker.Calls);
        Assert.Equal("ActivityChanged", h.Speaker.Calls[0].Trigger);

        aware.RaiseChanged(ActivityCategory.Media, "YouTube");          // category mismatch → no fire
        Assert.Single(h.Speaker.Calls);
    }

    [Fact]
    public void ActivityChanged_StampsAppClusterAndAppCtx()
    {
        var h = new EngineHarness(Rule("a", "ActivityChanged",
            new() { ["app_cluster"] = "games", ["app"] = "steam.exe" }));
        var aware = new FakeAwareness();
        using var wiring = new BarkTriggerWiring(h.Engine, aware);
        wiring.Start();

        aware.RaiseChanged(ActivityCategory.Gaming, "Steam", cluster: "games", app: "steam.exe"); // match
        Assert.Single(h.Speaker.Calls);

        // Mismatched cluster on a fresh engine → rule does not fire (condition false).
        var h2 = new EngineHarness(Rule("a", "ActivityChanged",
            new() { ["app_cluster"] = "games", ["app"] = "steam.exe" }));
        var aware2 = new FakeAwareness();
        using var wiring2 = new BarkTriggerWiring(h2.Engine, aware2);
        wiring2.Start();
        aware2.RaiseChanged(ActivityCategory.Gaming, "Steam", cluster: "social", app: "discord.exe");
        Assert.Empty(h2.Speaker.Calls);
    }

    [Fact]
    public void StillOnActivity_RaisesStillOnActivityTrigger_WithStillMinutesCtx()
    {
        // Rule fires only when still_minutes >= 5 → proves the ctx var was set from the duration.
        var h = new EngineHarness(Rule("s", "StillOnActivity", new() { ["still_minutes_gte"] = 5.0 }));
        var aware = new FakeAwareness { CurrentActivityDuration = TimeSpan.FromMinutes(6) };
        using var wiring = new BarkTriggerWiring(h.Engine, aware);
        wiring.Start();

        aware.RaiseStill(); // 6 min >= 5 → fires
        Assert.Single(h.Speaker.Calls);
        Assert.Equal("StillOnActivity", h.Speaker.Calls[0].Trigger);
    }

    [Fact]
    public void StillOnActivity_BelowThresholdCondition_DoesNotFire()
    {
        var h = new EngineHarness(Rule("s", "StillOnActivity", new() { ["still_minutes_gte"] = 5.0 }));
        var aware = new FakeAwareness { CurrentActivityDuration = TimeSpan.FromMinutes(2) };
        using var wiring = new BarkTriggerWiring(h.Engine, aware);
        wiring.Start();

        aware.RaiseStill(); // 2 min < 5 → condition false → no bark
        Assert.Empty(h.Speaker.Calls);
    }

    [Fact]
    public void Dispose_UnsubscribesAwareness_NoBarkAfterShutdown()
    {
        var h = new EngineHarness(Rule("a", "ActivityChanged"));
        var aware = new FakeAwareness();
        var wiring = new BarkTriggerWiring(h.Engine, aware); // not in a using: Dispose explicitly
        wiring.Start();
        wiring.Dispose();

        aware.RaiseChanged(ActivityCategory.Gaming, "Steam");
        Assert.Empty(h.Speaker.Calls); // unsubscribed → engine never sees the event
    }

    // ------------------------------------------------------------------
    // Chaos regression-guard routing (BarkTriggerRouting; WPF BarkService.cs:261-365)
    // ------------------------------------------------------------------

    [Fact]
    public void RouteOrFallback_RuleBackedTrigger_RoutesThroughEngine_NoFallback()
    {
        var h = new EngineHarness(Rule("r", "ChaosRunStarted"));
        var fellBack = false;

        BarkTriggerRouting.RouteOrFallback(h.Engine, "ChaosRunStarted",
            ctx => ctx.Set("difficulty", "Hard"), () => fellBack = true);

        Assert.False(fellBack);              // engine owned the fire; fallback never ran
        Assert.Single(h.Speaker.Calls);       // rule bark spoke
        Assert.Equal("ChaosRunStarted", h.Speaker.Calls[0].Trigger);
    }

    [Fact]
    public void RouteOrFallback_UnruledTrigger_KeepsFallback_NoEngineFire()
    {
        var h = new EngineHarness(Rule("r", "ChaosRunStarted")); // no rule for "ChaosMystery"
        var fellBack = false;

        BarkTriggerRouting.RouteOrFallback(h.Engine, "ChaosMystery", null, () => fellBack = true);

        Assert.True(fellBack);                // no rule → random-phrase fallback kept (no silent regression)
        Assert.Empty(h.Speaker.Calls);        // engine did not speak → no double-bark
    }

    [Fact]
    public void RouteOrFallback_NullEngine_FallsBack()
    {
        var fellBack = false;
        BarkTriggerRouting.RouteOrFallback(null, "ChaosRunStarted", null, () => fellBack = true);
        Assert.True(fellBack);
    }

    [Fact]
    public void HasTrigger_ReflectsLoadedRules()
    {
        var h = new EngineHarness(Rule("r", "ChaosRunStarted"));
        Assert.True(h.Engine.HasTrigger("ChaosRunStarted"));
        Assert.False(h.Engine.HasTrigger("ChaosMystery"));
    }
}
