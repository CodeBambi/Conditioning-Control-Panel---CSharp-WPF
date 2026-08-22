using CcpClient.Desktop.Haptics;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The C+ evaluator and its scheduler: what reaches the sink, at what level, at what
/// moment, and what deliberately never reaches it at all.
///
/// <para><b>Not one wall-clock wait, and not one poll.</b> Every timing here is driven through the
/// limb's injected <see cref="ISessionClock"/> by hand, which is also the evidence for the packet's
/// central architectural claim: a limb with a 10 Hz loop could not be advanced by a clock nobody
/// ticks, and one of the facts below asserts that a latched layer produces <b>no</b> further traffic
/// across a simulated minute.</para>
///
/// <para><b>Nothing here shows that a device moved.</b> The sink is a recording double; the product
/// sink refuses and has no device to address. The manual gate on
/// <c>HapticSinkFactory.DeviceManualGate</c> is untouched by every fact in this file.</para>
/// </summary>
public class HapticLimbTests
{
    // =====================================================================================
    //  THE DOUBLE ITSELF — a recorded hazard, refused in advance
    // =====================================================================================

    [Fact]
    public async Task TheRecordingSinkRecordsRAWAndTransformsNOTHING()
    {
        // A double whose Write clamped laundered the very clamp its fact existed to test.
        // This fact exists so that a future "tidy" of RecordingHapticSink — a clamp, a rounding, a
        // defensive copy, a de-duplication — reds HERE instead of silently making every level
        // assertion in this file vacuous.
        var sink = new RecordingHapticSink();
        var outputs = new[]
        {
            new HapticOutput(0, HapticLevel.Of(0.123456789)),
            new HapticOutput(7, HapticLevel.Of(1.0)),
            new HapticOutput(2, HapticLevel.Silent),
        };

        await sink.SetOutputsAsync("buttplug:0", outputs, TestContext.Current.CancellationToken);
        await sink.SetOutputsAsync("buttplug:0", outputs, TestContext.Current.CancellationToken);

        Assert.Equal(2, sink.Commands.Count);
        var recorded = sink.Commands[0];
        // The SAME list, by reference: nothing was copied, so nothing could have been changed on the
        // way in.
        Assert.Same(outputs, recorded.Outputs);
        Assert.Equal("buttplug:0", recorded.DeviceKey);
        Assert.Equal(0.123456789, recorded.Outputs[0].Level.Value, 12);
        Assert.Equal(7, recorded.Outputs[1].ActuatorIndex);
        Assert.True(recorded.Outputs[2].Level.IsSilent);
        // Two identical commands are TWO records. A double that suppressed the repeat would hide
        // exactly the kind of stuck level this evidence exists to catch.
        Assert.Equal(2, sink.Levels.Count);
    }

    // =====================================================================================
    //  PEAK-OF-SUM — the reason C+ was built rather than C
    // =====================================================================================

    [Fact]
    public void TwoOverlappingSamePriorityTransientsCombineToTheSUMMEDPeak_NotTheMax()
    {
        // THE FACT THIS WHOLE PACKET TURNS ON. Upstream sums transients WITHIN a priority group and
        // takes the max ACROSS groups (HapticMixer.cs:476-503). The flash decay ladder posts at
        // priority 1 (HapticService.cs:786) and the subliminal pulse posts at priority 1 (:880), and
        // BOTH are wired in this build, so this overlap happens in any session with Flash Images and
        // Subliminals armed together.
        var rig = new Rig();

        rig.Do(() => rig.Limb.FlashPlaced());
        rig.Do(() => rig.Limb.SubliminalShown("good girl"));
        rig.AdvanceTo(100);

        // At instant 100 the ladder's first rung is on its plateau (attack 41, hold 250) and the
        // subliminal tap is on its own (attack 8, hold 130). Both are at 0.5.
        // SUM: 0.5 + 0.5 = 1.0 -> x0.7 = 0.70 -> AT the 0.70 cap.
        Assert.Equal(0.70, rig.Limb.LastLevel, 10);
        Assert.Equal(0.70, rig.Sink.Levels[^1], 10);

        // AND WHAT THE DEGRADED RULE WOULD HAVE GIVEN, named so this fact reds rather than merely
        // changing a number nobody reads if the evaluator ever becomes MAX:
        // MAX: max(0.5, 0.5) = 0.5 -> x0.7 = 0.35 -> under the cap. A FACTOR OF TWO on the level a
        // person feels.
        var whatMaxWouldGive = HapticMix.Finish(Math.Max(0.5, 0.5));
        Assert.Equal(0.35, whatMaxWouldGive, 10);
        Assert.NotEqual(whatMaxWouldGive, rig.Limb.LastLevel);
        Assert.Equal(2, Math.Round(rig.Limb.LastLevel / whatMaxWouldGive));
    }

