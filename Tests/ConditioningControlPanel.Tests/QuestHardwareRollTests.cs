using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs#1151: a machine with no webcam was still dealt the blink-trainer quests, which it can
/// never complete - and rerolling could deal another one straight back. These exercise the same
/// pure predicates the daily and weekly rolls call, with the probes injected, so nothing here
/// touches a real device.
/// </summary>
public class QuestHardwareRollTests
{
    private static readonly DateTime AnyDay = new(2026, 9, 5);

    private static List<QuestDefinition> DailyPool(bool hasCamera, bool hasMicrophone = true) =>
        QuestService.FilterDailyRollPool(
            QuestDefinition.DailyQuests, excludeId: null, hasPremium: true,
            today: AnyDay, applyDateWindow: true, hasCamera: hasCamera, hasMicrophone: hasMicrophone);

    private static QuestDefinition Blink(string id) =>
        QuestDefinition.DailyQuests.Concat(QuestDefinition.WeeklyQuests).First(q => q.Id == id);

    [Fact]
    public void DailyRoll_WithNoCamera_NeverYieldsABlinkTrainerQuest()
    {
        var pool = DailyPool(hasCamera: false);
        Assert.NotEmpty(pool);   // the day is never lost, only the blink quests
        Assert.DoesNotContain(pool, q => q.Category == QuestCategory.BlinkTrainer);
        Assert.DoesNotContain(pool, q => q.Id == "blink_drill_d" || q.Id == "obedient_eyes_d");
    }

    [Fact]
    public void DailyRoll_WithACamera_CanStillYieldABlinkTrainerQuest()
    {
        var pool = DailyPool(hasCamera: true);
        Assert.Contains(pool, q => q.Id == "blink_drill_d");
        Assert.Contains(pool, q => q.Id == "obedient_eyes_d");
    }

    [Fact]
    public void WeeklyRoll_WithNoCamera_NeverYieldsABlinkTrainerQuest()
    {
        var full = QuestDefinition.WeeklyQuests.ToList();
        var gated = QuestHardwareGate.GateOrFallBack(full, hasCamera: false);
        Assert.NotEmpty(gated);
        Assert.DoesNotContain(gated, q => q.Category == QuestCategory.BlinkTrainer);
        Assert.Contains(QuestHardwareGate.GateOrFallBack(full, hasCamera: true), q => q.Id == "blink_century_w");
    }

    [Fact]
    public void GateNeverEmptiesThePool_ARollMustNeverFail()
    {
        // The pathological pool: everything left needs the camera this machine does not have. An
        // empty slot is worse than an impossible one, and the player still has their rerolls.
        var blinkOnly = QuestDefinition.DailyQuests.Where(q => q.Category == QuestCategory.BlinkTrainer).ToList();
        Assert.NotEmpty(blinkOnly);
        Assert.Equal(blinkOnly.Count, QuestHardwareGate.GateOrFallBack(blinkOnly, hasCamera: false).Count);
    }

    [Fact]
    public void ProbeThatThrows_IsReadAsCameraPresent_NeverAsAbsent()
    {
        // Fail OPEN: narrowing the pool because an enumeration blew up would take quests away
        // from people who own the camera.
        var gate = new QuestHardwareGate(() => throw new InvalidOperationException("enumeration exploded"));
        Assert.True(gate.HasCamera());
    }

    [Fact]
    public void ProbeIsRunOnceAndCached_SoAThreeSeatRollEnumeratesOnce()
    {
        int probes = 0;
        var gate = new QuestHardwareGate(() => { probes++; return false; }, () => true);
        Assert.False(gate.HasCamera());
        gate.HasCamera();
        gate.HasCamera();
        Assert.Equal(1, probes);
    }

    // ---- THE KEEP GATE. The half that was missing, and the reason #1151 kept reproducing: the
    // roll was gated but nothing ever re-examined a quest already sitting on the board.

    [Fact]
    public void KeepGate_DropsABlinkQuestAlreadyOnTheBoard_WhenTheCameraIsGone()
    {
        Assert.True(QuestHardwareGate.NeedsAbsentHardware(Blink("obedient_eyes_d"), hasCamera: false, hasMicrophone: true));
        Assert.True(QuestHardwareGate.NeedsAbsentHardware(Blink("blink_century_w"), hasCamera: false, hasMicrophone: true));
    }

    [Fact]
    public void KeepGate_LeavesEveryOtherQuestAlone()
    {
        foreach (var q in QuestDefinition.DailyQuests.Concat(QuestDefinition.WeeklyQuests))
        {
            var expected = q.Category == QuestCategory.BlinkTrainer;
            Assert.Equal(expected, QuestHardwareGate.NeedsAbsentHardware(q, hasCamera: false, hasMicrophone: false));
        }
    }

    [Fact]
    public void KeepGate_IsSilentWhenTheHardwareIsThere()
    {
        foreach (var q in QuestDefinition.DailyQuests.Concat(QuestDefinition.WeeklyQuests))
            Assert.False(QuestHardwareGate.NeedsAbsentHardware(q, hasCamera: true, hasMicrophone: true));
    }

    // ---- THE MICROPHONE. No embedded category needs one (mantras are typed), so the mic half of
    // the report is served by the definitions channel's `requiresHardware` field.

    [Fact]
    public void NoEmbeddedQuestNeedsAMicrophone_SoAMiclessMachineLosesNothing()
    {
        var all = QuestDefinition.DailyQuests.Concat(QuestDefinition.WeeklyQuests).ToList();
        Assert.DoesNotContain(all, q => QuestHardwareGate.NeedsMicrophone(q));
        Assert.Equal(
            DailyPool(hasCamera: true, hasMicrophone: true).Count,
            DailyPool(hasCamera: true, hasMicrophone: false).Count);
    }

