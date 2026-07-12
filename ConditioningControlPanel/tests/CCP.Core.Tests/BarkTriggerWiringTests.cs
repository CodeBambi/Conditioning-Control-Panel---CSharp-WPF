using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Awareness;
using ConditioningControlPanel.Core.Services.Bark;
using ConditioningControlPanel.Core.Services.BlinkTrainer;
using ConditioningControlPanel.Core.Services.Mantra;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Video;
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

    // ------------------------------------------------------------------
    // BARK-2: remaining contract-E triggers. Each rule is condition-keyed so it fires ONLY when the
    // wiring stamped the contract-E ctx var correctly (proof the var was set, not just the trigger).
    // ------------------------------------------------------------------

    private sealed class FakeBlinkTrainer : IBlinkTrainerService
    {
        public bool IsRunning { get; set; }
        public string LastError { get; } = "";
        public TimeSpan Remaining { get; } = TimeSpan.Zero;
        public bool Start() => true;
        public void Stop() { }
        public event Action? StateChanged;
        public void RaiseStateChanged(bool running) { IsRunning = running; StateChanged?.Invoke(); }
    }

    private sealed class FakeMantra : IMantraService
    {
        public string? CurrentMantra { get; } = null;
        public int Streak { get; set; }
        public int BestStreak { get; set; }
        public int Completions { get; set; }
        public int TargetCount { get; set; }
        public bool IsActive { get; set; }
        public event Action<int>? StreakChanged;
        public event Action? StreakBroken;
        public event Action? MantraCompleted;
        public event Action<int, int>? SessionComplete;
        public void StartSession(int targetReps) { }
        public bool TryCompleteMantra() => true;
        public void BreakStreak() { }
        public void EndSession() { }
        public void RaiseStreak(int s) => StreakChanged?.Invoke(s);
        public void RaiseCompleted() => MantraCompleted?.Invoke();
    }

    private sealed class FakeLockdown : ILockdownService
    {
        public bool IsActive { get; set; }
        public TimeSpan Remaining { get; set; }
        public TimeSpan LastActiveDuration { get; set; }
        public event Action? LockdownActivated;
        public event Action? LockdownDeactivated;
        public event Action<TimeSpan>? CountdownTick;
        public void Activate(TimeSpan duration) { }
        public void Deactivate() { }
        public bool TryExitWithPhrase(string phrase) => false;
        public void RecoverIfNeeded() { }
        public void RaiseTick(TimeSpan remaining) => CountdownTick?.Invoke(remaining);
        public void RaiseActivated() => LockdownActivated?.Invoke();
        public void Dispose() { }
    }

    private sealed class FakeAttentionCheck : IAttentionCheckService
    {
        public bool IsRunning { get; set; }
        public event Action? OnPass;
        public event Action? OnFail;
        public void Start() { }
        public void Stop() { }
        public void FireNow() { }
        public void RaiseFail() => OnFail?.Invoke();
        public void RaisePass() => OnPass?.Invoke();
    }

    private sealed class FakeVideo : IVideoService
    {
        public bool IsRunning => false;
        public bool IsPlaying => false;
        public string? LastVideoPath => null;
        public event EventHandler? VideoAboutToStart;
        public event EventHandler? VideoStarted;
        public event EventHandler? VideoEnded;
        public void Start() { }
        public void Stop() { }
        public void RefreshVideosPath() { }
        public void PlaySpecificVideo(string videoPath, bool strictMode) { }
        public void PlayRandomVideo() { }
        public void PlayUrl(string url) { }
        public void TriggerVideo() { }
        public void ForceCleanup() { }
        public void UpdateVolume() { }
        public void RaiseAboutToStart() => VideoAboutToStart?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public void VideoAboutToStart_RaisesTrigger()
    {
        // BARK-2 deviation fix: the WPF head wires VideoAboutToStart bare (BarkService.cs:467-468), but
        // BARK-2 had wired only VideoStarted/VideoEnded and dropped VideoAboutToStart. Verify the
        // now-added wiring raises the trigger (bare, no ctx) exactly like WPF.
        var h = new EngineHarness(Rule("v", "VideoAboutToStart"));
        var video = new FakeVideo();
        using var wiring = new BarkTriggerWiring(h.Engine, video: video);
        wiring.Start();

        video.RaiseAboutToStart();
        Assert.Single(h.Speaker.Calls);
        Assert.Equal("VideoAboutToStart", h.Speaker.Calls[0].Trigger);
    }

    [Fact]
    public void BlinkTrainerStateChanged_StampsRunningCtx()
    {
        var h = new EngineHarness(Rule("b", "BlinkTrainerStateChanged", new() { ["running"] = true }));
        var bt = new FakeBlinkTrainer();
        using var wiring = new BarkTriggerWiring(h.Engine, blinkTrainer: bt);
        wiring.Start();

        bt.RaiseStateChanged(true);   // running matches → fires
        Assert.Single(h.Speaker.Calls);
        Assert.Equal("BlinkTrainerStateChanged", h.Speaker.Calls[0].Trigger);
    }

    [Fact]
    public void MantraStreakChanged_StampsStreakCtx()
    {
        var h = new EngineHarness(Rule("s", "StreakChanged", new() { ["streak_gte"] = 3.0 }));
        var m = new FakeMantra();
        using var wiring = new BarkTriggerWiring(h.Engine, mantra: m);
        wiring.Start();

        m.RaiseStreak(5);   // 5 >= 3 → fires
        Assert.Single(h.Speaker.Calls);
        Assert.Equal("StreakChanged", h.Speaker.Calls[0].Trigger);
    }

    [Fact]
    public void MantraCompleted_RaisesTrigger()
    {
        var h = new EngineHarness(Rule("m", "MantraCompleted"));
        var m = new FakeMantra();
        using var wiring = new BarkTriggerWiring(h.Engine, mantra: m);
        wiring.Start();

        m.RaiseCompleted();
        Assert.Single(h.Speaker.Calls);
        Assert.Equal("MantraCompleted", h.Speaker.Calls[0].Trigger);
    }

    [Fact]
    public void LockdownCountdownTick_StampsRemainingSecCtx()
    {
        var h = new EngineHarness(Rule("l", "LockdownCountdownTick", new() { ["remaining_sec_lte"] = 30.0 }));
        var ld = new FakeLockdown();
        using var wiring = new BarkTriggerWiring(h.Engine, lockdown: ld);
        wiring.Start();

        ld.RaiseTick(TimeSpan.FromSeconds(10));   // 10 <= 30 → fires
        Assert.Single(h.Speaker.Calls);
        Assert.Equal("LockdownCountdownTick", h.Speaker.Calls[0].Trigger);
    }

    [Fact]
    public void LockdownActivated_RaisesTrigger()
    {
        var h = new EngineHarness(Rule("l", "LockdownActivated"));
        var ld = new FakeLockdown();
        using var wiring = new BarkTriggerWiring(h.Engine, lockdown: ld);
        wiring.Start();

        ld.RaiseActivated();
        Assert.Single(h.Speaker.Calls);
        Assert.Equal("LockdownActivated", h.Speaker.Calls[0].Trigger);
    }

    [Fact]
    public void AttentionCheckFail_RaisesExemptedTrigger()
    {
        // AttentionCheckFail is the global-gap exemption (engine skips the 60s min-gap by trigger name).
        var h = new EngineHarness(Rule("a", "AttentionCheckFail"));
        var ac = new FakeAttentionCheck();
        using var wiring = new BarkTriggerWiring(h.Engine, attentionCheck: ac);
        wiring.Start();

        ac.RaiseFail();
        Assert.Single(h.Speaker.Calls);
        Assert.Equal("AttentionCheckFail", h.Speaker.Calls[0].Trigger);
    }

    [Fact]
    public void NotifyUserMessage_RaisesUserMessageSentTrigger()
    {
        // The MUST requirement: the chat send path calls NotifyUserMessage so the chat-suppression
        // window opens AND the UserMessageSent trigger fires (WPF BarkService.cs:427).
        var h = new EngineHarness(Rule("u", "UserMessageSent"));
        using var wiring = new BarkTriggerWiring(h.Engine);
        wiring.Start();

        wiring.NotifyUserMessage();
        Assert.Single(h.Speaker.Calls);
        Assert.Equal("UserMessageSent", h.Speaker.Calls[0].Trigger);
    }

    [Fact]
    public void Dispose_UnsubscribesBark2Sources_NoBarkAfterShutdown()
    {
        var h = new EngineHarness(Rule("l", "LockdownCountdownTick", new() { ["remaining_sec_lte"] = 30.0 }));
        var ld = new FakeLockdown();
        var wiring = new BarkTriggerWiring(h.Engine, lockdown: ld);
        wiring.Start();
        wiring.Dispose();

        ld.RaiseTick(TimeSpan.FromSeconds(10));
        Assert.Empty(h.Speaker.Calls); // unsubscribed → no bark after shutdown (no leak)
    }
}
