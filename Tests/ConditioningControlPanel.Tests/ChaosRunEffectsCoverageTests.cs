using System.Linq;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The catalogues live in Core and the run effects in this head, keyed by id: a Core
/// catalogue id with no ChaosRunEffects entry would silently do nothing in a run.</summary>
public class ChaosRunEffectsCoverageTests
{
    [Fact]
    public void Every_habit_has_a_run_effect() =>
        Assert.Empty(ChaosUpgrades.All.Select(u => u.Id).Where(id => !ChaosRunEffects.Habits.ContainsKey(id)));

    [Fact]
    public void Every_lifetime_boon_has_a_run_effect() =>
        Assert.Empty(ChaosLifetimeBoons.All.Select(b => b.Id).Where(id => !ChaosRunEffects.Boons.ContainsKey(id)));

    [Fact]
    public void No_run_effect_without_a_catalogue_id()
    {
        Assert.Empty(ChaosRunEffects.Habits.Keys.Where(id => ChaosUpgrades.ById(id) == null));
        Assert.Empty(ChaosRunEffects.Boons.Keys.Where(id => ChaosLifetimeBoons.ById(id) == null));
    }
}