    [Fact]
    public void TwoBouncesInsideOneEnvelopeAlsoSUM_TheSecondReachableInstanceOfTheSameRule()
    {
        // Bounces post at priority 0 (HapticService.cs:821) and therefore sum with EACH OTHER. A
        // bounce renders as a 158 ms envelope, so two wall hits inside that window — routine at a
        // high speed setting in a narrow field — combine upstream and would MAX under option C.
        // This is a second, same-kind reachable instance INSIDE ONE MODULE.
        var rig = new Rig();

        rig.Do(() => rig.Limb.BounceHit());
        rig.Do(() => rig.Limb.BounceHit());
        rig.AdvanceTo(100);

        Assert.Equal(0.70, rig.Limb.LastLevel, 10);
        Assert.NotEqual(HapticMix.Finish(0.5), rig.Limb.LastLevel);
    }

    [Fact]
    public void TransientsInDIFFERENTPriorityGroupsTakeTheMax_AndDoNotSum()
    {
        // The other half of the rule, and it must be executed too: a claim about what a guard cannot
        // do needs the same evidence as a claim about what it can. A bounce is priority 0 and a
        // subliminal is priority 1 (HapticService.cs:821, :880), so they arbitrate rather than add.
        var rig = new Rig();

        rig.Do(() => rig.Limb.BounceHit());
        rig.Do(() => rig.Limb.SubliminalShown("good girl"));
        rig.AdvanceTo(100);

        Assert.Equal(0.35, rig.Limb.LastLevel, 10);
        Assert.NotEqual(0.70, rig.Limb.LastLevel);
    }

    [Fact]
    public void ThreeOverlappingHalvesStillStopAtTheCap_BecauseTheGroupIsClampedAndTheCapIsSafety()
    {
        // HapticMixer.cs:501 clamps a priority group at 1 before the master arithmetic, and :516
        // caps the finished level at 0.70. A burst cannot walk past either.
        var rig = new Rig();

        rig.Do(() => rig.Limb.BounceHit());
        rig.Do(() => rig.Limb.BounceHit());
        rig.Do(() => rig.Limb.BounceHit());
        rig.AdvanceTo(100);

        Assert.Equal(HapticMix.MasterCap, rig.Limb.LastLevel, 10);
    }

    // =====================================================================================
    //  The concurrency cap
    // =====================================================================================

    [Fact]
    public void TheConcurrencyCapDropsANewcomerThatDoesNotOutrankTheWeakestThingPlaying()
    {
        // HapticMixer.cs:894-915 — "the new pulse only gets in by out-ranking the weakest thing
        // playing, otherwise a burst just becomes one flat wall of buzz".
        var rig = new Rig();

        for (var i = 0; i < HapticMix.MaxConcurrentPulses + 1; i++)
        {
            rig.Do(() => rig.Limb.BounceHit());
        }

        Assert.Equal(HapticMix.MaxConcurrentPulses, rig.Limb.ActivePulses);
        Assert.Equal(1, rig.Limb.PulsesDropped);
        Assert.Equal(0, rig.Limb.PulsesEvicted);
    }

