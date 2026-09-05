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
/// pure predicates the daily and weekly rolls call, with the camera probe injected, so nothing
/// here touches a real device.
/// </summary>
public class QuestHardwareRollTests
{
    private static readonly DateTime AnyDay = new(2026, 9, 5);

    private static List<QuestDefinition> DailyPool(bool hasCamera) =>
        QuestService.FilterDailyRollPool(
            QuestDefinition.DailyQuests, excludeId: null, hasPremium: true,
            today: AnyDay, applyDateWindow: true, hasCamera: hasCamera);

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
        var gate = new QuestHardwareGate(() => { probes++; return false; });
        Assert.False(gate.HasCamera());
        gate.HasCamera();
        gate.HasCamera();
        Assert.Equal(1, probes);
    }
}
