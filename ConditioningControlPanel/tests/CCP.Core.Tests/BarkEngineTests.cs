using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Core.Services.Bark;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the BARK-1 slice-1 decision-engine contract ported from WPF
/// Services/Companion/BarkService.cs (contract: docs/bark-engine-contract.md):
/// condition operators + coercion (:1038-1170), the full gate order (:1316-1423),
/// one-shot latches incl. the guaranteed-vs-Safety asymmetry (:1325-1332),
/// variant rotation + recycle (:1388-1420, :1502-1520), CommitFire-in-dry-run
/// (:825-836, :1445-1488), rotation persistence (:1533-1574), idle pool-wide
/// no-repeat (:847-895), and ReloadRules semantics (:406-423).
/// All time-driven gates run on an injected clock; randomness is scripted.
/// </summary>
public class BarkEngineTests
{
    // ------------------------------------------------------------------
    // Fakes
    // ------------------------------------------------------------------

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public int SaveCount;
        public bool WasSettingsFileMissing => true;
        public List<string> PendingPresetReinstalls { get; } = new();
        public void Save() { }
        public void Save(bool suppressCloudBackup = false) => SaveCount++;
        public void SaveImmediate(bool suppressCloudBackup = false) { }
        public void RestoreFrom(AppSettings settings) { }
        public void Reset() { }
    }

    private sealed class FakeSpeaker : IBarkSpeaker
    {
        public readonly List<(string Line, string? Audio, bool Priority, string? Mood, string Trigger)> Calls = new();
        public void Speak(string line, string? audioPath, bool priority, string? mood, BarkContext ctx) =>
            Calls.Add((line, audioPath, priority, mood, ctx.Trigger));
    }

    private sealed class FakeLiveFields : IBarkLiveFields
    {
        public readonly Dictionary<string, object?> Fields = new(StringComparer.OrdinalIgnoreCase);
        public bool TryResolve(string field, out object? value) => Fields.TryGetValue(field, out value);
    }

    private sealed class FakeGateSignals : IBarkGateSignals
    {
        public bool Whisper, Narrator, Speaking, Busy;
        public bool IsWhisperAudioPlaying => Whisper;
        public bool IsNarratorPlaying => Narrator;
        public bool IsAvatarSpeaking => Speaking;
        public bool IsCompanionBusy(int windowMs) => Busy;
    }

    /// <summary>Deterministic Random: NextDouble is a settable constant, Next(max) drains a queue (default 0).</summary>
    private sealed class ScriptedRandom : Random
    {
        public double NextDoubleValue;
        public readonly Queue<int> NextInts = new();
        public override double NextDouble() => NextDoubleValue;
        public override int Next(int maxValue) =>
            NextInts.Count > 0 ? Math.Min(NextInts.Dequeue(), Math.Max(0, maxValue - 1)) : 0;
    }

    private sealed class Harness
    {
        public readonly FakeSettingsService Settings;
        public readonly FakeSpeaker Speaker = new();
        public readonly FakeLiveFields Live = new();
        public readonly FakeGateSignals Gates = new();
        public readonly ScriptedRandom Rng = new();
        public DateTime Now = new(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);
        public readonly BarkEngine Engine;

        public Harness(params BarkRule[] rules) : this(null, rules) { }

        public Harness(FakeSettingsService? settings, params BarkRule[] rules)
        {
            Settings = settings ?? new FakeSettingsService();
            Engine = new BarkEngine(
                Settings, Speaker, Live, Gates,
                mods: null, audioResolver: null,
                ruleLoader: () => new BarkRuleSet(rules),
                logger: null,
                utcNow: () => Now,
                rng: Rng);
            Engine.Start();
            Engine.DryRun = false; // deterministic regardless of CCP_BARK_DRYRUN in the environment
        }

        public void Advance(double ms) => Now = Now.AddMilliseconds(ms);
    }

    private static BarkRule Rule(
        string id, string trigger, string[]? pool = null,
        int priority = 0, int cooldownMs = 0, bool repeatable = true, string scope = "session",
        string cls = "normal", double chance = 1.0, Dictionary<string, object>? conditions = null,
        (string Text, string? Audio)[]? variants = null)
    {
        var vp = variants != null
            ? variants.Select(v => new BarkVariant(v.Text, v.Audio)).ToList()
            : (pool ?? new[] { id + "-line" }).Select(t => new BarkVariant(t)).ToList();
        return new BarkRule
        {
            Id = id, Trigger = trigger, VariantPool = vp, Priority = priority,
            CooldownMs = cooldownMs, Repeatable = repeatable, ScopeRaw = scope,
            ClassRaw = cls, Chance = chance, Conditions = conditions
        };
    }

    // ------------------------------------------------------------------
    // Condition operators + coercion (WPF BarkService.cs:1048-1170)
    // ------------------------------------------------------------------

    [Fact]
    public void Condition_BoolEq_MatchesLiveField()
    {
        var h = new Harness(Rule("r", "T", conditions: new() { ["video_playing"] = true }));
        h.Live.Fields["video_playing"] = false;
        Assert.False(h.Engine.Raise("T"));
        h.Live.Fields["video_playing"] = true;
        Assert.True(h.Engine.Raise("T"));
    }

    [Fact]
    public void Condition_NumericEq_UsesEpsilon()
    {
        var h = new Harness(Rule("r", "T", conditions: new() { ["combo"] = 5 }));
        Assert.False(h.Engine.Raise("T", c => c.Set("combo", 5.001)));
        Assert.True(h.Engine.Raise("T", c => c.Set("combo", 5.00005)));
    }

    [Fact]
    public void Condition_StringEq_IsCaseInsensitive()
    {
        var h = new Harness(Rule("r", "T", conditions: new() { ["difficulty"] = "Hard" }));
        Assert.True(h.Engine.Raise("T", c => c.Set("difficulty", "hard")));
    }

    [Theory]
    // op-suffix parsing + numeric relational table (WPF :1052-1082)
    [InlineData("combo_gte", 5, 5.0, true)]
    [InlineData("combo_gte", 5, 4.9, false)]
    [InlineData("combo_gt", 5, 5.0, false)]
    [InlineData("combo_gt", 5, 5.1, true)]
    [InlineData("combo_lt", 5, 4.0, true)]
    [InlineData("combo_lt", 5, 5.0, false)]
    [InlineData("combo_lte", 5, 5.0, true)]
    [InlineData("combo_lte", 5, 5.1, false)]
    [InlineData("combo_eq", 5, 5.0, true)]
    public void Condition_RelationalOperators(string key, int expected, double actual, bool shouldFire)
    {
        var h = new Harness(Rule("r", "T", conditions: new() { [key] = expected }));
        Assert.Equal(shouldFire, h.Engine.Raise("T", c => c.Set("combo", actual)));
    }

    [Fact]
    public void Condition_NullActual_IsFalse_EvenWhenGuaranteed()
    {
        // Well-known-but-unavailable live field resolves null -> condition false -> no rule match
        // (guaranteed cannot help: it never reaches the gate).
        var h = new Harness(Rule("r", "T", conditions: new() { ["days_away_gte"] = 3 }));
        h.Live.Fields["days_away"] = null; // well-known, currently unavailable
        Assert.False(h.Engine.Raise("T", guaranteed: true));
    }

    [Fact]
    public void Condition_StringCoercion_ToDouble()
    {
        var h = new Harness(Rule("r", "T", conditions: new() { ["combo_gte"] = "3" }));
        Assert.True(h.Engine.Raise("T", c => c.Set("combo", "5")));
    }

    [Fact]
    public void Condition_BoolActual_CoercesToNumericForEq()
    {
        // expected 1 (int) vs actual true -> TryDouble(true)=1 -> within epsilon (WPF :1069-1070)
        var h = new Harness(Rule("r", "T", conditions: new() { ["flag"] = 1 }));
        Assert.True(h.Engine.Raise("T", c => c.Set("flag", true)));
    }

    [Fact]
    public void Condition_LiveField_ShadowsCtxValue()
    {
        // Well-known fields resolve live BEFORE ctx (WPF :1086-1139): a fill-stamped master_volume
        // must not override the live read.
        var h = new Harness(Rule("r", "T", conditions: new() { ["master_volume_gt"] = 50 }));
        h.Live.Fields["master_volume"] = 0d;
        Assert.False(h.Engine.Raise("T", c => c.Set("master_volume", 100)));
    }

    [Fact]
    public void Condition_UnknownField_FallsBackToCtx()
    {
        var h = new Harness(Rule("r", "T", conditions: new() { ["custom_gt"] = 2 }));
        Assert.False(h.Engine.Raise("T")); // absent -> null -> false
        Assert.True(h.Engine.Raise("T", c => c.Set("custom", 3)));
    }

    // ------------------------------------------------------------------
    // Gate order (WPF BarkService.cs:1316-1423)
    // ------------------------------------------------------------------

    [Fact]
    public void Gate_GlobalMinGap_BlocksSecondBark_For60s()
    {
        var h = new Harness(Rule("a", "TA"), Rule("b", "TB"));
        Assert.True(h.Engine.Raise("TA"));
        Assert.False(h.Engine.Raise("TB")); // min-gap
        h.Advance(61_000);
        Assert.True(h.Engine.Raise("TB"));
    }

    [Fact]
    public void Gate_AttentionCheckFail_ExemptFromGlobalGap()
    {
        var h = new Harness(Rule("a", "TA"), Rule("acf", "AttentionCheckFail"));
        Assert.True(h.Engine.Raise("TA"));
        Assert.True(h.Engine.Raise("AttentionCheckFail")); // exempt (WPF :1367)
    }

    [Fact]
    public void Gate_PerRuleCooldown_StillAppliesToExemptTrigger()
    {
        var h = new Harness(Rule("acf", "AttentionCheckFail", cooldownMs: 20_000));
        Assert.True(h.Engine.Raise("AttentionCheckFail"));
        h.Advance(5_000);
        Assert.False(h.Engine.Raise("AttentionCheckFail")); // cooldown (WPF :1374-1380)
        h.Advance(16_000);
        Assert.True(h.Engine.Raise("AttentionCheckFail"));
    }

    [Fact]
    public void Gate_OneShotSession_FiresOnce_NoPersistedLatch()
    {
        var h = new Harness(Rule("once", "T", repeatable: false, scope: "session"));
        Assert.True(h.Engine.Raise("T"));
        h.Advance(61_000);
        Assert.False(h.Engine.Raise("T")); // already-fired (session, in-memory)
        Assert.Empty(h.Settings.Current.BarkLifetimeFired); // session scope never persists (WPF :1463)
    }

    [Fact]
    public void Gate_OneShotLifetime_PersistsLatch_AcrossEngines()
    {
        var settings = new FakeSettingsService();
        var rule = Rule("egg", "T", repeatable: false, scope: "lifetime");
        var h1 = new Harness(settings, rule);
        Assert.True(h1.Engine.Raise("T"));
        Assert.Contains("egg", settings.Current.BarkLifetimeFired); // MarkBarkFired (WPF :1464)

        var h2 = new Harness(settings, Rule("egg", "T", repeatable: false, scope: "lifetime"));
        Assert.False(h2.Engine.Raise("T")); // IsBarkFired latch (WPF :1428-1429)
    }

    [Fact]
    public void Gate_TierScope_LatchKeyCarriesTier()
    {
        var settings = new FakeSettingsService();
        settings.Current.PatreonTier = 1; // Level1
        var h = new Harness(settings, Rule("tier-egg", "T", repeatable: false, scope: "tier"));
        Assert.True(h.Engine.Raise("T"));
        Assert.Contains("tier-egg@Level1", settings.Current.BarkLifetimeFired); // WPF :1433-1437
    }

    [Fact]
    public void Gate_Guaranteed_DoesNotBypassOneShot()
    {
        var h = new Harness(Rule("once", "T", repeatable: false));
        Assert.True(h.Engine.Raise("T"));
        Assert.False(h.Engine.Raise("T", guaranteed: true)); // dedup NOT bypassed (WPF :1325-1332)
    }

    [Fact]
    public void Gate_Safety_BypassesOneShotDedup()
    {
        var h = new Harness(Rule("panic", "Panic", repeatable: false, cls: "safety"));
        Assert.True(h.Engine.Raise("Panic"));
        Assert.True(h.Engine.Raise("Panic")); // Safety exempt from dedup AND floors (WPF :1322, :1331)
    }

    [Fact]
    public void Gate_Guaranteed_BypassesTimingFloors()
    {
        var h = new Harness(Rule("a", "TA"), Rule("b", "TB"));
        Assert.True(h.Engine.Raise("TA"));
        Assert.True(h.Engine.Raise("TB", guaranteed: true)); // min-gap bypassed
    }

    [Fact]
    public void Gate_SafetyHold_BlocksOthersFor6s()
    {
        var h = new Harness(
            Rule("panic", "Panic", cls: "safety"),
            Rule("acf", "AttentionCheckFail")); // gap-exempt so only the hold can block it
        Assert.True(h.Engine.Raise("Panic")); // sets hold (WPF :1456-1457)
        h.Advance(3_000);
        Assert.False(h.Engine.Raise("AttentionCheckFail")); // safety-active (WPF :1337)
        h.Advance(4_000);
        Assert.True(h.Engine.Raise("AttentionCheckFail")); // hold expired
    }

    [Fact]
    public void Gate_WhisperActive_Blocks()
    {
        var h = new Harness(Rule("r", "T"));
        h.Gates.Whisper = true;
        Assert.False(h.Engine.Raise("T")); // WPF :1342
        h.Gates.Whisper = false;
        Assert.True(h.Engine.Raise("T"));
    }

    [Fact]
    public void Gate_NarratorActive_Blocks()
    {
        var h = new Harness(Rule("r", "T"));
        h.Gates.Narrator = true;
        Assert.False(h.Engine.Raise("T")); // WPF :1347
        h.Gates.Narrator = false;
        Assert.True(h.Engine.Raise("T"));
    }

    [Fact]
    public void Gate_ChatSuppression_AfterUserMessage_DefaultWindow()
    {
        var h = new Harness(Rule("r", "T"));
        h.Engine.NotifyUserMessage();
        Assert.False(h.Engine.Raise("T")); // chat-suppressed (WPF :1350-1353, default 10000ms)
        h.Advance(10_001);
        Assert.True(h.Engine.Raise("T"));
    }

    [Fact]
    public void Gate_ChatSuppression_WindowFromSettings()
    {
        var h = new Harness(Rule("r", "T"));
        h.Settings.Current.BarkChatSuppressionMs = 500;
        h.Engine.NotifyUserMessage();
        h.Advance(600);
        Assert.True(h.Engine.Raise("T"));
    }

    [Fact]
    public void Gate_CompanionBusySignal_Blocks()
    {
        var h = new Harness(Rule("r", "T"));
        h.Gates.Busy = true;
        Assert.False(h.Engine.Raise("T")); // WPF :1441
    }

    [Fact]
    public void Gate_AntiStale_NormalBlocked_HighPriorityPreempts()
    {
        var h = new Harness(Rule("low", "TL", priority: 0), Rule("high", "TH", priority: 100));
        h.Gates.Speaking = true;
        Assert.False(h.Engine.Raise("TL")); // queued-class bark dropped mid-bubble (WPF :1360-1362)
        Assert.True(h.Engine.Raise("TH"));  // priority >= 100 preempts
    }

    [Fact]
    public void Gate_AntiStale_NonNormalClassPreempts()
    {
        var h = new Harness(Rule("egg", "T", cls: "easter_egg"));
        h.Gates.Speaking = true;
        Assert.True(h.Engine.Raise("T")); // willPreempt: Class != Normal (WPF :1360)
    }

    [Fact]
    public void Gate_ChanceRoll_BlocksAndPasses()
    {
        var h = new Harness(Rule("r", "T", chance: 0.5));
        h.Rng.NextDoubleValue = 0.9;
        Assert.False(h.Engine.Raise("T")); // roll >= chance -> blocked (WPF :1384)
        h.Rng.NextDoubleValue = 0.4;
        Assert.True(h.Engine.Raise("T"));
    }

    [Fact]
    public void Gate_ChanceRoll_SkippedForSafety()
    {
        // The roll sits INSIDE the !bypass block (WPF :1334-1386) - Safety never rolls.
        var h = new Harness(Rule("panic", "Panic", cls: "safety", chance: 0.0));
        h.Rng.NextDoubleValue = 0.99;
        Assert.True(h.Engine.Raise("Panic"));
    }

    [Fact]
    public void Gate_ChanceRoll_SkippedForGuaranteed()
    {
        var h = new Harness(Rule("r", "T", chance: 0.0));
        h.Rng.NextDoubleValue = 0.99;
        Assert.True(h.Engine.Raise("T", guaranteed: true));
    }

    [Fact]
    public void Gate_EmptyPool_BlocksEvenSafetyAndGuaranteed()
    {
        var rule = Rule("s1", "T", cls: "safety");
        var h = new Harness(rule);
        // Disable the only line via the Phrase Manager surface (WPF ResolvePool :1178-1179).
        h.Settings.Current.DisabledPhraseIds.Add(BarkEngine.BarkLineId("s1", rule.VariantPool![0]));
        Assert.False(h.Engine.Raise("T", guaranteed: true)); // empty-pool is gate 1 (WPF :1318-1319)
    }

    [Fact]
    public void Matcher_PriorityDescending_FirstConditionsPassWins()
    {
        var high = Rule("high", "T", priority: 10, conditions: new() { ["combo_gte"] = 5 });
        var low = Rule("low", "T", priority: 1);

        var h1 = new Harness(high, low);
        Assert.True(h1.Engine.Raise("T", c => c.Set("combo", 3)));
        Assert.Equal("low-line", h1.Speaker.Calls.Single().Line); // high's conditions fail -> low wins

        var h2 = new Harness(high, low);
        Assert.True(h2.Engine.Raise("T", c => c.Set("combo", 6)));
        Assert.Equal("high-line", h2.Speaker.Calls.Single().Line); // priority-descending walk (WPF :791-794)
    }

    // ------------------------------------------------------------------
    // Variant rotation + recycle (WPF BarkService.cs:1388-1420, :1502-1520)
    // ------------------------------------------------------------------

    [Fact]
    public void Rotation_NoRepeatUntilExhausted_ThenRecycleAvoidsImmediateRepeat()
    {
        var h = new Harness(Rule("r", "T", pool: new[] { "v0", "v1", "v2" }));
        for (int i = 0; i < 4; i++)
            Assert.True(h.Engine.Raise("T", guaranteed: true));

        var lines = h.Speaker.Calls.Select(c => c.Line).ToList();
        Assert.Equal(3, lines.Take(3).Distinct().Count());   // no repeat until exhausted
        Assert.NotEqual(lines[2], lines[3]);                 // recycle reseeds last (WPF :1405-1412)
    }

    [Fact]
    public void Rotation_PersistedToSettings_OnCommit()
    {
        var h = new Harness(Rule("r", "T", pool: new[] { "v0", "v1", "v2" }));
        Assert.True(h.Engine.Raise("T"));
        Assert.True(h.Settings.Current.BarkVariantRotation.TryGetValue("r", out var used));
        Assert.Single(used!);                 // one line id recorded (WPF :1467-1477)
        Assert.True(h.Settings.SaveCount > 0); // debounced save requested (WPF :1563)
    }

    [Fact]
    public void Rotation_RestoredOnStart_SkipsAlreadySpokenLines()
    {
        var settings = new FakeSettingsService();
        var rule = Rule("r", "T", pool: new[] { "v0", "v1", "v2" });
        settings.Current.BarkVariantRotation["r"] = new List<string>
        {
            BarkEngine.BarkLineId("r", rule.VariantPool![0]),
            BarkEngine.BarkLineId("r", rule.VariantPool![1]),
        };
        var h = new Harness(settings, rule); // LoadRotationFromSettings on Start (WPF :1538-1553)
        Assert.True(h.Engine.Raise("T"));
        Assert.Equal("v2", h.Speaker.Calls.Single().Line); // only unused line remains
    }

    [Fact]
    public void OneShot_PoolPicksRandomLine()
    {
        var h = new Harness(Rule("r", "T", pool: new[] { "v0", "v1", "v2" }, repeatable: false));
        h.Rng.NextInts.Enqueue(2);
        Assert.True(h.Engine.Raise("T"));
        Assert.Equal("v2", h.Speaker.Calls.Single().Line); // rng.Next(pool.Count) (WPF :1418)
    }

    [Fact]
    public void Rotation_GlobalRecency_PrefersFreshAudio()
    {
        var a = Rule("a", "TA", variants: new[] { ("a-line", (string?)"shared.mp3") });
        var b = Rule("b", "TB", variants: new[]
        {
            ("b1", (string?)"shared.mp3"),
            ("b2", (string?)"other.mp3"),
        });
        var h = new Harness(a, b);
        Assert.True(h.Engine.Raise("TA", guaranteed: true)); // RememberSpoken("shared.mp3") (WPF :1452)
        Assert.True(h.Engine.Raise("TB", guaranteed: true));
        Assert.Equal("b2", h.Speaker.Calls[1].Line); // soft global de-dupe prefers fresh audio (WPF :1511-1518)
    }

    // ------------------------------------------------------------------
    // Dry-run: CommitFire advances state, speak + persistence suppressed
    // (WPF BarkService.cs:825-836, :1463, :1558, :1569)
    // ------------------------------------------------------------------

    [Fact]
    public void DryRun_ReturnsFalse_SuppressesSpeakAndPersistence_ButAdvancesState()
    {
        var h = new Harness(Rule("egg", "T", repeatable: false, scope: "lifetime"));
        h.Engine.DryRun = true;

        Assert.False(h.Engine.Raise("T")); // decision fired, dry-run returns (null,-1,null) (WPF :835)
        Assert.Empty(h.Speaker.Calls);
        Assert.Empty(h.Settings.Current.BarkLifetimeFired);   // MarkBarkFired suppressed (WPF :1463)
        Assert.Empty(h.Settings.Current.BarkVariantRotation); // PersistVariantRotation suppressed (WPF :1558)
        Assert.Equal(0, h.Settings.SaveCount);

        // In-memory session latch DID advance: the one-shot cannot fire again even off dry-run.
        h.Engine.DryRun = false;
        h.Advance(61_000);
        Assert.False(h.Engine.Raise("T"));
    }

    [Fact]
    public void DryRun_AdvancesGlobalGap()
    {
        var h = new Harness(Rule("a", "TA"), Rule("b", "TB"));
        h.Engine.DryRun = true;
        Assert.False(h.Engine.Raise("TA")); // dry decision still stamps _globalLastFireUtc (WPF :1449)
        h.Engine.DryRun = false;
        Assert.False(h.Engine.Raise("TB")); // min-gap from the dry fire
        h.Advance(61_000);
        Assert.True(h.Engine.Raise("TB"));
    }

    // ------------------------------------------------------------------
    // ReloadRules (WPF BarkService.cs:406-423)
    // ------------------------------------------------------------------

    [Fact]
    public void Reload_ClearsSessionOneShots()
    {
        var h = new Harness(Rule("once", "T", repeatable: false));
        Assert.True(h.Engine.Raise("T"));
        h.Engine.ReloadRules();
        Assert.True(h.Engine.Raise("T", guaranteed: true)); // dedup cleared; guaranteed skips min-gap
    }

    [Fact]
    public void Reload_PreservesLifetimeLatch()
    {
        var h = new Harness(Rule("egg", "T", repeatable: false, scope: "lifetime"));
        Assert.True(h.Engine.Raise("T"));
        h.Engine.ReloadRules();
        Assert.False(h.Engine.Raise("T", guaranteed: true)); // persisted latch survives (WPF :404)
    }

    [Fact]
    public void Reload_ClearsPerRuleCooldown()
    {
        var h = new Harness(Rule("acf", "AttentionCheckFail", cooldownMs: 60_000));
        Assert.True(h.Engine.Raise("AttentionCheckFail"));
        h.Engine.ReloadRules();
        Assert.True(h.Engine.Raise("AttentionCheckFail")); // _lastFiredUtc cleared (WPF :415)
    }

    [Fact]
    public void Reload_PreservesVariantRotation()
    {
        var h = new Harness(Rule("r", "T", pool: new[] { "v0", "v1", "v2" }));
        Assert.True(h.Engine.Raise("T", guaranteed: true));
        var first = h.Speaker.Calls[0].Line;
        h.Engine.ReloadRules();
        Assert.True(h.Engine.Raise("T", guaranteed: true));
        Assert.NotEqual(first, h.Speaker.Calls[1].Line); // rotation NOT cleared (WPF :416-418)
    }

    [Fact]
    public void Reload_DoesNotClearGlobalGap()
    {
        var h = new Harness(Rule("a", "TA"), Rule("b", "TB"));
        Assert.True(h.Engine.Raise("TA"));
        h.Engine.ReloadRules();
        Assert.False(h.Engine.Raise("TB")); // _globalLastFireUtc untouched by reload
    }

    // ------------------------------------------------------------------
    // Idle dispatch (WPF BarkService.cs:847-895)
    // ------------------------------------------------------------------

    private static BarkRule[] IdleRules() => new[]
    {
        Rule("idle1", "Idle", pool: new[] { "i1" }),
        Rule("idle2", "Idle", pool: new[] { "i2" }),
        Rule("idle3", "Idle", pool: new[] { "i3" }),
    };

    [Fact]
    public void Idle_PoolWideNoRepeat_AndPersistedRotation()
    {
        var h = new Harness(IdleRules());
        h.Settings.Current.MasterVolume = 100;
        for (int i = 0; i < 3; i++)
        {
            h.Engine.DispatchIdle();
            h.Advance(61_000); // clear the global min-gap between idle ticks
        }
        Assert.Equal(3, h.Speaker.Calls.Select(c => c.Line).Distinct().Count()); // no repeat (WPF :881)
        Assert.Equal(3, h.Settings.Current.BarkIdleRotation.Count);              // persisted (WPF :1486)
    }

    [Fact]
    public void Idle_RecycleAvoidsImmediateRepeat()
    {
        var h = new Harness(IdleRules());
        h.Settings.Current.MasterVolume = 100;
        for (int i = 0; i < 4; i++)
        {
            h.Engine.DispatchIdle();
            h.Advance(61_000);
        }
        Assert.Equal(4, h.Speaker.Calls.Count);
        Assert.NotEqual(h.Speaker.Calls[2].Line, h.Speaker.Calls[3].Line); // reseed last (WPF :884-885)
    }

    [Fact]
    public void Idle_SkipsWhenMuted()
    {
        var h = new Harness(IdleRules());
        h.Settings.Current.MasterVolume = 0;
        h.Engine.DispatchIdle();
        Assert.Empty(h.Speaker.Calls); // WPF :852
    }

    [Fact]
    public void Idle_SkipsWhileAvatarSpeaking()
    {
        var h = new Harness(IdleRules());
        h.Settings.Current.MasterVolume = 100;
        h.Gates.Speaking = true;
        h.Engine.DispatchIdle();
        Assert.Empty(h.Speaker.Calls); // WPF :853
    }

    [Fact]
    public void Idle_GatedBias_PrefersEligibleGatedRule()
    {
        var gated = Rule("idle-gated", "Idle", pool: new[] { "gated" },
            conditions: new() { ["video_playing"] = true });
        var h = new Harness(Rule("idle-a", "Idle", pool: new[] { "a" }),
                            Rule("idle-b", "Idle", pool: new[] { "b" }), gated);
        h.Settings.Current.MasterVolume = 100;
        h.Live.Fields["video_playing"] = true;
        h.Rng.NextDoubleValue = 0.1; // < GatedIdleBias 0.35 (WPF :892)
        h.Engine.DispatchIdle();
        Assert.Equal("gated", h.Speaker.Calls.Single().Line);
    }

    // ------------------------------------------------------------------
    // Speak seam arguments (WPF BarkService.cs:1578-1624, :1631-1649)
    // ------------------------------------------------------------------

    [Fact]
    public void Speaker_ReceivesSubstitutedLine()
    {
        var h = new Harness(Rule("r", "T", pool: new[] { "Hello {name}~" }));
        Assert.True(h.Engine.Raise("T", c => c.Set("name", "Bambi")));
        Assert.Equal("Hello Bambi~", h.Speaker.Calls.Single().Line); // {key} substitution (WPF :1643-1648)
    }

    [Theory]
    [InlineData("normal", 0, false)]
    [InlineData("normal", 100, true)]
    [InlineData("easter_egg", 0, true)]
    [InlineData("safety", 0, true)]
    public void Speaker_PriorityFlag_FollowsClassAndThreshold(string cls, int priority, bool expectPriority)
    {
        var h = new Harness(Rule("r", "T", cls: cls, priority: priority));
        Assert.True(h.Engine.Raise("T"));
        Assert.Equal(expectPriority, h.Speaker.Calls.Single().Priority); // WPF :1619
    }

    [Fact]
    public void Speaker_MoodAndTrigger_ArePassedThrough()
    {
        var rule = Rule("r", "T");
        rule.Mood = "smug";
        var h = new Harness(rule);
        Assert.True(h.Engine.Raise("T"));
        var call = h.Speaker.Calls.Single();
        Assert.Equal("smug", call.Mood);
        Assert.Equal("T", call.Trigger);
    }

    [Fact]
    public void BlankLineAfterSubstitution_SkipsSpeaker_ButRaiseReturnsTrue()
    {
        var h = new Harness(Rule("r", "T", pool: new[] { "{name}" }));
        Assert.True(h.Engine.Raise("T", c => c.Set("name", ""))); // WPF returns true at :808
        Assert.Empty(h.Speaker.Calls);                            // Speak early-returns on blank (WPF :1590)
    }

    // ------------------------------------------------------------------
    // Line ids + slug (WPF BarkService.cs:1197-1203; CompanionPhraseService.cs:84-95)
    // ------------------------------------------------------------------

    [Fact]
    public void BarkLineId_AudioKeyed_DropsExtension()
    {
        Assert.Equal("Bark:r1:flash_12", BarkEngine.BarkLineId("r1", new BarkVariant("x", "flash_12.mp3")));
    }

    [Fact]
    public void BarkLineId_TextOnly_UsesSlug()
    {
        Assert.Equal("Bark:r1:t_level up good girl",
            BarkEngine.BarkLineId("r1", new BarkVariant("LEVEL UP! Good girl!~")));
    }

    [Theory]
    [InlineData("*giggles* You clicked it~", "giggles you clicked it")]
    [InlineData("{token} test", "test")]
    [InlineData("", "")]
    public void Slugify_MatchesWpfCanonicalForm(string text, string expected)
    {
        Assert.Equal(expected, BarkEngine.Slugify(text));
    }

    // ------------------------------------------------------------------
    // Misc pipeline
    // ------------------------------------------------------------------

    [Fact]
    public void Raise_UnknownTrigger_ReturnsFalse()
    {
        var h = new Harness(Rule("r", "T"));
        Assert.False(h.Engine.Raise("Nope"));
    }

    [Fact]
    public void Raise_NoConditionMatch_DoesNotAdvanceState()
    {
        var h = new Harness(Rule("r", "T", conditions: new() { ["combo_gte"] = 5 }));
        Assert.False(h.Engine.Raise("T", c => c.Set("combo", 1)));
        Assert.True(h.Engine.Raise("T", c => c.Set("combo", 9))); // no gap stamped by the miss
    }

    [Fact]
    public void RuleCount_ReflectsLoadedRules()
    {
        var h = new Harness(Rule("a", "TA"), Rule("b", "TB"));
        Assert.Equal(2, h.Engine.RuleCount);
    }
}
