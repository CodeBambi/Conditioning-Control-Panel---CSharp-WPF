using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Possession.Effects;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Possession's rewrite pools (WPF RewritePools): a known word answers in the mod's voice and
/// the label's own case, a paragraph or a bare number is left alone, and a rewrite is never the original.</summary>
public sealed class RewritePoolsTests
{
    [Fact]
    public void AKnownWordAnswersInTheModsVoice_AndKeepsTheLabelsCaseAndPadding()
    {
        var rng = new Random(1);
        Assert.Equal("Their settings", RewritePools.Rewrite("Settings", BuiltInMods.CCPDefaultId, rng));
        Assert.Equal("HER SETTINGS", RewritePools.Rewrite("SETTINGS", BuiltInMods.BambiSleepId, rng));
        Assert.Equal(" stay ", RewritePools.Rewrite(" exit ", BuiltInMods.SissyHypnoId, rng));
        Assert.Equal("Kept", RewritePools.Rewrite("Lockdown", BuiltInMods.LockedId, rng));
    }

    [Fact]
    public void WhatIsNotALabelIsLeftAlone_AndARewriteIsNeverTheOriginal()
    {
        var rng = new Random(2);
        Assert.Null(RewritePools.Rewrite(null, BuiltInMods.CCPDefaultId, rng));
        Assert.Null(RewritePools.Rewrite("   ", BuiltInMods.CCPDefaultId, rng));
        Assert.Null(RewritePools.Rewrite(new string('a', 49), BuiltInMods.CCPDefaultId, rng));
        Assert.Null(RewritePools.Rewrite("42", BuiltInMods.CCPDefaultId, rng));
        for (int i = 0; i < 20; i++)
        {
            var said = RewritePools.Rewrite("Quests", BuiltInMods.CCPDefaultId, rng);
            Assert.NotNull(said);
            Assert.StartsWith("Quests", said);
            Assert.NotEqual("Quests", said);
        }
    }
}