    [Fact]
    public void AHigherPriorityArrivalEVICTSTheWeakestInsteadOfBeingDropped()
    {
        var rig = new Rig();

        for (var i = 0; i < HapticMix.MaxConcurrentPulses; i++)
        {
            rig.Do(() => rig.Limb.BounceHit());
        }

        Assert.Equal(HapticMix.MaxConcurrentPulses, rig.Limb.ActivePulses);

        // A subliminal is priority 1 against four priority-0 bounces: it out-ranks the weakest, so
        // one bounce leaves and the arrival gets in.
        rig.Do(() => rig.Limb.SubliminalShown("good girl"));

        Assert.Equal(HapticMix.MaxConcurrentPulses, rig.Limb.ActivePulses);
        Assert.Equal(1, rig.Limb.PulsesEvicted);
        Assert.Equal(0, rig.Limb.PulsesDropped);
    }

    // =====================================================================================
    //  The flash ladder, end to end
    // =====================================================================================

    [Fact]
    public void TheLadderReachesTheSinkAsEightDecayingRungs_AtUpstreamsOwnSpacing()
    {
        var rig = new Rig();
        rig.Do(() => rig.Limb.FlashPlaced());

        for (var rung = 0; rung < HapticEnvelopes.LadderRungs; rung++)
        {
            // Each rung's crest is 41 ms into it (Constant's attack), and rungs start 450 ms apart.
            rig.AdvanceTo((rung * HapticEnvelopes.LadderSpacingMs) + 41);

            var intensity = Math.Max(
                HapticEnvelopes.FlashIntensity * Math.Pow(HapticEnvelopes.LadderDecay, rung),
                HapticMix.MinPerceptibleIntensity);
            Assert.Equal(HapticMix.Finish(intensity), rig.Limb.LastLevel, 10);
            Assert.Equal(HapticMix.Finish(intensity), rig.Sink.Levels[^1], 10);
        }

        // The ladder decays but never falls silent while it plays: the 0.06 floor holds the last
        // three rungs at the same audible level (HapticService.cs:784).
        Assert.Equal(HapticMix.Finish(HapticMix.MinPerceptibleIntensity), rig.Limb.LastLevel, 10);

        // And it ENDS. 7*450 + 353 = 3503 ms, then silence, and then nothing wakes at all.
        rig.AdvanceTo(3503);
        Assert.Equal(0, rig.Limb.LastLevel);
        Assert.Equal(0, rig.Sink.Levels[^1]);
        Assert.Equal(0, rig.Limb.ActivePulses);

        var settled = rig.Sink.Commands.Count;
        rig.AdvanceTo(30_000);
        Assert.Equal(settled, rig.Sink.Commands.Count);
    }

    [Fact]
    public void ASecondFlashREPLACESTheLadderAlreadyRunningRatherThanStackingOnIt()
    {
        // HapticService.cs:774-776 cancels both flash kinds before posting, so a flash of three
        // images restarts the ladder twice instead of summing three of them. It is the one overlap
        // that does NOT sum even upstream, and a limb that got this wrong would triple a flash.
        var rig = new Rig();

        rig.Do(() => rig.Limb.FlashPlaced());
        rig.AdvanceTo(200);
        Assert.Equal(1, rig.Limb.ActivePulses);

        rig.Do(() => rig.Limb.FlashPlaced());
        Assert.Equal(1, rig.Limb.ActivePulses);

        // The new ladder starts from the TOP rung again...
        rig.AdvanceTo(241);
        Assert.Equal(HapticMix.Finish(HapticEnvelopes.FlashIntensity), rig.Limb.LastLevel, 10);
        // ...and it is emphatically not two 0.5 rungs summing to the cap.
        Assert.NotEqual(HapticMix.MasterCap, rig.Limb.LastLevel);
    }

    // =====================================================================================
    //  The continuous layer, its latch, and the absence of a loop
    // =====================================================================================

