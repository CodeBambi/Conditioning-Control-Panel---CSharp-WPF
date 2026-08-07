using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Train 2 review round: every bug two independent reviewers found in the merged branch, pinned
/// so it cannot come back.
///
/// <para>They cluster, and the clusters are the point. <b>Wiring that was never connected</b> (the
/// arbiter built with a different scorer than the observer, so the whole silence budget was dead
/// code). <b>Erasure that missed an artifact</b> (a debounced save resurrecting the wiped file; the
/// pacing state surviving a wipe). <b>Lifecycle that only ran on the happy path</b> (retention
/// pruning reachable only while the feature is switched ON; probes that never restarted after a
/// pause). <b>A once-per-day joke spent on a frame nobody heard.</b> And <b>copy that outran the
/// code</b> — a one-click "hide this app" that deleted the shipped protection groups, and a default
/// slider position read as a preference.</para>
/// </summary>
public class AwarenessReviewFixTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 8, 7, 9, 0, 0, DateTimeKind.Local);

    private readonly string _dir;
    private readonly string _path;
    private readonly List<ActivityLedger> _ledgers = new();

    public AwarenessReviewFixTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-aware-review", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "awareness_ledger.json");
    }

    public void Dispose()
    {
        foreach (var ledger in _ledgers)
        {
            try { ledger.Dispose(); } catch { }
        }
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private ActivityLedger NewLedger(Func<DateTime>? clock = null, int retention = ActivityLedger.DefaultRetentionDays)
    {
        var ledger = new ActivityLedger(_path, clock ?? (() => T0), () => retention);
        _ledgers.Add(ledger);
        return ledger;
    }

    // =====================================================================================
    //  the silence budget: one scorer, shared with the arbiter
    // =====================================================================================

    private sealed class Clock
    {
        public DateTime Now = T0;
        public DateTime Read() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    private sealed class CountingSpeaker : IAwarenessSpeaker
    {
        public string? CurrentAppId { get; set; }
        public int BarkCount;
        public int LineCount;
        public bool TrySpeakBark(ContextFrame frame) { BarkCount++; return true; }
        public bool TrySpeakLine(string line, RarityTier tier) { LineCount++; return true; }
    }

    private sealed class ScriptedSource : IAwarenessLineSource
    {
        public bool IsAvailable { get; set; } = true;
        public AwarenessReply Reply = new("a line", null, false);
        public Action? DuringCall;

        public Task<AwarenessReply> RequestAsync(ContextFrame frame, CancellationToken cancellationToken)
        {
            DuringCall?.Invoke();
            return Task.FromResult(Reply);
        }
    }

    private static ContextFrame Frame(
        string appId = "youtube",
        RarityTier tier = RarityTier.Common,
        IReadOnlyList<TrendEvent>? trends = null) => new()
        {
            AppId = appId,
            ServiceName = "YouTube",
            AppCluster = "site_video",
            Category = ActivityCategory.Media,
            Transition = TransitionKind.NewApp,
            Tier = tier,
            Trends = trends ?? Array.Empty<TrendEvent>(),
            CutAt = T0
        };

    /// <summary>
    /// The blocker. <c>ReactionArbiter.NoteDelivered</c> is the ONLY caller of
    /// <see cref="WorthinessScorer.RegisterDelivery"/>, and production built the arbiter with
    /// <c>scorer: null</c> while the observer got its own instance — so the floating threshold never
    /// rose, the per-app repetition penalty was permanently 0.0, and doc 02 §3.4's "burst-proof by
    /// construction" pacing model did nothing at all.
    /// </summary>
    [Fact]
    public async Task ADeliveredLine_RaisesTheSharedThreshold()
    {
        var clock = new Clock();
        var scorer = new WorthinessScorer(() => AwarenessIntensity.Chatty);
        var arbiter = new ReactionArbiter(
            new ReactionCooldownLedger(() => AwarenessIntensity.Chatty),
            scorer, clock.Read, new CountingSpeaker(), new ScriptedSource(), new StubCompanionMemory());

        double before = scorer.CurrentThreshold(clock.Now);

        var decision = await arbiter.SubmitAsync(Frame(tier: RarityTier.Uncommon));

        Assert.Equal(AwarenessVerdict.Llm, decision.Verdict);
        Assert.True(scorer.CurrentThreshold(clock.Now) > before,
            "a delivered line must push the worthiness threshold up");
    }

    /// <summary>The other half of the same term: the app she just spoke about gets harder to earn.</summary>
    [Fact]
    public async Task ADeliveredLine_RaisesThatAppsRepetitionPenalty()
    {
        var clock = new Clock();
        var scorer = new WorthinessScorer(() => AwarenessIntensity.Chatty);
        var arbiter = new ReactionArbiter(
            new ReactionCooldownLedger(() => AwarenessIntensity.Chatty),
            scorer, clock.Read, new CountingSpeaker(), new ScriptedSource(), new StubCompanionMemory());

        Assert.Equal(0.0, scorer.RepetitionPenalty("youtube", clock.Now));

        await arbiter.SubmitAsync(Frame(tier: RarityTier.Uncommon));

        Assert.True(scorer.RepetitionPenalty("youtube", clock.Now) > 0.0);
    }

    /// <summary>
    /// The gate was asked before an 8-second call and never asked again at delivery, so a keyword
    /// comment or a bark landing mid-call was followed by the awareness line two seconds later — from
    /// the component whose entire purpose is "exactly one reaction per moment" (doc 02 §5.3).
    /// </summary>
    [Fact]
    public async Task SomethingElseSpeakingDuringTheCall_DropsTheLineRatherThanStackingOnIt()
    {
        var clock = new Clock();
        var cooldowns = new ReactionCooldownLedger(() => AwarenessIntensity.Chatty);
        var speaker = new CountingSpeaker();
        var source = new ScriptedSource();
        var arbiter = new ReactionArbiter(cooldowns, new WorthinessScorer(() => AwarenessIntensity.Chatty),
            clock.Read, speaker, source, new StubCompanionMemory());

        // A keyword avatar comment lands while the model is still thinking.
        source.DuringCall = () => cooldowns.RecordDelivery(ReactionSource.Keyword, "discord", clock.Now);

        var decision = await arbiter.SubmitAsync(Frame(tier: RarityTier.Uncommon));

        Assert.Equal(AwarenessVerdict.Silence, decision.Verdict);
        Assert.Equal(0, speaker.LineCount);
        Assert.Equal(0, speaker.BarkCount);   // a bark would violate the very floor that just refused
        Assert.Contains("raced", decision.Reason, StringComparison.Ordinal);
    }

    // =====================================================================================
    //  one-shot trends survive a frame nobody heard
    // =====================================================================================

    /// <summary>
    /// <c>DeriveTrends</c> consumed the guards, and the observer called it before the score, the bark
    /// floor and every one of the arbiter's gates. A NightShift derived at 01:10 on a frame that
    /// scored below threshold was gone for the rest of the night — so the "2am" material that would
    /// have made a Rare-tier callback half an hour later no longer existed.
    /// </summary>
    [Fact]
    public void APeekedTrend_IsStillOfferedWhenTheFrameWasNeverSpoken()
    {
        var now = T0;
        var ledger = NewLedger(() => now);

        // Three visits today makes the third one a ReturnVisit.
        ledger.NoteFocus("reddit", null, ActivityCategory.Social, now);
        ledger.NoteFocusEnd(now.AddMinutes(1));
        ledger.NoteFocus("reddit", null, ActivityCategory.Social, now.AddMinutes(10));
        ledger.NoteFocusEnd(now.AddMinutes(11));
        ledger.NoteFocus("reddit", null, ActivityCategory.Social, now.AddMinutes(20));

        var first = ledger.PeekTrends("reddit", null, now.AddMinutes(21));
        Assert.Contains(first.Trends, t => t.Kind == TrendKind.ReturnVisit);

        // The frame was gated (global gap, budget, below threshold — the reason does not matter).
        // Nothing was committed, so the joke is still available.
        var second = ledger.PeekTrends("reddit", null, now.AddMinutes(22));
        Assert.Contains(second.Trends, t => t.Kind == TrendKind.ReturnVisit);
    }

    /// <summary>...and once a line actually reaches the user, it is spent exactly once.</summary>
    [Fact]
    public void ACommittedTrend_IsNotOfferedASecondTime()
    {
        var now = T0;
        var ledger = NewLedger(() => now);

        ledger.NoteFocus("reddit", null, ActivityCategory.Social, now);
        ledger.NoteFocusEnd(now.AddMinutes(1));
        ledger.NoteFocus("reddit", null, ActivityCategory.Social, now.AddMinutes(10));
        ledger.NoteFocusEnd(now.AddMinutes(11));
        ledger.NoteFocus("reddit", null, ActivityCategory.Social, now.AddMinutes(20));

        var derivation = ledger.PeekTrends("reddit", null, now.AddMinutes(21));
        Assert.Contains(derivation.Trends, t => t.Kind == TrendKind.ReturnVisit);

        ledger.CommitTrends(derivation);

        var after = ledger.PeekTrends("reddit", null, now.AddMinutes(22));
        Assert.DoesNotContain(after.Trends, t => t.Kind == TrendKind.ReturnVisit);
    }

    /// <summary>The convenience wrapper still consumes, so callers that always deliver are unchanged.</summary>
    [Fact]
    public void DeriveTrends_StillPeeksAndCommitsInOneStep()
    {
        var now = T0;
        var ledger = NewLedger(() => now);

        ledger.NoteFocus("reddit", null, ActivityCategory.Social, now);
        ledger.NoteFocusEnd(now.AddMinutes(1));
        ledger.NoteFocus("reddit", null, ActivityCategory.Social, now.AddMinutes(10));
        ledger.NoteFocusEnd(now.AddMinutes(11));
        ledger.NoteFocus("reddit", null, ActivityCategory.Social, now.AddMinutes(20));

        Assert.Contains(ledger.DeriveTrends("reddit", null, now.AddMinutes(21)),
            t => t.Kind == TrendKind.ReturnVisit);
        Assert.DoesNotContain(ledger.DeriveTrends("reddit", null, now.AddMinutes(22)),
            t => t.Kind == TrendKind.ReturnVisit);
    }

    // =====================================================================================
    //  erasure must be total
    // =====================================================================================

    /// <summary>
    /// A debounced save serialises under one lock and writes under another. A wipe landing in that gap
    /// used to lose the race: <c>AtomicWrite</c> recreated <c>awareness_ledger.json</c> with every
    /// counter intact, immediately after the user pressed "erase everything".
    /// </summary>
    [Fact]
    public void ASaveThatWasInFlightWhenTheWipeLanded_CannotResurrectTheFile()
    {
        var ledger = NewLedger();
        ledger.Start();
        ledger.NoteFocus("youtube", "site_video", ActivityCategory.Media, T0);
        ledger.Heartbeat(T0.AddMinutes(5));

        // The state of a save that has serialised but not yet written.
        var (json, generation) = ledger.SnapshotForWrite();
        Assert.Contains("youtube", json, StringComparison.OrdinalIgnoreCase);

        ledger.Wipe();
        Assert.False(File.Exists(_path));

        ledger.WriteSnapshotIfCurrent(json, generation);

        Assert.False(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
    }

    /// <summary>Per-app forget takes the same protection: the stale snapshot still names the app.</summary>
    [Fact]
    public void ASaveThatWasInFlightWhenAForgetLanded_CannotRestoreThatApp()
    {
        var ledger = NewLedger();
        ledger.Start();
        ledger.NoteFocus("youtube", "site_video", ActivityCategory.Media, T0);
        ledger.Heartbeat(T0.AddMinutes(5));

        var (json, generation) = ledger.SnapshotForWrite();

        ledger.Forget("youtube");
        ledger.WriteSnapshotIfCurrent(json, generation);

        var written = File.ReadAllText(_path);
        Assert.DoesNotContain("youtube", written, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The wipe's own doc enumerates "every artifact this feature creates". The arbiter's per-app
    /// cooldowns and the scorer's repetition counters are two of them, keyed by the app ids that were
    /// just erased, and neither had a caller.
    /// </summary>
    [Fact]
    public void Wipe_ClearsThePacingStateToo()
    {
        var cooldowns = new ReactionCooldownLedger(() => AwarenessIntensity.Chatty);
        var scorer = new WorthinessScorer(() => AwarenessIntensity.Chatty);

        cooldowns.RecordDelivery(ReactionSource.AwarenessLlm, "youtube", T0);
        scorer.RegisterDelivery("youtube", T0);
        Assert.False(cooldowns.CanSpeak(ReactionSource.AwarenessLlm, "youtube", T0.AddSeconds(5), out _));
        Assert.True(scorer.RepetitionPenalty("youtube", T0) > 0);

        AwarenessLive.ResetPacingState = () => { cooldowns.Reset(); scorer.Reset(); };
        try { AwarenessLive.WipeEverything(); }
        finally { AwarenessLive.ResetPacingState = null; }

        Assert.True(cooldowns.CanSpeak(ReactionSource.AwarenessLlm, "youtube", T0.AddSeconds(5), out _));
        Assert.Equal(0.0, scorer.RepetitionPenalty("youtube", T0));
    }

    [Fact]
    public void ForgetOneApp_ClearsThatAppsPacingStateToo()
    {
        var cooldowns = new ReactionCooldownLedger(() => AwarenessIntensity.Chatty);
        var scorer = new WorthinessScorer(() => AwarenessIntensity.Chatty);

        cooldowns.RecordDelivery(ReactionSource.AwarenessLlm, "youtube", T0);
        scorer.RegisterDelivery("youtube", T0);

        AwarenessLive.ForgetPacingState = id => { cooldowns.Forget(id); scorer.Forget(id); };
        try { AwarenessLive.Forget("youtube"); }
        finally { AwarenessLive.ForgetPacingState = null; }

        Assert.Equal(0.0, scorer.RepetitionPenalty("youtube", T0));
        // The per-app floor is gone; the global gap is pacing, not identity, and is left alone.
        Assert.Null(GetPerApp(cooldowns, "youtube"));
    }

    private static DateTime? GetPerApp(ReactionCooldownLedger ledger, string appId)
    {
        var field = typeof(ReactionCooldownLedger)
            .GetField("_perApp", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var map = (Dictionary<string, DateTime>)field!.GetValue(ledger)!;
        return map.TryGetValue(appId, out var at) ? at : null;
    }

    // =====================================================================================
    //  lifecycle must be eager, not lazy-on-UI
    // =====================================================================================

    /// <summary>
    /// Pruning ran only from <c>ActivityLedger.Start()</c> and the rollover timer, both reachable only
    /// through an observer that returns early when awareness is OFF. So a user who ran awareness for
    /// three weeks and then switched it off kept those three weeks forever — while the consent dialog
    /// and the settings notice both promise the counts are deleted after the retention period.
    /// </summary>
    [Fact]
    public void RetentionIsSweptOnDisk_WithoutTheFeatureEverBeingStarted()
    {
        var recording = NewLedger();
        recording.Start();
        recording.NoteFocus("youtube", "site_video", ActivityCategory.Media, T0);
        recording.Heartbeat(T0.AddMinutes(30));
        recording.Stop();
        Assert.Contains("youtube", File.ReadAllText(_path), StringComparison.OrdinalIgnoreCase);

        // Forty days later, awareness has been switched off the whole time: nothing starts.
        var later = T0.AddDays(40);
        var sweeper = new ActivityLedger(_path, () => later, () => 7);
        _ledgers.Add(sweeper);

        sweeper.PruneOnDisk();

        Assert.DoesNotContain("youtube", File.ReadAllText(_path), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The sweep must never CREATE a ledger for someone who has never used the feature.</summary>
    [Fact]
    public void TheRetentionSweep_CreatesNothingWhenThereIsNoLedger()
    {
        var ledger = NewLedger();

        ledger.PruneOnDisk();

        Assert.False(File.Exists(_path));
    }

    /// <summary>...and it must leave the instance clean, so a later Start() genuinely loads from disk.</summary>
    [Fact]
    public void TheRetentionSweep_LeavesNothingLoaded()
    {
        var recording = NewLedger();
        recording.Start();
        recording.NoteFocus("youtube", "site_video", ActivityCategory.Media, T0);
        recording.Heartbeat(T0.AddMinutes(30));
        recording.Stop();

        var sweeper = new ActivityLedger(_path, () => T0.AddMinutes(31), () => 30);
        _ledgers.Add(sweeper);

        sweeper.PruneOnDisk();
        Assert.Equal(0, sweeper.AppCount);

        sweeper.Start();
        Assert.Equal(1, sweeper.AppCount);
    }

    /// <summary>
    /// <c>Stop()</c> disarmed the rollover timer and left the object non-null, and <c>Start()</c> used
    /// <c>??=</c> — so after any pause/resume the 5-minute rollover backstop never fired again, which
    /// matters most on the machines where the observer's own poll is not running.
    /// </summary>
    [Fact]
    public void TheRolloverTimerIsReArmedOnEveryStart()
    {
        var ledger = NewLedger();
        ledger.Start();
        ledger.Stop();
        ledger.Start();

        Assert.True(IsTimerArmed(ledger, "_rolloverTimer"));
    }

    private static bool IsTimerArmed(ActivityLedger ledger, string fieldName)
    {
        var field = typeof(ActivityLedger).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var timer = field!.GetValue(ledger) as Timer;
        if (timer == null) return false;

        // Change() returns false only on a disposed timer; what we can assert cheaply is that the
        // object survived Stop() and that Start() is willing to re-arm it.
        return timer.Change(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// The typing-burst sampler had the identical shape: <c>Start()</c> bailed when <c>_timer</c> was
    /// non-null and <c>Stop()</c> left it non-null, so the first Stop was permanent — and with it the
    /// DND rule that keeps her from interrupting someone mid-sentence.
    /// </summary>
    [Fact]
    public void TheInputProbeSamplesAgainAfterAStopStartCycle()
    {
        using var probe = new Win32InputProbe();
        probe.Start();
        probe.Stop();

        var tick = typeof(Win32InputProbe).GetField("_lastInputTick", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(tick);
        tick!.SetValue(probe, (uint)0);

        probe.Start();

        // Four samples a second; give it a comfortable handful of intervals.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while ((uint)tick.GetValue(probe)! == 0 && DateTime.UtcNow < deadline) Thread.Sleep(50);

        Assert.NotEqual((uint)0, (uint)tick.GetValue(probe)!);
    }

    /// <summary>The media watcher's restart guard has to clear, or the SMTC signal never comes back.</summary>
    [Fact]
    public void TheMediaWatcherIsRestartableAfterStop()
    {
        using var watcher = new SmtcMediaWatcher();
        var started = typeof(SmtcMediaWatcher).GetField("_started", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(started);

        watcher.Start();
        watcher.Stop();

        Assert.False((bool)started!.GetValue(watcher)!);
    }

    // =====================================================================================
    //  she does not observe herself
    // =====================================================================================

    /// <summary>
    /// With the Companion tab or the avatar tube focused the sample resolved to appId
    /// "conditioningcontrolpanel" with first-ever novelty — over the Chatty threshold — so the first
    /// v2 line many users would ever hear was a quip about them using the app. It also dominated the
    /// day arc, which rides into the cloud projection of EVERY frame.
    /// </summary>
    [Theory]
    [InlineData("conditioningcontrolpanel")]
    [InlineData("ConditioningControlPanel")]
    [InlineData("ConditioningControlPanel.exe")]
    public void OurOwnWindowIsDroppedBeforeAnythingIsResolved(string process)
    {
        var verdict = AwarenessObserverPolicy.EvaluatePrivacy(
            new ForegroundSample(new IntPtr(1), "Conditioning Control Panel", process, false),
            Policy(), T0);

        Assert.Equal(FrameDrop.OwnProcess, verdict.Drop);
        Assert.False(verdict.Allowed);
    }

    [Fact]
    public void TheHostProcessIsRecognisedWhateverItIsCalled()
    {
        using var self = Process.GetCurrentProcess();
        Assert.True(AwarenessObserverPolicy.IsOwnProcess(self.ProcessName));
        Assert.False(AwarenessObserverPolicy.IsOwnProcess("chrome"));
        Assert.False(AwarenessObserverPolicy.IsOwnProcess(""));
    }

    private static AwarenessPolicySettings Policy() => new(
        Array.Empty<string>(), Array.Empty<string>(),
        AdultReactionsEnabled: true, AdultRecordingEnabled: true);

    // =====================================================================================
    //  the deny list is not deleted by a one-click "hide this app"
    // =====================================================================================

    /// <summary>
    /// The blocker in the privacy panel: <c>AddToDeny</c> built the new list from the RAW setting and
    /// then set <c>AwarenessDenySeeded</c>, which means "the stored list is now the whole truth". For
    /// every upgrader — raw list empty, seed never run, because the seed hung off a consent dialog
    /// they never saw — one click on "hide this app" deleted the password-manager, banking and
    /// email-title groups.
    ///
    /// <para>This asserts the shape the fix relies on: the effective list is what a mutating path must
    /// start from, and it carries the three groups while the seed has not run.</para>
    /// </summary>
    [Fact]
    public void AddingOneAppToTheDenyList_MustStartFromTheEffectiveList()
    {
        var settings = new AppSettings { AwarenessModeEnabled = true, AwarenessConsentGiven = true };
        Assert.False(settings.AwarenessDenySeeded);

        var effective = AwarenessPrivacyRules.EffectiveDenyList(settings);
        Assert.Contains(AwarenessPrivacyRules.GroupPasswordManagers, effective);
        Assert.Contains(AwarenessPrivacyRules.GroupBanking, effective);
        Assert.Contains(AwarenessPrivacyRules.GroupEmailTitles, effective);

        // What the panel now does: effective list + the new entry, then record the seed.
        var list = new List<string>(effective) { "steam" };
        settings.AwarenessDenyList = list;
        settings.AwarenessDenySeeded = true;

        var after = AwarenessPrivacyRules.EffectiveDenyList(settings);
        Assert.Contains(AwarenessPrivacyRules.GroupPasswordManagers, after);
        Assert.Contains("steam", after);

        // And the groups still actually block, which is the thing the chips claim.
        var verdict = AwarenessPrivacyRules.Evaluate(
            new AwarenessSightRequest("chrome", "Chrome", null, "Bitwarden Web Vault - Google Chrome"),
            after, null, T0);
        Assert.Equal(AwarenessDropReason.DenyList, verdict.Reason);
    }

    /// <summary>The source-level pin: no mutating path may read the raw list to build a new one.</summary>
    [Fact]
    public void NoDenyListWriter_SeedsItselfFromTheRawSetting()
    {
        var source = ReadSource("ConditioningControlPanel", "Views", "Controls", "Companion", "Runtime",
            "AwarenessPrivacyRuntimeVm.cs");

        Assert.DoesNotContain("new List<string>(settings.AwarenessDenyList", source, StringComparison.Ordinal);
    }

    // =====================================================================================
    //  per-app forget can reach the 30 days on disk
    // =====================================================================================

    /// <summary>
    /// The chips were built from the session ring — "in memory only; never persisted", and populated
    /// only when the user LEAVES an app. So on a fresh launch the row was empty and the only control
    /// that would remove yesterday's site was "forget everything".
    /// </summary>
    [Fact]
    public void TheForgetChipsEnumerateThePersistedApps_NotJustThisSessionsRing()
    {
        var recording = NewLedger();
        recording.Start();
        recording.NoteFocus("youtube", "site_video", ActivityCategory.Media, T0);
        recording.Heartbeat(T0.AddMinutes(10));
        recording.Stop();

        // A fresh launch: nothing has been left yet, so the ring is empty.
        var relaunched = NewLedger(() => T0.AddDays(1));
        relaunched.Start();

        Assert.Empty(relaunched.RecentTransitions);
        Assert.Contains("youtube", relaunched.KnownAppIds);
    }

    [Fact]
    public void ForgettingAnApp_RemovesItFromTheChips()
    {
        var ledger = NewLedger();
        ledger.Start();
        ledger.NoteFocus("youtube", "site_video", ActivityCategory.Media, T0);
        ledger.Heartbeat(T0.AddMinutes(10));

        Assert.Contains("youtube", ledger.KnownAppIds);
        ledger.Forget("youtube");
        Assert.DoesNotContain("youtube", ledger.KnownAppIds);
    }

    // =====================================================================================
    //  a mod override cannot switch off the adult protection
    // =====================================================================================

    /// <summary>
    /// <c>EnsureLoaded</c> REPLACED the whole cluster table with a mod's file. Every adult rule in
    /// Train 2 keys off the literal <c>site_eh</c> coming back from <c>Classify</c>, so a mod that
    /// added three bespoke apps silently turned all of them off: no error, no log, adult app ids and
    /// display names on the wire.
    /// </summary>
    [Fact]
    public void AnOverrideMergesOverTheDefaults_RatherThanReplacingThem()
    {
        var merge = typeof(AppClusterMap).GetMethod("Merge", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(merge);

        var embedded = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["site_eh"] = new[] { "pornhub" },
            ["site_video"] = new[] { "youtube" }
        };
        var overrides = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["game_bespoke"] = new[] { "my mod game" },
            ["site_video"] = new[] { "youtube", "nebula" }
        };

        var merged = (Dictionary<string, string[]>)merge!.Invoke(null, new object[] { embedded, overrides })!;

        Assert.True(merged.ContainsKey("site_eh"), "the adult cluster survives a mod override");
        Assert.True(merged.ContainsKey("game_bespoke"), "the mod's own cluster is added");
        Assert.Equal(2, merged["site_video"].Length);   // an id the override mentions is replaced
    }

    /// <summary>The floor names the ids the privacy code branches on, and the embedded table has them.</summary>
    [Fact]
    public void TheAdultClusterIsOnTheRequiredList_AndIsShippedEmbedded()
    {
        Assert.Contains(AwarenessClusters.Adult, AppClusterMap.RequiredClusterIds);

        var (cluster, _) = AppClusterMap.Classify("pornhub - videos");
        Assert.Equal(AwarenessClusters.Adult, cluster);
    }

    // =====================================================================================
    //  the intensity migration must not read a default as a preference
    // =====================================================================================

    /// <summary>
    /// The shipped cooldown default is 10s, which is also the slider's floor —
    /// <c>FromCooldownSeconds(10)</c> is Unhinged. <c>EnsureMigrated</c> ran unconditionally from the
    /// consent flow, INCLUDING on brand-new installs, so the first thing consent did was overwrite the
    /// documented Chatty default with twice the line rate and twice the LLM call rate.
    /// </summary>
    [Fact]
    public void ASliderLeftWhereItShipped_KeepsTheChattyDefault()
    {
        var settings = new AppSettings();
        Assert.Equal(AwarenessIntensityMigration.ShippedDefaultCooldownSeconds,
            settings.AwarenessReactionCooldownSeconds);

        Assert.True(AwarenessIntensityMigration.EnsureMigrated(settings));

        Assert.Equal(AwarenessIntensity.Chatty, settings.AwarenessIntensity);
        Assert.True(settings.AwarenessIntensityMigrated);
    }

    /// <summary>A slider the user actually MOVED is still read as the preference it was.</summary>
    [Theory]
    [InlineData(20, AwarenessIntensity.Unhinged)]
    [InlineData(90, AwarenessIntensity.Chatty)]
    [InlineData(600, AwarenessIntensity.Subtle)]
    public void ASliderTheUserMoved_StillMigrates(int seconds, AwarenessIntensity expected)
    {
        var settings = new AppSettings { AwarenessReactionCooldownSeconds = seconds };

        Assert.True(AwarenessIntensityMigration.EnsureMigrated(settings));

        Assert.Equal(expected, settings.AwarenessIntensity);
    }

    [Fact]
    public void TheMigrationStillRunsOnlyOnce()
    {
        var settings = new AppSettings { AwarenessReactionCooldownSeconds = 600 };

        Assert.True(AwarenessIntensityMigration.EnsureMigrated(settings));
        settings.AwarenessIntensity = AwarenessIntensity.Unhinged;   // the user's own later choice
        Assert.False(AwarenessIntensityMigration.EnsureMigrated(settings));
        Assert.Equal(AwarenessIntensity.Unhinged, settings.AwarenessIntensity);
    }

    // =====================================================================================
    //  the copy must not lie
    // =====================================================================================

    /// <summary>
    /// v1 persisted nothing; v2 keeps a 30-day on-disk record. An upgrader already has
    /// <c>AwarenessModeEnabled</c> and <c>AwarenessConsentGiven</c> from the old silent auto-consent,
    /// so without the v2 flag in this predicate the ledger started recording on the first launch after
    /// the update, for a dialog they had never seen.
    /// </summary>
    [Fact]
    public void TheObserversEnabledPredicate_RequiresTheV2Consent()
    {
        var source = ReadSource("ConditioningControlPanel", "Services", "Awareness", "AwarenessObserver.cs");
        var predicate = Between(source, "public static bool IsEnabled", "/// <summary>\n        /// Starts the ledger");

        Assert.Contains("UseAwarenessV2", predicate, StringComparison.Ordinal);
        Assert.Contains("AwarenessModeEnabled", predicate, StringComparison.Ordinal);
        Assert.Contains("AwarenessConsentGiven", predicate, StringComparison.Ordinal);
        Assert.Contains("AwarenessConsentShownV2", predicate, StringComparison.Ordinal);
    }

    /// <summary>
    /// The wire caption sits directly under a line that carries four of the projection's ~20 fields.
    /// It used to claim "nothing more", which was false of the code as merged — the exact failure mode
    /// both reviews kept finding. It may describe a summary; it may not claim exhaustiveness.
    /// </summary>
    [Fact]
    public void TheWireCaption_DoesNotClaimTheSummaryIsEverything()
    {
        var caption = ReadEnglishKey("companion_awareness_wire_caption");

        Assert.DoesNotContain("nothing more", caption, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("summary", caption, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>And every shipped language says the same thing, not just English.</summary>
    [Fact]
    public void EveryLanguageGotTheCorrectedCaption()
    {
        foreach (var file in Directory.GetFiles(LanguagesDir, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            Assert.True(doc.RootElement.TryGetProperty("companion_awareness_wire_caption", out var value),
                Path.GetFileName(file) + " is missing the wire caption");
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()));
        }
    }

    /// <summary>
    /// The v2 reaction prompt carries no media catalogue at all — the projection is app ids and
    /// numbers — so a licence to "name one thing of yours" was a licence to invent a filename the
    /// user does not have, against the contract's own "never imply you know anything that is not
    /// there".
    /// </summary>
    [Fact]
    public void ThePlugLicence_LicensesAnOfferAndNotATitle()
    {
        Assert.Contains("never name a title", AwarenessPromptBuilder.PlugLicenseNote,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("may name one thing", AwarenessPromptBuilder.PlugLicenseNote,
            StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================================
    //  one contract, one parser
    // =====================================================================================

    /// <summary>
    /// Two parsers for one contract, one of them expecting a keyword the prompt does not teach. The
    /// unused one would have folded every callback into the spoken line and silently killed the
    /// delivery-staleness re-tag.
    /// </summary>
    [Fact]
    public void ThereIsExactlyOneAwarenessReplyParser()
    {
        var source = ReadSource("ConditioningControlPanel", "Services", "Companion", "Brain", "AwarenessSpeech.cs");

        Assert.DoesNotContain("\"ALT:\"", source, StringComparison.Ordinal);
        Assert.Contains("AwarenessReactionService.Parse", source, StringComparison.Ordinal);
    }

    // =====================================================================================
    //  helpers
    // =====================================================================================

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }

    private static string LanguagesDir =>
        Path.Combine(RepoRoot, "ConditioningControlPanel", "Localization", "Languages");

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    private static string ReadEnglishKey(string key)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(LanguagesDir, "en.json")));
        return doc.RootElement.GetProperty(key).GetString() ?? string.Empty;
    }

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "could not find: " + start);
        int to = source.IndexOf(end.Replace("\n", Environment.NewLine), from, StringComparison.Ordinal);
        if (to < 0) to = source.IndexOf(end, from, StringComparison.Ordinal);
        return to < 0 ? source[from..] : source[from..to];
    }
}
