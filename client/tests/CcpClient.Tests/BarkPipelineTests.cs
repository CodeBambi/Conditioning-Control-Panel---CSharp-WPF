using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Companion;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-032 q2 bark content pipeline conformance: WPF-schema loader tolerance, condition
/// ops, the VERIFIED gate order + bypass matrix (BarkService.cs:1305-1422), variant
/// rotation/recycle/recency, payload integrity (text/audio/emotion as ONE unit), mute
/// text-only degradation (typed, surfaced), disabled-phrase persistence on the SP-005
/// machinery (round-trip + quarantine), TEXT-derived pacing math (Speech.cs:148-168), and
/// priority routing through q1's arbitration.
/// </summary>
public sealed class BarkPipelineTests
{
    // ---------- loader ----------

    [Fact]
    public void Loader_WpfSchema_DefaultsAndVariantForms()
    {
        var rules = BarkRuleLoader.Parse(DefaultBarkRules.ManifestJson, _ => { });
        Assert.Equal(8, rules.Count);

        var wave = rules.Single(r => r.Id == "chaos_wave_cleared");
        Assert.True(wave.Repeatable); // WPF default true
        Assert.Equal(1.0, wave.Chance); // WPF default 1.0
        Assert.Equal(BarkClass.Normal, wave.Class);
        Assert.Equal(20000, wave.CooldownMs);
        Assert.Equal(3, wave.VariantPool.Count);
        Assert.Equal("chaos_wave_cleared_1.mp3", wave.VariantPool[0].Audio);
        Assert.Null(wave.VariantPool[2].Audio); // text-only variant

        var egg = rules.Single(r => r.Id == "egg_mute_secret");
        Assert.Equal(BarkClass.EasterEgg, egg.Class);
        Assert.Equal(BarkScope.Lifetime, egg.Scope);
        Assert.False(egg.Repeatable);
        Assert.True(egg.Conditions.ContainsKey("mute_eq"));

        var safety = rules.Single(r => r.Id == "safety_panic");
        Assert.Equal(BarkClass.Safety, safety.Class);
    }

    [Fact]
    public void Loader_MalformedAndInvalid_NeverThrows_PartialSet()
    {
        var logs = new List<string>();
        Assert.Empty(BarkRuleLoader.Parse("not json {{{", logs.Add));
        Assert.Empty(BarkRuleLoader.Parse("{\"not\":\"an array\"}", logs.Add));

        var rules = BarkRuleLoader.Parse(
            """
            [
              { "id": "ok", "trigger": "T", "variant_pool": ["hi"] },
              { "id": "", "trigger": "T", "variant_pool": ["no id"] },
              { "trigger": "T", "variant_pool": ["missing id field"] },
              { "id": "no-pool", "trigger": "T" },
              42,
              { "id": "poolref", "trigger": "T2", "pool_ref": "Giggles" }
            ]
            """, logs.Add);
        Assert.Equal(2, rules.Count); // ok + poolref (valid but resolves empty — named limit)
        Assert.Contains(logs, l => l.Contains("pool_ref"));
    }

    // ---------- conditions ----------

    [Fact]
    public void Conditions_Operators_And_UnresolvableFieldFails()
    {
        var rule = new BarkRule
        {
            Id = "r", Trigger = "T",
            Conditions = new Dictionary<string, object?> { ["wave_gte"] = 3L, ["difficulty_eq"] = "Hard", ["quick_eq"] = true },
            VariantPool = [new BarkVariant("x", null)],
        };
        var fills = new Dictionary<string, object?> { ["wave"] = 5.0, ["difficulty"] = "hard", ["quick"] = true };
        Assert.True(BarkConditions.Pass(rule, _ => null, fills));

        fills["wave"] = 2.0;
        Assert.False(BarkConditions.Pass(rule, _ => null, fills));

        // Unresolvable field → condition fails (WPF :1079).
        fills.Remove("wave");
        Assert.False(BarkConditions.Pass(rule, _ => null, fills));

        // Numeric-string coercion + bool→0/1 (WPF TryDouble :1138-1156).
        var numRule = new BarkRule
        {
            Id = "r", Trigger = "T",
            Conditions = new Dictionary<string, object?> { ["n_gt"] = 2L },
            VariantPool = [new BarkVariant("x", null)],
        };
        Assert.True(BarkConditions.Pass(numRule, _ => null,
            new Dictionary<string, object?> { ["n"] = "2.5" }));
    }