    [Fact]
    public void TheVideoLayerLatchesAtTheShippedLevelAndProducesNOTrafficWhileItHolds()
    {
        // This is the "no 10 Hz loop" claim, executed. Upstream's mixer would tick ten times a second
        // for the whole clip; this limb evaluates when something changes and not otherwise.
        var rig = new Rig();

        rig.Do(() => rig.Limb.VideoStarted());
        Assert.Equal(HapticEnvelopes.VideoBackgroundLevel(), rig.Limb.Layer, 10);
        Assert.Equal(HapticMix.Finish(HapticEnvelopes.VideoBackgroundLevel()), rig.Limb.LastLevel, 10);
        Assert.Equal(0.06, rig.Limb.LastLevel, 10);

        var settled = rig.Sink.Commands.Count;
        rig.AdvanceTo(60_000);

        // A whole simulated minute of clip, and not one further command: the level is LEVEL-SET and
        // holding it is the provider's business (HapticContracts.cs:70-73).
        Assert.Equal(settled, rig.Sink.Commands.Count);
        Assert.Equal(HapticEnvelopes.VideoBackgroundLevel(), rig.Limb.Layer, 10);

        rig.Do(() => rig.Limb.VideoStopped());
        Assert.Equal(0, rig.Limb.Layer);
        Assert.Equal(0, rig.Limb.LastLevel);
        Assert.Equal(0, rig.Sink.Levels[^1]);
    }

    [Fact]
    public void ATransientRidesOVERTheVideoLayerRatherThanReplacingIt()
    {
        // HapticMixer.cs:506 — raw = max(floor, transient). The clip's floor survives the pulse and
        // the pulse is heard over it.
        var rig = new Rig();

        rig.Do(() => rig.Limb.VideoStarted());
        rig.Do(() => rig.Limb.SubliminalShown("good girl"));
        rig.AdvanceTo(100);
        Assert.Equal(HapticMix.Finish(HapticEnvelopes.SubliminalIntensity), rig.Limb.LastLevel, 10);

        // The tap ends at 158 ms and the floor is still there afterwards.
        rig.AdvanceTo(158);
        Assert.Equal(HapticMix.Finish(HapticEnvelopes.VideoBackgroundLevel()), rig.Limb.LastLevel, 10);
    }

    [Fact]
    public void ARisingFloorIsSlewLimitedAndSchedulesItsOwnContinuation_WhileAFallIsInstant()
    {
        // HapticMixer.cs:470-474 and :73-75 — "Safety, not taste: a session that resumes at 100% with
        // no ramp is how people get hurt." One tick's grant is TickMs/SoftRampMs = 0.125.
        var rig = new Rig();

        rig.Do(() => rig.Limb.SetLayer(0.8));
        Assert.Equal(HapticMix.Finish(0.125), rig.Limb.LastLevel, 10);

        rig.AdvanceTo(100);
        Assert.Equal(HapticMix.Finish(0.25), rig.Limb.LastLevel, 10);

        // The climb continues on its own — there is no poll to carry it, so the evaluator schedules
        // the next step itself.
        rig.AdvanceTo(600);
        Assert.Equal(HapticMix.Finish(0.8), rig.Limb.LastLevel, 10);

        // ...and stops climbing once it arrives.
        var settled = rig.Sink.Commands.Count;
        rig.AdvanceTo(5_000);
        Assert.Equal(settled, rig.Sink.Commands.Count);

        // A FALL is instant: nothing is slew-limited on the way down.
        rig.Do(() => rig.Limb.SetLayer(0));
        Assert.Equal(0, rig.Limb.LastLevel);
    }

    [Fact]
    public void AnAutoZeroedLayerClearsITSELF_SoAnOwnerThatDiesCannotLeaveItHeld()
    {
        // Upstream's auto-zero latch (HapticMixer.cs:661-673). No wired site passes one today — the
        // video layer latches exactly as upstream's does — so this exercises the verb rather than a
        // product path, and it is the safety property the day a producer needs it.
        var rig = new Rig();

        rig.Do(() => rig.Limb.SetLayer(0.5, autoZeroMs: 300));
        Assert.Equal(0.5, rig.Limb.Layer, 10);

        rig.AdvanceTo(299);
        Assert.True(rig.Limb.Layer > 0);

        rig.AdvanceTo(300);
        Assert.Equal(0, rig.Limb.Layer);
        Assert.Equal(0, rig.Limb.LastLevel);
        Assert.Equal(0, rig.Sink.Levels[^1]);
    }