    [Theory]
    [InlineData("microphone")]
    [InlineData("mic")]
    [InlineData("MIC")]
    [InlineData("camera, microphone")]
    public void AServerQuestDeclaringAMicrophone_IsGatedOnAMiclessMachine(string declared)
    {
        var quest = new QuestDefinition("spoken_w", "Spoken", "", QuestType.Weekly,
            QuestCategory.Mantra, 10, 100, "*") { RequiresHardware = declared };

        Assert.True(QuestHardwareGate.NeedsAbsentHardware(quest, hasCamera: true, hasMicrophone: false));
        Assert.False(QuestHardwareGate.NeedsAbsentHardware(quest, hasCamera: true, hasMicrophone: true));

        var pool = new List<QuestDefinition> { QuestDefinition.WeeklyQuests[0], quest };
        Assert.DoesNotContain(
            QuestHardwareGate.GateOrFallBack(pool, hasCamera: true, hasMicrophone: false),
            q => q.Id == "spoken_w");
    }

    [Fact]
    public void AnUnrecognisedHardwareWord_GatesNothing_ATypoMustNotCostAQuest()
    {
        var quest = new QuestDefinition("odd_w", "Odd", "", QuestType.Weekly,
            QuestCategory.Flash, 10, 100, "*") { RequiresHardware = "cammera" };
        Assert.False(QuestHardwareGate.NeedsAbsentHardware(quest, hasCamera: false, hasMicrophone: false));
    }

    // ---- RESOLUTION. The fail-open must be temporary, or it is just the old bug with a comment.

    [Fact]
    public void ATimedOutProbe_ReadsPresentButUnresolved_SoTheBoardIsRecheckedLater()
    {
        using var released = new System.Threading.ManualResetEventSlim(false);
        var gate = new QuestHardwareGate(
            () => { released.Wait(TimeSpan.FromSeconds(5)); return false; },
            () => true);

        var duringProbe = gate.Snapshot();
        Assert.True(duringProbe.HasCamera);      // fail open: nobody loses a quest to a slow probe
        Assert.False(duringProbe.Resolved);      // ...but the decision is explicitly not trusted

        released.Set();
        var settled = SpinUntilResolved(gate);
        Assert.True(settled.Resolved);
        Assert.False(settled.HasCamera);         // the real answer, in time for the recheck
    }

    // ---- THE CAMERA PROBE'S THREE ANSWERS. A camera that cannot be COUNTED is not a camera that
    // is MISSING: the enumerators the probe calls used to turn a broken COM registration, a wedged
    // driver or a privacy block into an empty list, and an empty list reads as absent AND RESOLVED
    // - the one combination this gate must never produce by accident, because it is permanent.

    [Fact]
    public void AnEnumerationThatThrows_ReadsPresentAndUNRESOLVED()
    {
        int probes = 0;
        var gate = new QuestHardwareGate(
            () => { System.Threading.Interlocked.Increment(ref probes); throw new InvalidOperationException("COM registration is broken"); },
            () => true);

        // Snapshot kicks the probe off and waits on it; a loaded runner may need more than the
        // gate's 400ms budget, and the in-flight task is reused rather than probed again.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        QuestHardwareState snapshot;
        do { snapshot = gate.Snapshot(); }
        while (System.Threading.Volatile.Read(ref probes) == 0 && DateTime.UtcNow < deadline);

        Assert.Equal(1, System.Threading.Volatile.Read(ref probes));   // it ran, and it threw
        Assert.True(snapshot.HasCamera);   // fail open: nobody loses four blink quests to a throw
        Assert.False(snapshot.Resolved);   // ...and the recheck is armed, so it is not forever
    }

    [Fact]
    public void ACleanEnumerationThatFoundNothing_ReadsAbsentAndRESOLVED()
    {
        var snapshot = SpinUntilResolved(new QuestHardwareGate(() => false, () => true));
        Assert.False(snapshot.HasCamera);
        Assert.True(snapshot.Resolved);    // the only way to say "this machine has no camera"
    }

    [Fact]
    public void ACleanEnumerationThatFoundACamera_ReadsPresentAndRESOLVED()
    {
        var snapshot = SpinUntilResolved(new QuestHardwareGate(() => true, () => true));
        Assert.True(snapshot.HasCamera);
        Assert.True(snapshot.Resolved);
    }

    [Fact]
    public void TheDevicePickersEnumeration_StillSwallowsItsOwnFailures()
    {
        // The other half of the fix: the strict form is for the gate alone. The webcam tracking
        // feature keeps the forgiving one, so a device list that cannot be built is still an empty
        // picker rather than an exception in the Lab tab.
        Assert.Null(Record.Exception(() => WebcamDeviceEnumerator.Enumerate()));
    }

    [Fact]
    public void ASettledProbe_IsResolved_SoNoRecheckIsArmedForever()
    {
        var gate = new QuestHardwareGate(() => false, () => false);
        var snapshot = SpinUntilResolved(gate);
        Assert.True(snapshot.Resolved);
        Assert.False(snapshot.HasCamera);
        Assert.False(snapshot.HasMicrophone);
    }

    private static QuestHardwareState SpinUntilResolved(QuestHardwareGate gate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        QuestHardwareState snapshot;
        do { snapshot = gate.Snapshot(); }
        while (!snapshot.Resolved && DateTime.UtcNow < deadline);
        return snapshot;
    }
}
