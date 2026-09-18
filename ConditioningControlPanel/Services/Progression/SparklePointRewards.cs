using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

public enum SparklePointSource { LevelUp, BubbleMilestone }

/// <summary>A settled local credit, distinct from a balance restored or received from another device.</summary>
public readonly record struct SparklePointAward(int Amount, int Balance, SparklePointSource Source);

/// <summary>Presentation-only notifications. This service never writes the point ledger.</summary>
public static class SparklePointRewards
{
    public static event EventHandler<SparklePointAward>? Awarded;

    /// <summary>Call only after a local award is saved. Capped and zero credits remain quiet.</summary>
    public static void PublishCredit(int previousBalance, int currentBalance, SparklePointSource source)
    {
        var balance = SparklePoints.Clamp(currentBalance);
        var amount = balance - SparklePoints.Clamp(previousBalance);
        if (amount <= 0) return;
        var listeners = Awarded;
        if (listeners == null) return;
        var award = new SparklePointAward(amount, balance, source);
        foreach (EventHandler<SparklePointAward> listener in listeners.GetInvocationList())
        {
            try { listener(null, award); }
            catch (Exception ex)
            {
                // A broken decoration cannot block another subscriber or unwind a settled credit.
                App.Logger?.Debug("Sparkle point award subscriber failed: {Error}", ex.Message);
            }
        }
    }
}
