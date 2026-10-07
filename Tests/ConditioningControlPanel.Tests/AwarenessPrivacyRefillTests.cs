using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ConditioningControlPanel.Views.Controls.Companion.Runtime;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1323: the Awareness privacy card syncs every 1.5 s while it is on screen, and it used
/// to clear and refill all four chip rows on every tick, regenerating every chip and re-laying out
/// the whole page. A row is now refilled only when what it shows changed.
/// </summary>
public class AwarenessPrivacyRefillTests
{
    private static List<(string, Func<string>)> Items(params string[] keys)
    {
        var list = new List<(string, Func<string>)>();
        foreach (var k in keys) { var key = k; list.Add((key, () => key)); }
        return list;
    }

    [Fact]
    public void AnUnchangedRowIsNotTouched()
    {
        var target = new ObservableCollection<string>();
        var keys = new List<string>();
        Assert.True(AwarenessPrivacyRuntimeVm.RefillIfChanged(target, keys, Items("chrome", "discord")));

        int changes = 0;
        target.CollectionChanged += (_, _) => changes++;
        for (int tick = 0; tick < 5; tick++)
            Assert.False(AwarenessPrivacyRuntimeVm.RefillIfChanged(target, keys, Items("chrome", "discord")));

        Assert.Equal(0, changes);
        Assert.Equal(new[] { "chrome", "discord" }, target);
    }

    [Theory]
    [InlineData("discord,chrome")]
    [InlineData("chrome")]
    [InlineData("chrome,discord,steam")]
    [InlineData("")]
    public void AChangedRowIsRefilled(string joined)
    {
        var next = joined.Length == 0 ? Array.Empty<string>() : joined.Split(',');
        var target = new ObservableCollection<string>();
        var keys = new List<string>();
        AwarenessPrivacyRuntimeVm.RefillIfChanged(target, keys, Items("chrome", "discord"));

        Assert.True(AwarenessPrivacyRuntimeVm.RefillIfChanged(target, keys, Items(next)));
        Assert.Equal(next, target);
    }

    [Fact]
    public void ARowEmptiedFromOutsideIsRefilled()
    {
        var target = new ObservableCollection<string>();
        var keys = new List<string>();
        AwarenessPrivacyRuntimeVm.RefillIfChanged(target, keys, Items("chrome"));
        target.Clear();

        Assert.True(AwarenessPrivacyRuntimeVm.RefillIfChanged(target, keys, Items("chrome")));
        Assert.Equal(new[] { "chrome" }, target);
    }
}