    // =====================================================================================
    //  The gate — central, never at a call site (D204)
    // =====================================================================================

    [Fact]
    public void ACLOSEDGateRefusesTheTransientOutright_AndTheMomentIsStillCounted()
    {
        // D204, preserved rather than "fixed": upstream refuses centrally inside the mixer
        // (HapticMixer.cs:843) and its activity readout fires afterwards unconditionally
        // (HapticService.cs:390, :791, :849), so an unentitled user with no toy still watches the app
        // report what it asked for. A limb that added a gate check at a module would change
        // user-observable behaviour.
        var rig = new Rig { GateOpen = false };

        rig.Do(() => rig.Limb.FlashPlaced());
        rig.Do(() => rig.Limb.SubliminalShown("good girl"));
        rig.AdvanceTo(1_000);

        Assert.Equal(2, rig.Limb.Moments);
        Assert.Equal(2, rig.Limb.Refusals);
        Assert.Equal(0, rig.Limb.ActivePulses);
        Assert.Empty(rig.Sink.Commands);
    }

    [Fact]
    public void ACLOSEDGateSTORESALayerAndDeliversNothing_WhichIsUpstreamsOwnSplit()
    {
        // HapticMixer.SetLayer (:661-678) does NOT consult the gate; the TICK does (:253-258). So a
        // level set while the gate is shut is remembered and simply never sent, and it starts being
        // sent the moment the gate opens without anybody re-issuing it.
        var rig = new Rig { GateOpen = false };

        rig.Do(() => rig.Limb.VideoStarted());
        Assert.Equal(HapticEnvelopes.VideoBackgroundLevel(), rig.Limb.Layer, 10);
        Assert.Empty(rig.Sink.Commands);
        Assert.Equal(0, rig.Limb.Refusals);

        rig.GateOpen = true;
        rig.Do(() => rig.Limb.SetLayer(HapticEnvelopes.VideoBackgroundLevel()));
        Assert.Equal(0.06, rig.Sink.Levels[^1], 10);
    }

    [Fact]
    public void AGateThatTHROWSIsReadAsCLOSED()
    {
        // Upstream's own decision: try { … } catch { return false; } (HapticMixer.cs:198-203). A
        // capability that cannot answer must never be read as permission.
        var clock = new ManualClock();
        var sink = new RecordingHapticSink();
        using var limb = new HapticLimb(
            sink, clock, () => throw new InvalidOperationException("authority unreachable"),
            () => sink.DeviceKeys);

        limb.FlashPlaced();
        clock.Advance(TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(4));

        Assert.Equal(1, limb.Moments);
        Assert.Equal(1, limb.Refusals);
        Assert.Empty(sink.Commands);
    }

    // =====================================================================================
    //  NOTHING MOVES — the product path
    // =====================================================================================

