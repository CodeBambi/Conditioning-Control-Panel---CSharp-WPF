using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Settings;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Coverage for the portable <see cref="DtrhHostOrchestrator"/> (slice S2c-1 of the DTRH web-game
/// port): the inbound page-message router, the XP formula + cap, the run-ended ordering invariants,
/// input clamps, the queue-until-ready outbound buffer, and teardown. Drives the orchestrator through
/// its public <c>HandleMessage</c> against in-memory fakes so assertions land on the posted messages
/// and the recorded native calls. All facts mirror the WPF <c>DtrhHostService</c> contract with
/// cited line numbers in <see cref="DtrhHostOrchestrator"/>.
/// </summary>
public class DtrhHostOrchestratorTests
{
    // ---- in-memory test doubles ------------------------------------------------------------------

    private sealed class FakeBrowserHost : IBrowserHost
    {
        public readonly List<string> Posts = new();
        public List<string>? Log;   // shared ordered cross-fake log for invariant assertions

        public event EventHandler<string>? WebMessageReceived;
        public event EventHandler<string>? TitleChanged;
        public event EventHandler<Uri>? Navigated;

        public Task NavigateAsync(Uri url) => Task.CompletedTask;
        public Task<string> ExecuteScriptAsync(string script) => Task.FromResult(string.Empty);

        public void PostWebMessageAsJson(string json)
        {
            Posts.Add(json);
            if (Log != null)
            {
                if (json.Contains("\"type\":\"meta\"")) Log.Add("rebroadcast");
                else if (json.Contains("\"type\":\"payout-result\"")) Log.Add("payout");
            }
        }

        public void Raise(string json) => WebMessageReceived?.Invoke(this, json);

        public IReadOnlyList<JObject> PostedOfType(string type) => Posts
            .Select(p => { try { return JObject.Parse(p); } catch { return null; } })
            .Where(o => o != null && (string?)o!["type"] == type)
            .Select(o => o!)
            .ToList();

        // touch the unused events so the analyzer stays quiet
        public void Unused() { TitleChanged?.Invoke(this, ""); Navigated?.Invoke(this, new Uri("about:blank")); }
    }

    private sealed class FakeEffects : IDtrhNativeEffects
    {
        public List<string>? Log;
        public string RunConfigJson = "{\"difficulty\":\"Easy\"}";
        public bool? LastScripted;
        public int SfxCount;
        public string? LastFireKind;
        public int LastFireStrength;
        public double LastFireDurationMult;
        public int FireCount;
        public int RestoreMainWindowCount;
        public int NotifyRunCompletedCount;
        public List<bool> HostFullscreenCalls = new();

        public void PlaySfx(string name, float scale) => SfxCount++;
        public void FirePayload(string kind, int strength, double durationMult)
        {
            FireCount++; LastFireKind = kind; LastFireStrength = strength; LastFireDurationMult = durationMult;
        }
        public void SetWorldFrozen(bool frozen) => Log?.Add(frozen ? "freeze-on" : "freeze-off");
        public void ReclaimBrowserFocus() { }
        public void SetHostFullscreen(bool on) => HostFullscreenCalls.Add(on);
        public void RouteBark(string barkJson) => Log?.Add("bark");
        public void NotifyRunStarted(string difficulty) { }
        public void NotifyRunCompleted(int finalXp, string difficulty) => NotifyRunCompletedCount++;
        public void SyncReveals(string reason) => Log?.Add("sync");
        public string BuildRunConfigJson(bool scripted) { LastScripted = scripted; return RunConfigJson; }
        public string ActiveModId() => "builtin-test";
        public void RestoreMainWindow() => RestoreMainWindowCount++;
    }

    private sealed class FakeChaosMetaStore : IChaosMetaStore
    {
        public ChaosMetaState State { get; set; } = new();
        public int RankIndex { get; set; }
        public int SaveCount;
        public void Save() => SaveCount++;
    }

    private sealed class FakeProgression : IProgressionService
    {
        public int TotalAdded;
        public XPSource? LastSource;
        public void AddXP(int amount, XPSource source) { TotalAdded += amount; LastSource = source; }
        public double GetSessionXPMultiplier(int playerLevel) => 1.0;
        public double GetXPForLevel(int level) => 0;
        public double GetTotalXP(int level, double currentXP) => 0;
        public double GetCurrentLevelXP(int level, double totalXP) => 0;
        public event EventHandler<int>? LevelUp { add { } remove { } }
    }

    private sealed class FakeSkillTree : ISkillTreeService
    {
        public double Multiplier = 1.0;
        public bool HasSkill(string skillId) => false;
        public double GetTotalXpMultiplier() => Multiplier;
        public int TotalPointsSpent => 0;
        public event EventHandler<string>? SkillUnlocked { add { } remove { } }
        public event EventHandler? PinkRushStarted { add { } remove { } }
        public Task<(bool Success, string? Error)> PurchaseSkillAsync(string skillId) => Task.FromResult((false, (string?)null));
        public void Start() { }
        public void Stop() { }
        public void TriggerPinkRush() { }
        public bool UseStreakShield() => false;
        public bool UseOopsieInsurance() => false;
        public int GetDailyStreakBonus(int consecutiveDays) => 0;
        public int GetDailyFreeRerolls() => 0;
        public void AddConditioningTime(double minutes) { }
    }

    private sealed class FakeSettingsService : ISettingsService
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

    private sealed class TestAppEnvironment : IAppEnvironment
    {
        public string Root { get; }
        public string BaseDirectory { get; } = AppContext.BaseDirectory;
        public string UserDataPath { get; }
        public string ApplicationDataPath { get; }
        public string EffectiveAssetsPath { get; }

        public TestAppEnvironment()
        {
            Root = Path.Combine(Path.GetTempPath(), $"ccp-dtrhorch-{Guid.NewGuid():N}");
            UserDataPath = Path.Combine(Root, "local");
            ApplicationDataPath = Path.Combine(Root, "roaming");
            EffectiveAssetsPath = Path.Combine(Root, "assets");
            Directory.CreateDirectory(UserDataPath);
        }
    }

    // ---- harness ---------------------------------------------------------------------------------

    private sealed class Harness
    {
        public FakeBrowserHost Browser = new();
        public FakeEffects Fx = new();
        public FakeChaosMetaStore Store = new();
        public FakeProgression Progression = new();
        public FakeSkillTree SkillTree = new();
        public FakeSettingsService Settings = new();
        public TestAppEnvironment Env = new();
        public DtrhHostOrchestrator Orch = null!;

        public Harness Build(bool testMode = false, bool withLog = false)
        {
            if (withLog) { var log = new List<string>(); Browser.Log = log; Fx.Log = log; }
            var manifest = new DtrhAssetManifest(Env, NullLogger<DtrhAssetManifest>.Instance);
            var assetStats = new DtrhAssetStatsStore(Env, NullLogger<DtrhAssetStatsStore>.Instance);
            var sessionStats = new DtrhSessionStatsStore(Env, NullLogger<DtrhSessionStatsStore>.Instance);
            var sentinel = new ChaosCrashSentinel(Env, NullLogger<ChaosCrashSentinel>.Instance);
            Orch = new DtrhHostOrchestrator(
                Browser, Fx, Store, manifest, assetStats, sessionStats, Env,
                NullLogger<DtrhHostOrchestrator>.Instance, NullLogger<DtrhMetaBridge>.Instance,
                Progression, SkillTree, null, null, sentinel, Settings, bark: null, testMode: testMode);
            return this;
        }

        public List<string> Log => Browser.Log!;
    }

    private static string Msg(object o) => JsonConvert.SerializeObject(o);

    // ---- ready / outbound queue ------------------------------------------------------------------

    [Fact]
    public void Ready_PostsInitMetaAndManifest()
    {
        var h = new Harness().Build();
        h.Orch.HandleMessage(Msg(new { type = "ready" }));

        Assert.Single(h.Browser.PostedOfType("init"));
        Assert.Single(h.Browser.PostedOfType("meta"));
        Assert.Single(h.Browser.PostedOfType("manifest"));
        var init = h.Browser.PostedOfType("init")[0];
        Assert.Equal(1, (int)init["protocol"]!);
        Assert.Equal("builtin-test", (string?)init["modId"]);
    }

    [Fact]
    public void Post_IsQueuedUntilReady_ThenFlushed()
    {
        var h = new Harness().Build();
        h.Orch.Post(new { type = "ping" });
        Assert.Empty(h.Browser.Posts);   // queued, page not ready

        h.Orch.HandleMessage(Msg(new { type = "ready" }));
        Assert.Contains(h.Browser.Posts, p => p.Contains("\"type\":\"ping\""));
    }

    // ---- router ----------------------------------------------------------------------------------

    [Fact]
    public void VnSpeaking_GatesSfx()
    {
        var h = new Harness().Build();
        h.Orch.HandleMessage(Msg(new { type = "vn-speaking", on = true }));
        h.Orch.HandleMessage(Msg(new { type = "sfx", name = "ripple_cast", scale = 0.7 }));
        Assert.Equal(0, h.Fx.SfxCount);

        h.Orch.HandleMessage(Msg(new { type = "vn-speaking", on = false }));
        h.Orch.HandleMessage(Msg(new { type = "sfx", name = "ripple_cast", scale = 0.7 }));
        Assert.Equal(1, h.Fx.SfxCount);
    }

    [Fact]
    public void FirePayload_ClampsStrengthAndDuration()
    {
        var h = new Harness().Build();
        h.Orch.HandleMessage(Msg(new { type = "run-started", difficulty = "Gentle" }));
        h.Orch.HandleMessage(Msg(new { type = "fire-payload", kind = "audio", strength = 250, durationMult = 99.0 }));
        Assert.Equal(1, h.Fx.FireCount);
        Assert.Equal("audio", h.Fx.LastFireKind);
        Assert.Equal(100, h.Fx.LastFireStrength);           // clamped 0..100
        Assert.Equal(10.0, h.Fx.LastFireDurationMult, 3);   // clamped 0.1..10
    }

    [Fact]
    public void FirePayload_UnknownKind_IsIgnored()
    {
        var h = new Harness().Build();
        h.Orch.HandleMessage(Msg(new { type = "fire-payload", kind = "visual", strength = 50 }));
        Assert.Equal(0, h.Fx.FireCount);
    }

    [Fact]
    public void MetaCommand_RoutedToBridge()
    {
        var h = new Harness().Build();
        // add-gold is a valid bridge op; a successful handle bumps Rev.
        int before = h.Orch.Meta.Rev;
        h.Orch.HandleMessage(Msg(new { type = "meta-command", op = "add-gold", amount = 50 }));
        Assert.True(h.Orch.Meta.Rev > before);
    }

    [Fact]
    public void FreezeState_DedupsTransitions()
    {
        var h = new Harness().Build(withLog: true);
        h.Orch.HandleMessage(Msg(new { type = "freeze-state", on = true }));
        h.Orch.HandleMessage(Msg(new { type = "freeze-state", on = true }));   // dedup — no second call
        Assert.Equal(1, h.Log.Count(x => x == "freeze-on"));
    }

    // ---- XP + run-ended ordering -----------------------------------------------------------------

    [Fact]
    public void RunEnded_AppliesXpCapAndSkillMultiplier()
    {
        var h = new Harness();
        h.SkillTree.Multiplier = 1.5;
        h.Build();
        h.Orch.HandleMessage(Msg(new { type = "ready" }));
        h.Orch.HandleMessage(Msg(new { type = "run-started", difficulty = "Gentle" }));
        // score 1000 exceeds cap 250 * (60/60) * 2.0 = 500 -> baseXp = 500, finalXp = 750.
        h.Orch.HandleMessage(Msg(new { type = "run-ended", score = 1000.0, durationSec = 60.0, difficultyMult = 2.0, difficulty = "Gentle" }));

        var payout = Assert.Single(h.Browser.PostedOfType("payout-result"));
        Assert.Equal(500.0, (double)payout["baseXp"]!, 3);
        Assert.Equal(1.5, (double)payout["skillMult"]!, 3);
        Assert.Equal(750.0, (double)payout["finalXp"]!, 3);
        Assert.Equal(500, h.Progression.TotalAdded);        // banks baseXp (int), not finalXp
        Assert.Equal(XPSource.Chaos, h.Progression.LastSource);
        Assert.Equal(1, h.Fx.NotifyRunCompletedCount);
    }

    [Fact]
    public void RunEnded_Order_SyncBeforeRebroadcastBeforePayout()
    {
        var h = new Harness().Build(withLog: true);
        h.Orch.HandleMessage(Msg(new { type = "ready" }));
        h.Orch.HandleMessage(Msg(new { type = "run-started", difficulty = "Gentle" }));
        h.Log.Clear();   // drop the init-time meta snapshot so IndexOf sees only run-end order
        h.Orch.HandleMessage(Msg(new { type = "run-ended", score = 10.0, durationSec = 60.0, difficultyMult = 1.0, difficulty = "Gentle" }));

        // AwardRun also broadcasts a meta snapshot BEFORE SyncReveals; the invariant is that the
        // post-reveal Rebroadcast lands AFTER SyncReveals and BEFORE the payout reply (so the page
        // sees fresh pendingReveals). Assert a rebroadcast exists in the (sync, payout) window.
        int sync = h.Log.IndexOf("sync");
        int payout = h.Log.IndexOf("payout");
        Assert.True(sync >= 0 && payout >= 0, $"log: {string.Join(",", h.Log)}");
        Assert.True(sync < payout, "SyncReveals must precede payout-result");
        bool rebroadcastAfterSync = false;
        for (int i = sync + 1; i < payout; i++) if (h.Log[i] == "rebroadcast") rebroadcastAfterSync = true;
        Assert.True(rebroadcastAfterSync, $"a meta rebroadcast must land after SyncReveals and before payout; log: {string.Join(",", h.Log)}");
    }

    [Fact]
    public void RunEnded_ResumesWorldFreeze()
    {
        var h = new Harness().Build(withLog: true);
        h.Orch.HandleMessage(Msg(new { type = "run-started", difficulty = "Gentle" }));
        h.Orch.HandleMessage(Msg(new { type = "freeze-state", on = true }));
        h.Orch.HandleMessage(Msg(new { type = "run-ended", score = 10.0, durationSec = 60.0, difficultyMult = 1.0, difficulty = "Gentle" }));
        Assert.Contains("freeze-off", h.Log);
    }

    [Fact]
    public void RunEnded_TestMode_SkipsProgressionAndReveals()
    {
        var h = new Harness().Build(testMode: true, withLog: true);
        h.Orch.HandleMessage(Msg(new { type = "ready" }));
        h.Orch.HandleMessage(Msg(new { type = "run-started", difficulty = "Gentle" }));
        h.Orch.HandleMessage(Msg(new { type = "run-ended", score = 100.0, durationSec = 60.0, difficultyMult = 1.0, difficulty = "Gentle" }));
        Assert.Equal(0, h.Progression.TotalAdded);
        Assert.DoesNotContain("sync", h.Log);
        var payout = Assert.Single(h.Browser.PostedOfType("payout-result"));
        Assert.True((bool)payout["dryRun"]!);
    }

    // ---- request-run deal ------------------------------------------------------------------------

    [Fact]
    public void RequestRun_SpendsForceScriptedRunAtDeal()
    {
        var h = new Harness();
        h.Store.State.ForceScriptedRun = true;
        h.Store.State.RunsCompleted = 5;
        h.Build();
        h.Orch.HandleMessage(Msg(new { type = "ready" }));
        h.Orch.HandleMessage(Msg(new { type = "request-run" }));

        Assert.True(h.Fx.LastScripted);                        // force => scripted deal
        Assert.False(h.Store.State.ForceScriptedRun);          // spent at deal time
        Assert.True(h.Store.SaveCount >= 1);
        Assert.Single(h.Browser.PostedOfType("run-config"));
    }

    [Fact]
    public void PersistRunSetup_ClampsDuration()
    {
        var h = new Harness().Build();
        h.Orch.HandleMessage(Msg(new { type = "request-run", setup = new { durationSec = 5000, waveCount = 99 } }));
        // PersistRunSetup clamps 5000->1200; the AppSettings setter then further caps to its own
        // [60,900] range, so the effective stored value is 900. Wave clamps 99->12.
        Assert.Equal(900, h.Settings.Current.ChaosRunDurationSec);
        Assert.Equal(12, h.Settings.Current.ChaosWaveCount);
    }

    // ---- teardown --------------------------------------------------------------------------------

    [Fact]
    public void FullscreenSet_InvokesSeamAndEchoesWpfShape()
    {
        // WPF ApplyHostFullscreen (DtrhHostService.cs:286-302): fullscreen-set -> host window toggle,
        // then echo {type:"fullscreen", on} back so the page dock button + Esc ladder stay in sync (:298).
        var h = new Harness().Build();
        h.Orch.HandleMessage(Msg(new { type = "ready" }));
        h.Browser.Posts.Clear();

        h.Orch.HandleMessage(Msg(new { type = "fullscreen-set", on = true }));
        Assert.Equal(new[] { true }, h.Fx.HostFullscreenCalls);
        // Byte-for-byte WPF echo shape/casing (DtrhHostService.cs:298).
        Assert.Contains("{\"type\":\"fullscreen\",\"on\":true}", h.Browser.Posts);

        h.Orch.HandleMessage(Msg(new { type = "fullscreen-set", on = false }));
        Assert.Equal(new[] { true, false }, h.Fx.HostFullscreenCalls);
        Assert.Contains("{\"type\":\"fullscreen\",\"on\":false}", h.Browser.Posts);

        // Missing 'on' defaults to false (WPF :271 (bool?)o["on"] ?? false).
        h.Orch.HandleMessage(Msg(new { type = "fullscreen-set" }));
        Assert.Equal(new[] { true, false, false }, h.Fx.HostFullscreenCalls);
    }

    [Fact]
    public void FullscreenSet_BeforeReady_QueuesEchoUntilFlush()
    {
        // The echo rides the standard queue-until-ready outbound buffer (WPF ChaosWebViewHost.Post).
        var h = new Harness().Build();
        h.Orch.HandleMessage(Msg(new { type = "fullscreen-set", on = true }));
        Assert.Equal(new[] { true }, h.Fx.HostFullscreenCalls);   // seam fires immediately
        Assert.Empty(h.Browser.Posts);                            // echo queued, page not ready

        h.Orch.HandleMessage(Msg(new { type = "ready" }));
        Assert.Contains("{\"type\":\"fullscreen\",\"on\":true}", h.Browser.Posts);
    }

    [Fact]
    public void ExitDone_TearsDown_ResumesFreeze_RestoresWindow()
    {
        var h = new Harness().Build(withLog: true);
        bool closed = false;
        h.Orch.Closed += () => closed = true;
        h.Orch.HandleMessage(Msg(new { type = "freeze-state", on = true }));
        h.Orch.HandleMessage(Msg(new { type = "exit-done" }));

        Assert.Contains("freeze-off", h.Log);                  // freeze resumed on teardown
        Assert.Equal(1, h.Fx.RestoreMainWindowCount);
        Assert.True(closed);
    }
}