    [Fact]
    public void Conditions_LiveFields_TakePrecedence_OverFills()
    {
        var rule = new BarkRule
        {
            Id = "r", Trigger = "T",
            Conditions = new Dictionary<string, object?> { ["master_volume_gte"] = 50L },
            VariantPool = [new BarkVariant("x", null)],
        };
        // Live read wins over the fill (WPF ResolveField :1099-1137).
        Assert.True(BarkConditions.Pass(rule, f => f == "master_volume" ? 80 : null,
            new Dictionary<string, object?> { ["master_volume"] = 10 }));
    }

    // ---------- matching + gate ----------

    [Fact]
    public void Matching_FirstConditionPassingWins_GateBlocksWinner_NoFallback()
    {
        // chaos_wave_cleared_high (priority 110, wave_gte 5) outranks chaos_wave_cleared
        // (priority 100). With the high rule's conditions failing, the normal rule fires.
        var h = NewHarness();
        var outcome = h.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 });
        var surfaced = Assert.IsType<BarkOutcome.Surfaced>(outcome);
        Assert.Equal("chaos_wave_cleared", surfaced.Payload.RuleId);

        // With wave >= 5 the conditioned rule wins.
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        outcome = h.Raise("ChaosWaveCleared", new() { ["wave"] = 7.0 });
        surfaced = Assert.IsType<BarkOutcome.Surfaced>(outcome);
        Assert.Equal("chaos_wave_cleared_high", surfaced.Payload.RuleId);

        // Gate-blocked winner = NO fallback: high rule matches by conditions; put the whole
        // gate behind a whisper so the winner blocks — the lower-priority rule must NOT fire.
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        h.Arb.PlayWhisper("whisper.wav", 0.5f);
        outcome = h.Raise("ChaosWaveCleared", new() { ["wave"] = 9.0 });
        var gated = Assert.IsType<BarkOutcome.Gated>(outcome);
        Assert.Equal("chaos_wave_cleared_high", gated.RuleId);
        Assert.Equal("whisper-active", gated.Reason);
    }

    [Fact]
    public void Gate_WhisperBusy_Blocks_WithTypedReason()
    {
        var h = NewHarness();
        h.Arb.PlayWhisper("whisper.wav", 0.5f);
        var outcome = h.Raise("ChaosComboMilestone", new() { ["combo"] = 5.0 });
        Assert.Equal("whisper-active", Assert.IsType<BarkOutcome.Gated>(outcome).Reason);
    }

    [Fact]
    public void Gate_AntiStale_OrdinaryDroppedWhileVoiceActive_PriorityExempt()
    {
        var h = NewHarness();
        h.Arb.PlayVoice("active.wav", 0.5f); // voice channel busy
        // Ordinary bark (priority 90 < 100) while speaking → typed drop (WPF :1358-1365 —
        // the ONLY freshness mechanism).
        var outcome = h.Raise("ChaosBubbleDetonated");
        Assert.Equal("speaking", Assert.IsType<BarkOutcome.Gated>(outcome).Reason);

        // Priority (>= 100) preempts even while speaking (WPF :1624).
        outcome = h.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 });
        Assert.IsType<BarkOutcome.Surfaced>(outcome);
    }

    [Fact]
    public void Gate_MinGap_BlocksSecondFireWithin60s()
    {
        var h = NewHarness();
        h.Pipeline.LoadRules(
        [
            new BarkRule
            {
                Id = "gap", Trigger = "Gap", Priority = 150,
                VariantPool = [new BarkVariant("a", "gap1.mp3"), new BarkVariant("b", "gap2.mp3")],
            },
        ]);
        Assert.IsType<BarkOutcome.Surfaced>(h.Raise("Gap"));
        var gated = Assert.IsType<BarkOutcome.Gated>(h.Raise("Gap"));
        Assert.Equal("min-gap", gated.Reason);

        h.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.IsType<BarkOutcome.Surfaced>(h.Raise("Gap"));
    }

    [Fact]
    public void Gate_Cooldown_PerRule_Blocks_Until_Elapsed()
    {
        var h = NewHarness();
        h.Pipeline.LoadRules(
        [
            new BarkRule
            {
                Id = "cd", Trigger = "CD", Priority = 200, CooldownMs = 120000,
                VariantPool = [new BarkVariant("x", "a.mp3")],
            },
        ]);
        Assert.IsType<BarkOutcome.Surfaced>(h.Raise("CD"));

        h.Clock.Advance(TimeSpan.FromSeconds(61)); // min-gap (60s) elapsed, cooldown (120s) not
        Assert.Equal("cooldown", Assert.IsType<BarkOutcome.Gated>(h.Raise("CD")).Reason);

        h.Clock.Advance(TimeSpan.FromSeconds(61)); // 122s total — both elapsed
        Assert.IsType<BarkOutcome.Surfaced>(h.Raise("CD"));
    }

    [Fact]
    public void Gate_Chance_RollsLast_RngInjected()
    {
        var h = NewHarness(rng: () => 0.9); // >= chance 0.5 → blocked
        var gated = Assert.IsType<BarkOutcome.Gated>(h.Raise("ChaosBubbleDetonated"));
        Assert.Equal("chance", gated.Reason);

        var h2 = NewHarness(rng: () => 0.1); // < 0.5 → fires
        Assert.IsType<BarkOutcome.Surfaced>(h2.Raise("ChaosBubbleDetonated"));
    }

    [Fact]
    public void Gate_SafetyHold_Set_Even_At_VolumeZero()
    {
        // Consult binding 4a: a Safety fire at volume 0 (text-only) STILL sets the 6s hold —
        // mute degrades the audio channel, never the gate state.
        var h = NewHarness();
        h.Pipeline.MasterVolume = 0;
        var egg = Assert.IsType<BarkOutcome.SurfacedTextOnly>(h.Raise("Panic"));
        Assert.Equal(BarkSuppression.VolumeZero, egg.Reason);

        var gated = Assert.IsType<BarkOutcome.Gated>(h.Raise("ChaosComboMilestone", new() { ["combo"] = 5.0 }));
        Assert.Equal("safety-active", gated.Reason);

        // Hold expired after 6s — but the Panic fire ALSO stamped the global min-gap (WPF
        // CommitFire stamps unconditionally), so the floor proof uses guaranteed (which
        // skips min-gap, never the latch — the bypass matrix under test).
        h.Clock.Advance(TimeSpan.FromSeconds(7));
        Assert.IsType<BarkOutcome.SurfacedTextOnly>(h.Raise("ChaosComboMilestone", new() { ["combo"] = 5.0 }, guaranteed: true));
    }

    [Fact]
    public void Gate_BypassMatrix_GuaranteedSkipsFloorGates_NotTheLatch()
    {
        // Consult binding 3 (WPF :1305-1422): guaranteed skips safety-hold/whisper/chat/
        // anti-stale/min-gap/cooldown/chance — but NEVER the one-shot latch.
        var h = NewHarness();
        Assert.IsType<BarkOutcome.Surfaced>(h.Raise("ChaosDefuseFirst")); // lifetime one-shot (audio resolves)
        var gated = Assert.IsType<BarkOutcome.Gated>(h.Raise("ChaosDefuseFirst", guaranteed: true));
        Assert.StartsWith("already-fired", gated.Reason);

        // guaranteed DOES skip min-gap on a repeatable rule.
        Assert.Equal("min-gap", Assert.IsType<BarkOutcome.Gated>(
            h.Raise("ChaosComboMilestone", new() { ["combo"] = 5.0 })).Reason);
        Assert.IsType<BarkOutcome.Surfaced>(
            h.Raise("ChaosComboMilestone", new() { ["combo"] = 6.0 }, guaranteed: true));
    }

    [Fact]
    public void Gate_MinGapExemption_AttentionCheckFail()
    {
        var h = NewHarness();
        h.Raise("ChaosComboMilestone", new() { ["combo"] = 5.0 });
        // AttentionCheckFail is exempt from the 60s global min-gap (WPF :78).
        Assert.IsType<BarkOutcome.Surfaced>(h.Raise("AttentionCheckFail"));
    }

    [Fact]
    public void OneShot_SessionLatch_ClearedByLoadRules_LifetimePersists()
    {
        var h = NewHarness();
        var sessionOnce = new BarkRule
        {
            Id = "once_session", Trigger = "OnceSession", Repeatable = false,
            Scope = BarkScope.Session, VariantPool = [new BarkVariant("one", null)],
        };
        h.Pipeline.LoadRules(h.Rules.Concat([sessionOnce]));
        // guaranteed skips the floor gates (min-gap from prior fires) but never the latch —
        // so this firing IS the LoadRules-cleared-session-latch evidence.
        Assert.IsType<BarkOutcome.SurfacedTextOnly>(h.Raise("OnceSession", guaranteed: true));
        Assert.Equal("already-fired (Session)",
            Assert.IsType<BarkOutcome.Gated>(h.Raise("OnceSession", guaranteed: true)).Reason);

        // LoadRules clears SESSION latches (WPF ReloadRules :406-425)…
        h.Pipeline.LoadRules(h.Rules.Concat([sessionOnce]));
        Assert.IsType<BarkOutcome.SurfacedTextOnly>(h.Raise("OnceSession", guaranteed: true));

        // …but PRESERVES lifetime latches.
        Assert.IsType<BarkOutcome.Surfaced>(h.Raise("ChaosDefuseFirst", guaranteed: true));
        h.Pipeline.LoadRules(h.Rules.Concat([sessionOnce]));
        Assert.Equal("already-fired (Lifetime)",
            Assert.IsType<BarkOutcome.Gated>(h.Raise("ChaosDefuseFirst")).Reason);
    }

    // ---------- variant selection ----------

    [Fact]
    public void Variants_NoRepeatUntilExhaustion_RecycleExcludesLast()
    {
        // rng=0 always picks candidates[0]; with 3 variants the first three fires must be
        // DISTINCT line ids (no-repeat), and the fourth (recycle) must differ from the third.
        // (Variant 3 is text-only by design — the outcome type follows the variant, the
        // LINE IDENTITY is what the rotation tracks.)
        var h = NewHarness(rng: () => 0.0);
        var seen = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            seen.Add(LineIdOf(h.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 }, guaranteed: true)));
        }

        Assert.Equal(3, seen.Distinct().Count());

        var fourth = LineIdOf(h.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 }, guaranteed: true));
        Assert.NotEqual(seen[2], fourth); // recycle reseed excludes the just-played line
    }

    private static string LineIdOf(BarkOutcome outcome) => outcome switch
    {
        BarkOutcome.Surfaced s => s.Payload.LineId,
        BarkOutcome.SurfacedTextOnly t => t.Payload.LineId,
        _ => throw new Xunit.Sdk.XunitException($"expected a surfaced outcome, got {outcome.GetType().Name}"),
    };

    [Fact]
    public async Task Variants_Rotation_Persists_Across_Pipeline_Instances()
    {
        using var dir = new TempDir();
        var path = dir.Path("companion.json");
        var h1 = NewHarness(storePath: path, rng: () => 0.0);
        var first = Assert.IsType<BarkOutcome.Surfaced>(
            h1.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 }, guaranteed: true)).Payload.LineId;
        await h1.Pipeline.FlushAsync();

        // A fresh pipeline over the SAME store restores the rotation (WPF LoadRotationFromSettings).
        var h2 = NewHarness(storePath: path, rng: () => 0.0);
        var second = Assert.IsType<BarkOutcome.Surfaced>(
            h2.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 }, guaranteed: true)).Payload.LineId;
        Assert.NotEqual(first, second);
    }

    // ---------- payload integrity ----------

    [Fact]
    public void Payload_OneUnit_Substitution_Emotion_LineIdentity()
    {
        var h = NewHarness(liveFields: f => f == "focused_app" ? "Code" : null);
        BarkPayload? surfaced = null;
        h.Pipeline.BarkSurfaced += p => surfaced = p;

        var outcome = h.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 });
        var s = Assert.IsType<BarkOutcome.Surfaced>(outcome);

        // The event payload and the outcome payload are the SAME immutable unit (never torn).
        Assert.Same(s.Payload, surfaced);
        Assert.Contains("Wave 3", s.Payload.Text); // {wave} substitution
        Assert.Equal("chaos_wave_cleared", s.Payload.RuleId);
        Assert.Equal("pleased, teasing", s.Payload.Mood); // free string, passed through untouched
        Assert.False(s.Payload.IsAi);
        if (s.Payload.AudioPath is not null)
        {
            Assert.Equal("resolver-audio", s.Payload.AudioPath);
            Assert.NotNull(s.Payload.EmotionLineId); // audio filename stem
            Assert.StartsWith("Bark:chaos_wave_cleared:", s.Payload.LineId);
        }
    }

    [Fact]
    public void Payload_Substitution_FocusedApp_TokenZero()
    {
        var h = NewHarness(liveFields: f => f == "focused_app" ? "Player" : null);
        var rules = new[]
        {
            new BarkRule
            {
                Id = "sub", Trigger = "Sub",
                VariantPool = [new BarkVariant("watching {0} in {place}", null)],
            },
        };
        h.Pipeline.LoadRules(rules);
        var outcome = Assert.IsType<BarkOutcome.SurfacedTextOnly>(
            h.Raise("Sub", new() { ["place"] = "the zone" }));
        Assert.Equal("watching Player in the zone", outcome.Payload.Text);
        Assert.Equal(BarkSuppression.NoAudioAsset, outcome.Reason);
    }

    [Fact]
    public void LineId_TextOnly_StableSlug_AudioStemStable()
    {
        // Slugify (CompanionPhraseService.cs:84-95): drop {tokens}, lowercase, non-alnum→space.
        Assert.Equal("giggles you clicked it", BarkText.Slugify("*giggles* You {0} clicked it~"));
        Assert.Equal("Bark:r:chaos_wave_cleared_1",
            BarkText.LineId("r", new BarkVariant("whatever", "chaos_wave_cleared_1.mp3")));
        Assert.Equal("Bark:r:t_hello", // the {wave} token drops entirely (WPF Slugify)
            BarkText.LineId("r", new BarkVariant("Hello {wave}!", null)));
    }

    // ---------- mute / suppression ----------

    [Fact]
    public void Mute_TextOnly_Typed_NoVoiceCall_EventStillFires()
    {
        var h = NewHarness();
        var playersBefore = h.Backend.Players.Count;
        BarkPayload? surfaced = null;
        h.Pipeline.BarkSurfaced += p => surfaced = p;
        h.Pipeline.Muted = true;

        var outcome = Assert.IsType<BarkOutcome.SurfacedTextOnly>(
            h.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 }));
        Assert.Equal(BarkSuppression.Muted, outcome.Reason);
        Assert.Equal(playersBefore, h.Backend.Players.Count); // never a voice call
        Assert.NotNull(surfaced); // the surface fires — mute is NEVER silent
    }

    [Fact]
    public void VolumeZero_TextOnly_Egg_SkipsResolution()
    {
        var h = NewHarness();
        h.Pipeline.MasterVolume = 0;
        var outcome = Assert.IsType<BarkOutcome.SurfacedTextOnly>(
            h.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 }));
        Assert.Equal(BarkSuppression.VolumeZero, outcome.Reason);
        Assert.Empty(h.Resolver.Calls); // volume-zero skips audio resolution (WPF egg :1590-1598)
    }

    [Fact]
    public void Suppression_NoAudioAsset_Vs_ResolveFailed_Distinct()
    {
        // Consult binding 4b: the two text-only reasons never collapse.
        var h = NewHarness();
        // chaos_detonated chance-gated: rng 0.1 fires; variants[1] has NO audio → NoAudioAsset…
        // (rng picks candidates[0] = the audio variant; use a dedicated rule for determinism)
        h.Pipeline.LoadRules(
        [
            new BarkRule { Id = "noaudio", Trigger = "NA", VariantPool = [new BarkVariant("text only", null)] },
            new BarkRule { Id = "missing", Trigger = "M", VariantPool = [new BarkVariant("with audio", "gone.mp3")] },
        ]);
        var noAsset = Assert.IsType<BarkOutcome.SurfacedTextOnly>(h.Raise("NA"));
        Assert.Equal(BarkSuppression.NoAudioAsset, noAsset.Reason);
        Assert.Null(noAsset.Payload.EmotionLineId);

        var resolveFailed = Assert.IsType<BarkOutcome.SurfacedTextOnly>(h.Raise("M", guaranteed: true));
        Assert.Equal(BarkSuppression.AudioResolveFailed, resolveFailed.Reason);
        Assert.Equal("gone", resolveFailed.Payload.EmotionLineId); // stem survives the miss
    }

    [Fact]
    public void Suppression_AudioUnavailable_TornDownArbitration_NeverThrows()
    {
        // TryStart-pattern compliance: the pipeline adds NO player-start paths; a torn-down
        // arbitration yields typed Unavailable → text-only surface, never an exception.
        var h = NewHarness();
        h.Arb.Dispose();
        var outcome = Assert.IsType<BarkOutcome.SurfacedTextOnly>(
            h.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 }));
        Assert.Equal(BarkSuppression.AudioUnavailable, outcome.Reason);
    }

    // ---------- priority routing ----------

    [Fact]
    public void Routing_Priority_Preempts_ClearingQueue_Ordinary_Queues()
    {
        var h = NewHarness();
        // Queue two ordinary lines via direct arbitration (behind a playing voice).
        h.Arb.PlayVoice("base.wav", 0.5f);
        h.Arb.QueueVoice("q1.wav", 0.5f);
        h.Arb.QueueVoice("q2.wav", 0.5f);
        Assert.Equal(2, h.Arb.QueuedVoiceCount);

        // Priority bark (>= 100) → PlayVoicePriority: queue cleared + immediate start (WPF :319-360).
        var outcome = Assert.IsType<BarkOutcome.Surfaced>(
            h.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 }));
        Assert.True(outcome.Priority);
        Assert.Equal(0, h.Arb.QueuedVoiceCount);
        Assert.True(h.Backend.Players[^1].Playing);

        // Ordinary bark (priority 90 < 100) while voice idle → QueueVoice (not a preempt).
        h.Backend.Players[^1].RaiseEnded(); // voice completes
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        var ordinary = Assert.IsType<BarkOutcome.Surfaced>(h.Raise("ChaosBubbleDetonated"));
        Assert.False(ordinary.Priority);
    }

    // ---------- pacing (TEXT-derived, WPF Speech.cs:148-168) ----------

    [Fact]
    public void Pacing_WpfFormula_DirectCases()
    {
        // The pure formula against the WPF cases (Speech.cs:148-168):
        Assert.Equal(2.0, BarkPipeline.ComputePacingSeconds(50, previousIsAi: false));  // floor
        Assert.Equal(3.0, BarkPipeline.ComputePacingSeconds(150, previousIsAi: false)); // +50 chars × 0.02
        Assert.Equal(7.0, BarkPipeline.ComputePacingSeconds(50, previousIsAi: true));   // +5 AI bonus
        Assert.Equal(9.0, BarkPipeline.ComputePacingSeconds(200, previousIsAi: true));  // 2 + 5 + 100×0.02
        Assert.Equal(2.0, BarkPipeline.ComputePacingSeconds(100, previousIsAi: false)); // threshold exact → no bonus
    }

    [Fact]
    public void Pacing_LastHandedTracking_ThroughRealArbitration()
    {
        var h = NewHarness();
        var longRule = new BarkRule
        {
            Id = "long", Trigger = "Long", Priority = 0,
            VariantPool = [new BarkVariant(new string('x', 150), "long.mp3")],
        };
        var shortRule = new BarkRule
        {
            Id = "short", Trigger = "Short", Priority = 0,
            VariantPool = [new BarkVariant("tiny", "short.mp3")],
        };
        h.Pipeline.LoadRules([longRule, shortRule]);

        // First ordinary line: queued, then the ASAP pacing timer starts it.
        Assert.IsType<BarkOutcome.Surfaced>(h.Raise("Long"));
        h.Clock.Advance(TimeSpan.Zero); // fires the due-immediately pacing timer
        Assert.Single(h.Backend.Players);
        Assert.True(h.Backend.Players[0].Playing);
        h.Backend.Players[0].RaiseEnded(); // voice ends; pipeline's last-handed = (150, not-AI)
        Assert.False(h.Arb.VoiceActive);

        // Next ordinary line derives its gap from the line AHEAD (consult binding 1):
        // 2.0 + (150-100)*0.02 = 3.0s — not the 2.0s floor of the line before "Long".
        Assert.IsType<BarkOutcome.Surfaced>(h.Raise("Short", guaranteed: true));
        Assert.Equal(1, h.Arb.QueuedVoiceCount);
        Assert.Single(h.Backend.Players); // no player yet — pacing timer pending

        h.Clock.Advance(TimeSpan.FromSeconds(2.9));
        Assert.Single(h.Backend.Players);
        h.Clock.Advance(TimeSpan.FromSeconds(0.2)); // 3.1s > 3.0s debt
        Assert.Equal(2, h.Backend.Players.Count);
        Assert.True(h.Backend.Players[1].Playing);
    }

    // ---------- disabled-phrase persistence (SP-005 machinery) ----------

    [Fact]
    public async Task Disabled_AllVariants_EmptyPoolGate_ReEnableRoundTrips()
    {
        using var dir = new TempDir();
        var path = dir.Path("companion.json");
        var h = NewHarness(storePath: path, rng: () => 0.0);

        // Disable every variant of the rule → the empty-pool gate (WPF :1172-1180).
        var rule = h.Rules.Single(r => r.Id == "chaos_wave_cleared");
        foreach (var v in rule.VariantPool)
        {
            h.Pipeline.DisablePhrase(BarkText.LineId(rule.Id, v));
        }

        var gated = Assert.IsType<BarkOutcome.Gated>(h.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 }));
        Assert.Equal("empty-pool", gated.Reason);

        // Round-trip through the REAL store: a fresh pipeline over the same file still sees them disabled.
        await h.Pipeline.FlushAsync();
        var h2 = NewHarness(storePath: path, rng: () => 0.0);
        Assert.Equal("empty-pool",
            Assert.IsType<BarkOutcome.Gated>(h2.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 })).Reason);

        // Re-enable one → selection works again at the NEXT raise (no caching).
        h2.Pipeline.EnablePhrase(BarkText.LineId(rule.Id, rule.VariantPool[0]));
        Assert.IsType<BarkOutcome.Surfaced>(h2.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 }, guaranteed: true));
    }

    [Fact]
    public async Task Disabled_CorruptStore_Quarantined_TypedDegraded_NothingSilentlyDisabled()
    {
        using var dir = new TempDir();
        var path = dir.Path("companion.json");
        File.WriteAllText(path, "garbage {{{ not json");

        var h = NewHarness(storePath: path, rng: () => 0.0);
        var quarantined = Assert.IsType<LoadOutcome.Quarantined>(h.Store.LastLoadOutcome);
        Assert.True(h.Store.LastLoadOutcome!.IsDegraded);
        Assert.True(File.Exists(quarantined.BackupPath)); // original bytes preserved
        // Flagged defaults: NOTHING disabled — the bark fires (never silent degradation).
        Assert.IsType<BarkOutcome.Surfaced>(h.Raise("ChaosWaveCleared", new() { ["wave"] = 3.0 }));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LifetimeLatch_Persists_Across_Instances()
    {
        using var dir = new TempDir();
        var path = dir.Path("companion.json");
        var h1 = NewHarness(storePath: path, rng: () => 0.0);
        Assert.IsType<BarkOutcome.Surfaced>(h1.Raise("ChaosDefuseFirst"));
        await h1.Pipeline.FlushAsync();

        var h2 = NewHarness(storePath: path, rng: () => 0.0);
        Assert.Equal("already-fired (Lifetime)",
            Assert.IsType<BarkOutcome.Gated>(h2.Raise("ChaosDefuseFirst")).Reason);
    }

    // ---------- harness ----------

    private sealed class RecordingResolver : IBarkAudioResolver
    {
        public List<string> Calls { get; } = [];

        public string? Resolve(string audioFileName)
        {
            Calls.Add(audioFileName);
            // Every .mp3 resolves EXCEPT files named "gone.mp3".
            return audioFileName == "gone.mp3" ? null : "resolver-audio";
        }
    }

    private sealed class Harness
    {
        public required FakeBackend Backend { get; init; }
        public required ManualClock Clock { get; init; }
        public required SoundArbitration Arb { get; init; }
        public required PersistenceStore<CompanionStateDocument> Store { get; init; }
        public required BarkPipeline Pipeline { get; init; }
        public required RecordingResolver Resolver { get; init; }
        public required IReadOnlyList<BarkRule> Rules { get; init; }

        public BarkOutcome Raise(string trigger, Dictionary<string, object?>? fills = null, bool guaranteed = false) =>
            Pipeline.Raise(trigger, fills, guaranteed);
    }

    private static Harness NewHarness(
        string? storePath = null,
        Func<double>? rng = null,
        Func<string, object?>? liveFields = null)
    {
        var backend = new FakeBackend();
        var clock = new ManualClock();
        var arb = new SoundArbitration(
            backend, new FakeDuckSink(), clock, new SoundArbitrationOptions(), _ => { });
        arb.Initialize(null);

        var dir = storePath is null ? new TempDir() : null;
        var path = storePath ?? dir!.Path("companion.json");
        var store = new PersistenceStore<CompanionStateDocument>(
            new OperationRegistry().OwnerFor("CompanionState"),
            new ListLogSink(),
            path,
            CompanionStateDocument.CurrentSchemaVersion);
        store.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        var resolver = new RecordingResolver();
        var rules = BarkRuleLoader.Parse(DefaultBarkRules.ManifestJson, _ => { });
        var pipeline = new BarkPipeline(
            arb, store, resolver, rules,
            new BarkPipelineOptions { Clock = clock, Rng = rng ?? (() => 0.0), LiveFields = liveFields },
            _ => { });
        return new Harness
        {
            Backend = backend, Clock = clock, Arb = arb, Store = store,
            Pipeline = pipeline, Resolver = resolver, Rules = rules,
        };
    }

    // ---------- fakes (SoundArbitrationTests shape, self-contained copy) ----------

    private sealed class FakeBackend : IAudioBackend
    {
        public List<FakePlayer> Players { get; } = [];

        public IReadOnlyList<string> EnumerateDevices() => ["Fake Endpoint"];

        public bool TryInit(string? deviceName, out string? error)
        {
            error = null;
            return true;
        }

        public IAudioPlayer CreatePlayer(string path, float volume)
        {
            var p = new FakePlayer();
            Players.Add(p);
            return p;
        }

        public void Dispose() { }
    }

    private sealed class FakePlayer : IAudioPlayer
    {
        public bool Playing { get; private set; }

        public event EventHandler? PlaybackEnded;
        public AudioPlayerState State => Playing ? AudioPlayerState.Playing : AudioPlayerState.Stopped;
        public double PositionSec => 0;
        public float Volume { get; set; }

        public void Play() => Playing = true;
        public void Pause() { }
        public void Stop() => Playing = false;
        public void Dispose() { }
        public void RaiseEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeDuckSink : IAudioDuckSink
    {
        public bool TryApply(float strength, out string? error)
        {
            error = "typed unavailable";
            return false;
        }

        public void Restore() { }
    }

    private sealed class ManualClock : ISoundClock
    {
        private sealed class Entry
        {
            public DateTimeOffset Due;
            public required Action Fire;
            public bool Cancelled;
        }

        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            _timers.Add(entry);
            return new CancelHandle(entry);
        }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            while (true)
            {
                var next = _timers.Where(t => !t.Cancelled && t.Due <= UtcNow).OrderBy(t => t.Due).FirstOrDefault();
                if (next is null)
                {
                    return;
                }

                _timers.Remove(next);
                next.Fire();
            }
        }

        private sealed class CancelHandle(Entry entry) : IDisposable
        {
            public void Dispose() => entry.Cancelled = true;
        }
    }

    private sealed class ListLogSink : ILogSink
    {
        public void Log(string message) { }
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ccp-bark-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_root);

        public string Path(string fileName) => System.IO.Path.Combine(_root, fileName);

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