    [Fact]
    public void OnTheREALSinkAWholeSessionOfMomentsSendsABSOLUTELYNOTHING()
    {
        // The central trap, executed. The limb commands correctly and there is no device key to
        // address, because HapticSinkFactory.AdmittedRoutes is empty and nothing was ever asked of a
        // server. The commands are FORMED and never delivered, and that is the honest state of this
        // build.
        var clock = new ManualClock();
        var sink = HapticSinkFactory.Create();
        using var limb = new HapticLimb(sink, clock, () => true, () => []);

        limb.FlashPlaced();
        limb.VideoStarted();
        limb.SubliminalShown("you will drop");
        limb.BounceHit();
        limb.VideoStopped();
        for (var i = 0; i < 40; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(5, limb.Moments);
        Assert.Equal(0, limb.Refusals);
        Assert.Equal(0, limb.Sends);
        Assert.True(limb.EvaluationsWithNoDevice > 0);
        Assert.Equal(limb.Evaluations, limb.EvaluationsWithNoDevice);
        Assert.Null(limb.LastOutcome);
        // And the sink itself was never touched at all — not even to be refused.
        Assert.Equal(0, ((UnadmittedHapticSink)sink).RefusedCalls);
    }

    // =====================================================================================
    //  Teardown
    // =====================================================================================

    [Fact]
    public void CLEARDropsEverythingAndCancelsEveryScheduledWake_WithoutTouchingTheSink()
    {
        // Upstream's ClearAll (HapticMixer.cs:1044-1060): drop the transients, zero the layers, and
        // leave the provider to the ONE all-stop its owner runs. A limb that all-stopped for itself
        // would spend the stop budget twice on the teardown path.
        var rig = new Rig();

        rig.Do(() => rig.Limb.FlashPlaced());
        rig.Do(() => rig.Limb.VideoStarted());
        Assert.Equal(1, rig.Limb.ActivePulses);

        rig.Limb.Clear();

        Assert.Equal(0, rig.Limb.ActivePulses);
        Assert.Equal(0, rig.Limb.Layer);
        Assert.Equal(0, rig.Sink.StopAlls);

        var settled = rig.Sink.Commands.Count;
        rig.AdvanceTo(10_000);
        Assert.Equal(settled, rig.Sink.Commands.Count);
    }

    [Fact]
    public void ADISPOSEDLimbCommandsNothingAndSchedulesNothing()
    {
        var rig = new Rig();
        rig.Do(() => rig.Limb.FlashPlaced());
        var settled = rig.Sink.Commands.Count;

        rig.Limb.Dispose();
        rig.Limb.FlashPlaced();
        rig.Limb.VideoStarted();
        rig.Limb.BounceHit();
        rig.AdvanceTo(10_000);

        Assert.Equal(settled, rig.Sink.Commands.Count);
        Assert.Equal(0, rig.Limb.ActivePulses);
        Assert.Equal(0, rig.Limb.Layer);
    }

    // =====================================================================================
    //  Rig and doubles
    // =====================================================================================

    private sealed class Rig
    {
        private long _at;

        public Rig(int devices = 1)
        {
            Sink = new RecordingHapticSink(devices: devices);
            Limb = new HapticLimb(Sink, Clock, () => GateOpen, () => Sink.DeviceKeys);
        }

        public ManualClock Clock { get; } = new();

        public RecordingHapticSink Sink { get; }

        public HapticLimb Limb { get; }

        public bool GateOpen { get; set; } = true;

        /// <summary>Issue a command and let its zero-delay wake run AT THE CURRENT INSTANT, so every
        /// wake this limb schedules afterwards is dated from where the clock really is. Without this
        /// the manual clock's jump would date a whole envelope from the end of the jump.</summary>
        public void Do(Action command)
        {
            command();
            Clock.Advance(TimeSpan.Zero);
        }

        /// <summary>Move to an absolute instant on the limb's own timeline.</summary>
        public void AdvanceTo(long milliseconds)
        {
            Clock.Advance(TimeSpan.FromMilliseconds(milliseconds - _at));
            _at = milliseconds;
        }
    }

    /// <summary>
    /// The manual clock, in the established shape: due timers fire in due order inside
    /// <see cref="Advance"/>, and a timer a callback schedules fires in the same pass when it is
    /// already due. Zero wall-clock.
    /// </summary>
    private sealed class ManualClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            lock (_timers)
            {
                _timers.Add(entry);
            }

            return new Handle(this, entry);
        }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => t.Due <= UtcNow).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null)
                    {
                        return;
                    }

                    _timers.Remove(next);
                }

                next.Fire();
            }
        }

        private sealed class Entry
        {
            public DateTimeOffset Due { get; init; }

            public required Action Fire { get; init; }
        }

        private sealed class Handle(ManualClock clock, Entry entry) : IDisposable
        {
            public void Dispose()
            {
                lock (clock._timers)
                {
                    clock._timers.Remove(entry);
                }
            }
        }
    }
}
